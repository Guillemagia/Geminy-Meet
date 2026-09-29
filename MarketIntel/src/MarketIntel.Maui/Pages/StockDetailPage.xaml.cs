using MarketIntel.Maui.Localization;
using MarketIntel.Maui.Services;
using MarketIntel.Maui.ViewModels;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Maui.Pages;

public partial class StockDetailPage : ContentPage
{
    private readonly StockDetailViewModel _vm;
    private readonly PaperTradingStore _paper;

    public StockDetailPage(StockDetailViewModel vm, PaperTradingStore paper)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _paper = paper;
    }

    private async void OnAskAssistant(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_vm.Symbol))
            await Shell.Current.GoToAsync($"assistant?symbol={_vm.Symbol}");
    }

    private async void OnPaperTrade(object? sender, EventArgs e)
    {
        var s = _vm.Signal;
        if (s is null) return;
        var loc = LocalizationResourceManager.Instance;
        if (s.Direction == SignalDirection.Neutral || s.IsNoTrade)
        {
            await DisplayAlertAsync(loc.Get("Paper_DialogTitle"), loc.Format("Paper_NeutralMsg", _vm.Symbol), loc.Get("Common_OK"));
            return;
        }

        bool isLong = s.Direction == SignalDirection.Bullish;
        bool ok = _paper.Open(_vm.Symbol, isLong, s.Price);
        await DisplayAlertAsync(loc.Get("Paper_DialogTitle"),
            ok ? loc.Format("Paper_OpenedMsg", isLong ? "LONG" : "SHORT", _vm.Symbol, s.Price)
               : loc.Format("Paper_AlreadyOpen", _vm.Symbol),
            loc.Get("Common_OK"));
    }
}
