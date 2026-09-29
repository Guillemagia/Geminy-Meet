using System.Collections.ObjectModel;
using MarketIntel.Maui.Localization;
using MarketIntel.Maui.Models;
using MarketIntel.Maui.Services;

namespace MarketIntel.Maui.ViewModels;

/// <summary>An open position enriched with its live price and P&amp;L for display.</summary>
public sealed record OpenPositionView(
    string Symbol, bool IsLong, decimal EntryPrice, decimal CurrentPrice, decimal Shares, decimal Pnl, double PnlPercent)
{
    public string Side => IsLong ? "LONG" : "SHORT";
}

public sealed class PaperTradingViewModel : ObservableObject
{
    private readonly MarketApiClient _api;
    private readonly PaperTradingStore _store;

    public PaperTradingViewModel(MarketApiClient api, PaperTradingStore store)
    {
        _api = api;
        _store = store;
        RefreshCommand = new AsyncCommand(LoadAsync);
        CloseCommand = new RelayCommand(o => { if (o is string s) _ = CloseAsync(s); });
    }

    public ObservableCollection<OpenPositionView> Positions { get; } = [];
    public ObservableCollection<ClosedTrade> History { get; } = [];

    public bool HasPositions => Positions.Count > 0;
    public bool HasHistory => History.Count > 0;

    private decimal _equity = PaperTradingStore.StartingCapital;
    public decimal Equity { get => _equity; set => SetProperty(ref _equity, value); }

    private decimal _realized;
    public decimal Realized { get => _realized; set => SetProperty(ref _realized, value); }

    private decimal _unrealized;
    public decimal Unrealized { get => _unrealized; set => SetProperty(ref _unrealized, value); }

    private double _totalReturnPercent;
    public double TotalReturnPercent { get => _totalReturnPercent; set => SetProperty(ref _totalReturnPercent, value); }

    private string _stats = "";
    public string Stats { get => _stats; set => SetProperty(ref _stats, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncCommand RefreshCommand { get; }
    public RelayCommand CloseCommand { get; }

    private bool _loading;

    public async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        IsBusy = true;
        try
        {
            var open = _store.GetOpen();
            var prices = new Dictionary<string, decimal>();
            if (open.Count > 0)
            {
                var quotes = await _api.GetWatchlistAsync(open.Select(p => p.Symbol));
                foreach (var q in quotes) prices[q.Symbol] = q.Price;
            }

            decimal unrealized = 0;
            Positions.Clear();
            foreach (var p in open)
            {
                decimal cur = prices.TryGetValue(p.Symbol, out var px) && px > 0 ? px : p.EntryPrice;
                decimal pnl = (p.IsLong ? cur - p.EntryPrice : p.EntryPrice - cur) * p.Shares;
                double pnlPct = p.EntryPrice > 0
                    ? (double)((p.IsLong ? cur - p.EntryPrice : p.EntryPrice - cur) / p.EntryPrice) * 100 : 0;
                unrealized += pnl;
                Positions.Add(new OpenPositionView(p.Symbol, p.IsLong, p.EntryPrice, cur, p.Shares, Math.Round(pnl, 2), Math.Round(pnlPct, 2)));
            }

            var closed = _store.GetClosed();
            History.Clear();
            foreach (var t in closed) History.Add(t);

            Realized = _store.RealizedPnl();
            Unrealized = Math.Round(unrealized, 2);
            Equity = Math.Round(PaperTradingStore.StartingCapital + Realized + Unrealized, 2);
            TotalReturnPercent = Math.Round((double)((Equity - PaperTradingStore.StartingCapital) / PaperTradingStore.StartingCapital) * 100, 2);

            int trades = closed.Count;
            int wins = closed.Count(t => t.Pnl > 0);
            double winRate = trades > 0 ? (double)wins / trades * 100 : 0;
            Stats = trades > 0
                ? LocalizationResourceManager.Instance.Format("Paper_Stats", trades, winRate)
                : LocalizationResourceManager.Instance.Get("Paper_NoClosed");

            OnPropertyChanged(nameof(HasPositions));
            OnPropertyChanged(nameof(HasHistory));
        }
        finally
        {
            IsBusy = false;
            _loading = false;
        }
    }

    private async Task CloseAsync(string symbol)
    {
        var pos = Positions.FirstOrDefault(p => p.Symbol == symbol);
        if (pos is null) return;
        _store.Close(symbol, pos.CurrentPrice);
        await LoadAsync();
    }
}
