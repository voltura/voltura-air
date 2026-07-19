using System.Windows.Controls;
using System.Windows.Threading;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Connection;

public partial class ConnectionPageView : WpfUserControl
{
    internal ConnectionPageView(
        IReadOnlyList<ConnectionCandidateItem> candidates,
        bool useAutomaticNetwork,
        bool useAutomaticPort,
        string manualPort,
        string currentHostUrl,
        string selectedIp,
        string selectedPort,
        string status,
        Action save,
        Action refresh)
    {
        InitializeComponent();
        NetworkCandidateList.ItemsSource = candidates;
        NetworkCandidateList.SelectedItem = candidates.FirstOrDefault(candidate => candidate.IsSelected);
        NetworkAutomaticButton.IsChecked = useAutomaticNetwork;
        NetworkManualButton.IsChecked = !useAutomaticNetwork;
        PortAutomaticButton.IsChecked = useAutomaticPort;
        PortManualButton.IsChecked = !useAutomaticPort;
        ManualPortTextBox.Text = manualPort;
        CurrentHostUrlCard.Value = currentHostUrl;
        SelectedIpCard.Value = selectedIp;
        SelectedPortCard.Value = selectedPort;
        ConnectionStatusText.Text = status;

        WireSegmentPair(NetworkAutomaticButton, NetworkManualButton);
        WireSegmentPair(PortAutomaticButton, PortManualButton);
        SaveButton.Click += (_, _) => save();
        RefreshButton.Click += (_, _) => refresh();
    }

    internal ConnectionCandidateItem? SelectedCandidate => NetworkCandidateList.SelectedItem as ConnectionCandidateItem;

    internal bool UsesAutomaticNetwork => NetworkAutomaticButton.IsChecked == true;

    internal bool UsesAutomaticPort => PortAutomaticButton.IsChecked == true;

    internal WpfTextBox PortTextBox => ManualPortTextBox;

    internal WpfTextBlock PortValidationText => ManualPortValidationText;

    internal WpfTextBlock StatusText => ConnectionStatusText;

    internal WpfToggleButton AutomaticPortButton => PortAutomaticButton;

    internal WpfToggleButton ManualPortButton => PortManualButton;

    private void OnNetworkCandidateListKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs eventArgs)
    {
        if (eventArgs.NewFocus != NetworkCandidateList || NetworkCandidateList.Items.Count == 0)
        {
            return;
        }

        if (NetworkCandidateList.SelectedIndex < 0)
        {
            NetworkCandidateList.SelectedIndex = 0;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!NetworkCandidateList.IsKeyboardFocusWithin ||
                NetworkCandidateList.ItemContainerGenerator.ContainerFromIndex(NetworkCandidateList.SelectedIndex) is not ListBoxItem selectedItem)
            {
                return;
            }

            selectedItem.Focus();
        }, DispatcherPriority.Input);
    }

    private static void WireSegmentPair(WpfToggleButton first, WpfToggleButton second)
    {
        first.Click += (_, _) => SetSelected(first, second);
        second.Click += (_, _) => SetSelected(second, first);
    }

    private static void SetSelected(WpfToggleButton selected, WpfToggleButton other)
    {
        selected.IsChecked = true;
        other.IsChecked = false;
    }
}
