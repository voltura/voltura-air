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
}
