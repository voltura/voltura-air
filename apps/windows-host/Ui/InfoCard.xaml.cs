using System.Windows;
using System.Windows.Controls;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Ui;

public partial class InfoCard : WpfUserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(InfoCard),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(string),
        typeof(InfoCard),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty EmphasizedProperty = DependencyProperty.Register(
        nameof(Emphasized),
        typeof(bool),
        typeof(InfoCard),
        new PropertyMetadata(false));

    public static readonly DependencyProperty MonospaceProperty = DependencyProperty.Register(
        nameof(Monospace),
        typeof(bool),
        typeof(InfoCard),
        new PropertyMetadata(false));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions),
        typeof(object),
        typeof(InfoCard),
        new PropertyMetadata(null));

    public InfoCard()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool Emphasized
    {
        get => (bool)GetValue(EmphasizedProperty);
        set => SetValue(EmphasizedProperty, value);
    }

    public bool Monospace
    {
        get => (bool)GetValue(MonospaceProperty);
        set => SetValue(MonospaceProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
