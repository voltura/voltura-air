using Concentus.Structs;
using Microsoft.Win32;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class ScreenViewSettingsTests : IsolatedHostSettingsTest
{
    [Fact]
    public void MissingSettingsDefaultToAutomaticAndChoicesPersist()
    {
        Assert.Equal(DirectScreenQualityMode.Automatic, AppScreenViewSettings.Load().DirectQuality);

        AppScreenViewSettings.Save(new ScreenViewSettingsSnapshot(DirectScreenQualityMode.Quality));

        Assert.Equal(DirectScreenQualityMode.Quality, AppScreenViewSettings.Load().DirectQuality);
    }

    [Fact]
    public void MalformedSettingRecoversOnlyToScreenViewDefault()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, true);
        key.SetValue(AppScreenViewSettings.ValueName, "{\"directQuality\":999}", RegistryValueKind.String);

        Assert.Equal(AppScreenViewSettings.Default, AppScreenViewSettings.Load());
    }

    [Fact]
    public void SoundQualityDefaultsToHighAndPersistsSeparatelyFromVideoQuality()
    {
        AppScreenViewSettings.Save(new ScreenViewSettingsSnapshot(DirectScreenQualityMode.DataSaver));

        Assert.Equal(ScreenViewSoundQuality.High, AppScreenViewSettings.LoadSoundQuality());

        AppScreenViewSettings.SaveSoundQuality(ScreenViewSoundQuality.Standard);

        Assert.Equal(ScreenViewSoundQuality.Standard, AppScreenViewSettings.LoadSoundQuality());
        Assert.Equal(DirectScreenQualityMode.DataSaver, AppScreenViewSettings.Load().DirectQuality);
    }

    [Fact]
    public void MalformedSoundQualityRecoversToHigh()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, true);
        key.SetValue(AppScreenViewSettings.SoundValueName, "{\"soundQuality\":999}", RegistryValueKind.String);

        Assert.Equal(ScreenViewSoundQuality.High, AppScreenViewSettings.LoadSoundQuality());
    }

    [Fact]
    public void FailedSoundQualityWritePreservesTheSettingAndDoesNotNotify()
    {
        AppScreenViewSettings.SaveSoundQuality(ScreenViewSoundQuality.Standard);
        var changes = 0;
        EventHandler changed = (_, _) => changes++;
        AppScreenViewSettings.SoundQualityChanged += changed;
        HostSettingsJsonValue.BeforeWriteForTests = (_, _) => throw new IOException("injected write failure");
        try
        {
            Assert.Throws<IOException>(() => AppScreenViewSettings.SaveSoundQuality(ScreenViewSoundQuality.Low));
        }
        finally
        {
            HostSettingsJsonValue.BeforeWriteForTests = null;
            AppScreenViewSettings.SoundQualityChanged -= changed;
        }

        Assert.Equal(ScreenViewSoundQuality.Standard, AppScreenViewSettings.LoadSoundQuality());
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(ScreenViewSoundQuality.High, 96_000, 2)]
    [InlineData(ScreenViewSoundQuality.Standard, 64_000, 2)]
    [InlineData(ScreenViewSoundQuality.Low, 48_000, 1)]
    public void SoundEncoderAppliesPresetOnTheSameEncoder(
        ScreenViewSoundQuality initial,
        int bitrate,
        int channels)
    {
        using var encoder = ScreenViewSystemAudioCapture.CreateEncoder(ScreenViewSoundQuality.High);

        ScreenViewSystemAudioCapture.ApplyEncodingProfile(encoder, initial);

        Assert.Equal(bitrate, encoder.Bitrate);
        Assert.Equal(channels, encoder.ForceChannels);
        Assert.True(encoder.UseVBR);
        Assert.True(encoder.UseConstrainedVBR);
    }

    [Fact]
    public void SoundEncoderEmitsStereoMonoStereoWithoutReplacement()
    {
        using var encoder = ScreenViewSystemAudioCapture.CreateEncoder(ScreenViewSoundQuality.High);
        var pcm = new short[ScreenViewSystemAudioCapture.FrameSamples * ScreenViewSystemAudioCapture.Channels];
        for (int sample = 0; sample < ScreenViewSystemAudioCapture.FrameSamples; sample++)
        {
            double phase = 2 * Math.PI * 440 * sample / ScreenViewSystemAudioCapture.SampleRate;
            pcm[sample * 2] = (short)(Math.Sin(phase) * 12_000);
            pcm[(sample * 2) + 1] = (short)(Math.Cos(phase) * 8_000);
        }

        Assert.Equal(2, EncodeAndReadChannels(encoder, pcm));
        ScreenViewSystemAudioCapture.ApplyEncodingProfile(encoder, ScreenViewSoundQuality.Low);
        Assert.Equal(1, EncodeAndReadChannels(encoder, pcm));
        ScreenViewSystemAudioCapture.ApplyEncodingProfile(encoder, ScreenViewSoundQuality.Standard);
        Assert.Equal(2, EncodeAndReadChannels(encoder, pcm));
    }

    private static int EncodeAndReadChannels(Concentus.IOpusEncoder encoder, short[] pcm)
    {
        var packet = new byte[1275];
        int encoded = encoder.Encode(
            pcm,
            ScreenViewSystemAudioCapture.FrameSamples,
            packet,
            packet.Length);
        Assert.True(encoded > 0);
        return OpusPacketInfo.GetNumEncodedChannels(packet.AsSpan(0, encoded));
    }
}
