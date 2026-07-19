using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VolturaAir.Host.Ui;
using TextBox = System.Windows.Controls.TextBox;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.DataObject;

namespace VolturaAir.Host.Features.Connection;

internal sealed class ConnectionPageController(
    WebHostService webHost,
    HostVisualFactory visuals,
    Action<string> serverUrlChanged,
    Action requestViewRefresh)
{
    private ConnectionPageView? _page;

    public ConnectionPageView CreateView()
    {
        var settings = AppNetworkSettings.Load();
        var candidates = LanAddressSelector.GetCandidates();
        var selection = LanAddressSelector.Select(candidates, settings);
        _page = new ConnectionPageView(
            ConnectionCandidateItem.Create(candidates, selection?.Candidate),
            settings.NetworkMode == NetworkSelectionMode.Automatic,
            settings.PortMode == PortSelectionMode.Automatic,
            (settings.ManualPort ?? webHost.Port).ToString(CultureInfo.InvariantCulture),
            webHost.ServerUrl,
            webHost.AdvertisedHostAddress,
            webHost.Port.ToString(CultureInfo.InvariantCulture),
            selection?.Warning ?? webHost.AddressSelectionWarning ?? webHost.PortSelectionWarning ?? string.Empty,
            Save,
            requestViewRefresh);
        _page.AutomaticPortButton.Click += (_, _) => UpdatePortInputState();
        _page.ManualPortButton.Click += (_, _) => UpdatePortInputState();
        _page.PortTextBox.PreviewTextInput += OnManualPortPreviewTextInput;
        _page.PortTextBox.TextChanged += OnManualPortTextChanged;
        WpfDataObject.AddPastingHandler(_page.PortTextBox, OnManualPortPaste);
        UpdatePortInputState();
        return _page;
    }

    private void Save()
    {
        if (_page is not { } page)
        {
            return;
        }

        var current = AppNetworkSettings.Load();
        var networkMode = page.UsesAutomaticNetwork ? NetworkSelectionMode.Automatic : NetworkSelectionMode.Manual;
        var portMode = page.UsesAutomaticPort ? PortSelectionMode.Automatic : PortSelectionMode.Manual;
        string? manualAddress = null;
        string? manualAdapterId = null;
        string? manualAdapterName = null;
        if (networkMode == NetworkSelectionMode.Manual)
        {
            if (page.SelectedCandidate is not { } selected)
            {
                ShowStatus("Choose a network address before saving manual mode.", isError: true);
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

            if (!int.TryParse(page.PortTextBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort))
            {
                ShowStatus(PortSelector.ManualPortRangeMessage, isError: true);
                return;
            }

            var portValidationError = PortSelector.GetManualPortValidationError(parsedPort);
            if (portValidationError is not null)
            {
                ShowStatus(portValidationError, isError: true);
                return;
            }

            if (parsedPort != webHost.Port && !WebHostService.IsPortAvailable(parsedPort))
            {
                ShowStatus($"Port {parsedPort} is already in use.", isError: true);
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
        webHost.UpdateAdvertisedHostAddress(hostAddress, selection?.Candidate);
        serverUrlChanged(webHost.ServerUrl);
        if (networkMode == NetworkSelectionMode.Automatic)
        {
            AppNetworkSettings.SetLastAutomaticHostAddress(hostAddress);
        }

        var status = selection?.Warning ?? "Connection settings saved.";
        if (portMode == PortSelectionMode.Manual && manualPort != webHost.Port)
        {
            status = $"{status} Port change will apply after restarting Voltura Air.";
        }

        ShowStatus(status, selection?.Warning is not null);
    }

    private void ShowStatus(string message, bool isError)
    {
        if (_page is not { } page)
        {
            return;
        }

        page.StatusText.Text = message;
        page.StatusText.Foreground = visuals.Brush(isError ? "DangerBrush" : "AccentBrush");
    }

    private void UpdatePortInputState()
    {
        if (_page is not { } page)
        {
            return;
        }

        var enabled = !page.UsesAutomaticPort;
        page.PortTextBox.IsEnabled = enabled;
        page.PortTextBox.Opacity = enabled ? 1 : 0.62;
        if (enabled)
        {
            ValidateManualPortText(showEmptyWarning: false);
        }
        else
        {
            ShowManualPortValidation(string.Empty, isError: false);
        }
    }

    private void OnManualPortPreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs eventArgs)
    {
        if (_page is not { } page)
        {
            return;
        }

        var proposed = GetProposedText(page.PortTextBox, eventArgs.Text);
        if (proposed.Any(character => !char.IsDigit(character)) || proposed.Length > 5)
        {
            eventArgs.Handled = true;
            ShowManualPortValidation(proposed.Length > 5 ? PortSelector.ManualPortRangeMessage : "Port must use numbers only.", isError: true);
        }
    }

    private void OnManualPortPaste(object sender, DataObjectPastingEventArgs eventArgs)
    {
        if (!eventArgs.DataObject.GetDataPresent(WpfDataFormats.Text) ||
            eventArgs.DataObject.GetData(WpfDataFormats.Text) is not string text ||
            text.Any(character => !char.IsDigit(character)) ||
            text.Length > 5)
        {
            eventArgs.CancelCommand();
            ShowManualPortValidation("Port must use numbers only and be between 49152 and 65535.", isError: true);
        }
    }

    private void OnManualPortTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        ValidateManualPortText(showEmptyWarning: false);
    }

    private bool ValidateManualPortText(bool showEmptyWarning)
    {
        if (_page is not { } page)
        {
            return true;
        }

        var text = page.PortTextBox.Text.Trim();
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
        if (_page is not { } page)
        {
            return;
        }

        page.PortValidationText.Text = message;
        page.PortValidationText.Foreground = visuals.Brush(isError ? "DangerBrush" : "MutedTextBrush");
    }

    private static string GetProposedText(TextBox textBox, string replacement)
    {
        return textBox.Text
            .Remove(textBox.SelectionStart, textBox.SelectionLength)
            .Insert(textBox.SelectionStart, replacement);
    }
}
