using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace VolturaAir.Host;

internal sealed class AppLaunchEditorDialog : Window
{
    private readonly AppLaunchAction? _existing;
    private readonly TextBox _labelInput;
    private readonly TextBox _pathInput;
    private readonly TextBox _argumentsInput;
    private readonly TextBlock _errorText;

    private AppLaunchEditorDialog(Window owner, AppLaunchAction? existing)
    {
        _existing = existing;
        Owner = owner;
        Title = existing is null ? "Add application launch button" : "Edit application launch button";
        Width = 590;
        MinWidth = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Padding = new Thickness(0);
        WpfTheme.Apply(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("MainWindow.Styles.xaml", UriKind.Relative) });

        _labelInput = CreateTextInput(existing?.Label ?? string.Empty);
        _labelInput.MaxLength = AppLaunchSettings.MaxLabelLength;
        _labelInput.Width = 140;
        _labelInput.HorizontalAlignment = HorizontalAlignment.Left;
        _pathInput = CreateTextInput(existing?.ExecutablePath ?? string.Empty);
        _argumentsInput = CreateTextInput(existing?.Arguments ?? string.Empty);
        _errorText = new TextBlock
        {
            Foreground = Brush("DangerBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };

        Content = BuildContent();
    }

    public static bool ShowAndSave(Window owner, AppLaunchAction? existing = null)
    {
        return new AppLaunchEditorDialog(owner, existing).ShowDialog() == true;
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = _existing is null ? "Add a host-approved launch button" : "Update this host-approved launch button",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextBrush")
        });
        root.Children.Add(new TextBlock
        {
            Text = "The phone will see only the label. The executable path and arguments stay on this PC.",
            Foreground = Brush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 16)
        });

        root.Children.Add(CreateLabel("Button label"));
        root.Children.Add(_labelInput);
        root.Children.Add(new TextBlock
        {
            Text = $"Use 1–{AppLaunchSettings.MaxLabelLength} characters. The full label is shown on the phone.",
            Foreground = Brush("MutedTextBrush"),
            Margin = new Thickness(0, 2, 0, 0)
        });
        root.Children.Add(CreateLabel("Executable (.exe)"));

        var pathRow = new Grid();
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathRow.Children.Add(_pathInput);
        var browse = CreateButton("Browse...", isPrimary: false);
        browse.Margin = new Thickness(8, 4, 0, 4);
        browse.Click += (_, _) => BrowseForExecutable();
        Grid.SetColumn(browse, 1);
        pathRow.Children.Add(browse);
        root.Children.Add(pathRow);

        root.Children.Add(CreateLabel("Optional arguments"));
        root.Children.Add(_argumentsInput);
        root.Children.Add(new TextBlock
        {
            Text = "Arguments are passed directly to the executable. Command shells, scripts, and relative paths are not accepted.",
            Foreground = Brush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        root.Children.Add(_errorText);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        var cancel = CreateButton("Cancel", isPrimary: false);
        cancel.IsCancel = true;
        var save = CreateButton(_existing is null ? "Review and add" : "Review and save", isPrimary: true);
        save.IsDefault = true;
        save.Click += (_, _) => ReviewAndSave();
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        root.Children.Add(actions);
        return root;
    }

    private void BrowseForExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an application executable",
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            _pathInput.Text = dialog.FileName;
        }
    }

    private void ReviewAndSave()
    {
        if (!AppLaunchSettings.TryNormalizeCustom(
            _labelInput.Text,
            _pathInput.Text,
            _argumentsInput.Text,
            out var label,
            out var path,
            out var arguments,
            out var error))
        {
            _errorText.Text = error;
            return;
        }

        var command = string.IsNullOrWhiteSpace(arguments) ? path : $"{path} {arguments}";
        var approved = ThemedConfirmationDialog.Show(
            Owner,
            "Approve custom application command?",
            $"Paired devices with application-launch permission will be able to run:\n\n{label}\n{command}\n\nOnly approve commands you trust. You can remove this button at any time.",
            _existing is null ? "Approve and add" : "Approve and save",
            "Cancel",
            ConfirmationTone.Warning);
        if (!approved)
        {
            return;
        }

        if (!AppLaunchSettings.TrySaveCustom(label, path, arguments, _existing?.Id, out _, out error))
        {
            _errorText.Text = error;
            return;
        }

        DialogResult = true;
    }

    private TextBlock CreateLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush"),
            Margin = new Thickness(0, 10, 0, 0)
        };
    }

    private TextBox CreateTextInput(string value)
    {
        return new TextBox
        {
            Text = value,
            MinHeight = 36,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 4, 0, 4),
            Background = Brush("SurfaceRaisedBrush"),
            Foreground = Brush("TextBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1)
        };
    }

    private Button CreateButton(string text, bool isPrimary)
    {
        return new Button
        {
            Content = text,
            MinHeight = 36,
            MinWidth = 88,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0),
            Background = isPrimary ? Brush("AccentBrush") : Brush("SurfaceRaisedBrush"),
            Foreground = isPrimary ? Brush("AccentTextBrush") : Brush("TextBrush"),
            BorderBrush = isPrimary ? Brush("AccentBrush") : Brush("BorderBrush"),
            BorderThickness = new Thickness(1)
        };
    }

    private Brush Brush(string key) => (Brush)Resources[key];
}
