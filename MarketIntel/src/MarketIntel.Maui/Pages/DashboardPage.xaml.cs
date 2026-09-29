using MarketIntel.Maui.ViewModels;
using MarketIntel.Shared.Contracts;

namespace MarketIntel.Maui.Pages;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _vm;

    public DashboardPage(DashboardViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.OpenSymbolRequested += OnOpenSymbol;
    }

    private async void OnOpenSymbol(string symbol)
        => await Shell.Current.GoToAsync($"detail?symbol={symbol}");

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.EnsureNotificationsAsync();
        if (_vm.Opportunities.Count == 0)
            await _vm.LoadAsync();
    }

    private async void OnOpportunitySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not OpportunityDto op) return;
        ((CollectionView)sender!).SelectedItem = null; // clear selection highlight
        await Shell.Current.GoToAsync($"detail?symbol={op.Symbol}");
    }
}
