namespace VolturaAir.Host;

public sealed class GuardedActionExecutor : IRemoteActionExecutor
{
    private readonly IRemoteActionExecutor _inner;

    public GuardedActionExecutor(IRemoteActionExecutor inner)
    {
        _inner = inner;
    }

    public bool TryExecute(string action)
    {
        if (!AppClientControlSettings.IsEnabled())
        {
            return false;
        }

        return _inner.TryExecute(action);
    }
}

public sealed class GuardedAudioController : ISystemAudioController
{
    private readonly ISystemAudioController _inner;

    public GuardedAudioController(ISystemAudioController inner)
    {
        _inner = inner;
    }

    public AudioState GetState()
    {
        return AppClientControlSettings.IsEnabled() ? _inner.GetState() : new AudioState(0, false);
    }

    public AudioState ToggleMute()
    {
        return AppClientControlSettings.IsEnabled() ? _inner.ToggleMute() : new AudioState(0, false);
    }

    public AudioState SetVolume(int volume)
    {
        return AppClientControlSettings.IsEnabled() ? _inner.SetVolume(volume) : new AudioState(0, false);
    }
}
