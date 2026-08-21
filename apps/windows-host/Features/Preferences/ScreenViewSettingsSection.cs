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
            "Controls Direct local network Screen View. Automatic uses the best stable quality supported by the display, PC, receiving device, and network."));
        var qualityLabel = visuals.CreateLabel("Direct quality");
        parent.Children.Add(qualityLabel);

        var automatic = CreateChoice("Automatic (recommended)", DirectScreenQualityMode.Automatic, current);
        var quality = CreateChoice("Quality", DirectScreenQualityMode.Quality, current);
        var saver = CreateChoice("Data saver", DirectScreenQualityMode.DataSaver, current);
        parent.Children.Add(automatic);
        parent.Children.Add(visuals.CreateMutedText("Balances native detail and fluid motion, adapting only when necessary."));
        parent.Children.Add(quality);
        parent.Children.Add(visuals.CreateMutedText("Keeps the selected display at its readable resolution and adapts frame rate when needed."));
        parent.Children.Add(saver);
        parent.Children.Add(visuals.CreateMutedText("Uses up to 4 Mbps and 1920 × 1080 at 30 fps on Direct connections."));
        preferenceVisuals.RegisterLabel(qualityLabel, automatic);

        foreach (var choice in new[] { automatic, quality, saver })
        {
            choice.Checked += (_, _) =>
            {
                if (!isLoading() && choice.Tag is DirectScreenQualityMode selected)
                    AppScreenViewSettings.Save(new ScreenViewSettingsSnapshot(selected));
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
}
