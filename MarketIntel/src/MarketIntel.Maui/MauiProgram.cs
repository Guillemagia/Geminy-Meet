using MarketIntel.Maui.Pages;
using MarketIntel.Maui.Services;
using MarketIntel.Maui.ViewModels;
using Microsoft.Extensions.Logging;

namespace MarketIntel.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// HTTP client for the backend API.
		builder.Services.AddHttpClient<MarketApiClient>();

		// Local persistence for watchlist, alert rules and paper trading.
		builder.Services.AddSingleton<WatchlistStore>();
		builder.Services.AddSingleton<PaperTradingStore>();

		// On-device notifications for fired alerts.
		builder.Services.AddSingleton<ILocalNotifier, LocalNotifier>();

		// View models.
		builder.Services.AddSingleton<DashboardViewModel>();
		builder.Services.AddTransient<StockDetailViewModel>();
		builder.Services.AddTransient<AssistantViewModel>();
		builder.Services.AddSingleton<PaperTradingViewModel>();

		// Pages.
		builder.Services.AddSingleton<DashboardPage>();
		builder.Services.AddTransient<StockDetailPage>();
		builder.Services.AddTransient<AssistantPage>();
		builder.Services.AddSingleton<PaperTradingPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
