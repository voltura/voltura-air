using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using WpfControl = System.Windows.Controls.Control;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Connection;

public partial class ConnectionPlayFlowButton : WpfUserControl
{
    private Storyboard? _glowStoryboard;

    public ConnectionPlayFlowButton()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    internal event RoutedEventHandler Click
    {
        add => PlayFlowButton.Click += value;
        remove => PlayFlowButton.Click -= value;
    }

    internal double GlowAngle => PlayFlowGlowEdgeRotation.Angle;

    internal bool AnimationRunning => _glowStoryboard is not null;

    internal bool ColorGlowVisible =>
        PlayFlowOuterGlow.Opacity > 0 && PlayFlowEdgeGlow.Opacity > 0;

    internal void StartAnimation()
    {
        if (_glowStoryboard is not null || !IsLoaded)
        {
            return;
        }

        PlayFlowOuterGlow.Opacity = 1;
        PlayFlowEdgeGlow.Opacity = 0.95;
        PlayFlowButton.ClearValue(UIElement.FocusableProperty);
        PlayFlowButton.ClearValue(WpfControl.BorderBrushProperty);
        _glowStoryboard = (Storyboard)FindResource("PlayFlowEdgeGlowStoryboard");
        _glowStoryboard.Begin(
            this,
            HandoffBehavior.SnapshotAndReplace,
            isControllable: true);
    }

    internal void StopAnimation()
    {
        PlayFlowOuterGlow.Opacity = 0;
        PlayFlowEdgeGlow.Opacity = 0;
        if (PlayFlowButton.IsKeyboardFocusWithin)
        {
            Keyboard.ClearFocus();
        }

        PlayFlowButton.SetCurrentValue(UIElement.FocusableProperty, false);
        PlayFlowButton.SetResourceReference(WpfControl.BorderBrushProperty, "BorderBrush");
        _glowStoryboard?.Remove(this);
        _glowStoryboard = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs) => StartAnimation();

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs) => StopAnimation();
}
