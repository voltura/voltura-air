using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfExpander = System.Windows.Controls.Expander;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Connection;

public partial class ConnectionPageView : WpfUserControl
{
    private bool _suppressCandidateSelection;
    private bool _suppressControlEvents;

    internal ConnectionPageView(
        Action openAdapterChooser,
        Action useAutomaticAdapter,
        Action refreshAdapters,
        Action cancelAdapterChooser,
        Action<ConnectionTransportMode> setTransportMode,
        Action<RelayScreenQuality> setRelayScreenQuality,
        Action<string> setCustomRelayEndpoint,
        Action<bool> setUseCustomPort,
        Action<bool> setAdvancedExpanded,
        Action cancelChanges,
        Action saveAndRestart,
        Action? retryRelay = null,
        Action? refreshRelayUsage = null,
        Action<bool>? setEnhancedCapabilitiesEnabled = null)
    {
        InitializeComponent();
        ChooseAdapterButton.Click += (_, _) => openAdapterChooser();
        ReturnAutomaticAdapterButton.Click += (_, _) => useAutomaticAdapter();
        RefreshAdaptersButton.Click += (_, _) => refreshAdapters();
        CancelAdapterChooserButton.Click += (_, _) => cancelAdapterChooser();
        DirectLanRadioButton.Checked += (_, _) => RunUserAction(() => setTransportMode(ConnectionTransportMode.DirectLan));
        RelayRadioButton.Checked += (_, _) => RunUserAction(() => setTransportMode(ConnectionTransportMode.Relay));
        EnhancedCapabilitiesCheckBox.Checked += (_, _) => RunUserAction(() => setEnhancedCapabilitiesEnabled?.Invoke(true));
        EnhancedCapabilitiesCheckBox.Unchecked += (_, _) => RunUserAction(() => setEnhancedCapabilitiesEnabled?.Invoke(false));
        RelayStandardRadioButton.Checked += (_, _) => RunUserAction(() => setRelayScreenQuality(RelayScreenQuality.Standard));
        RelayDataSaverRadioButton.Checked += (_, _) => RunUserAction(() => setRelayScreenQuality(RelayScreenQuality.DataSaver));
        RelayHighRadioButton.Checked += (_, _) => RunUserAction(() => setRelayScreenQuality(RelayScreenQuality.High));
        CustomRelayEndpointTextBox.TextChanged += (_, _) => RunUserAction(() => setCustomRelayEndpoint(CustomRelayEndpointTextBox.Text));
        UseSpecificPortCheckBox.Checked += (_, _) => RunUserAction(() => setUseCustomPort(true));
        UseSpecificPortCheckBox.Unchecked += (_, _) => RunUserAction(() => setUseCustomPort(false));
        AdvancedConnectionExpander.Expanded += (_, _) => RunUserAction(() => setAdvancedExpanded(true));
        AdvancedConnectionExpander.Collapsed += (_, _) => RunUserAction(() => setAdvancedExpanded(false));
        CancelChangesButton.Click += (_, _) => cancelChanges();
        SaveRestartButton.Click += (_, _) => saveAndRestart();
        RetryRelayButton.Click += (_, _) => retryRelay?.Invoke();
        RefreshRelayUsageButton.Click += (_, _) => refreshRelayUsage?.Invoke();
        OpenConnectionExplainerButton.Click += (_, _) =>
        {
            if (Window.GetWindow(this) is { } owner)
            {
                ConnectionExplainerDialog.Show(
                    owner,
                    CurrentTransportMode,
                    CurrentEnhancedCapabilitiesEnabled);
            }
        };
    }

    internal string RelayConnectionStatus
    {
        set => RelayConnectionStatusText.Text = value;
    }

