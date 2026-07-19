using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VolturaAir.Host.Ui;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Connect;

public partial class ConnectPageView : WpfUserControl
{
    private readonly Action _createNewCode;
    private readonly Action _copyLink;
    private readonly Action _changeAdapter;
    private readonly Func<DateTimeOffset> _getCurrentTime;
    private readonly DateTimeOffset _refreshAt;
    private readonly DispatcherTimer _countdownTimer;
    private bool _refreshRequested;
    private bool _timerReleased;

    public ConnectPageView(
        BitmapSource qrCode,
        string pairingLink,
        string hostUrl,
        string selectedAdapter,
        bool showSelectedAdapter,
        string selectedIp,
        string selectedPort,
        string? addressWarning,
        string? addressWarningEmphasis,
        string? portWarning,
        DateTimeOffset refreshAt,
        Action createNewCode,
        Action copyLink,
        Action changeAdapter,
        Func<DateTimeOffset>? getCurrentTime = null)
    {
        InitializeComponent();
        _createNewCode = createNewCode;
        _copyLink = copyLink;
        _changeAdapter = changeAdapter;
        _getCurrentTime = getCurrentTime ?? GetCurrentTime;
        _refreshAt = refreshAt;
        _countdownTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += OnCountdownTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
        QrCodeImage.Source = qrCode;
        PairingLinkCard.Value = pairingLink;
        HostUrlCard.Value = hostUrl;
        SelectedAdapterCard.Value = selectedAdapter;
        SelectedAdapterCard.Visibility = showSelectedAdapter || !string.IsNullOrWhiteSpace(addressWarning)
            ? Visibility.Visible
            : Visibility.Collapsed;
        AdapterChangeButton.Visibility = showSelectedAdapter ? Visibility.Visible : Visibility.Collapsed;
        SelectedIpCard.Value = selectedIp;
        SelectedPortCard.Value = selectedPort;
        AddressWarningNotice.SetMessage(addressWarning, addressWarningEmphasis);
        SetNotice(PortWarningNotice, PortWarningText, portWarning);
        RenderCountdown(_getCurrentTime());
    }

    internal static string FormatRefreshCountdown(DateTimeOffset refreshAt, DateTimeOffset now)
    {
        var secondsRemaining = Math.Max(0, (int)Math.Ceiling((refreshAt - now).TotalSeconds));
        return $"Refreshes in {secondsRemaining / 60}:{secondsRemaining % 60:00}";
    }

    internal void ProcessCountdown()
    {
        if (_timerReleased || _refreshRequested)
        {
            return;
        }

        var now = _getCurrentTime();
        RenderCountdown(now);
        if (now >= _refreshAt)
        {
            RequestNewCode();
        }
    }

    private static DateTimeOffset GetCurrentTime() => DateTimeOffset.UtcNow;

    internal AdapterWarningNotice AddressWarningNotice => (SelectedAdapterCard.Actions as SpacingStackPanel)?.Children
        .OfType<AdapterWarningNotice>()
        .SingleOrDefault()
        ?? throw new InvalidOperationException("The network adapter warning notice is unavailable.");

    private System.Windows.Controls.Button AdapterChangeButton => (SelectedAdapterCard.Actions as SpacingStackPanel)?.Children
        .OfType<System.Windows.Controls.Button>()
        .SingleOrDefault()
        ?? throw new InvalidOperationException("The network adapter change action is unavailable.");

    internal TextBlock AddressWarningText => AddressWarningNotice.Text;

    private static void SetNotice(
        FrameworkElement notice,
        TextBlock textBlock,
        string? message,
        string? emphasis = null)
    {
        textBlock.Inlines.Clear();
        if (string.IsNullOrWhiteSpace(message))
        {
            notice.Visibility = Visibility.Collapsed;
            return;
        }

        notice.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(emphasis))
        {
            textBlock.Inlines.Add(new Run(message));
            return;
        }

        var emphasisIndex = message.IndexOf(emphasis, StringComparison.Ordinal);
        if (emphasisIndex < 0)
        {
            textBlock.Inlines.Add(new Run(message));
            return;
        }

        if (emphasisIndex > 0)
        {
            textBlock.Inlines.Add(new Run(message[..emphasisIndex]));
        }

        textBlock.Inlines.Add(new Run(emphasis) { FontWeight = FontWeights.Bold });
        var suffixIndex = emphasisIndex + emphasis.Length;
        if (suffixIndex < message.Length)
        {
            textBlock.Inlines.Add(new Run(message[suffixIndex..]));
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (IsVisible)
        {
            StartCountdown();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        ReleaseTimer();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (_timerReleased)
        {
            return;
        }

        if (IsVisible)
        {
            StartCountdown();
        }
        else
        {
            _countdownTimer.Stop();
        }
    }

    private void OnCountdownTick(object? sender, EventArgs eventArgs)
    {
        ProcessCountdown();
    }

    private void OnNewCodeClicked(object sender, RoutedEventArgs eventArgs)
    {
        RequestNewCode();
    }

    private void OnCopyLinkClicked(object sender, RoutedEventArgs eventArgs)
    {
        _copyLink();
    }

    private void OnChangeAdapterClicked(object sender, RoutedEventArgs eventArgs)
    {
        _changeAdapter();
    }

    private void StartCountdown()
    {
        ProcessCountdown();
        if (!_timerReleased && !_refreshRequested)
        {
            _countdownTimer.Start();
        }
    }

    private void RenderCountdown(DateTimeOffset now)
    {
        PairingCodeCard.Value = FormatRefreshCountdown(_refreshAt, now);
    }

    private void RequestNewCode()
    {
        if (_refreshRequested)
        {
            return;
        }

        _refreshRequested = true;
        _countdownTimer.Stop();
        PairingCodeCard.Value = "Refreshing code…";
        _createNewCode();
    }

    private void ReleaseTimer()
    {
        if (_timerReleased)
        {
            return;
        }

        _timerReleased = true;
        _countdownTimer.Stop();
        _countdownTimer.Tick -= OnCountdownTick;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        IsVisibleChanged -= OnIsVisibleChanged;
    }
}
