using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VolturaAir.Host.Ui;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfButton = System.Windows.Controls.Button;

namespace VolturaAir.Host.Features.Devices;

public partial class DevicesPageView : WpfUserControl
{
    internal DevicesPageView(
        IReadOnlyList<DeviceListItem> devices,
        Action<string> deviceExpanded,
        Action<string> deviceCollapsed,
        Func<string, bool?, (bool? Override, bool Effective)?> setShowModeButtons,
        Func<string, int, bool> savePointerSpeed,
        Func<string, int?> useGlobalPointerSpeed,
        Func<string, DevicePermissionKind, bool?, bool> setPermission,
        Action<string> removeDevice,
        Action cleanUpDuplicates,
        Action removeAll)
    {
        InitializeComponent();
        DeviceList.ItemsSource = devices;
        _deviceExpanded = deviceExpanded;
        _deviceCollapsed = deviceCollapsed;
        _setShowModeButtons = setShowModeButtons;
        _savePointerSpeed = savePointerSpeed;
        _useGlobalPointerSpeed = useGlobalPointerSpeed;
        _setPermission = setPermission;
        _removeDevice = removeDevice;
        CleanUpDuplicatesButton.Click += (_, _) => cleanUpDuplicates();
        RemoveAllButton.Click += (_, _) => removeAll();
    }

    internal WpfListBox Devices => DeviceList;

    private readonly Action<string> _deviceExpanded;
    private readonly Action<string> _deviceCollapsed;
    private readonly Func<string, bool?, (bool? Override, bool Effective)?> _setShowModeButtons;
    private readonly Func<string, int, bool> _savePointerSpeed;
    private readonly Func<string, int?> _useGlobalPointerSpeed;
    private readonly Func<string, DevicePermissionKind, bool?, bool> _setPermission;
    private readonly Action<string> _removeDevice;

    private void OnDeviceExpanded(object sender, RoutedEventArgs eventArgs)
    {
        if (eventArgs.OriginalSource is not Expander { DataContext: DeviceListItem device })
        {
            return;
        }

        foreach (var item in DeviceList.Items.OfType<DeviceListItem>())
        {
            item.IsExpanded = item.ClientId == device.ClientId;
        }

        DeviceList.SelectedItem = device;
        RemoveDeviceButton.Tag = device;
        RemoveDeviceButton.IsEnabled = true;
        _deviceExpanded(device.ClientId);
    }

    private void OnDeviceCollapsed(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Expander accordion &&
            ReferenceEquals(eventArgs.OriginalSource, accordion) &&
            accordion.DataContext is DeviceListItem device)
        {
            device.CollapseChildren();
            _deviceCollapsed(device.ClientId);
        }
    }

    private void OnDeviceListPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is not (Key.Enter or Key.Space) ||
            Keyboard.FocusedElement is not ListBoxItem ||
            DeviceList.SelectedItem is not DeviceListItem device)
        {
            return;
        }

        device.IsExpanded = !device.IsExpanded;
        eventArgs.Handled = true;
    }

    private void OnSavePointerSpeed(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not WpfButton button || FindAncestor<DeviceListItem>(button) is not { } device)
        {
            return;
        }

        if (_savePointerSpeed(device.ClientId, device.PointerSpeed))
        {
            device.ApplyPointerSpeed(device.PointerSpeed, hasOverride: true);
        }
    }

    private void OnTrackpadExpanded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Expander { DataContext: DeviceListItem device })
        {
            device.OpenTrackpad();
        }
    }

    private void OnAppearanceExpanded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Expander { DataContext: DeviceListItem device })
        {
            device.OpenAppearance();
        }
    }

    private void OnPermissionsExpanded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Expander { DataContext: DeviceListItem device })
        {
            device.OpenPermissions();
        }
    }

    private void OnUseGlobalPointerSpeed(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is WpfButton button && FindAncestor<DeviceListItem>(button) is { } device)
        {
            if (_useGlobalPointerSpeed(device.ClientId) is { } pointerSpeed)
            {
                device.ApplyPointerSpeed(pointerSpeed, hasOverride: false);
            }
        }
    }

    private void OnUseGlobalModeButtons(object sender, RoutedEventArgs eventArgs) => SetModeButtons(sender, null);

    private void OnShowModeButtons(object sender, RoutedEventArgs eventArgs) => SetModeButtons(sender, true);

    private void OnHideModeButtons(object sender, RoutedEventArgs eventArgs) => SetModeButtons(sender, false);

    private void SetModeButtons(object sender, bool? value)
    {
        if (sender is not WpfButton button || FindAncestor<DeviceListItem>(button) is not { } device)
        {
            return;
        }

        if (_setShowModeButtons(device.ClientId, value) is { } profile)
        {
            device.ApplyShowModeButtons(profile.Override, profile.Effective);
        }
    }

    private void OnUseGlobalPermission(object sender, RoutedEventArgs eventArgs) => SetPermission(sender, null);

    private void OnAllowPermission(object sender, RoutedEventArgs eventArgs) => SetPermission(sender, true);

    private void OnBlockPermission(object sender, RoutedEventArgs eventArgs) => SetPermission(sender, false);

    private void SetPermission(object sender, bool? value)
    {
        if (sender is not WpfButton { DataContext: DevicePermissionItem permission })
        {
            return;
        }

        if (_setPermission(permission.ClientId, permission.Kind, value))
        {
            permission.SetOverrideValue(value);
        }
    }

    private void OnRemoveDevice(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is WpfButton { Tag: DeviceListItem device })
        {
            _removeDevice(device.ClientId);
        }
    }

    private static T? FindAncestor<T>(FrameworkElement element)
        where T : class
    {
        for (FrameworkElement? current = element; current is not null; current = current.Parent as FrameworkElement)
        {
            if (current.DataContext is T match)
            {
                return match;
            }
        }

        return null;
    }

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