    internal string RelayUsage
    {
        set
        {
            RelayUsageText.Text = value;
            RelayUsageText.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    internal double RelayUsagePercent
    {
        set => RelayUsageProgressBar.Value = value;
    }

    internal string RelayUsageSummary
    {
        set
        {
            RelayUsageSummaryText.Text = value;
            AutomationProperties.SetName(RelayUsageProgressBar, value);
        }
    }

    internal string RelayUsageThresholds
    {
        set => RelayUsageThresholdsText.Text = value;
    }

    internal bool ShowsRelayUsageMeter
    {
        set => RelayUsageMeterPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal bool ShowsRelayRetry
    {
        set => RetryRelayButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal bool ShowsRelayUsageRefresh
    {
        set => RefreshRelayUsageButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal event Action<ConnectionCandidateItem>? CandidateSelected;

    internal ConnectionTransportMode TransportMode
    {
        set
        {
            DirectLanRadioButton.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, value == ConnectionTransportMode.DirectLan);
            RelayRadioButton.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, value == ConnectionTransportMode.Relay);
            RelaySettingsPanel.Visibility = value == ConnectionTransportMode.Relay ? Visibility.Visible : Visibility.Collapsed;
            CurrentConnectionPanel.Visibility = value == ConnectionTransportMode.DirectLan ? Visibility.Visible : Visibility.Collapsed;
            ChooseAdapterButton.Visibility = value == ConnectionTransportMode.DirectLan ? Visibility.Visible : Visibility.Collapsed;
            AdvancedConnectionExpander.Visibility = value == ConnectionTransportMode.DirectLan ? Visibility.Visible : Visibility.Collapsed;
            EnhancedCapabilitiesCheckBox.Visibility = value == ConnectionTransportMode.DirectLan ? Visibility.Visible : Visibility.Collapsed;
            EnhancedCapabilitiesDescriptionText.Visibility = value == ConnectionTransportMode.DirectLan ? Visibility.Visible : Visibility.Collapsed;
            EnhancedCapabilitiesPanel.Visibility = value == ConnectionTransportMode.DirectLan ? Visibility.Visible : Visibility.Collapsed;
            if (value == ConnectionTransportMode.Relay) AdapterChooserPanel.Visibility = Visibility.Collapsed;
        }
    }

    internal ConnectionTransportMode CurrentTransportMode =>
        RelayRadioButton.IsChecked == true
            ? ConnectionTransportMode.Relay
            : ConnectionTransportMode.DirectLan;

    internal bool CurrentEnhancedCapabilitiesEnabled =>
        EnhancedCapabilitiesCheckBox.IsChecked == true;

    internal RelayScreenQuality RelayScreenQuality
    {
        set
        {
            RelayStandardRadioButton.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, value == RelayScreenQuality.Standard);
            RelayDataSaverRadioButton.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, value == RelayScreenQuality.DataSaver);
            RelayHighRadioButton.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, value == RelayScreenQuality.High);
        }
    }

    internal bool EnhancedCapabilitiesEnabled
    {
        set
        {
            EnhancedCapabilitiesCheckBox.SetCurrentValue(WpfCheckBox.IsCheckedProperty, value);
            PortSettingsTitleText.Text = value ? "Standard Local port settings" : "Port settings";
            UpdatePortSettingsAccessibleName();
        }
    }

    internal string CustomRelayEndpoint
    {
        set
        {
            if (!string.Equals(CustomRelayEndpointTextBox.Text, value, StringComparison.Ordinal))
            {
                CustomRelayEndpointTextBox.Text = value;
            }
        }
    }

