using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Connect;

public partial class ConnectPageView : WpfUserControl
{
    private readonly Action _createNewCode;
    private readonly Func<DateTimeOffset> _getCurrentTime;
    private readonly DateTimeOffset _refreshAt;
    private readonly DispatcherTimer _countdownTimer;
    private bool _refreshRequested;
    private bool _timerReleased;

    public ConnectPageView(
        BitmapSource qrCode,
        string status,
        string pairingLink,
        string hostUrl,
        string selectedIp,
        string selectedPort,
        string? addressWarning,
        string? portWarning,
        DateTimeOffset refreshAt,
        Action createNewCode,
        Action copyLink,
        Func<DateTimeOffset>? getCurrentTime = null)
    {
        InitializeComponent();
        _createNewCode = createNewCode;
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
        StatusCard.Value = status;
        PairingLinkCard.Value = pairingLink;
        HostUrlCard.Value = hostUrl;
        SelectedIpCard.Value = selectedIp;
        SelectedPortCard.Value = selectedPort;
        SetNotice(AddressWarningNotice, AddressWarningText, addressWarning);
        SetNotice(PortWarningNotice, PortWarningText, portWarning);
        RenderCountdown(_getCurrentTime());
        NewCodeButton.Click += (_, _) => RequestNewCode();
        CopyLinkButton.Click += (_, _) => copyLink();
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

    private static void SetNotice(FrameworkElement notice, TextBlock textBlock, string? message)
    {
        textBlock.Text = message ?? string.Empty;
        notice.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
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
