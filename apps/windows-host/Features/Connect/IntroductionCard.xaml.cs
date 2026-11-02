using System.Windows;

namespace VolturaAir.Host.Features.Connect;

public partial class IntroductionCard : System.Windows.Controls.UserControl
{
    private readonly Action _dismiss;

    public IntroductionCard(Action dismiss)
    {
        _dismiss = dismiss;
        InitializeComponent();
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => _dismiss();
}
