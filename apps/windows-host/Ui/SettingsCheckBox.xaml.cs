using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace VolturaAir.Host.Ui;

public partial class SettingsCheckBox : WpfUserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(SettingsCheckBox),
        new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
        nameof(IsChecked),
        typeof(bool?),
        typeof(SettingsCheckBox),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty HasInformationProperty = DependencyProperty.Register(
        nameof(HasInformation),
        typeof(bool),
        typeof(SettingsCheckBox),
        new PropertyMetadata(false, OnHasInformationChanged));

    public static readonly DependencyProperty InformationAccessibleNameProperty = DependencyProperty.Register(
        nameof(InformationAccessibleName),
        typeof(string),
        typeof(SettingsCheckBox),
        new PropertyMetadata(string.Empty));

    public SettingsCheckBox()
    {
        InitializeComponent();
        UpdateInformationVisibility();
        Toggle.Checked += (_, eventArgs) => Checked?.Invoke(this, eventArgs);
        Toggle.Unchecked += (_, eventArgs) => Unchecked?.Invoke(this, eventArgs);
    }

    public event RoutedEventHandler? Checked;

    public event RoutedEventHandler? Unchecked;

    public event RoutedEventHandler? InformationRequested;

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool? IsChecked
    {
        get => (bool?)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public bool HasInformation
    {
        get => (bool)GetValue(HasInformationProperty);
        set => SetValue(HasInformationProperty, value);
    }

    public string InformationAccessibleName
    {
        get => (string)GetValue(InformationAccessibleNameProperty);
        private set => SetValue(InformationAccessibleNameProperty, value);
    }

    private static void OnLabelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var control = (SettingsCheckBox)dependencyObject;
        control.InformationAccessibleName = $"More information about {eventArgs.NewValue as string}";
    }

    private static void OnHasInformationChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        ((SettingsCheckBox)dependencyObject).UpdateInformationVisibility();
    }

    private void UpdateInformationVisibility()
    {
        InformationButton.Visibility = HasInformation ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnInformationClick(object sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        InformationRequested?.Invoke(this, eventArgs);
    }

    private void OnChromePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        var source = eventArgs.OriginalSource as DependencyObject;
        if (IsWithin(source, Toggle) || IsWithin(source, InformationButton))
        {
            return;
        }

        _ = Toggle.Focus();
        Toggle.IsChecked = Toggle.IsChecked != true;
        eventArgs.Handled = true;
    }

    private static bool IsWithin(DependencyObject? source, DependencyObject ancestor)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, ancestor))
            {
                return true;
            }

            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }
}
