using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.DataObject;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace VolturaAir.Host.Features.Connection;

internal sealed record ConnectionPortValidation(bool IsValid, string Message);

internal sealed class ConnectionPortController(Action presentationChanged)
{
    private ConnectionPageState? _state;
    private ConnectionPageView? _page;

    public void Attach(ConnectionPageView page, ConnectionPageState state)
    {
        _page = page;
        _state = state;
        page.PortTextBox.PreviewTextInput += OnPreviewTextInput;
        page.PortTextBox.TextChanged += OnTextChanged;
        WpfDataObject.AddPastingHandler(page.PortTextBox, OnPaste);
    }

    public void ValidateAndStore()
    {
        if (_state is null)
        {
            return;
        }

        _state.SetManualPort(int.TryParse(
            _state.ManualPortText.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var port) ? port : null);
    }

    public ConnectionPortValidation GetValidation()
    {
        if (_state is null || !_state.UsesCustomPort)
        {
            return new ConnectionPortValidation(true, string.Empty);
        }

        var text = _state.ManualPortText.Trim();
        if (text.Length == 0)
        {
            return new ConnectionPortValidation(false, PortSelector.ManualPortRangeMessage);
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            return new ConnectionPortValidation(false, "Port must use numbers only.");
        }

        var validationError = PortSelector.GetManualPortValidationError(port);
        if (validationError is not null)
        {
            return new ConnectionPortValidation(false, validationError);
        }

        if (port != _state.ActivePort && !WebHostService.IsPortAvailable(port))
        {
            return new ConnectionPortValidation(false, $"Port {port} is already in use.");
        }

        return new ConnectionPortValidation(true, "Port is available.");
    }

    private void OnPreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs eventArgs)
    {
        if (_page is null)
        {
            return;
        }

        var proposed = GetProposedText(_page.PortTextBox, eventArgs.Text);
        if (proposed.Any(character => !char.IsDigit(character)) || proposed.Length > 5)
        {
            eventArgs.Handled = true;
            ShowValidation(
                proposed.Length > 5 ? PortSelector.ManualPortRangeMessage : "Port must use numbers only.",
                isError: true);
        }
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs eventArgs)
    {
        if (!eventArgs.DataObject.GetDataPresent(WpfDataFormats.Text) ||
            eventArgs.DataObject.GetData(WpfDataFormats.Text) is not string text ||
            text.Any(character => !char.IsDigit(character)) ||
            text.Length > 5)
        {
            eventArgs.CancelCommand();
            ShowValidation("Port must use numbers only and be between 49152 and 65535.", isError: true);
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        if (_state is null || _page is null || _page.IsApplyingPresentation)
        {
            return;
        }

        _state.ManualPortText = _page.ManualPort;
        ValidateAndStore();
        presentationChanged();
    }

    private void ShowValidation(string message, bool isError)
    {
        if (_page is null)
        {
            return;
        }

        _page.PortValidation = message;
        _page.PortValidationIsError = isError;
    }

    private static string GetProposedText(WpfTextBox textBox, string replacement) => textBox.Text
        .Remove(textBox.SelectionStart, textBox.SelectionLength)
        .Insert(textBox.SelectionStart, replacement);
}
