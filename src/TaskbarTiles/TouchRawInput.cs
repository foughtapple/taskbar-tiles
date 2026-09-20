// Passive Raw Input digitizer reader. No RIDEV_NOLEGACY, pointer redirection,
// capture, injection, replay, foreground changes or device-feature writes.
// Unsupported/virtual mouse-only paths remain detection-only.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class TouchHidNative
    {
        internal const int Success = 0x110000;
        [StructLayout(LayoutKind.Sequential)] internal struct DeviceRegistration { internal ushort Page, Usage; internal uint Flags; internal IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] internal struct DeviceList { internal IntPtr Device; internal uint Type; }
        [DllImport("user32.dll", SetLastError=true)] internal static extern bool RegisterRawInputDevices(DeviceRegistration[] devices, uint count, uint size);
        [DllImport("user32.dll")] internal static extern uint GetRawInputData(IntPtr raw, uint command, IntPtr data, ref uint size, uint header);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
        [DllImport("user32.dll")] internal static extern uint GetRawInputDeviceList([Out] DeviceList[] devices, ref uint count, uint size);
        [DllImport("user32.dll")] internal static extern int GetMessageTime();
        [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr data, [Out] byte[] caps);
        [DllImport("hid.dll")] internal static extern int HidP_GetValueCaps(int type, [Out] byte[] caps, ref ushort count, IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetButtonCaps(int type, [Out] byte[] caps, ref ushort count, IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, IntPtr data, byte[] report, uint length);
        [DllImport("hid.dll")] internal static extern int HidP_GetUsages(int type, ushort page, ushort link, [Out] ushort[] usages, ref uint count, IntPtr data, byte[] report, uint length);
        internal static string Hash(string value)
        { using (var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", ""); }
    }
    sealed class TouchHidDevice : IDisposable
    {
        sealed class Cap
        {
            internal ushort Page, Link, Usage; internal byte Report; internal bool Button; internal int Min, Max;
        }
        readonly List<Cap> caps = new List<Cap>();
        readonly TouchFrameAssembler assembler = new TouchFrameAssembler();
        IntPtr parsed;
        int length, reportId;
        List<Cap> xs;
        Cap count;
        internal readonly TouchDeviceEvidence Evidence = new TouchDeviceEvidence();
        internal TouchHidDevice(IntPtr device)
        {
            uint size=0;
            TouchHidNative.GetRawInputDeviceInfo(device,0x20000007,IntPtr.Zero,ref size);
            if(size==0 || size>32768) throw new InvalidOperationException("No stable input device identity.");
            IntPtr name=Marshal.AllocHGlobal(checked((int)(size+1)*2)); string path;
            try { if(TouchHidNative.GetRawInputDeviceInfo(device,0x20000007,name,ref size)==uint.MaxValue) throw new InvalidOperationException(); path=Marshal.PtrToStringUni(name,(int)size).TrimEnd('\0'); }
            finally { Marshal.FreeHGlobal(name); }
            size=0; TouchHidNative.GetRawInputDeviceInfo(device,0x20000005,IntPtr.Zero,ref size);
            if(size==0 || size>1048576) throw new InvalidOperationException("Digitizer metadata unavailable.");
            parsed=Marshal.AllocHGlobal((int)size);
            try
            {
                if(TouchHidNative.GetRawInputDeviceInfo(device,0x20000005,parsed,ref size)==uint.MaxValue) throw new InvalidOperationException();
                var general=new byte[64]; if(TouchHidNative.HidP_GetCaps(parsed,general)!=TouchHidNative.Success) throw new InvalidOperationException();
                ushort usage=BitConverter.ToUInt16(general,0), page=BitConverter.ToUInt16(general,2);
                if(page!=13 || (usage!=1 && usage!=2 && usage!=4)) throw new InvalidOperationException("Not a touchscreen or pen collection.");
                Evidence.Kind=usage==4?"Touch":"Pen"; length=BitConverter.ToUInt16(general,4);
                Evidence.Key=TouchHidNative.Hash((path??"").ToUpperInvariant()+"|"+page+"|"+usage+"|"+length);
                Evidence.Name="device " + Evidence.Key.Substring(0,8);
                ReadCaps(BitConverter.ToUInt16(general,48),false); ReadCaps(BitConverter.ToUInt16(general,46),true);
                xs=caps.Where(c=>!c.Button && c.Page==1 && c.Usage==0x30).ToList();
                if(xs.Count==0 || xs.Count>256 || xs.Select(c=>c.Report).Distinct().Count()!=1) { Evidence.Problem="Unsupported coordinate report layout"; return; }
                reportId=xs[0].Report;
                count=caps.FirstOrDefault(c=>!c.Button && c.Page==13 && c.Usage==0x54 && c.Report==reportId);
                if(Evidence.Kind=="Touch" && count==null) { Evidence.Problem="Contact-count field unavailable; cannot prove all contacts ended"; return; }
                if(Evidence.Kind=="Pen" && xs.Count!=1) { Evidence.Problem="Multi-pen report layout not yet supported"; return; }
                foreach(var x in xs)
                    if(x.Max<=x.Min || Find(x,1,0x31)==null || Find(x,13,0x42)==null || (Evidence.Kind=="Touch" && Find(x,13,0x51)==null))
                    { Evidence.Problem="Contact ID, tip or coordinate metadata missing"; return; }
                Evidence.HoverKnown=Evidence.Kind=="Pen" && Find(xs[0],13,0x32)!=null;
                Evidence.Supported=true; Evidence.Problem="Waiting for passive contact/release test";
            }
            catch { Dispose(); throw; }
        }
        Cap Find(Cap slot, ushort page, ushort usage)
        { return caps.FirstOrDefault(c=>c.Page==page && c.Usage==usage && c.Link==slot.Link && c.Report==slot.Report); }
        void ReadCaps(ushort count,bool buttons)
        {
            if(count==0) return; if(count>4096) throw new InvalidOperationException("Excessive report metadata.");
            var data=new byte[count*72];
            int result=buttons?TouchHidNative.HidP_GetButtonCaps(0,data,ref count,parsed):TouchHidNative.HidP_GetValueCaps(0,data,ref count,parsed);
            if(result!=TouchHidNative.Success) throw new InvalidOperationException("Could not read report metadata.");
            for(int i=0;i<count;i++)
            {
                int o=i*72; ushort min=BitConverter.ToUInt16(data,o+56), max=data[o+12]!=0?BitConverter.ToUInt16(data,o+58):min;
                if(max<min || max-min>512) continue;
                for(int u=min;u<=max;u++) caps.Add(new Cap { Page=BitConverter.ToUInt16(data,o),Report=data[o+2],Link=BitConverter.ToUInt16(data,o+6),
                    Usage=(ushort)u,Button=buttons,Min=buttons?0:BitConverter.ToInt32(data,o+40),Max=buttons?1:BitConverter.ToInt32(data,o+44) });
            }
        }
        uint Value(Cap cap,byte[] report)
        {
            if(cap==null) throw new InvalidOperationException("Missing report field.");
            if(cap.Button)
            {
                uint count=128; var list=new ushort[count];
                if(TouchHidNative.HidP_GetUsages(0,cap.Page,cap.Link,list,ref count,parsed,report,(uint)report.Length)!=TouchHidNative.Success) throw new InvalidOperationException("Unreliable tip/range state.");
                return list.Take((int)count).Contains(cap.Usage)?1u:0u;
            }
            uint result;
            if(TouchHidNative.HidP_GetUsageValue(0,cap.Page,cap.Link,cap.Usage,out result,parsed,report,(uint)report.Length)!=TouchHidNative.Success) throw new InvalidOperationException("Unreliable contact value.");
            return result;
        }
        double Coordinate(Cap cap,byte[] report)
        {
            long n=Value(cap,report); if(cap.Min<0) n=unchecked((int)n);
            if(n<cap.Min || n>cap.Max || cap.Max<=cap.Min) throw new InvalidOperationException("Invalid contact coordinate.");
            return (n-cap.Min)/(double)(cap.Max-cap.Min);
        }
        internal TouchFrame Read(byte[] report,uint tick)
        {
            if(!Evidence.Supported) return null;
            if(report.Length!=length) throw new InvalidOperationException("Unexpected report length.");
            if(report.Length==0 || report[0]!=reportId) return null;
            bool pen=Evidence.Kind=="Pen";
            int total=pen?1:checked((int)Value(count,report));
            var slots=new List<TouchContact>();
            // Continuation packets have a zero count. The assembler, not the idle
            // timer, decides when a complete hybrid frame has arrived.
            int take=total==0?xs.Count:Math.Min(total,xs.Count);
            foreach(var x in xs.Take(take))
            {
                bool down=Value(Find(x,13,0x42),report)!=0;
                slots.Add(new TouchContact { Id=pen?0:checked((int)Value(Find(x,13,0x51),report)),Down=down,
                    X=down?Coordinate(x,report):0,Y=down?Coordinate(Find(x,1,0x31),report):0 });
            }
            var contacts=pen?slots:assembler.Feed(total,slots);
            bool hover=pen && Evidence.HoverKnown && Value(Find(xs[0],13,0x32),report)!=0;
            return new TouchFrame { Device=Evidence.Key,Pen=pen,HoverKnown=Evidence.HoverKnown,Hover=hover,Complete=contacts!=null,
                Contacts=contacts??new List<TouchContact> { new TouchContact { Id=-1,Down=true } },Tick=tick };
        }
        public void Dispose() { if(parsed!=IntPtr.Zero) { Marshal.FreeHGlobal(parsed); parsed=IntPtr.Zero; } }
    }
    sealed class RawTouchSource : NativeWindow, IDisposable
    {
        readonly Dictionary<IntPtr,TouchHidDevice> devices=new Dictionary<IntPtr,TouchHidDevice>();
        readonly HashSet<IntPtr> rejected=new HashSet<IntPtr>();
        readonly Action<TouchFrame> frame;
        readonly Action<string> invalid;
        bool disposed;
        internal int PhysicalVersion, TypingVersion;
        internal uint LastDigitizerTick;
        internal bool Registered { get; private set; }
        internal string Status="Not listening";
        internal IEnumerable<TouchDeviceEvidence> Devices { get { return devices.Values.Select(d=>d.Evidence).ToArray(); } }
        internal RawTouchSource(Action<TouchFrame> onFrame,Action<string> onInvalid)
        {
            frame=onFrame; invalid=onInvalid;
            CreateHandle(new CreateParams { Caption="Taskbar Tiles passive digitizer input",Parent=new IntPtr(-3) });
            var registrations=new ushort[] {1,2,4}.Select(u=>new TouchHidNative.DeviceRegistration {Page=13,Usage=u,Flags=0x100|0x2000,Target=Handle}).Concat(new[] { new TouchHidNative.DeviceRegistration {Page=1,Usage=2,Flags=0x100|0x2000,Target=Handle}, new TouchHidNative.DeviceRegistration {Page=1,Usage=6,Flags=0x100|0x2000,Target=Handle} }).ToArray();
            Registered=TouchHidNative.RegisterRawInputDevices(registrations,(uint)registrations.Length,(uint)Marshal.SizeOf(typeof(TouchHidNative.DeviceRegistration)));
            Status=Registered?"Passive Raw Input active; no touch input is intercepted":"Raw Input registration failed; automatic return unavailable";
            Scan();
        }
        internal void Scan()
        {
            if(disposed) return;
            uint count=0,size=(uint)Marshal.SizeOf(typeof(TouchHidNative.DeviceList));
            if(TouchHidNative.GetRawInputDeviceList(null,ref count,size)==uint.MaxValue || count>4096) return;
            var list=new TouchHidNative.DeviceList[count]; uint n=TouchHidNative.GetRawInputDeviceList(list,ref count,size); if(n==uint.MaxValue) return;
            foreach(var d in list.Take((int)n).Where(d=>d.Type==2)) GetDevice(d.Device);
        }
        TouchHidDevice GetDevice(IntPtr h)
        {
            TouchHidDevice d; if(devices.TryGetValue(h,out d)) return d; if(rejected.Contains(h)) return null;
            try { d=new TouchHidDevice(h); devices[h]=d; return d; }
            catch { rejected.Add(h); return null; }
        }
        protected override void WndProc(ref Message m)
        {
            try { ReadMessage(ref m); }
            finally { base.WndProc(ref m); }
        }
        void ReadMessage(ref Message m)
        {
            if(m.Msg==0x00FE)
            {
                invalid("input device connected/disconnected; pending return discarded");
                if(m.WParam.ToInt32()==2) { TouchHidDevice old; if(devices.TryGetValue(m.LParam,out old)) { old.Dispose(); devices.Remove(m.LParam); } rejected.Remove(m.LParam); }
                else GetDevice(m.LParam);
            }
            if(m.Msg==0x00FF && !disposed)
            {
                IntPtr buffer=IntPtr.Zero;
                try
                {
                    uint size=0,header=(uint)(8+2*IntPtr.Size);
                    if(TouchHidNative.GetRawInputData(m.LParam,0x10000003,IntPtr.Zero,ref size,header)==uint.MaxValue || size<header+8 || size>1048576) return;
                    buffer=Marshal.AllocHGlobal((int)size);
                    if(TouchHidNative.GetRawInputData(m.LParam,0x10000003,buffer,ref size,header)==uint.MaxValue ) return;
                    int type=Marshal.ReadInt32(buffer);
                    if(type==0)
                    {
                        if(size<header+24) return;
                        uint extra=unchecked((uint)Marshal.ReadInt32(buffer,(int)header+20));
                        if((extra & 0xFFFFFF00u)!=0xFF515700u && (Marshal.ReadInt32(buffer,(int)header+12)!=0 || Marshal.ReadInt32(buffer,(int)header+16)!=0 || Marshal.ReadInt16(buffer,(int)header+4)!=0)) PhysicalVersion++;
                        return;
                    }
                    if(type==1)
                    {
                        if(size<header+16) return; int key=Marshal.ReadInt16(buffer,(int)header+6);
                        if((Marshal.ReadInt16(buffer,(int)header+2)&1)==0 && !TouchShortcutKeys.IsShortcut(key) && key!=0x10&&key!=0x11&&key!=0x12&&!(key>=0xA0&&key<=0xA5)) TypingVersion++;
                        return;
                    }
                    if(type!=2)return;
                    IntPtr handle=Marshal.ReadIntPtr(buffer,8); var d=GetDevice(handle); if(d==null) return;
                    if(!d.Evidence.Supported){invalid("unclassified digitizer activity; automatic return blocked");return;}
                    int bytes=Marshal.ReadInt32(buffer,(int)header),count=Marshal.ReadInt32(buffer,(int)header+4);
                    if(bytes<=0 || count<0 || count>4096 || (long)bytes*count>size-header-8) throw new InvalidOperationException("Invalid HID packet size.");
                    for(int i=0;i<count;i++)
                    {
                        var data=new byte[bytes]; Marshal.Copy(IntPtr.Add(buffer,(int)header+8+i*bytes),data,0,bytes);
                        var f=d.Read(data,unchecked((uint)TouchHidNative.GetMessageTime()));
                        if(f!=null) { LastDigitizerTick=f.Tick; d.Evidence.Observe(f,Environment.TickCount & int.MaxValue); frame(f); }
                    }
                }
                catch(Exception ex) { Status="Input rejected: "+ex.Message; invalid("incomplete or unsupported input report"); }
                finally { if(buffer!=IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
            }

        }
        public void Dispose()
        {
            if(disposed) return; disposed=true;
            var remove=new ushort[] {1,2,4}.Select(u=>new TouchHidNative.DeviceRegistration {Page=13,Usage=u,Flags=1,Target=IntPtr.Zero}).Concat(new[] { new TouchHidNative.DeviceRegistration {Page=1,Usage=2,Flags=1}, new TouchHidNative.DeviceRegistration {Page=1,Usage=6,Flags=1} }).ToArray();
            if(Registered) TouchHidNative.RegisterRawInputDevices(remove,(uint)remove.Length,(uint)Marshal.SizeOf(typeof(TouchHidNative.DeviceRegistration)));
            foreach(var d in devices.Values) d.Dispose(); devices.Clear(); DestroyHandle();
        }
    }
}
