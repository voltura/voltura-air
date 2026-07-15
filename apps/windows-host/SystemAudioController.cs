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
        private bool _disposed;

        private AudioEndpointHandle(object endpointObject, IAudioEndpointVolume endpoint)
        {
            _endpointObject = endpointObject;
            _endpoint = endpoint;
        }

        public static AudioEndpointHandle Create()
        {
            object? enumeratorObject = null;
            IMMDevice? device = null;
            object? endpointObject = null;
            try
            {
                var enumeratorType = Type.GetTypeFromCLSID(MMDeviceEnumeratorId, throwOnError: true)
                    ?? throw new InvalidOperationException("Windows audio endpoint enumerator is unavailable.");
                enumeratorObject = Activator.CreateInstance(enumeratorType)
                    ?? throw new InvalidOperationException("Windows audio endpoint enumerator could not be created.");
                var enumerator = (IMMDeviceEnumerator)enumeratorObject;
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device));
                var audioEndpointVolumeId = AudioEndpointVolumeId;
                Marshal.ThrowExceptionForHR(device.Activate(ref audioEndpointVolumeId, ClsctxAll, nint.Zero, out endpointObject));
                var handle = new AudioEndpointHandle(endpointObject, (IAudioEndpointVolume)endpointObject);
                endpointObject = null;
                return handle;
            }
            finally
            {
                ReleaseComObject(endpointObject);
                ReleaseComObject(device);
                ReleaseComObject(enumeratorObject);
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseComObject(_endpointObject);
        }

        private static void ReleaseComObject(object? value)
        {
            try
            {
                if (value is not null && Marshal.IsComObject(value))
                {
                    _ = Marshal.ReleaseComObject(value);
                }
            }
            catch (InvalidComObjectException)
            {
            }
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

    // This interface is activated through the classic COM path below; generated COM interop
    // would require changing the activation and lifetime model together.
#pragma warning disable SYSLIB1096
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
#pragma warning restore SYSLIB1096
}
