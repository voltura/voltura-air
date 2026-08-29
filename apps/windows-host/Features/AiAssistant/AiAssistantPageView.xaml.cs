using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VolturaAir.Host.Ui;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfButton = System.Windows.Controls.Button;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfPanel = System.Windows.Controls.Panel;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.AiAssistant;

public partial class AiAssistantPageView : WpfUserControl, IDisposable
{
    private static readonly string[] WorkingPhrases =
    [
        "Working…", "Checking…", "Looking it up…", "Thinking…", "Investigating…",
        "Putting the answer together…"
    ];
    private readonly DispatcherTimer _workingTimer = new() { Interval = TimeSpan.FromMilliseconds(2400) };
    private readonly IUrlOpenService _urlOpenService;
    private int _workingPhrase;
    private bool _opened;
    private bool _working;
    private bool _pending;

    internal AiAssistantPageView(IUrlOpenService? urlOpenService = null)
    {
        InitializeComponent();
        _urlOpenService = urlOpenService ?? new UrlOpenService();
        _workingTimer.Tick += OnWorkingTimerTick;
    }

    internal event Action? RetryRequested;
    internal event Action? SendRequested;
    internal event Action? NewConversationRequested;
    internal string Question => QuestionTextBox.Text;

    internal void SetOpening()
    {
        StatePanel.Visibility = Visibility.Visible;
        ConversationScroller.Visibility = Visibility.Collapsed;
        StateTitleText.Text = "Opening AI Assistant";
        StateMessageText.Text = string.Empty;
        RetryButton.Visibility = Visibility.Collapsed;
        SetInteraction(opened: false, working: false, pending: true);
        StatusText.Text = "Opening…";
    }

    internal void ShowUnavailable(string title, string message)
    {
        StatePanel.Visibility = Visibility.Visible;
        ConversationScroller.Visibility = Visibility.Collapsed;
        StateTitleText.Text = title;
        StateMessageText.Text = message;
        RetryButton.Visibility = Visibility.Visible;
        SetInteraction(opened: false, working: false, pending: false);
        StatusText.Text = string.Empty;
    }

    internal void ShowConversation(IReadOnlyList<AiAssistantConversationMessage> messages)
    {
        StatePanel.Visibility = Visibility.Collapsed;
        ConversationScroller.Visibility = Visibility.Visible;
        ConversationPanel.Children.Clear();
        if (messages.Count == 0 && !_working)
        {
            ConversationPanel.Children.Add(CreateWelcome());
        }
        foreach (AiAssistantConversationMessage message in messages)
        {
            ConversationPanel.Children.Add(CreateMessage(message));
        }
        if (_working)
        {
            ConversationPanel.Children.Add(CreateWorkingIndicator());
        }
        Dispatcher.BeginInvoke(ScrollToEnd, DispatcherPriority.Loaded);
    }

    internal void SetReady(string status = "Ready")
    {
        SetInteraction(opened: true, working: false, pending: false);
        StatusText.Text = status;
    }

    internal void SetPending(string status)
    {
        SetInteraction(opened: _opened, working: _working, pending: true);
        StatusText.Text = status;
    }

    internal void SetWorking(IReadOnlyList<AiAssistantConversationMessage> messages)
    {
        _workingPhrase = 0;
        SetInteraction(opened: true, working: true, pending: false);
        StatusText.Text = WorkingPhrases[0];
        ShowConversation(messages);
    }

    internal void SetFailure(string message)
    {
        SetInteraction(opened: _opened, working: false, pending: false);
        StatusText.Text = message;
    }

    internal void ClearQuestion() => QuestionTextBox.Clear();

    private void SetInteraction(bool opened, bool working, bool pending)
    {
        _opened = opened;
        _working = working;
        _pending = pending;
        QuestionTextBox.IsEnabled = opened && !working && !pending;
        SendButton.IsEnabled = QuestionTextBox.IsEnabled && !string.IsNullOrWhiteSpace(QuestionTextBox.Text);
        NewConversationButton.IsEnabled = opened && !working && !pending;
        if (working && !_workingTimer.IsEnabled) _workingTimer.Start();
        else if (!working) _workingTimer.Stop();
    }

