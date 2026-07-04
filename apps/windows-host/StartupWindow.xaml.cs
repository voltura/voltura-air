using System.Windows;
using WpfApplication = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;

namespace VolturaAir.Host;

public partial class StartupWindow : Window
{
    private string _errorDetails = string.Empty;

    public StartupWindow()
    {
        InitializeComponent();
        WpfTheme.Apply(this);
        SetIcon(this);
    }

    public void ShowError(string message, string details)
    {
        _errorDetails = details;
        StatusText.Text = "Voltura Air could not start.";
        StartupProgress.Visibility = Visibility.Collapsed;
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        ErrorActions.Visibility = Visibility.Visible;
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
}
