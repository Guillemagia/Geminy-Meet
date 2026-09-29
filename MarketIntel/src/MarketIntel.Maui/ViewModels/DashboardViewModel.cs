using System.Collections.ObjectModel;
using MarketIntel.Maui.Localization;
using MarketIntel.Maui.Models;
using MarketIntel.Maui.Services;
using MarketIntel.Shared.Contracts;

namespace MarketIntel.Maui.ViewModels;

/// <summary>An alert rule that is currently satisfied by a symbol.</summary>
public sealed record AlertHit(string Symbol, int Score, string RuleText);

public sealed class DashboardViewModel : ObservableObject
{
    private readonly MarketApiClient _api;
    private readonly WatchlistStore _store;
    private readonly ILocalNotifier _notifier;
    private readonly HashSet<string> _notified = [];

    public DashboardViewModel(MarketApiClient api, WatchlistStore store, ILocalNotifier notifier)
    {
        _api = api;
        _store = store;
        _notifier = notifier;
        RefreshCommand = new AsyncCommand(LoadAsync);
        AddSymbolCommand = new AsyncCommand(AddSymbolAsync);
        RemoveSymbolCommand = new RelayCommand(o => _ = RemoveSymbolAsync(o as string));
        AddAlertCommand = new RelayCommand(_ => AddAlert());
        RemoveRuleCommand = new RelayCommand(o => RemoveRule(o as AlertRule));
        OpenSymbolCommand = new RelayCommand(o => { if (o is string s && !string.IsNullOrWhiteSpace(s)) OpenSymbolRequested?.Invoke(s); });

        foreach (var r in _store.GetRules()) Rules.Add(r);
    }

    /// <summary>Raised when the user taps a watchlist item; the page handles navigation.</summary>
    public event Action<string>? OpenSymbolRequested;

    public ObservableCollection<OpportunityDto> Opportunities { get; } = [];
    public ObservableCollection<EconomicEventDto> Events { get; } = [];
    public ObservableCollection<OpportunityDto> Watchlist { get; } = [];
    public ObservableCollection<AlertHit> FiredAlerts { get; } = [];
    public ObservableCollection<AlertRule> Rules { get; } = [];

    public bool HasEvents => Events.Count > 0;
    public bool HasWatchlist => Watchlist.Count > 0;
    public bool HasFiredAlerts => FiredAlerts.Count > 0;

    // Inputs bound to the UI.
    private string _newSymbol = "";
    public string NewSymbol { get => _newSymbol; set => SetProperty(ref _newSymbol, value); }

    private string _newAlertSymbol = "";
    public string NewAlertSymbol { get => _newAlertSymbol; set => SetProperty(ref _newAlertSymbol, value); }

    private string _newAlertScore = "85";
    public string NewAlertScore { get => _newAlertScore; set => SetProperty(ref _newAlertScore, value); }

    private MetaDto? _meta;
    public MetaDto? Meta
    {
        get => _meta;
        set
        {
            if (!SetProperty(ref _meta, value)) return;
            OnPropertyChanged(nameof(BannerTitle));
            OnPropertyChanged(nameof(BannerDetail));
            OnPropertyChanged(nameof(IsSynthetic));
        }
    }

    public bool IsSynthetic => _meta?.Synthetic ?? true;
    public string BannerTitle => _meta is null || _meta.Synthetic
        ? LocalizationResourceManager.Instance.Get("Banner_Synthetic")
        : LocalizationResourceManager.Instance.Format("Banner_Live", _meta.Provider);
    public string BannerDetail => _meta?.Disclaimer
        ?? LocalizationResourceManager.Instance.Get("Banner_DefaultDetail");

    // Language selector (bound to the picker on the dashboard).
    public IReadOnlyList<LanguageOption> Languages { get; } = AppLanguages.Available;

