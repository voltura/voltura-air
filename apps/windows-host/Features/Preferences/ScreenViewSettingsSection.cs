using System.Windows.Controls;
using VolturaAir.Host.Ui;
using WpfRadioButton = System.Windows.Controls.RadioButton;

namespace VolturaAir.Host.Features.Preferences;

internal sealed class ScreenViewSettingsSection(
    HostVisualFactory visuals,
    PreferencesVisualFactory preferenceVisuals,
    Func<bool> isLoading)
{
    public void AddTo(StackPanel parent)
    {
        DirectScreenQualityMode current = AppScreenViewSettings.Load().DirectQuality;
        parent.Children.Add(visuals.CreateMutedText(
            "Quality for Direct connections. Relay quality is set under Connection."));
        var qualityLabel = visuals.CreateLabel("Quality");
        parent.Children.Add(qualityLabel);

        var automatic = CreateChoice("Automatic (recommended)", DirectScreenQualityMode.Automatic, current);
        var quality = CreateChoice("Full resolution", DirectScreenQualityMode.Quality, current);
        var saver = CreateChoice("Data saver", DirectScreenQualityMode.DataSaver, current);
        parent.Children.Add(automatic);
        parent.Children.Add(visuals.CreateMutedText("Adjusts resolution and frame rate for a stable picture."));
        parent.Children.Add(quality);
        parent.Children.Add(visuals.CreateMutedText("Keeps the display at full resolution and adjusts frame rate."));
        parent.Children.Add(saver);
        parent.Children.Add(visuals.CreateMutedText("Limits video to 4 Mbps and 1080p."));
        preferenceVisuals.RegisterLabel(qualityLabel, automatic);

        foreach (var choice in new[] { automatic, quality, saver })
        {
            choice.Checked += (_, _) =>
            {
                if (!isLoading() && choice.Tag is DirectScreenQualityMode selected)
                    AppScreenViewSettings.Save(new ScreenViewSettingsSnapshot(selected));
            };
        }

        parent.Children.Add(visuals.CreateMutedText(
            "Sound quality applies to both Direct and Relay Screen viewing. It can be overridden for each paired device."));
        var soundLabel = visuals.CreateLabel("Sound quality");
        parent.Children.Add(soundLabel);

        ScreenViewSoundQuality currentSound = AppScreenViewSettings.LoadSoundQuality();
        var high = CreateSoundChoice("High", ScreenViewSoundQuality.High, currentSound);
        var standard = CreateSoundChoice("Standard", ScreenViewSoundQuality.Standard, currentSound);
        var low = CreateSoundChoice("Low", ScreenViewSoundQuality.Low, currentSound);
        parent.Children.Add(high);
        parent.Children.Add(visuals.CreateMutedText("Best detail for music and movies. Stereo."));
        parent.Children.Add(standard);
        parent.Children.Add(visuals.CreateMutedText("Good stereo sound with lower network use."));
        parent.Children.Add(low);
        parent.Children.Add(visuals.CreateMutedText("Reduced-detail mono sound with the lowest network use."));
        preferenceVisuals.RegisterLabel(soundLabel, high);

        foreach (var choice in new[] { high, standard, low })
        {
            choice.Checked += (_, _) =>
            {
                if (!isLoading() && choice.Tag is ScreenViewSoundQuality selected)
                    AppScreenViewSettings.SaveSoundQuality(selected);
            };
        }
    }

    private WpfRadioButton CreateChoice(string text, DirectScreenQualityMode mode, DirectScreenQualityMode current) => new()
    {
        Content = text,
        GroupName = "DirectScreenQuality",
        Tag = mode,
        IsChecked = mode == current,
        Foreground = visuals.Brush("TextBrush")
    };

    private WpfRadioButton CreateSoundChoice(
        string text,
        ScreenViewSoundQuality quality,
        ScreenViewSoundQuality current) => new()
        {
            Content = text,
            GroupName = "ScreenViewSoundQuality",
            Tag = quality,
            IsChecked = quality == current,
            Foreground = visuals.Brush("TextBrush")
        };
}
