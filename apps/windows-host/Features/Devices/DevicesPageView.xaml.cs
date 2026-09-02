using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VolturaAir.Host.Ui;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace VolturaAir.Host.Features.Devices;

public partial class DevicesPageView : WpfUserControl
{
    internal DevicesPageView(
        IReadOnlyList<DeviceListItem> devices,
        Action<string> deviceExpanded,
        Action<string> deviceCollapsed,
        Func<string, bool?, (bool? Override, bool Effective)?> setShowModeButtons,
        Func<string, bool?, (bool? Override, bool Effective)?> setControlDepth,
        Func<string, ScreenViewSoundQuality?, (ScreenViewSoundQuality? Override, ScreenViewSoundQuality Effective)?> setScreenSoundQuality,
        Func<string, int, bool> savePointerSpeed,
        Func<string, int?> useGlobalPointerSpeed,
        Func<string, DeviceAccessProfile, DeviceAccessViewState?> setAccessProfile,
        Func<string, DevicePermissionKind, bool, DeviceAccessViewState?> setPermission,
        Func<string, bool?, (bool? Override, bool Effective)?> setProtectedFileFilter,
        Action<string> removeDevice,
        Action cleanUpDuplicates,
        Action removeAll)
    {
        InitializeComponent();
        DeviceList.ItemsSource = devices;
        _deviceExpanded = deviceExpanded;
        _deviceCollapsed = deviceCollapsed;
        _setShowModeButtons = setShowModeButtons;
        _setControlDepth = setControlDepth;
        _setScreenSoundQuality = setScreenSoundQuality;
        _savePointerSpeed = savePointerSpeed;
        _useGlobalPointerSpeed = useGlobalPointerSpeed;
        _setAccessProfile = setAccessProfile;
        _setPermission = setPermission;
        _setProtectedFileFilter = setProtectedFileFilter;
        _removeDevice = removeDevice;
        CleanUpDuplicatesButton.Click += (_, _) => cleanUpDuplicates();
        RemoveAllButton.Click += (_, _) => removeAll();
    }

    internal WpfListBox Devices => DeviceList;

    private readonly Action<string> _deviceExpanded;
    private readonly Action<string> _deviceCollapsed;
    private readonly Func<string, bool?, (bool? Override, bool Effective)?> _setShowModeButtons;
    private readonly Func<string, bool?, (bool? Override, bool Effective)?> _setControlDepth;
    private readonly Func<string, ScreenViewSoundQuality?, (ScreenViewSoundQuality? Override, ScreenViewSoundQuality Effective)?> _setScreenSoundQuality;
    private readonly Func<string, int, bool> _savePointerSpeed;
    private readonly Func<string, int?> _useGlobalPointerSpeed;
    private readonly Func<string, DeviceAccessProfile, DeviceAccessViewState?> _setAccessProfile;
    private readonly Func<string, DevicePermissionKind, bool, DeviceAccessViewState?> _setPermission;
    private readonly Func<string, bool?, (bool? Override, bool Effective)?> _setProtectedFileFilter;
    private readonly Action<string> _removeDevice;

    internal void FocusAccessProfile(string clientId, Action<bool> completed)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (DeviceList.Items.OfType<DeviceListItem>().FirstOrDefault(item =>
                string.Equals(item.ClientId, clientId, StringComparison.Ordinal)) is not { } device)
            {
                completed(false);
                return;
            }

            device.IsExpanded = true;
            device.OpenPermissions();
            DeviceList.SelectedItem = device;
            DeviceList.ScrollIntoView(device);
            UpdateLayout();
            if (DeviceList.ItemContainerGenerator.ContainerFromItem(device) is not ListBoxItem row)
            {
                completed(false);
                return;
            }

            var selector = FindVisualDescendants<WpfComboBox>(row)
                .FirstOrDefault(control => control.Name == "AccessProfileSelector");
            selector?.BringIntoView();
            completed(selector?.Focus() == true && selector.IsKeyboardFocusWithin);
        }, DispatcherPriority.ApplicationIdle);
    }

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

    private void OnScreenViewingExpanded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Expander { DataContext: DeviceListItem device })
        {
            device.OpenScreenViewing();
        }
    }

    private void OnScreenSoundQualityChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (sender is not WpfComboBox
            {
                DataContext: DeviceListItem device,
                SelectedItem: DeviceSoundQualityChoice choice
            } || device.ScreenSoundQualityOverride == choice.Quality)
        {
            return;
        }

        if (_setScreenSoundQuality(device.ClientId, choice.Quality) is { } profile)
        {
            device.ApplyScreenSoundQuality(profile.Override, profile.Effective);
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

    private void OnUseGlobalControlDepth(object sender, RoutedEventArgs eventArgs) => SetControlDepth(sender, null);

    private void OnEnableControlDepth(object sender, RoutedEventArgs eventArgs) => SetControlDepth(sender, true);

    private void OnDisableControlDepth(object sender, RoutedEventArgs eventArgs) => SetControlDepth(sender, false);

    private void SetControlDepth(object sender, bool? value)
    {
        if (sender is not WpfButton button || FindAncestor<DeviceListItem>(button) is not { } device)
        {
            return;
        }

        if (_setControlDepth(device.ClientId, value) is { } profile)
        {
            device.ApplyControlDepth(profile.Override, profile.Effective);
        }
    }

    private void OnAllowPermission(object sender, RoutedEventArgs eventArgs) => SetPermission(sender, true);

    private void OnBlockPermission(object sender, RoutedEventArgs eventArgs) => SetPermission(sender, false);

    private void OnUseGlobalProtectedFileFilter(object sender, RoutedEventArgs eventArgs) => SetProtectedFileFilter(sender, null);

    private void OnHideProtectedFiles(object sender, RoutedEventArgs eventArgs) => SetProtectedFileFilter(sender, true);

    private void OnShowProtectedFiles(object sender, RoutedEventArgs eventArgs) => SetProtectedFileFilter(sender, false);

    private void SetPermission(object sender, bool value)
    {
        if (sender is not WpfButton { DataContext: DevicePermissionItem permission })
        {
            return;
        }

        if (_setPermission(permission.ClientId, permission.Kind, value) is { } state &&
            DeviceList.Items.OfType<DeviceListItem>().FirstOrDefault(item =>
                string.Equals(item.ClientId, permission.ClientId, StringComparison.Ordinal)) is { } device)
        {
            device.ApplyAccessProfile(state.Profile, state.Permissions);
        }
    }

    private void SetProtectedFileFilter(object sender, bool? value)
    {
        if (sender is not WpfButton { DataContext: ProtectedFileFilterItem filter })
        {
            return;
        }

        if (_setProtectedFileFilter(filter.ClientId, value) is { } state)
        {
            filter.Apply(state.Override, state.Effective);
        }
    }

    private void OnAccessProfileChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (sender is not WpfComboBox
            {
                DataContext: DeviceListItem device,
                SelectedValue: DeviceAccessProfile profile
            } ||
            profile == device.AccessProfile)
        {
            return;
        }

        if (_setAccessProfile(device.ClientId, profile) is { } state)
        {
            device.ApplyAccessProfile(state.Profile, state.Permissions);
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

    private static IEnumerable<T> FindVisualDescendants<T>(System.Windows.DependencyObject parent)
        where T : System.Windows.DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
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