    internal string RelayEndpointValidation
    {
        set
        {
            RelayEndpointValidationText.Text = value;
            RelayEndpointValidationText.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    internal IReadOnlyList<ConnectionCandidateItem> Candidates
    {
        set
        {
            _suppressCandidateSelection = true;
            NetworkCandidateList.ItemsSource = value;
            NetworkCandidateList.SelectedItem = value.FirstOrDefault(candidate => candidate.IsSelected);
            _suppressCandidateSelection = false;
            AdapterChooserEmptyText.Visibility = value.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NetworkCandidateList.Visibility = value.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    internal string ActiveAdapter
    {
        set => ActiveAdapterText.Text = value;
    }

    internal string ActiveEndpoint
    {
        set => ActiveEndpointText.Text = value;
    }

    internal string ActiveSelectionMode
    {
        set => ActiveSelectionModeText.Text = value;
    }

    internal string ConnectionWarning
    {
        set
        {
            ConnectionWarningText.Text = value;
            ConnectionWarningText.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    internal bool ShowsUnavailableAdapter
    {
        set => PendingAdapterUnavailableText.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal bool ShowsReturnToAutomaticAdapter
    {
        set => ReturnAutomaticAdapterButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal bool IsAdapterChooserOpen
    {
        get => AdapterChooserPanel.Visibility == Visibility.Visible;
        set => AdapterChooserPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal bool IsAdvancedExpanded
    {
        get => AdvancedConnectionExpander.IsExpanded;
        set => AdvancedConnectionExpander.SetCurrentValue(WpfExpander.IsExpandedProperty, value);
    }

    internal bool UsesCustomPort
    {
        get => UseSpecificPortCheckBox.IsChecked == true;
        set
        {
            UseSpecificPortCheckBox.SetCurrentValue(WpfCheckBox.IsCheckedProperty, value);
            ManualPortPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    internal string ManualPort
    {
        get => ManualPortTextBox.Text;
        set
        {
            if (!string.Equals(ManualPortTextBox.Text, value, StringComparison.Ordinal))
            {
                ManualPortTextBox.Text = value;
            }
        }
    }

    internal string PortHeaderStatus
    {
        set
        {
            PortHeaderStatusText.Text = value;
            UpdatePortSettingsAccessibleName();
        }
    }

    private void UpdatePortSettingsAccessibleName() => AutomationProperties.SetName(
        AdvancedConnectionExpander,
        $"{PortSettingsTitleText.Text}, {PortHeaderStatusText.Text}");

    internal string PortValidation
    {
        set
        {
            ManualPortValidationText.Text = value;
            AutomationProperties.SetHelpText(ManualPortTextBox, value);
        }
    }

    internal bool PortValidationIsError
    {
        set => ManualPortValidationText.SetResourceReference(
            ForegroundProperty,
            value ? "DangerBrush" : "MutedTextBrush");
    }

    internal string Feedback
    {
        set
        {
            ConnectionStatusText.Text = value;
            ConnectionStatusText.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    internal bool FeedbackIsError
    {
        set => ConnectionStatusText.SetResourceReference(
            ForegroundProperty,
            value ? "DangerBrush" : "AccentStrongBrush");
    }

    internal bool ShowsActionPanel
    {
        set => PendingActionsPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal string ActionHeading
    {
        set => PendingActionHeadingText.Text = value;
    }

    internal string ActionGuidance
    {
        set
        {
            PendingActionGuidanceText.Text = value;
            PendingActionGuidanceText.Visibility = string.IsNullOrWhiteSpace(value)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    internal string AdapterChange
    {
        set => PendingAdapterChangeText.Text = value;
    }

    internal string ConnectionMethodChange
    {
        set => PendingConnectionMethodChangeText.Text = value;
    }

    internal bool ShowsConnectionMethodChange
    {
        set => PendingConnectionMethodChangePanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal bool ShowsAdapterChange
    {
        set => PendingAdapterChangePanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal string PortChange
    {
        set => PendingPortChangeText.Text = value;
    }

    internal bool ShowsPortChange
    {
        set => PendingPortChangePanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal string AdapterChangeText => PendingAdapterChangeText.Text;

    internal string PortChangeText => PendingPortChangeText.Text;

    internal string ActionHeadingText => PendingActionHeadingText.Text;

    internal string PrimaryActionText
    {
        set => SaveRestartButton.Content = value;
    }

    internal bool PrimaryActionEnabled
    {
        set => SaveRestartButton.IsEnabled = value;
    }

    internal bool ShowsCancelChanges
    {
        set => CancelChangesButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal WpfTextBox PortTextBox => ManualPortTextBox;

    internal WpfTextBlock PortValidationText => ManualPortValidationText;

    internal WpfTextBlock StatusText => ConnectionStatusText;

    internal WpfButton PrimaryActionButton => SaveRestartButton;

    internal WpfButton AdapterChooserButton => ChooseAdapterButton;

    internal bool IsApplyingPresentation => _suppressControlEvents;

    internal void ApplyPresentation(Action<ConnectionPageView> update)
    {
        _suppressControlEvents = true;
        try
        {
            update(this);
        }
        finally
        {
            _suppressControlEvents = false;
        }
    }

    internal void FocusAdapterChooser()
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (NetworkCandidateList.Items.Count > 0)
            {
                NetworkCandidateList.Focus();
            }
            else
            {
                RefreshAdaptersButton.Focus();
            }
        }, DispatcherPriority.Input);
    }

    internal void FocusAdapterChooserButton() => ChooseAdapterButton.Focus();

    internal void FocusPortInput()
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!ManualPortPanel.IsVisible)
            {
                return;
            }

            var focusScope = FocusManager.GetFocusScope(ManualPortTextBox);
            FocusManager.SetFocusedElement(focusScope, ManualPortTextBox);
            Keyboard.Focus(ManualPortTextBox);
            ManualPortTextBox.SelectAll();
            ManualPortPanel.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void OnNetworkCandidatePreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        ConnectionScrollViewer.RaiseEvent(new MouseWheelEventArgs(
            eventArgs.MouseDevice,
            eventArgs.Timestamp,
            eventArgs.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = ConnectionScrollViewer
        });
    }

    private void OnNetworkCandidateMouseUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(NetworkCandidateList, source) is not ListBoxItem item)
        {
            return;
        }

        NetworkCandidateList.SelectedItem = item.DataContext;
        CommitCandidateSelection();
    }

    private void OnNetworkCandidateKeyDown(object sender, WpfKeyEventArgs eventArgs)
    {
        if (eventArgs.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        eventArgs.Handled = true;
        CommitCandidateSelection();
    }

    private void CommitCandidateSelection()
    {
        if (_suppressCandidateSelection || NetworkCandidateList.SelectedItem is not ConnectionCandidateItem selected)
        {
            return;
        }

        CandidateSelected?.Invoke(selected);
    }

    private void RunUserAction(Action action)
    {
        if (!_suppressControlEvents)
        {
            action();
        }
    }
}
