using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using Orientation = System.Windows.Controls.Orientation;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void AddGlobalPointerSpeedSetting(StackPanel parent)
    {
        parent.Children.Add(CreateLabel("Default pointer speed"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var currentSpeed = AppPointerSettings.GetDefaultPointerSpeed();
        var slider = new Slider
        {
            Minimum = DevicePointerProfile.MinPointerSpeed,
            Maximum = DevicePointerProfile.MaxPointerSpeed,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Width = 220,
            Value = currentSpeed,
            Margin = new Thickness(0, 0, 12, 0)
        };
        var output = new TextBlock
        {
            Text = $"{currentSpeed.ToString(CultureInfo.InvariantCulture)}%",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Resources["TextBrush"],
            MinWidth = 48
        };
        slider.ValueChanged += (_, _) =>
        {
            var speed = (int)Math.Round(slider.Value);
            output.Text = $"{speed.ToString(CultureInfo.InvariantCulture)}%";
            if (!_isLoadingPreferences)
            {
                AppPointerSettings.SetDefaultPointerSpeed(speed);
            }
        };
        row.Children.Add(slider);
        row.Children.Add(output);
        parent.Children.Add(row);
    }

    private void SaveGlobalPermissions(CheckBox sleep, CheckBox volume, CheckBox remoteLaunch)
    {
        if (_isLoadingPreferences)
        {
            return;
        }

        AppPermissionSettings.Save(new HostPermissionSet(
            AllowPcSleep: sleep.IsChecked == true,
            AllowVolumeControl: volume.IsChecked == true,
            AllowRemoteAppLaunch: remoteLaunch.IsChecked == true));
    }

    private void AddYoutubeUrlSetting(StackPanel parent)
    {
        parent.Children.Add(CreateLabel("YouTube URL"));
        parent.Children.Add(CreateMutedText("Used when a paired device triggers the YouTube remote launch action. The URL stays on this PC."));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var input = new TextBox
        {
            Text = AppRemoteSettings.GetYoutubeUrl(),
            Width = 360,
            Margin = new Thickness(0, 0, 12, 0)
        };
        row.Children.Add(input);
        row.Children.Add(CreateButton("Save URL", (_, _) => SaveYoutubeUrl(input), primary: true));
        parent.Children.Add(row);
    }

    private void SaveYoutubeUrl(TextBox input)
    {
        if (AppRemoteSettings.TrySetYoutubeUrl(input.Text, out var normalizedUrl))
        {
            input.Text = normalizedUrl;
            ShowToast("YouTube URL updated");
            return;
        }

        ShowToast("Enter a valid http or https URL");
    }

    private void SetThemeMode(AppThemeMode mode)
    {
        if (!_isLoadingPreferences)
        {
            AppThemeSettings.SetMode(mode);
        }
    }

    private void SetDefaultRemoteMode(AppRemoteMode mode)
    {
        if (!_isLoadingPreferences)
        {
            AppRemoteSettings.SetDefaultRemoteMode(mode);
        }
    }
}
