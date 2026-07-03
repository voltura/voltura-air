using System.Text.Json;
using System.Runtime.InteropServices;

namespace VolturaAir.Host;

public sealed record AudioState(int Volume, bool Muted);

public interface ISystemAudioController
{
    AudioState GetState();

    AudioState ToggleMute();

    AudioState SetVolume(int volume);
}

public static class AudioMessageRouter
{
    public static bool TryHandle(JsonElement message, ISystemAudioController audioController, out AudioState? state)
    {
        state = null;
        if (!message.TryGetProperty("type", out var typeProperty))
        {
            return false;
        }

        try
        {
            switch (typeProperty.GetString())
            {
                case "audio.mute.toggle":
                    state = audioController.ToggleMute();
                    return true;
                case "audio.volume.set":
                    state = audioController.SetVolume(GetVolume(message));
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            state = null;
            return true;
        }
    }

    public static AudioState? TryGetState(ISystemAudioController audioController)
    {
        try
        {
            return audioController.GetState();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            return null;
        }
    }

    private static int GetVolume(JsonElement message)
    {
        return message.TryGetProperty("volume", out var value) && value.TryGetDouble(out var volume)
            ? (int)Math.Clamp(Math.Round(volume), 0, 100)
            : 0;
    }
}
