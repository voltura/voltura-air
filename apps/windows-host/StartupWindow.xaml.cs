using System.Windows;
using WpfApplication = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;

namespace VolturaAir.Host;

public partial class StartupWindow : Window
{
    private const double ErrorWindowWidth = 620;
    private const double ErrorWindowHeight = 440;
    private string _errorDetails = string.Empty;
    private Action? _disableWatchdogAndRestart;

    public StartupWindow()
    {
        InitializeComponent();
        WpfTheme.Apply(this);
        SetIcon(this);
        SetStartupAppImage();
    }

    public void ShowError(string message, string details, Action? disableWatchdogAndRestart = null)
    {
        ResizeForError();
        _errorDetails = details;
        _disableWatchdogAndRestart = disableWatchdogAndRestart;
        StatusText.Text = "Voltura Air could not start.";
        StartupProgress.Visibility = Visibility.Collapsed;
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        DisableWatchdogButton.Visibility = disableWatchdogAndRestart is null ? Visibility.Collapsed : Visibility.Visible;
        ErrorActions.Visibility = Visibility.Visible;
    }

    private void ResizeForError()
    {
        var widthIncrease = Math.Max(0, ErrorWindowWidth - Width);
        var heightIncrease = Math.Max(0, ErrorWindowHeight - Height);
        Width = Math.Max(Width, ErrorWindowWidth);
        Height = Math.Max(Height, ErrorWindowHeight);
        if (!IsLoaded)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        Left = Math.Clamp(Left - (widthIncrease / 2), workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        Top = Math.Clamp(Top - (heightIncrease / 2), workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
    }

    private void OnDisableWatchdogClicked(object sender, RoutedEventArgs e)
    {
        var disableWatchdogAndRestart = _disableWatchdogAndRestart;
        if (disableWatchdogAndRestart is null)
        {
            return;
        }

        DisableWatchdogButton.IsEnabled = false;
        try
        {
            disableWatchdogAndRestart();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _errorDetails = $"{_errorDetails}{Environment.NewLine}{Environment.NewLine}Recovery action failed:{Environment.NewLine}{ex}";
            ErrorText.Text = "The watchdog setting could not be changed. Copy the error details and close Voltura Air.";
            DisableWatchdogButton.IsEnabled = true;
        }
    }

    private void OnCopyDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_errorDetails))
        {
            WpfClipboard.SetText(_errorDetails);
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        WpfApplication.Current.Shutdown();
    }

    private static void SetIcon(Window window)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAir.ico");
        if (File.Exists(iconPath))
        {
            window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconPath));
        }
    }

    private void SetStartupAppImage()
    {
        var imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolturaAir-256.png");
        if (File.Exists(imagePath))
        {
            StartupAppImage.Source = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(imagePath));
        }
    }
}
