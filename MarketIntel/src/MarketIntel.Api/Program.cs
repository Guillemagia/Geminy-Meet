using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Localization;
using MarketIntel.Api.Assistant;
using MarketIntel.Api.Localization;
using MarketIntel.Api.Calendar;
using MarketIntel.Api.Engines;
using MarketIntel.Api.MarketData;
using MarketIntel.Api.Ml;
using MarketIntel.Api.News;
using MarketIntel.Api.Notifications;
using MarketIntel.Api.Options;
using MarketIntel.Api.Services;
using MarketIntel.Shared.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Serialize enums as strings so the API is readable and the client is resilient to reordering.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Allow the MAUI app (any origin during development) to call the API.
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Personal single-user build: no subscription tiers, no rate limiting, no paywalls —
// every feature is unlocked. Output caching is kept purely as a performance optimization.
builder.Services.AddSingleton<DeviceRegistry>();

// Real push via Firebase Cloud Messaging when configured (Fcm:ProjectId + service-account file);
// otherwise the logging sender stands in during development.
var fcm = builder.Configuration.GetSection(FcmOptions.SectionName).Get<FcmOptions>() ?? new FcmOptions();
builder.Services.AddSingleton(fcm);
if (fcm.IsConfigured)
{
    builder.Services.AddHttpClient<INotificationService, FcmNotificationService>();
    Console.WriteLine($"[Notifications] FCM push HABILITADO (proyecto {fcm.ProjectId})");
}
else
{
    builder.Services.AddSingleton<INotificationService, LoggingNotificationService>();
    Console.WriteLine("[Notifications] FCM no configurado — usando logger");
}

// Only endpoints that explicitly opt in via CacheOutput() are cached.
builder.Services.AddOutputCache();

// --- Market-data provider selection ---
// If Alpaca keys are configured (appsettings / user-secrets / env vars), use real data.
// Otherwise fall back to the synthetic mock so the platform always runs.
builder.Services.Configure<AlpacaOptions>(builder.Configuration.GetSection(AlpacaOptions.SectionName));
var alpaca = builder.Configuration.GetSection(AlpacaOptions.SectionName).Get<AlpacaOptions>();
if (alpaca?.IsConfigured == true)
    builder.Services.AddHttpClient<IMarketDataProvider, AlpacaMarketDataProvider>();
else
    builder.Services.AddSingleton<IMarketDataProvider, MockMarketDataProvider>();

// --- Engines and orchestration ---
builder.Services.AddSingleton<IndicatorEngine>();
builder.Services.AddSingleton<RiskEngine>();
builder.Services.AddSingleton<SignalEngine>();
builder.Services.AddSingleton<MarketRegimeEngine>();
builder.Services.AddSingleton<EnsembleEngine>();
builder.Services.AddSingleton<StackingEnsembleEngine>();
builder.Services.AddSingleton<RegimeStackingEngine>();
builder.Services.AddSingleton<SupportResistanceEngine>();
builder.Services.AddSingleton<PricePatternEngine>();
builder.Services.AddSingleton<MarketStructureEngine>();
builder.Services.AddSingleton<IEventCalendar, MockEventCalendar>();
builder.Services.AddSingleton<EventRiskEngine>();

// --- Machine learning (V2) ---
builder.Services.AddSingleton<FeatureExtractor>();
builder.Services.AddSingleton<BacktestEngine>();
builder.Services.AddSingleton<WalkForwardValidator>();
builder.Services.AddSingleton<ModelService>();
builder.Services.AddSingleton<AccuracyEngine>();
builder.Services.AddSingleton<CalibrationEngine>();
builder.Services.AddSingleton<SignalQualityEngine>();
builder.Services.AddSingleton<SignalRegistry>();

// --- Options ---
builder.Services.AddSingleton<IOptionsProvider, MockOptionsProvider>();
builder.Services.AddSingleton<OptionsEngine>();

// --- News & sentiment ---
builder.Services.AddSingleton<INewsProvider, MockNewsProvider>();
builder.Services.AddSingleton<NewsEngine>();

// --- Grounded assistant ---
builder.Services.AddSingleton<AssistantEngine>();

builder.Services.AddSingleton<MarketAnalysisService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Culture per request: the app sends its language in Accept-Language; backend-generated text
// (analysis, assistant, calendar…) is emitted in that language. Falls back to Spanish.
var supportedCultures = Loc.AvailableLanguages.ToArray();
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("es")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures));

app.UseCors();
app.UseOutputCache();

// ---------------- API surface ----------------

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timeUtc = DateTime.UtcNow }))
   .WithName("Health");

// Readiness probe for orchestrators (k8s/compose).
app.MapGet("/api/ready", () => Results.Ok(new { ready = true })).WithName("Ready");

// Register a device push token (real delivery via FCM/APNs is wired behind INotificationService).
app.MapPost("/api/devices", (string? token, DeviceRegistry devices) =>
{
    if (string.IsNullOrWhiteSpace(token)) return Results.BadRequest("token requerido");
    devices.Register(token);
    return Results.Ok(new { registered = true, total = devices.Count });
}).WithName("RegisterDevice");

