using System.Collections.ObjectModel;
using MarketIntel.Maui.Localization;
using MarketIntel.Maui.Services;

namespace MarketIntel.Maui.ViewModels;

/// <summary>A single chat bubble.</summary>
public sealed record ChatMessage(bool IsUser, string Text);

[QueryProperty(nameof(Symbol), "symbol")]
public sealed class AssistantViewModel : ObservableObject
{
    private readonly MarketApiClient _api;

    public AssistantViewModel(MarketApiClient api)
    {
        _api = api;
        SendCommand = new AsyncCommand(SendAsync);
        AskCommand = new RelayCommand(o => { if (o is string s) _ = AskAsync(s); });
    }

    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public ObservableCollection<string> Suggestions { get; } = [];

    private string _symbol = "";
    public string Symbol
    {
        get => _symbol;
        set { if (SetProperty(ref _symbol, value)) { OnPropertyChanged(nameof(Title)); _ = InitAsync(); } }
    }

    /// <summary>Localized page title, e.g. "Assistant · AAPL".</summary>
    public string Title => LocalizationResourceManager.Instance.Format("Assistant_Title", Symbol);

    private string _input = "";
    public string Input { get => _input; set => SetProperty(ref _input, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncCommand SendCommand { get; }
    public RelayCommand AskCommand { get; }

    private async Task InitAsync()
    {
        if (string.IsNullOrWhiteSpace(Symbol)) return;
        Messages.Clear();
        Messages.Add(new ChatMessage(false, LocalizationResourceManager.Instance.Format("Assist_Greeting", Symbol)));
        await AskAsync("resumen"); // seed with a summary + suggestions
    }

    private Task SendAsync()
    {
        var q = Input.Trim();
        Input = "";
        return string.IsNullOrEmpty(q) ? Task.CompletedTask : AskAsync(q);
    }

    private async Task AskAsync(string question)
    {
        if (string.IsNullOrWhiteSpace(Symbol) || IsBusy) return;
        IsBusy = true;
        if (question != "resumen") Messages.Add(new ChatMessage(true, question));
        try
        {
            var reply = await _api.AskAsync(Symbol, question);
            if (reply is not null)
            {
                Messages.Add(new ChatMessage(false, reply.Answer));
                Suggestions.Clear();
                foreach (var s in reply.Suggestions) Suggestions.Add(s);
            }
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage(false, LocalizationResourceManager.Instance.Format("Assist_ErrorReply", ex.Message)));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
