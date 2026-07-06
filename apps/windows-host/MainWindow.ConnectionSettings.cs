using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using WpfDataFormats = System.Windows.DataFormats;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void SaveConnectionSettings()
    {
        if (_networkCandidateList is null ||
            _networkAutomaticButton is null ||
            _portAutomaticButton is null ||
            _manualPortTextBox is null ||
            _connectionStatusText is null)
        {
            return;
        }

        var current = AppNetworkSettings.Load();
        var networkMode = _networkAutomaticButton.IsChecked == true ? NetworkSelectionMode.Automatic : NetworkSelectionMode.Manual;
        var portMode = _portAutomaticButton.IsChecked == true ? PortSelectionMode.Automatic : PortSelectionMode.Manual;
        string? manualAddress = null;
        string? manualAdapterId = null;
        string? manualAdapterName = null;
        if (networkMode == NetworkSelectionMode.Manual)
        {
            if (_networkCandidateList.SelectedItem is not ListBoxItem { Tag: CandidateListItem selected })
            {
                ShowConnectionStatus("Choose a network address before saving manual mode.", isError: true);
                return;
            }

            manualAddress = selected.Candidate.Address.ToString();
            manualAdapterId = selected.Candidate.AdapterId;
            manualAdapterName = LanAddressSelector.GetAdapterDisplayName(selected.Candidate);
        }

        int? manualPort = null;
        if (portMode == PortSelectionMode.Manual)
        {
            if (!ValidateManualPortText(showEmptyWarning: true))
            {
                return;
            }

            if (!int.TryParse(_manualPortTextBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort))
            {
                ShowConnectionStatus(PortSelector.ManualPortRangeMessage, isError: true);
                return;
            }

            var portValidationError = PortSelector.GetManualPortValidationError(parsedPort);
            if (portValidationError is not null)
            {
                ShowConnectionStatus(portValidationError, isError: true);
                return;
            }

            if (parsedPort != _webHost.Port && !WebHostService.IsPortAvailable(parsedPort))
            {
                ShowConnectionStatus($"Port {parsedPort} is already in use.", isError: true);
                return;
            }

            manualPort = parsedPort;
        }

        var updated = current with
        {
            NetworkMode = networkMode,
            ManualHostAddress = manualAddress,
            ManualAdapterId = manualAdapterId,
            ManualAdapterName = manualAdapterName,
            PortMode = portMode,
            ManualPort = manualPort
        };
        AppNetworkSettings.Save(updated);

        var selection = LanAddressSelector.Select(LanAddressSelector.GetCandidates(), updated);
        var hostAddress = selection?.Address.ToString() ?? WebHostService.GetDnsLanAddressFallback() ?? "127.0.0.1";
        _webHost.UpdateAdvertisedHostAddress(hostAddress, selection?.Candidate);
        UpdateServerUrl(_webHost.ServerUrl);
        if (networkMode == NetworkSelectionMode.Automatic)
        {
            AppNetworkSettings.SetLastAutomaticHostAddress(hostAddress);
        }

        var status = selection?.Warning ?? "Connection settings saved.";
        if (portMode == PortSelectionMode.Manual && manualPort != _webHost.Port)
        {
            status = $"{status} Port change will apply after restarting Voltura Air.";
        }

        ShowConnectionStatus(status, selection?.Warning is not null);
    }

    private void ShowConnectionStatus(string message, bool isError)
    {
        if (_connectionStatusText is null)
        {
            return;
        }

        _connectionStatusText.Text = message;
        _connectionStatusText.Foreground = isError ? (Brush)Resources["DangerBrush"] : (Brush)Resources["AccentBrush"];
    }

    private void UpdatePortInputState()
    {
        if (_manualPortTextBox is null)
        {
            return;
        }

        var enabled = _portManualButton?.IsChecked == true;
        _manualPortTextBox.IsEnabled = enabled;
        _manualPortTextBox.Opacity = enabled ? 1 : 0.62;
        if (enabled)
        {
            ValidateManualPortText(showEmptyWarning: false);
        }
        else
        {
            ShowManualPortValidation(string.Empty, isError: false);
        }
    }

    private void OnManualPortPreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        if (_manualPortTextBox is null)
        {
            return;
        }

        var proposed = GetProposedText(_manualPortTextBox, e.Text);
        if (proposed.Any(character => !char.IsDigit(character)) || proposed.Length > 5)
        {
            e.Handled = true;
            ShowManualPortValidation(proposed.Length > 5 ? PortSelector.ManualPortRangeMessage : "Port must use numbers only.", isError: true);
        }
    }

    private void OnManualPortPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(WpfDataFormats.Text) ||
            e.DataObject.GetData(WpfDataFormats.Text) is not string text ||
            text.Any(character => !char.IsDigit(character)) ||
            text.Length > 5)
        {
            e.CancelCommand();
            ShowManualPortValidation("Port must use numbers only and be between 49152 and 65535.", isError: true);
        }
    }

    private void OnManualPortTextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateManualPortText(showEmptyWarning: false);
    }

    private bool ValidateManualPortText(bool showEmptyWarning)
    {
        if (_manualPortTextBox is null)
        {
            return true;
        }

        var text = _manualPortTextBox.Text.Trim();
        if (text.Length == 0)
        {
            if (showEmptyWarning)
            {
                ShowManualPortValidation(PortSelector.ManualPortRangeMessage, isError: true);
            }

            return !showEmptyWarning;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            ShowManualPortValidation("Port must use numbers only.", isError: true);
            return false;
        }

        var message = PortSelector.GetManualPortValidationError(port);
        if (message is not null)
        {
            ShowManualPortValidation(message, isError: true);
            return false;
        }

        ShowManualPortValidation("Manual port looks valid.", isError: false);
        return true;
    }

    private void ShowManualPortValidation(string message, bool isError)
    {
        if (_manualPortValidationText is null)
        {
            return;
        }

        _manualPortValidationText.Text = message;
        _manualPortValidationText.Foreground = isError ? (Brush)Resources["DangerBrush"] : (Brush)Resources["MutedTextBrush"];
    }

    private static string GetProposedText(TextBox textBox, string replacement)
    {
        var text = textBox.Text;
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;
        return text.Remove(selectionStart, selectionLength).Insert(selectionStart, replacement);
    }
}