// Dev-only: send a test push to all registered devices.
app.MapPost("/api/notify/test", async (DeviceRegistry devices, INotificationService push, CancellationToken ct) =>
{
    foreach (var token in devices.Tokens)
        await push.SendAsync(token, Loc.T("Notify.TestTitle"), Loc.T("Notify.TestBody"), ct);
    return Results.Ok(new { sent = devices.Count });
}).WithName("NotifyTest");

app.MapGet("/api/meta", (MarketAnalysisService svc) => Results.Ok(new MetaDto(
    Provider: svc.ProviderName,
    Synthetic: svc.IsSyntheticData,
    Disclaimer: Loc.T("Meta.Disclaimer")
))).WithName("Meta");

app.MapGet("/api/market/regime", async (MarketAnalysisService svc, CancellationToken ct)
    => Results.Ok(await svc.GetRegimeAsync(ct))).WithName("MarketRegime")
    .CacheOutput(p => p.Expire(TimeSpan.FromSeconds(15)).SetVaryByHeader("Accept-Language"));

app.MapGet("/api/calendar/upcoming", async (int? hours, MarketAnalysisService svc, CancellationToken ct)
    => Results.Ok(await svc.GetUpcomingEventsAsync(hours ?? 48, ct))).WithName("UpcomingEvents");

app.MapGet("/api/signals/{symbol}", async (string symbol, MarketAnalysisService svc, CancellationToken ct)
    => Results.Ok(await svc.GetSignalAsync(symbol, ct))).WithName("Signal");

app.MapGet("/api/scanner/top", async (int? take, MarketAnalysisService svc, CancellationToken ct) =>
{
    // Personal build: no tier cap; just a sane upper bound to protect the provider.
    return Results.Ok(await svc.GetTopOpportunitiesAsync(Math.Min(take ?? 8, 50), ct));
}).WithName("TopOpportunities");

app.MapGet("/api/watchlist", async (string? symbols, MarketAnalysisService svc, CancellationToken ct) =>
{
    var syms = (symbols ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return Results.Ok(await svc.GetQuotesAsync(syms, ct));
}).WithName("Watchlist");

app.MapGet("/api/assistant/{symbol}", async (string symbol, string? q, MarketAnalysisService svc, OptionsEngine optEngine, AssistantEngine assistant, CancellationToken ct) =>
{
    var signal = await svc.GetSignalAsync(symbol, ct);
    var options = await optEngine.AnalyzeAsync(symbol, ct);
    return Results.Ok(assistant.Answer(signal, options, q ?? ""));
}).WithName("Assistant");

app.MapGet("/api/options/{symbol}", async (string symbol, OptionsEngine options, CancellationToken ct) =>
{
    var result = await options.AnalyzeAsync(symbol, ct);
    return result is null ? Results.NotFound($"Sin opciones para {symbol}") : Results.Ok(result);
}).WithName("Options");

app.MapGet("/api/news/{symbol}", async (string symbol, NewsEngine news, CancellationToken ct) =>
{
    var result = await news.AnalyzeAsync(symbol, ct);
    return result is null ? Results.NotFound($"Sin noticias para {symbol}") : Results.Ok(result);
}).WithName("News");

app.MapGet("/api/accuracy", async (AccuracyEngine accuracy, CancellationToken ct)
    => Results.Ok(await accuracy.ComputeAsync(ct))).WithName("Accuracy")
    .CacheOutput(p => p.Expire(TimeSpan.FromSeconds(30)).SetVaryByHeader("Accept-Language"));

// Walk-forward calibration: proves (or honestly disproves) that each grade hits at its claimed rate.
app.MapGet("/api/calibration", async (CalibrationEngine calibration, CancellationToken ct)
    => Results.Ok(await calibration.ComputeAsync(ct))).WithName("Calibration")
    .CacheOutput(p => p.Expire(TimeSpan.FromSeconds(60)).SetVaryByHeader("Accept-Language"));

// Live signal registry: forward-accumulated evidence of real emitted signals and their outcomes.
app.MapGet("/api/registry", async (SignalRegistry registry, CancellationToken ct)
    => Results.Ok(await registry.GetStatsAsync(ct))).WithName("RegistryStats");

app.MapGet("/api/backtest/{symbol}", async (string symbol, ModelService model, CancellationToken ct) =>
{
    var result = await model.GetBacktestAsync(symbol, ct);
    return result is null ? Results.NotFound($"Sin datos suficientes para {symbol}") : Results.Ok(result);
}).WithName("Backtest");

app.MapGet("/api/candles/{symbol}", async (string symbol, string? tf, int? count, MarketAnalysisService svc, CancellationToken ct) =>
{
    if (!Enum.TryParse<Timeframe>(tf ?? "H1", true, out var timeframe))
        return Results.BadRequest($"Timeframe inválido: {tf}");
    var candles = await svc.GetCandlesAsync(symbol, timeframe, count ?? 200, ct);
    return Results.Ok(candles);
}).WithName("Candles");

app.Run();

// Exposed for integration testing.
public partial class Program;
