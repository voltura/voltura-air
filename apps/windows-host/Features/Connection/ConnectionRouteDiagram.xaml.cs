using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Connection;

public partial class ConnectionRouteDiagram : WpfUserControl
{
    private static readonly PathGeometry PacketRoute = CreatePacketRoute();

    private Storyboard? _activeStoryboard;
    private ConnectionTransportMode _method;
    private bool _enhancedCapabilitiesEnabled;
    private bool _synchronizingEnhancedRouteToggle;

    public ConnectionRouteDiagram()
    {
        InitializeComponent();
        EnhancedRouteToggle.Checked += OnEnhancedRouteToggled;
        EnhancedRouteToggle.Unchecked += OnEnhancedRouteToggled;
        PlayFlowAction.Click += OnPlayFlowClicked;
    }

    internal event Action<bool>? EnhancedRouteChanged;

    internal bool AnimationsRunning { get; private set; }

    internal bool ShowsInitialLoadStage =>
        InitialLoadStagePanel.Visibility == Visibility.Visible;

    internal bool ShowsEnhancedRoute => EnhancedRouteToggle.IsChecked == true;

    internal bool ShowsMainStageLabel => MainStageLabel.Visibility == Visibility.Visible;

    internal string MainStageLabelText => MainStageLabel.Text;

    internal double InitialGlowOffset => Canvas.GetLeft(InitialLoadTrackGlow);

    internal double MainGlowOffset => Canvas.GetLeft(MainRouteTrackGlow);

    internal int ActiveInitialLoadPassCount { get; private set; }

    internal int ActiveMainRoutePassCount { get; private set; }

    internal double PlayButtonGlowAngle => PlayFlowAction.GlowAngle;

    internal bool PlayButtonGlowRunning => PlayFlowAction.AnimationRunning;

    internal bool PlayButtonColorGlowVisible => PlayFlowAction.ColorGlowVisible;

    internal void ShowRoute(
        ConnectionTransportMode method,
        bool enhancedCapabilitiesEnabled,
        bool animate)
    {
        StopAnimations();
        _method = method;
        _enhancedCapabilitiesEnabled = enhancedCapabilitiesEnabled;
        var usesRelay = method == ConnectionTransportMode.Relay;
        var showsInitialLoad = !usesRelay && enhancedCapabilitiesEnabled;

        EnhancedRouteTogglePanel.Visibility = usesRelay
            ? Visibility.Collapsed
            : Visibility.Visible;
        _synchronizingEnhancedRouteToggle = true;
        EnhancedRouteToggle.SetCurrentValue(
            System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            enhancedCapabilitiesEnabled);
        _synchronizingEnhancedRouteToggle = false;

        InitialLoadStagePanel.Visibility = showsInitialLoad
            ? Visibility.Visible
            : Visibility.Collapsed;
        MainStageLabel.Visibility = showsInitialLoad
            ? Visibility.Visible
            : Visibility.Collapsed;
        MainStageLabel.Text = showsInitialLoad
            ? "2  NORMAL USE — LOCAL ROUTE"
            : "1  NORMAL USE — LOCAL ROUTE";
        HomeNetworkIcon.Visibility = usesRelay ? Visibility.Collapsed : Visibility.Visible;
        VolturaCloudIcon.Visibility = usesRelay ? Visibility.Visible : Visibility.Collapsed;
        MiddleRouteTitle.Text = usesRelay ? "Voltura cloud" : "Home network";
        MiddleRouteSubtitle.Text = usesRelay ? "Internet" : "Router or network";
        AutomationProperties.SetName(
            MiddleRouteNode,
            usesRelay ? "Voltura cloud over the Internet" : "Home network or router");
        AutomationProperties.SetName(
            this,
            usesRelay
                ? "Phone or tablet connects through the Voltura cloud to the PC."
                : showsInitialLoad
                    ? "First, the device loads the secure web app from voltura dot se. Then normal communication travels through the home network to the PC."
                    : "Phone or tablet connects through the home network to the PC.");
        RouteExplanationText.Text = usesRelay
            ? "Your device and PC communicate securely through Voltura over the Internet. This works away from home and when a direct local connection is unavailable."
            : showsInitialLoad
                ? "The Internet is used for the initial secure web-app load. Normal controller communication then stays on your home network."
                : "Your device and PC communicate directly through the same home network.";

        if (animate)
        {
            StartAnimations();
        }
    }

