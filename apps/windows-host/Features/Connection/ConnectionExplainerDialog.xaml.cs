using System.Windows;
using System.Windows.Controls.Primitives;

namespace VolturaAir.Host.Features.Connection;

public partial class ConnectionExplainerDialog : Window
{
    private bool _enhancedCapabilitiesEnabled;
    private bool _synchronizingSelection;

    private ConnectionExplainerDialog(
        Window owner,
        ConnectionTransportMode initialMethod,
        bool enhancedCapabilitiesEnabled)
    {
        _enhancedCapabilitiesEnabled = enhancedCapabilitiesEnabled;
        InitializeComponent();
        Owner = owner;
        WpfTheme.Apply(this);
        WpfTheme.TrackAccessibilityChanges(this, static () => { });
        RouteDiagramView.EnhancedRouteChanged += OnEnhancedRouteChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
        SelectMethod(initialMethod, animate: false);
    }

    internal ConnectionTransportMode SelectedMethod { get; private set; }

    internal bool AnimationsRunning => RouteDiagramView.AnimationsRunning;

    internal ConnectionRouteDiagram Diagram => RouteDiagramView;

    internal static void Show(
        Window owner,
        ConnectionTransportMode initialMethod,
        bool enhancedCapabilitiesEnabled) =>
        _ = new ConnectionExplainerDialog(
            owner,
            initialMethod,
            enhancedCapabilitiesEnabled).ShowDialog();

    internal static ConnectionExplainerDialog CreateForTest(
        Window owner,
        ConnectionTransportMode initialMethod,
        bool enhancedCapabilitiesEnabled) =>
        new(owner, initialMethod, enhancedCapabilitiesEnabled);

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        Loaded -= OnLoaded;
        RouteDiagramView.StartAnimations();
        (SelectedMethod == ConnectionTransportMode.Relay
            ? RelayMethodButton
            : DirectMethodButton).Focus();
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        Closed -= OnClosed;
        RouteDiagramView.EnhancedRouteChanged -= OnEnhancedRouteChanged;
        RouteDiagramView.StopAllAnimations();
    }

    private void OnEnhancedRouteChanged(bool enabled) =>
        _enhancedCapabilitiesEnabled = enabled;

    private void OnMethodClicked(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is ToggleButton button && button.IsChecked != true)
        {
            button.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        }
    }

    private void OnMethodChecked(object sender, RoutedEventArgs eventArgs)
    {
        if (_synchronizingSelection)
        {
            return;
        }

        SelectMethod(
            ReferenceEquals(sender, RelayMethodButton)
                ? ConnectionTransportMode.Relay
                : ConnectionTransportMode.DirectLan,
            animate: IsLoaded);
    }

    private void SelectMethod(ConnectionTransportMode method, bool animate)
    {
        SelectedMethod = method;
        var usesRelay = method == ConnectionTransportMode.Relay;
        _synchronizingSelection = true;
        DirectMethodButton.SetCurrentValue(ToggleButton.IsCheckedProperty, !usesRelay);
        RelayMethodButton.SetCurrentValue(ToggleButton.IsCheckedProperty, usesRelay);
        _synchronizingSelection = false;

        RouteDiagramView.ShowRoute(
            method,
            _enhancedCapabilitiesEnabled,
            animate);
    }
}
