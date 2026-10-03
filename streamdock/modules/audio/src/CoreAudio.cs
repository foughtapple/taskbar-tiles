// Windows Core Audio endpoint control only: no audio client, capture stream or speaker writes.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TaskbarTilesAudio
{
    sealed class CoreAudioBackend : IAudioBackend
    {
        internal const int Capture = 1, Multimedia = 1;
        static readonly Guid VolumeInterface = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        static readonly Guid EventContext = new Guid("BD9C4BA1-8690-40BD-B675-4CC376067021");
        readonly Action signal;
        IMMDeviceEnumerator enumerator;
        DeviceNotifications devices;
        NativeEndpoint endpoint;
        long revision;
        internal CoreAudioBackend(Action signal) { this.signal = signal; }
        public long Revision { get { return Interlocked.Read(ref revision); } }
        void Changed(bool deviceChange)
        {
            if (deviceChange) Interlocked.Increment(ref revision);
            try { signal(); } catch { } // Callbacks only signal; never block or release COM here.
        }
        internal static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
        internal static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        public void Start()
        {
            if (enumerator != null) return;
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            devices = new DeviceNotifications(Changed);
            try { Check(enumerator.RegisterEndpointNotificationCallback(devices)); }
            catch { Release(enumerator); enumerator = null; devices = null; throw; }
        }
        public IAudioEndpoint OpenDefault()
        {
            Start();
            IMMDevice device = null;
            try {
                int hr = enumerator.GetDefaultAudioEndpoint(Capture, Multimedia, out device);
                if (hr == unchecked((int)0x80070490)) { ResetEndpoint(); return null; }
                Check(hr);
                string id; Check(device.GetId(out id));
                uint state; Check(device.GetState(out state));
                if ((state & 1) == 0) { ResetEndpoint(); return null; }
                if (endpoint != null && endpoint.Id == id) return endpoint;
                ResetEndpoint();
                object activated; Guid iid = VolumeInterface;
                Check(device.Activate(ref iid, 23, IntPtr.Zero, out activated));
                var volume = (IAudioEndpointVolume)activated;
                try { endpoint = new NativeEndpoint(id, volume, () => Changed(false)); }
                catch { Release(volume); throw; }
                return endpoint;
            } finally { Release(device); }
        }
        void ResetEndpoint() { if (endpoint != null) { endpoint.Dispose(); endpoint = null; Changed(true); } }
        public void Invalidate() { Changed(true); Stop(); }
        public void Stop()
        {
            ResetEndpoint();
            if (enumerator != null) {
                try { if (devices != null) enumerator.UnregisterEndpointNotificationCallback(devices); } catch { }
                finally { Release(enumerator); enumerator = null; devices = null; }
            }
        }
        public void Dispose() { Stop(); }
        sealed class NativeEndpoint : IAudioEndpoint, IDisposable
        {
            readonly string id;
            IAudioEndpointVolume volume;
            readonly VolumeNotifications notifications;
            public string Id { get { return id; } }
            internal NativeEndpoint(string id, IAudioEndpointVolume volume, Action changed)
            {
                this.id = id; this.volume = volume; notifications = new VolumeNotifications(changed);
                Check(volume.RegisterControlChangeNotify(notifications));
            }
            public bool ReadMute() { bool muted; Check(volume.GetMute(out muted)); return muted; }
            public void WriteMute(bool muted) { Guid context = EventContext; Check(volume.SetMute(muted, ref context)); }
            public void Dispose() {
                if (volume == null) return;
                try { volume.UnregisterControlChangeNotify(notifications); } catch { }
                finally { Release(volume); volume = null; }
            }
        }
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class DeviceNotifications : IMMNotificationClient
    {
        readonly Action<bool> changed;
        internal DeviceNotifications(Action<bool> changed) { this.changed = changed; }
        public int OnDeviceStateChanged(string id, uint state) { changed(true); return 0; }
        public int OnDeviceAdded(string id) { changed(false); return 0; }
        public int OnDeviceRemoved(string id) { changed(true); return 0; }
        public int OnDefaultDeviceChanged(int flow, int role, string id) { if (flow == CoreAudioBackend.Capture && role == CoreAudioBackend.Multimedia) changed(true); return 0; }
        public int OnPropertyValueChanged(string id, PropertyKey key) { changed(false); return 0; }
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class VolumeNotifications : IAudioEndpointVolumeCallback
    {
        readonly Action changed;
        internal VolumeNotifications(Action changed) { this.changed = changed; }
        public int OnNotify(IntPtr data) { changed(); return 0; }
    }
    [StructLayout(LayoutKind.Sequential)] public struct PropertyKey { public Guid FormatId; public uint Id; }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }
    [ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback { [PreserveSig] int OnNotify(IntPtr data); }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint count);
        [PreserveSig] int VolumeStepUp(ref Guid context);
        [PreserveSig] int VolumeStepDown(ref Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float min, out float max, out float increment);
    }
}
