using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Diagnostics;

public partial class ApplicationLogView : WpfUserControl
{
    internal ApplicationLogView(bool loggingEnabled)
    {
        InitializeComponent();
        LoggingToggle.IsChecked = loggingEnabled;
    }
}
