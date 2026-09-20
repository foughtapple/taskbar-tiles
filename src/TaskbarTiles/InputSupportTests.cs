using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
namespace TaskbarTiles
{
    static class InputSupportTests
    {
        static int checks;
        static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException("FAILED: " + message); }
        internal static void Run(StringBuilder log)
        {
            checks = 0;
            for (int flags = 0; flags < 32; flags++)
            {
                bool enabled = (flags & 1) != 0, stop = (flags & 2) != 0, held = (flags & 4) != 0, old = (flags & 8) != 0, request = (flags & 16) != 0;
                Check(ShortcutPolicy.Renew(enabled, stop, held, old ? 60000 : 59999, request) == (enabled && !stop && !held && (old || request)), "idle repair respects preference/held keys/shutdown");
            }
            for (int cursor = 0; cursor < 128; cursor++)
            {
                Check(TouchSource.Classify(0xFF515700U | (uint)cursor, 0) == "Pen (promoted)", "pen signature");
                Check(TouchSource.Classify(0xFF515780U | (uint)cursor, 0) == "Touch (promoted)", "touch signature");
            }
            Check(TouchSource.Classify(0, 0) == "Mouse / unclassified", "unmarked source never certifies physical mouse");
            Check(TouchSource.Classify(0, 1) == "Injected / unknown", "unidentified injected input never certifies touch");
            for (int flags = 0; flags < 16; flags++)
                Check(TouchSource.CanArmReturn((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, (flags & 8) != 0) == (flags == 15), "no automatic return without all evidence");
            Check(Marshal.SizeOf(typeof(TouchNative.PointerInfo)) == (IntPtr.Size == 8 ? 96 : 88), "POINTER_INFO layout");
            Check(Marshal.SizeOf(typeof(TouchNative.PenInfo)) == (IntPtr.Size == 8 ? 120 : 112), "POINTER_PEN_INFO layout");
            Check(Marshal.SizeOf(typeof(TouchNative.RawDevice)) == (IntPtr.Size == 8 ? 16 : 12), "RAWINPUTDEVICE layout");
            var defaults = new Options(); Check(defaults.TouchTestMonitorKey == "", "touch monitoring is opt-in");
            var roundTrip = Options.Parse(new[] { "TouchTestMonitorKey=test-device", "InterceptAltTab=false", "AppLabelFontSize=29" });
            Check(roundTrip.TouchTestMonitorKey == "test-device" && !roundTrip.InterceptAltTab && roundTrip.AppLabelFontSize == 29, "new field preserves existing preferences");
            log.AppendLine("PASS: " + checks + " shortcut renewal/source-marker/touch safety and interop assertions. No device compatibility is claimed.");
        }
        [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        static void Wait(Func<bool> condition, int ms, string description)
        {
            var timer = Stopwatch.StartNew();
            while (!condition() && timer.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(15); }
            Check(condition(), description);
        }
        // Native fixture only; F24 has no Taskbar Tiles action and is not logged.
        internal static int NativeTest()
        {
            var log = new StringBuilder();
            try
            {
                Run(log); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new Form { Text = "Shortcut input test", ShowInTaskbar = false })
                using (var hook = new KeyboardHook(form.Handle))
                {
                    Check(hook.Installed, "hook installed on dedicated thread");
                    long before = hook.CallbackCount;
                    keybd_event(0x87, 0, 0, UIntPtr.Zero); keybd_event(0x87, 0, 2, UIntPtr.Zero);
                    Wait(() => hook.CallbackCount > before, 2500, "native callback receives pass-through F24");
                    hook.TestDetachRegistration(); before = hook.CallbackCount;
                    keybd_event(0x87, 0, 0, UIntPtr.Zero); keybd_event(0x87, 0, 2, UIntPtr.Zero);
                    Thread.Sleep(100); Check(hook.CallbackCount == before, "simulate hook removed while cached registration remains");
                    int count = hook.RegistrationCount; hook.RequestRepair();
                    Wait(() => hook.RegistrationCount > count, 4000, "repair reinstalls native registration without app restart");
                    before = hook.CallbackCount;
                    keybd_event(0x87, 0, 0, UIntPtr.Zero); keybd_event(0x87, 0, 2, UIntPtr.Zero);
                    Wait(() => hook.CallbackCount > before, 2500, "callbacks resume after repair");
                    hook.Enabled = false; count = hook.RegistrationCount; hook.RequestRepair();
                    var timer = Stopwatch.StartNew(); while (timer.ElapsedMilliseconds < 1300) { Application.DoEvents(); Thread.Sleep(20); }
                    Check(hook.RegistrationCount == count, "manual off is not automatically overridden");
                }
                log.AppendLine("PASS: real keyboard hook removal/reinstall and disabled-preference checks. No user desktop or touchscreen driver was tested.");
                File.WriteAllText(Path.Combine(Program.Home, "input-support-test.log"), log.ToString()); return 0;
            }
            catch (Exception ex) { log.AppendLine(ex.ToString()); File.WriteAllText(Path.Combine(Program.Home, "input-support-test.log"), log.ToString()); return 1; }
        }
    }
}
