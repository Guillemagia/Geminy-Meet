using MarketIntel.Maui.ViewModels;

namespace MarketIntel.Maui.Pages;

public partial class PaperTradingPage : ContentPage
{
    private readonly PaperTradingViewModel _vm;

    public PaperTradingPage(PaperTradingViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
