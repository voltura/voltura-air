using System.Windows;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void RefreshStatusText()
    {
        NavStatusText.Text = _pairingManager.HasActiveController
            ? $"Connected: {_pairingManager.ActiveDeviceSummary}"
            : _pairingManager.IsPaired
                ? $"{_pairingManager.PairedDeviceCount} paired device{Plural(_pairingManager.PairedDeviceCount)}"
                : "Ready to pair";
    }

    private void RefreshNavigationTheme()
    {
        foreach (var button in _navigationButtons)
        {
            var isActive = button == GetButtonForPage(_activePage);
            button.Background = isActive ? (Brush)Resources["AccentBrush"] : (Brush)Resources["SurfaceRaisedBrush"];
            button.Foreground = isActive ? (Brush)Resources["AccentTextBrush"] : (Brush)Resources["TextBrush"];
            button.BorderBrush = isActive ? (Brush)Resources["AccentBrush"] : (Brush)Resources["BorderBrush"];
        }
    }

    private Button GetButtonForPage(HostPage page)
    {
        return page switch
        {
            HostPage.Connect => ConnectNavButton,
            HostPage.Devices => DevicesNavButton,
            HostPage.Connection => ConnectionNavButton,
            HostPage.Preferences => PreferencesNavButton,
            HostPage.Diagnostics => DiagnosticsNavButton,
            _ => ConnectNavButton
        };
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var pairedDeviceCount = _pairingManager.PairedDeviceCount;
            if (pairedDeviceCount != _lastPairedDeviceCount)
            {
                _lastPairedDeviceCount = pairedDeviceCount;
                NewPairing();
                return;
            }

            RefreshStatusText();
            if (_activePage is HostPage.Connect or HostPage.Devices or HostPage.Diagnostics)
            {
                SelectPage(_activePage);
            }
        });
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            WpfTheme.Apply(this);
            SelectPage(_activePage);
        });
    }
}
