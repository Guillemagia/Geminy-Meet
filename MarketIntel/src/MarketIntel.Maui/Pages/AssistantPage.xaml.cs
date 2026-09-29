using System.Collections.Specialized;
using MarketIntel.Maui.ViewModels;

namespace MarketIntel.Maui.Pages;

public partial class AssistantPage : ContentPage
{
    private readonly AssistantViewModel _vm;

    public AssistantPage(AssistantViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        // Auto-scroll to the newest message.
        _vm.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm.Messages.Count > 0)
            MessagesView.ScrollTo(_vm.Messages.Count - 1, position: ScrollToPosition.End, animate: true);
    }
}