    private SpacingStackPanel CreateWelcome()
    {
        var panel = new SpacingStackPanel
        {
            Spacing = 10,
            MaxWidth = 520,
            Margin = new Thickness(20, 36, 20, 20),
            HorizontalAlignment = WpfHorizontalAlignment.Center
        };
        panel.Children.Add(Text("Ask the Voltura Air Assistant", 20, FontWeights.SemiBold, TextAlignment.Center));
        panel.Children.Add(Text("Get help with Voltura Air and information available on your PC.", 14, FontWeights.Normal, TextAlignment.Center, muted: true));
        panel.Children.Add(Text(
            "This is a powerful, read-only tool. It can read information with the same Windows-user access available when you use Codex locally on this PC. The conversation is stored by Codex on your PC.",
            13,
            FontWeights.Normal,
            TextAlignment.Center,
            muted: true));
        var suggestions = new WrapPanel { HorizontalAlignment = WpfHorizontalAlignment.Center };
        AddSuggestion(suggestions, "Top features", "What are the top features of Voltura Air?");
        AddSuggestion(suggestions, "Test Phone webcam", "How do I test Phone webcam before first use?");
        AddSuggestion(suggestions, "Direct or Relay?", "What is the difference between Direct and Relay?");
        panel.Children.Add(suggestions);
        return panel;
    }

    private void AddSuggestion(WpfPanel panel, string label, string question)
    {
        var button = new WpfButton { Content = label, Margin = new Thickness(4), Padding = new Thickness(10, 6, 10, 6) };
        button.Click += (_, _) =>
        {
            QuestionTextBox.Text = question;
            QuestionTextBox.Focus();
            QuestionTextBox.CaretIndex = question.Length;
            RefreshSendButton();
        };
        panel.Children.Add(button);
    }

    private Border CreateMessage(AiAssistantConversationMessage message)
    {
        var content = new SpacingStackPanel { Spacing = 6 };
        content.Children.Add(Text(message.Sender == "user" ? "You" : "Assistant", 12, FontWeights.SemiBold));
        if (message.Sender == "assistant")
        {
            content.Children.Add(AiAssistantMarkdownRenderer.Create(message.Text, _urlOpenService));
        }
        else
        {
            content.Children.Add(Text(message.Text, 14, FontWeights.Normal));
        }
        var border = new Border
        {
            MaxWidth = 640,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = content,
            HorizontalAlignment = message.Sender == "user" ? WpfHorizontalAlignment.Right : WpfHorizontalAlignment.Left
        };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
        border.SetResourceReference(Border.BorderBrushProperty, message.Sender == "user" ? "AccentBrush" : "BorderBrush");
        return border;
    }

    private TextBlock CreateWorkingIndicator() =>
        Text(WorkingPhrases[_workingPhrase], 13, FontWeights.Normal, TextAlignment.Left, muted: true);

    private static TextBlock Text(
        string value,
        double size,
        FontWeight weight,
        TextAlignment alignment = TextAlignment.Left,
        bool muted = false)
    {
        var text = new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = weight,
            TextAlignment = alignment,
            TextWrapping = TextWrapping.Wrap
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedTextBrush" : "TextBrush");
        return text;
    }

    private void OnWorkingTimerTick(object? sender, EventArgs e)
    {
        if (!_working)
        {
            _workingTimer.Stop();
            return;
        }
        _workingPhrase = (_workingPhrase + 1) % WorkingPhrases.Length;
        StatusText.Text = WorkingPhrases[_workingPhrase];
        if (ConversationPanel.Children.Count > 0 &&
            ConversationPanel.Children[^1] is TextBlock indicator)
        {
            indicator.Text = WorkingPhrases[_workingPhrase];
        }
    }

    private void OnRetryClicked(object sender, RoutedEventArgs e) => RetryRequested?.Invoke();
    private void OnSendClicked(object sender, RoutedEventArgs e) => RequestSend();
    private void OnNewConversationClicked(object sender, RoutedEventArgs e) => NewConversationRequested?.Invoke();

    private void OnQuestionPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        Dispatcher.BeginInvoke(RefreshSendButton, DispatcherPriority.Input);
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            RequestSend();
        }
    }

    private void OnQuestionTextChanged(object sender, TextChangedEventArgs e) => RefreshSendButton();

    private void RequestSend()
    {
        RefreshSendButton();
        if (SendButton.IsEnabled) SendRequested?.Invoke();
    }

    private void RefreshSendButton() =>
        SendButton.IsEnabled = _opened && !_working && !_pending && !string.IsNullOrWhiteSpace(QuestionTextBox.Text);

    private void ScrollToEnd() => ConversationScroller.ScrollToEnd();

    public void Dispose()
    {
        _workingTimer.Stop();
        _workingTimer.Tick -= OnWorkingTimerTick;
        GC.SuppressFinalize(this);
    }
}

internal sealed record AiAssistantConversationMessage(string Id, string Sender, string Text);
