using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Shapes;
using Button = System.Windows.Controls.Button;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using MediaColors = System.Windows.Media.Colors;
using MediaGradientStop = System.Windows.Media.GradientStop;
using MediaLinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using MediaPoint = System.Windows.Point;
using WpfCursors = System.Windows.Input.Cursors;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void AddApplicationLoggingSettings(StackPanel parent)
    {
        var applicationLogging = CreateCheckBox("Write application log", AppLoggingSettings.IsEnabled());
        applicationLogging.Checked += (_, _) =>
        {
            AppLoggingSettings.SetEnabled(true);
            _appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_logging", Outcome: "enabled"));
        };
        applicationLogging.Unchecked += (_, _) =>
        {
            _appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_logging", Outcome: "disabled"));
            AppLoggingSettings.SetEnabled(false);
        };
        parent.Children.Add(applicationLogging);
        parent.Children.Add(CreateMutedText("Off by default. Typed text, pointer coordinates, and pairing secrets are excluded."));
        parent.Children.Add(CreateDetailsDisclosure("application logs", $"Records sanitized remote commands, host actions, outcomes, responses, and Windows errors. Daily JSON Lines files are written to {_appLog.LogDirectory}."));

        parent.Children.Add(CreateLabel("Keep application logs for"));
        var logRetention = new ComboBox { Width = 180, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 12) };
        logRetention.SetResourceReference(FrameworkElement.StyleProperty, "ModernComboBoxStyle");
        foreach (var days in new[] { 1, 2, 7, 14, 30 })
        {
            logRetention.Items.Add(new ComboBoxItem
            {
                Content = days == 1 ? "1 day" : $"{days} days",
                Tag = days,
                IsSelected = days == AppLoggingSettings.GetMaxAgeDays()
            });
        }

        logRetention.SelectionChanged += (_, _) =>
        {
            if (!_isLoadingPreferences && logRetention.SelectedItem is ComboBoxItem item && item.Tag is int days)
            {
                AppLoggingSettings.SetMaxAgeDays(days);
                _appLog.Write(new AppLogEntry("host_action", "windows_host", Action: "application_log_retention", Outcome: "changed", Detail: $"days={days}"));
            }
        };
        parent.Children.Add(logRetention);
    }

    private void AddWindowsLockPolicySetting(StackPanel parent)
    {
        var status = _workstationLockPolicy.GetStatus();
        string? actionLabel = null;
        var enablePolicy = false;
        switch (status.State)
        {
            case WorkstationLockPolicyState.NotExplicitlyDisabled:
                parent.Children.Add(CreateMutedText("Windows does not explicitly disable workstation locking for the current user. Test the native Windows lock action below if Lock PC is not working."));
                actionLabel = "Test Lock PC";
                break;
            case WorkstationLockPolicyState.Disabled:
                var disabledText = CreateMutedText("Windows explicitly disables workstation locking for the current user.");
                disabledText.Foreground = (Brush)Resources["DangerBrush"];
                parent.Children.Add(disabledText);
                actionLabel = "Enable Windows locking";
                enablePolicy = true;
                break;
            default:
                var unavailableText = CreateMutedText("Voltura Air could not read the current-user Windows locking policy.");
                unavailableText.Foreground = (Brush)Resources["DangerBrush"];
                parent.Children.Add(unavailableText);
                break;
        }

        parent.Children.Add(CreateMutedText("Controls whether Windows allows Lock PC and Win+L for this user."));
        if (actionLabel is not null)
        {
            var actionButton = CreateButton(actionLabel, (_, _) => EnableOrTestWindowsLocking(enablePolicy), primary: true);
            actionButton.HorizontalAlignment = HorizontalAlignment.Left;
            parent.Children.Add(actionButton);
        }
    }

    private StackPanel AddPreferencesSection(StackPanel parent, List<Expander> sections, string title)
    {
        var content = new StackPanel();
        var expander = new Expander
        {
            Header = title,
            Content = content,
            IsExpanded = false,
            Style = (Style)Resources["PreferencesAccordionStyle"]
        };
        expander.Expanded += (_, _) =>
        {
            foreach (var section in sections)
            {
                if (!ReferenceEquals(section, expander))
                {
                    section.IsExpanded = false;
                }
            }

            SetPreferencesTitle(title);
        };
        expander.Collapsed += (_, _) =>
        {
            if (sections.All(section => !section.IsExpanded))
            {
                SetPreferencesTitle(null);
            }
        };
        sections.Add(expander);
        parent.Children.Add(expander);
        return content;
    }

    private void SetPreferencesTitle(string? sectionTitle)
    {
        if (_activePage == HostPage.Preferences)
        {
            PageTitleText.Text = string.IsNullOrWhiteSpace(sectionTitle)
                ? "Preferences"
                : $"Preferences > {sectionTitle}";
        }
    }

    private void EnableOrTestWindowsLocking(bool enablePolicy)
    {
        var title = enablePolicy ? "Enable Windows locking" : "Test Lock PC";
        var message = enablePolicy
            ? "Voltura Air will enable locking for this Windows user, refresh user policy, and test Lock PC. The test may immediately lock this PC."
            : "Voltura Air will test the native Windows Lock PC action. This may immediately lock this PC.";
        if (!ThemedConfirmationDialog.Show(
                this,
                title,
                message,
                enablePolicy ? "Enable and test" : "Test Lock PC",
                "Cancel",
                ConfirmationTone.Question))
        {
            return;
        }

        if (enablePolicy)
        {
            var result = _workstationLockPolicy.TryEnable();
            if (!result.Succeeded)
            {
                SelectPage(HostPage.Preferences);
                ShowToast(result.Message);
                return;
            }
        }

        var lockResult = _powerController.TryExecute(SystemPowerActions.Lock);
        _appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: "test_windows_lock",
            Outcome: lockResult.Succeeded ? "lock_request_accepted" : "failed",
            Code: lockResult.Succeeded ? null : "VAIR-POWER-EXECUTION-FAILED",
            Win32Error: lockResult.Win32Error));
        SelectPage(HostPage.Preferences);
        ShowToast(lockResult.Succeeded
            ? "Windows accepted the lock request."
            : "Windows still prevents workstation locking. A Windows policy or another program may control this setting.");
    }

    private void AddGlobalPointerSpeedSetting(StackPanel parent)
    {
        parent.Children.Add(CreateLabel("Default pointer speed"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var currentSpeed = AppPointerSettings.GetDefaultPointerSpeed();
        var slider = new Slider
        {
            Style = (Style)Resources["ModernSliderStyle"],
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

    private void AddCustomPointerSetting(StackPanel parent)
    {
        var current = AppPointerSettings.GetCustomPointer();
        var customPointer = CreateCheckBox("Custom pointer", current.Enabled);
        parent.Children.Add(customPointer);

        var controls = new StackPanel { Margin = new Thickness(0, 0, 0, 12), IsEnabled = current.Enabled };
        controls.Children.Add(CreateLabel("Size"));
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var size = new Slider
        {
            Style = (Style)Resources["ModernSliderStyle"],
            Minimum = AppPointerSettings.MinCustomPointerSize,
            Maximum = AppPointerSettings.MaxCustomPointerSize,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Width = 220,
            Value = current.Size,
            Margin = new Thickness(0, 0, 12, 0)
        };
        var sizeValue = new TextBlock { Text = current.Size.ToString(CultureInfo.InvariantCulture), VerticalAlignment = VerticalAlignment.Center, MinWidth = 48, Foreground = (Brush)Resources["TextBrush"] };
        sizeRow.Children.Add(size);
        sizeRow.Children.Add(sizeValue);
        controls.Children.Add(sizeRow);

        controls.Children.Add(CreateLabel("Color"));
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var colorButton = CreateButton(string.Empty, (_, _) => { }, primary: false);
        colorButton.Width = 132;
        var colorPopup = CreateCustomPointerColorPopup(colorButton, current.Color, selected =>
        {
            SaveCustomPointer(customPointer.IsChecked == true, (int)Math.Round(size.Value), selected);
            SetCustomPointerColorButton(colorButton, selected);
        });
        colorButton.Click += (_, _) => colorPopup.IsOpen = !colorPopup.IsOpen;
        SetCustomPointerColorButton(colorButton, current.Color);
        colorRow.Children.Add(colorButton);
        controls.Children.Add(colorRow);
        parent.Children.Add(controls);

        var sizePreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        sizePreviewTimer.Tick += (_, _) =>
        {
            sizePreviewTimer.Stop();
            SaveCustomPointer(customPointer.IsChecked == true, (int)Math.Round(size.Value), GetCustomPointerColor(colorButton));
        };

        customPointer.Checked += (_, _) =>
        {
            controls.IsEnabled = true;
            SaveCustomPointer(true, (int)Math.Round(size.Value), GetCustomPointerColor(colorButton));
        };
        customPointer.Unchecked += (_, _) =>
        {
            controls.IsEnabled = false;
            SaveCustomPointer(false, (int)Math.Round(size.Value), GetCustomPointerColor(colorButton));
        };
        size.ValueChanged += (_, _) =>
        {
            var selected = (int)Math.Round(size.Value);
            sizeValue.Text = selected.ToString(CultureInfo.InvariantCulture);
            if (!_isLoadingPreferences)
            {
                sizePreviewTimer.Stop();
                sizePreviewTimer.Start();
            }
        };
    }

    private void SaveCustomPointer(bool enabled, int size, uint color)
    {
        var settings = new CustomPointerSettings(enabled, size, color);
        try
        {
            _customPointerService.Apply(settings);
            AppPointerSettings.SetCustomPointer(settings);
            _appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "custom_pointer",
                Outcome: enabled ? "enabled" : "disabled",
                Detail: $"size={settings.Size};color=#{settings.Color:X6}"));
        }
        catch (Exception exception)
        {
            _appLog.Write(new AppLogEntry(
                Event: "host_action",
                Source: "windows_host",
                Action: "custom_pointer",
                Outcome: "failed",
                Detail: exception.Message));
            ShowToast("Custom pointer could not be applied. Your Windows cursor scheme was restored.");
        }
    }

    private Popup CreateCustomPointerColorPopup(Button owner, uint initialColor, Action<uint> preview)
    {
        var initial = initialColor;
        var (hue, saturation, value) = RgbToHsv(initialColor);
        var draft = initialColor;
        var synchronizing = false;
        var input = new TextBox { Text = $"#{draft:X6}", Width = 104, Margin = new Thickness(0, 0, 8, 0) };
        var swatch = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(5), Background = CreateCustomPointerBrush(draft), BorderBrush = (Brush)Resources["BorderBrush"], BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0) };
        var surface = new Canvas { Width = 184, Height = 116, Cursor = WpfCursors.Cross };
        var saturationLayer = new Border { Width = 184, Height = 116 };
        var valueLayer = new Border { Width = 184, Height = 116, Background = new MediaLinearGradientBrush(MediaColor.FromArgb(0, 0, 0, 0), MediaColors.Black, 90) };
        var marker = new Ellipse { Width = 12, Height = 12, Stroke = MediaBrushes.White, StrokeThickness = 2, Fill = MediaBrushes.Transparent, IsHitTestVisible = false };
        surface.Children.Add(saturationLayer);
        surface.Children.Add(valueLayer);
        surface.Children.Add(marker);
        var hueSlider = new Slider
        {
            Style = (Style)Resources["ModernSliderStyle"],
            Minimum = 0,
            Maximum = 360,
            Width = 184,
            Value = hue,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var popup = new Popup
        {
            PlacementTarget = owner,
            Placement = PlacementMode.Bottom,
            StaysOpen = true,
            AllowsTransparency = true
        };
        void RefreshSurface()
        {
            saturationLayer.Background = new MediaLinearGradientBrush(MediaColors.White, CreateCustomPointerBrush(HsvToRgb(hue, 1, 1)).Color, 0);
            Canvas.SetLeft(marker, saturation * surface.Width - marker.Width / 2);
            Canvas.SetTop(marker, (1 - value) * surface.Height - marker.Height / 2);
        }

        void UpdateDraft(uint color, bool updateInput = true)
        {
            draft = color;
            swatch.Background = CreateCustomPointerBrush(draft);
            if (updateInput)
            {
                synchronizing = true;
                input.Text = $"#{draft:X6}";
                synchronizing = false;
            }

            preview(draft);
        }

        hueSlider.ValueChanged += (_, _) =>
        {
            hue = hueSlider.Value;
            RefreshSurface();
            UpdateDraft(HsvToRgb(hue, saturation, value));
        };
        void PickSurface(MediaPoint point)
        {
            saturation = Math.Clamp(point.X / surface.Width, 0, 1);
            value = Math.Clamp(1 - point.Y / surface.Height, 0, 1);
            RefreshSurface();
            UpdateDraft(HsvToRgb(hue, saturation, value));
        }

        surface.MouseLeftButtonDown += (_, eventArgs) =>
        {
            surface.CaptureMouse();
            PickSurface(eventArgs.GetPosition(surface));
        };
        surface.MouseMove += (_, eventArgs) =>
        {
            if (surface.IsMouseCaptured)
            {
                PickSurface(eventArgs.GetPosition(surface));
            }
        };
        surface.MouseLeftButtonUp += (_, _) => surface.ReleaseMouseCapture();
        input.TextChanged += (_, _) =>
        {
            if (!synchronizing && TryParseCustomPointerColor(input.Text, out var color))
            {
                (hue, saturation, value) = RgbToHsv(color);
                hueSlider.Value = hue;
                RefreshSurface();
                UpdateDraft(color, updateInput: false);
            }
        };
        var applyButton = CreateButton("Apply", (_, _) => popup.IsOpen = false, primary: true);
        var cancelButton = CreateButton("Cancel", (_, _) =>
        {
            preview(initial);
            popup.IsOpen = false;
        });
        popup.Opened += (_, _) =>
        {
            initial = GetCustomPointerColor(owner);
            draft = initial;
            (hue, saturation, value) = RgbToHsv(draft);
            synchronizing = true;
            hueSlider.Value = hue;
            input.Text = $"#{draft:X6}";
            synchronizing = false;
            swatch.Background = CreateCustomPointerBrush(draft);
            RefreshSurface();
        };
        RefreshSurface();
        popup.Child = new Border
        {
            Background = (Brush)Resources["SurfaceRaisedBrush"],
            BorderBrush = (Brush)Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Custom color", FontWeight = FontWeights.SemiBold, Foreground = (Brush)Resources["TextBrush"], Margin = new Thickness(0, 0, 0, 8) },
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, input } },
                    surface,
                    new TextBlock { Text = "Hue", Foreground = (Brush)Resources["MutedTextBrush"], Margin = new Thickness(0, 8, 0, 0) },
                    hueSlider,
                    new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0), Children = { applyButton, cancelButton } }
                }
            }
        };
        return popup;
    }

    private static bool TryParseCustomPointerColor(string value, out uint color)
    {
        var hex = value.Trim().TrimStart('#');
        return uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out color) && color <= 0xFFFFFF;
    }

    private static uint GetCustomPointerColor(Button button) => button.Tag is uint color ? color : AppPointerSettings.DefaultCustomPointerColor;

    private static (double Hue, double Saturation, double Value) RgbToHsv(uint color)
    {
        var red = ((color >> 16) & 0xFF) / 255d;
        var green = ((color >> 8) & 0xFF) / 255d;
        var blue = (color & 0xFF) / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        var hue = delta == 0 ? 0 : max == red ? 60 * ((green - blue) / delta % 6) : max == green ? 60 * ((blue - red) / delta + 2) : 60 * ((red - green) / delta + 4);
        return (hue < 0 ? hue + 360 : hue, max == 0 ? 0 : delta / max, max);
    }

    private static uint HsvToRgb(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var second = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var offset = value - chroma;
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, second, 0d), < 120 => (second, chroma, 0d), < 180 => (0d, chroma, second),
            < 240 => (0d, second, chroma), < 300 => (second, 0d, chroma), _ => (chroma, 0d, second)
        };
        var redByte = (uint)Math.Round((red + offset) * 255);
        var greenByte = (uint)Math.Round((green + offset) * 255);
        var blueByte = (uint)Math.Round((blue + offset) * 255);
        return (redByte << 16) | (greenByte << 8) | blueByte;
    }

    private static SolidColorBrush CreateCustomPointerBrush(uint color) => new(MediaColor.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color));

    private static void SetCustomPointerColorButton(Button button, uint color)
    {
        button.Tag = color;
        button.Content = $"#{color:X6}";
        button.Background = CreateCustomPointerBrush(color);
        button.Foreground = color > 0x808080 ? MediaBrushes.Black : MediaBrushes.White;
    }

    private void SaveGlobalPermissions(
        CheckBox sleep,
        CheckBox volume,
        CheckBox remoteLaunch,
        CheckBox urlOpen,
        CheckBox pcLock,
        CheckBox blackoutDisplay,
        CheckBox displayOff,
        CheckBox screenSaver,
        CheckBox awakeControl,
        CheckBox clipboardRead,
        CheckBox signOut,
        CheckBox restart,
        CheckBox shutdown)
    {
        if (_isLoadingPreferences)
        {
            return;
        }

        AppPermissionSettings.Save(new HostPermissionSet(
            AllowPcSleep: sleep.IsChecked == true,
            AllowVolumeControl: volume.IsChecked == true,
            AllowRemoteAppLaunch: remoteLaunch.IsChecked == true,
            AllowUrlOpen: urlOpen.IsChecked == true,
            AllowPcLock: pcLock.IsChecked == true,
            AllowBlackoutDisplay: blackoutDisplay.IsChecked == true,
            AllowDisplayOff: displayOff.IsChecked == true,
            AllowScreenSaver: screenSaver.IsChecked == true,
            AllowAwakeControl: awakeControl.IsChecked == true,
            AllowClipboardRead: clipboardRead.IsChecked == true,
            AllowSignOut: signOut.IsChecked == true,
            AllowRestart: restart.IsChecked == true,
            AllowShutdown: shutdown.IsChecked == true));
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