    private LanguageOption? _selectedLanguage =
        AppLanguages.Available.FirstOrDefault(o => o.Code == LocalizationResourceManager.Instance.CurrentLanguage);
    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!SetProperty(ref _selectedLanguage, value) || value is null) return;
            LocalizationResourceManager.Instance.SetLanguage(value.Code);
            // Re-raise the computed banner strings and reload so backend text comes back localized.
            OnPropertyChanged(nameof(BannerTitle));
            OnPropertyChanged(nameof(BannerDetail));
            _ = LoadAsync();
        }
    }

    private MarketRegimeDto? _regime;
    public MarketRegimeDto? Regime { get => _regime; set => SetProperty(ref _regime, value); }

    private AccuracyDashboardDto? _accuracy;
    public AccuracyDashboardDto? Accuracy { get => _accuracy; set { if (SetProperty(ref _accuracy, value)) OnPropertyChanged(nameof(HasAccuracy)); } }
    public bool HasAccuracy => _accuracy is { TotalSample: > 0 };

    private CalibrationReportDto? _calibration;
    public CalibrationReportDto? Calibration { get => _calibration; set { if (SetProperty(ref _calibration, value)) OnPropertyChanged(nameof(HasCalibration)); } }
    public bool HasCalibration => _calibration is { Buckets.Count: > 0 };

    private SignalRegistryStatsDto? _registry;
    public SignalRegistryStatsDto? Registry
    {
        get => _registry;
        set { if (SetProperty(ref _registry, value)) { OnPropertyChanged(nameof(HasRegistry)); OnPropertyChanged(nameof(RegistrySummary)); } }
    }
    public bool HasRegistry => _registry is not null;
    public string RegistrySummary => _registry is null ? ""
        : LocalizationResourceManager.Instance.Format("Reg_Counts", _registry.Total, _registry.Resolved, _registry.Pending);

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(IsNotBusy)); } }
    public bool IsNotBusy => !_isBusy;

    private string? _error;
    public string? Error { get => _error; set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(_error);

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand AddSymbolCommand { get; }
    public RelayCommand RemoveSymbolCommand { get; }
    public RelayCommand AddAlertCommand { get; }
    public RelayCommand RemoveRuleCommand { get; }
    public RelayCommand OpenSymbolCommand { get; }

    private bool _loading;

    public async Task LoadAsync()
    {
        // Guard on a private flag, not IsBusy: RefreshView sets IsBusy before invoking the command.
        if (_loading) return;
        _loading = true;
        IsBusy = true;
        Error = null;
        try
        {
            var metaTask = _api.GetMetaAsync();
            var regimeTask = _api.GetRegimeAsync();
            var oppsTask = _api.GetTopOpportunitiesAsync(8);
            var eventsTask = _api.GetUpcomingEventsAsync(120);
            var watchTask = _api.GetWatchlistAsync(_store.GetSymbols());
            var accuracyTask = _api.GetAccuracyAsync();
            var calibrationTask = _api.GetCalibrationAsync();
            var registryTask = _api.GetRegistryAsync();
            await Task.WhenAll(metaTask, regimeTask, oppsTask, eventsTask, watchTask, accuracyTask, calibrationTask, registryTask);

            Meta = metaTask.Result;
            Regime = regimeTask.Result;
            Accuracy = accuracyTask.Result;
            Calibration = calibrationTask.Result;
            Registry = registryTask.Result;

            Replace(Opportunities, oppsTask.Result);
            Replace(Events, eventsTask.Result);
            Replace(Watchlist, watchTask.Result);
            OnPropertyChanged(nameof(HasEvents));
            OnPropertyChanged(nameof(HasWatchlist));

            EvaluateAlerts();
        }
        catch (Exception ex)
        {
            Error = LocalizationResourceManager.Instance.Format("Dash_ConnectError", ApiConfig.BaseUrl, ex.Message);
        }
        finally
        {
            IsBusy = false;
            _loading = false;
        }
    }

    public async Task AddSymbolAsync()
    {
        var sym = NewSymbol.Trim().ToUpperInvariant();
        // Up to 12 chars so crypto (e.g. AVAXUSD, LINKUSD) can be added, not just short tickers.
        if (sym.Length is 0 or > 12) return;
        var symbols = _store.GetSymbols();
        if (!symbols.Contains(sym)) symbols.Insert(0, sym);
        _store.SaveSymbols(symbols);
        NewSymbol = "";
        await LoadAsync();
    }

    private async Task RemoveSymbolAsync(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        var symbols = _store.GetSymbols();
        symbols.RemoveAll(s => string.Equals(s, symbol, StringComparison.OrdinalIgnoreCase));
        _store.SaveSymbols(symbols);
        await LoadAsync();
    }

    private void AddAlert()
    {
        if (!int.TryParse(NewAlertScore, out var score)) return;
        score = Math.Clamp(score, 1, 100);
        var rule = new AlertRule { Symbol = NewAlertSymbol.Trim().ToUpperInvariant(), MinScore = score };
        Rules.Add(rule);
        _store.SaveRules(Rules);
        NewAlertSymbol = "";
        EvaluateAlerts();
    }

    private void RemoveRule(AlertRule? rule)
    {
        if (rule is null) return;
        Rules.Remove(rule);
        _store.SaveRules(Rules);
        EvaluateAlerts();
    }

    /// <summary>Fire any rule satisfied by the current scanner + watchlist quotes.</summary>
    private void EvaluateAlerts()
    {
        FiredAlerts.Clear();
        var quotes = Opportunities.Concat(Watchlist)
            .GroupBy(q => q.Symbol)
            .Select(g => g.First());

        foreach (var rule in Rules)
            foreach (var q in quotes)
            {
                bool symbolMatch = string.IsNullOrWhiteSpace(rule.Symbol) ||
                                   string.Equals(rule.Symbol, q.Symbol, StringComparison.OrdinalIgnoreCase);
                if (symbolMatch && q.Score >= rule.MinScore &&
                    !FiredAlerts.Any(a => a.Symbol == q.Symbol && a.RuleText == rule.Describe()))
                    FiredAlerts.Add(new AlertHit(q.Symbol, q.Score, rule.Describe()));
            }

        OnPropertyChanged(nameof(HasFiredAlerts));

        // Fire a real on-device notification for alerts we haven't notified yet this session.
        foreach (var hit in FiredAlerts)
        {
            string key = $"{hit.Symbol}|{hit.RuleText}";
            if (_notified.Add(key))
                _notifier.Show(LocalizationResourceManager.Instance.Format("Alert_NotifyTitle", hit.Symbol, hit.Score),
                    hit.RuleText, key.GetHashCode());
        }
    }

    /// <summary>Ask for notification permission (Android 13+). Called once from the page.</summary>
    public Task EnsureNotificationsAsync() => _notifier.EnsurePermissionAsync();

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }
}
