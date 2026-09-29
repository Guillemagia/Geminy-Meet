using MarketIntel.Maui.Services;
using MarketIntel.Shared.Contracts;

namespace MarketIntel.Maui.ViewModels;

// QueryProperty binds the "symbol" navigation parameter from Shell routing.
[QueryProperty(nameof(Symbol), "symbol")]
public sealed class StockDetailViewModel : ObservableObject
{
    private readonly MarketApiClient _api;

    public StockDetailViewModel(MarketApiClient api)
    {
        _api = api;
        RefreshCommand = new AsyncCommand(LoadAsync);
    }

    private string _symbol = "";
    public string Symbol
    {
        get => _symbol;
        set { if (SetProperty(ref _symbol, value)) _ = LoadAsync(); }
    }

    private SignalDto? _signal;
    public SignalDto? Signal { get => _signal; set => SetProperty(ref _signal, value); }

    private BacktestResultDto? _backtest;
    public BacktestResultDto? Backtest { get => _backtest; set { if (SetProperty(ref _backtest, value)) OnPropertyChanged(nameof(HasBacktest)); } }
    public bool HasBacktest => _backtest is { Trades: > 0 };

    private OptionsAnalysisDto? _options;
    public OptionsAnalysisDto? Options { get => _options; set { if (SetProperty(ref _options, value)) OnPropertyChanged(nameof(HasOptions)); } }
    public bool HasOptions => _options is { Best.Count: > 0 };

    private NewsAnalysisDto? _news;
    public NewsAnalysisDto? News { get => _news; set { if (SetProperty(ref _news, value)) OnPropertyChanged(nameof(HasNews)); } }
    public bool HasNews => _news is { Items.Count: > 0 };

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(IsNotBusy)); } }
    public bool IsNotBusy => !_isBusy;

    private string? _error;
    public string? Error { get => _error; set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(_error);

    public AsyncCommand RefreshCommand { get; }

    private bool _loading;

    public async Task LoadAsync()
    {
        // Guard on a private flag, not IsBusy: RefreshView sets IsBusy before invoking the command.
        if (string.IsNullOrWhiteSpace(Symbol) || _loading) return;
        _loading = true;
        IsBusy = true;
        Error = null;
        try
        {
            var signalTask = _api.GetSignalAsync(Symbol);
            var backtestTask = _api.GetBacktestAsync(Symbol);
            var optionsTask = _api.GetOptionsAsync(Symbol);
            var newsTask = _api.GetNewsAsync(Symbol);
            await Task.WhenAll(signalTask, backtestTask, optionsTask, newsTask);
            Signal = signalTask.Result;
            Backtest = backtestTask.Result;
            Options = optionsTask.Result;
            News = newsTask.Result;
        }
        catch (Exception ex)
        {
            Error = $"No se pudo cargar {Symbol}. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _loading = false;
        }
    }
}
