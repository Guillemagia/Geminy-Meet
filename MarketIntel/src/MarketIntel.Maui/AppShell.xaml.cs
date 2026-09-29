using MarketIntel.Maui.Pages;

namespace MarketIntel.Maui;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		// Pages navigated to with a "symbol" query parameter.
		Routing.RegisterRoute("detail", typeof(StockDetailPage));
		Routing.RegisterRoute("assistant", typeof(AssistantPage));
	}
}
