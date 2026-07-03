using System.Runtime.InteropServices;

namespace VolturaAir.Host;

public sealed class SystemAudioController : ISystemAudioController
{
    private static readonly Guid AudioEndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid EmptyEventContext = Guid.Empty;
    private const int ClsctxAll = 23;

    public AudioState GetState()
    {
        using var endpoint = AudioEndpointHandle.Create();
        return endpoint.GetState();
    }

    public AudioState ToggleMute()
    {
        using var endpoint = AudioEndpointHandle.Create();
        var state = endpoint.GetState();
        endpoint.SetMute(!state.Muted);
        return endpoint.GetState();
    }

    public AudioState SetVolume(int volume)
    {
        using var endpoint = AudioEndpointHandle.Create();
        endpoint.SetMute(false);
        endpoint.SetVolume(volume);
        return endpoint.GetState();
    }

    private sealed class AudioEndpointHandle : IDisposable
    {
        private static readonly Guid MMDeviceEnumeratorId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private readonly object _endpointObject;
        private readonly IAudioEndpointVolume _endpoint;

        private AudioEndpointHandle(object endpointObject, IAudioEndpointVolume endpoint)
        {
            _endpointObject = endpointObject;
            _endpoint = endpoint;
        }

        public static AudioEndpointHandle Create()
        {
            var enumeratorType = Type.GetTypeFromCLSID(MMDeviceEnumeratorId, throwOnError: true)
                ?? throw new InvalidOperationException("Windows audio endpoint enumerator is unavailable.");
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device));
            try
            {
                var audioEndpointVolumeId = AudioEndpointVolumeId;
                Marshal.ThrowExceptionForHR(device.Activate(ref audioEndpointVolumeId, ClsctxAll, nint.Zero, out var endpointObject));
                return new AudioEndpointHandle(endpointObject, (IAudioEndpointVolume)endpointObject);
            }
            finally
            {
                Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
        }

        public AudioState GetState()
        {
            Marshal.ThrowExceptionForHR(_endpoint.GetMasterVolumeLevelScalar(out var volume));
            Marshal.ThrowExceptionForHR(_endpoint.GetMute(out var muted));
            return new AudioState((int)Math.Round(Math.Clamp(volume, 0, 1) * 100), muted);
        }

        public void SetMute(bool muted)
        {
            var eventContext = EmptyEventContext;
            Marshal.ThrowExceptionForHR(_endpoint.SetMute(muted, ref eventContext));
        }

        public void SetVolume(int volume)
        {
            var eventContext = EmptyEventContext;
            var scalar = Math.Clamp(volume, 0, 100) / 100f;
            Marshal.ThrowExceptionForHR(_endpoint.SetMasterVolumeLevelScalar(scalar, ref eventContext));
        }

        public void Dispose()
        {
            Marshal.ReleaseComObject(_endpointObject);
        }
    }

    private enum EDataFlow
    {
        Render = 0
    }

    private enum ERole
    {
        Multimedia = 1
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out nint devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsctx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(nint notify);

        [PreserveSig]
        int UnregisterControlChangeNotify(nint notify);

        [PreserveSig]
        int GetChannelCount(out uint channelCount);

        [PreserveSig]
        int SetMasterVolumeLevel(float level, ref Guid eventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolumeLevel(out float level);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float level, ref Guid eventContext);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float level);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
