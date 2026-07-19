using System.Windows.Controls;
using System.Windows.Threading;
using VolturaAir.Host.Ui;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Devices;

public partial class DevicesPageView : WpfUserControl
{
    internal DevicesPageView(
        IReadOnlyList<DeviceListItem> devices,
        Action selectionChanged,
        Action cleanUpDuplicates,
        Action disconnectAll)
    {
        InitializeComponent();
        DeviceList.ItemsSource = devices;
        DeviceList.SelectionChanged += (_, _) => selectionChanged();
        CleanUpDuplicatesButton.Click += (_, _) => cleanUpDuplicates();
        DisconnectAllButton.Click += (_, _) => disconnectAll();
    }

    internal WpfListBox Devices => DeviceList;

    internal SpacingStackPanel Details => DeviceDetailsPanel;

    private void OnDeviceListKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs eventArgs)
    {
        if (eventArgs.NewFocus != DeviceList || DeviceList.Items.Count == 0)
        {
            return;
        }

        if (DeviceList.SelectedIndex < 0)
        {
            DeviceList.SelectedIndex = 0;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!DeviceList.IsKeyboardFocusWithin ||
                DeviceList.ItemContainerGenerator.ContainerFromIndex(DeviceList.SelectedIndex) is not ListBoxItem selectedItem)
            {
                return;
            }

            selectedItem.Focus();
        }, DispatcherPriority.Input);
    }
}
