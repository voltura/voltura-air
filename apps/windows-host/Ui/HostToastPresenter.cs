using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Panel = System.Windows.Controls.Panel;

namespace VolturaAir.Host.Ui;

internal enum HostToastTone
{
    Success,
    Failure
}

internal sealed class HostToastLiveRegion : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new HostToastAutomationPeer(this);

    public void Announce()
    {
        UIElementAutomationPeer.CreatePeerForElement(this)?
            .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private sealed class HostToastAutomationPeer(HostToastLiveRegion owner) : FrameworkElementAutomationPeer(owner);
}

internal sealed class HostToastPresenter : IDisposable
{
    private readonly Grid _root;
    private readonly HostVisualFactory _visuals;
    private readonly Func<string> _defaultTitle;
    private readonly DispatcherTimer _timer;
    private HostToastLiveRegion? _toast;
    private bool _disposed;

    public HostToastPresenter(Grid root, HostVisualFactory visuals, Func<string> defaultTitle)
    {
        _root = root;
        _visuals = visuals;
        _defaultTitle = defaultTitle;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
        _timer.Tick += OnTimerTick;
        _root.IsVisibleChanged += OnRootIsVisibleChanged;
    }

    public void Show(string message, string? title = null, HostToastTone tone = HostToastTone.Success)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RemoveToast();

        _toast = CreateToast(title ?? _defaultTitle(), message, tone);
        Grid.SetRow(_toast, 2);
        Panel.SetZIndex(_toast, 50);
        _root.Children.Add(_toast);
        _toast.Announce();
        if (_root.IsVisible)
        {
            _timer.Start();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _root.IsVisibleChanged -= OnRootIsVisibleChanged;
        RemoveToast();
    }

    private HostToastLiveRegion CreateToast(string title, string message, HostToastTone tone)
    {
        var isFailure = tone == HostToastTone.Failure;
        var toneBrush = _visuals.Brush(isFailure ? "DangerBrush" : "AccentBrush");
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var accentStrip = new Border
        {
            Background = toneBrush,
            CornerRadius = new CornerRadius(4, 0, 0, 4)
        };
        layout.Children.Add(accentStrip);

        var badge = new Border
        {
            Width = 22,
            Height = 22,
            Margin = new Thickness(14, 12, 10, 12),
            CornerRadius = new CornerRadius(11),
            Background = toneBrush,
            Child = new TextBlock
            {
                Text = isFailure ? "!" : "\u2713",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = isFailure ? _visuals.Brush("SurfaceBrush") : _visuals.Brush("AccentTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(badge, 1);
        layout.Children.Add(badge);

        var text = new SpacingStackPanel
        {
            Margin = new Thickness(0, 10, 16, 10),
            Spacing = UiTokens.Space2xs,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = _visuals.Brush("MutedTextBrush")
        });
        text.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = _visuals.Brush("TextBrush")
        });
        Grid.SetColumn(text, 2);
        layout.Children.Add(text);

        var toast = new HostToastLiveRegion
        {
            Tag = tone,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = _visuals.Brush("SurfaceBrush"),
            BorderBrush = _visuals.Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, UiTokens.SpaceLg),
            MinWidth = 260,
            MaxWidth = 380,
            Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                Direction = 270,
                Opacity = 0.32,
                ShadowDepth = 8,
                Color = Colors.Black
            },
            Child = layout
        };
        AutomationProperties.SetName(toast, $"{title}: {message}");
        AutomationProperties.SetItemStatus(toast, isFailure ? "Error" : "Success");
        AutomationProperties.SetLiveSetting(toast, isFailure ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        return toast;
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        RemoveToast();
    }

    private void OnRootIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (!_disposed && _toast is not null && _root.IsVisible)
        {
            _toast.Announce();
            _timer.Stop();
            _timer.Start();
        }
    }

    private void RemoveToast()
    {
        _timer.Stop();
        if (_toast is null)
        {
            return;
        }

        _root.Children.Remove(_toast);
        _toast = null;
    }
}
