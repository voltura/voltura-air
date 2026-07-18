using System.Windows;
using WpfButton = System.Windows.Controls.Button;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Features.Diagnostics;

public partial class SystemDiagnosticsView : WpfUserControl
{
    private readonly Action<DiagnosticItem> _copyDetail;

    internal SystemDiagnosticsView(
        IReadOnlyList<DiagnosticItem> details,
        Action<DiagnosticItem> copyDetail,
        Action copyDiagnostics,
        Action openProductPage)
    {
        InitializeComponent();
        _copyDetail = copyDetail;
        DetailsList.ItemsSource = details;
        CopyDiagnosticsButton.Click += (_, _) => copyDiagnostics();
        OpenProductPageButton.Click += (_, _) => openProductPage();
    }

    private void OnCopyDetailClicked(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is WpfButton { DataContext: DiagnosticItem detail })
        {
            _copyDetail(detail);
        }
    }
}