    internal void StartAnimations(bool ignoreReducedMotion = false)
    {
        StopAnimations();
        if (!IsLoaded ||
            (!ignoreReducedMotion &&
             (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)))
        {
            return;
        }

        PlayFlowAction.StopAnimation();
        var storyboard = new Storyboard();
        if (_method == ConnectionTransportMode.DirectLan && _enhancedCapabilitiesEnabled)
        {
            ActiveInitialLoadPassCount = 1;
            ActiveMainRoutePassCount = 2;
            AddPacketPass(
                storyboard,
                InitialLoadPacket,
                InitialLoadPacketTransform,
                InitialLoadTrackGlow,
                TimeSpan.FromMilliseconds(150),
                TimeSpan.FromMilliseconds(600));
            AddPacketPass(
                storyboard,
                MainRoutePacket,
                MainRoutePacketTransform,
                MainRouteTrackGlow,
                TimeSpan.FromMilliseconds(1500),
                TimeSpan.FromMilliseconds(850));
            AddPacketPass(
                storyboard,
                MainRoutePacket,
                MainRoutePacketTransform,
                MainRouteTrackGlow,
                TimeSpan.FromMilliseconds(3400),
                TimeSpan.FromMilliseconds(850));
        }
        else
        {
            ActiveInitialLoadPassCount = 0;
            ActiveMainRoutePassCount = 2;
            AddPacketPass(
                storyboard,
                MainRoutePacket,
                MainRoutePacketTransform,
                MainRouteTrackGlow,
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(1100));
            AddPacketPass(
                storyboard,
                MainRoutePacket,
                MainRoutePacketTransform,
                MainRouteTrackGlow,
                TimeSpan.FromMilliseconds(2600),
                TimeSpan.FromMilliseconds(1100));
        }

        storyboard.Completed += (_, _) =>
        {
            if (ReferenceEquals(_activeStoryboard, storyboard))
            {
                StopAnimations();
            }
        };
        _activeStoryboard = storyboard;
        AnimationsRunning = true;
        storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    internal void StopAnimations()
    {
        _activeStoryboard?.Remove(this);
        _activeStoryboard = null;
        InitialLoadPacket.Opacity = 0;
        MainRoutePacket.Opacity = 0;
        InitialLoadTrackGlow.Opacity = 0;
        MainRouteTrackGlow.Opacity = 0;
        InitialLoadTrackGlow.SetCurrentValue(Canvas.LeftProperty, 95d);
        MainRouteTrackGlow.SetCurrentValue(Canvas.LeftProperty, 95d);
        ActiveInitialLoadPassCount = 0;
        ActiveMainRoutePassCount = 0;
        AnimationsRunning = false;
        PlayFlowAction.StartAnimation();
    }

    internal void StopAllAnimations()
    {
        StopAnimations();
        PlayFlowAction.StopAnimation();
    }

    private void OnEnhancedRouteToggled(object sender, RoutedEventArgs eventArgs)
    {
        if (_synchronizingEnhancedRouteToggle || _method != ConnectionTransportMode.DirectLan)
        {
            return;
        }

        var enabled = EnhancedRouteToggle.IsChecked == true;
        ShowRoute(ConnectionTransportMode.DirectLan, enabled, animate: IsLoaded);
        EnhancedRouteChanged?.Invoke(enabled);
    }

    private void OnPlayFlowClicked(object sender, RoutedEventArgs eventArgs) =>
        StartAnimations(ignoreReducedMotion: true);

    private static void AddPacketPass(
        Storyboard storyboard,
        FrameworkElement packet,
        MatrixTransform transform,
        FrameworkElement trackGlow,
        TimeSpan beginTime,
        TimeSpan travelTime)
    {
        var motion = new MatrixAnimationUsingPath
        {
            PathGeometry = PacketRoute,
            BeginTime = beginTime,
            Duration = new Duration(travelTime),
            AutoReverse = true,
            DoesRotateWithTangent = false,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(motion, transform);
        Storyboard.SetTargetProperty(
            motion,
            new PropertyPath(MatrixTransform.MatrixProperty));
        storyboard.Children.Add(motion);

        var trackGlowMotion = new DoubleAnimation(95, 455, new Duration(travelTime))
        {
            BeginTime = beginTime,
            AutoReverse = true,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(trackGlowMotion, trackGlow);
        Storyboard.SetTargetProperty(
            trackGlowMotion,
            new PropertyPath(Canvas.LeftProperty));
        storyboard.Children.Add(trackGlowMotion);

        var opacity = new DoubleAnimation(1, 1, new Duration(travelTime + travelTime))
        {
            BeginTime = beginTime,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(opacity, packet);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(opacity);

        var trackGlowOpacity = new DoubleAnimation(1, 1, new Duration(travelTime + travelTime))
        {
            BeginTime = beginTime,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(trackGlowOpacity, trackGlow);
        Storyboard.SetTargetProperty(trackGlowOpacity, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(trackGlowOpacity);
    }

    private static PathGeometry CreatePacketRoute()
    {
        var geometry = PathGeometry.CreateFromGeometry(
            Geometry.Parse("M120,42 L480,42"));
        geometry.Freeze();
        return geometry;
    }
}
