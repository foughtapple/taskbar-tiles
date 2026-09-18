// Scope focus loss to the whole Settings session, not individual child controls.
// Nothing in this file calls Apply/Commit/Save or launches/reopens the switcher.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class SettingsFocusPolicy
    {
        internal static bool IsOutside(bool enabled, bool armed, bool closing, bool hasForeground,
            uint foregroundPid, uint currentPid, bool ownedBySettings)
        {
            return enabled && armed && !closing && hasForeground && foregroundPid != 0 &&
                foregroundPid != currentPid && !ownedBySettings;
        }
    }
    sealed partial class SettingsWindow
    {
        readonly Timer dismissTimer = new Timer { Interval = 80 };
        readonly uint settingsProcess = (uint)Process.GetCurrentProcess().Id;
        bool dismissOnFocusLoss, dismissalArmed, closingSettings;
        DateTime? outsideSince;
        internal bool DismissedByFocusLoss { get; private set; }
        void SetupSettingsDismissal(bool enabled)
        {
            dismissOnFocusLoss = enabled;
            Activated += delegate { if (!DismissedByFocusLoss) { dismissalArmed = true; outsideSince = null; } };
            Shown += delegate { dismissTimer.Start(); };
            FormClosed += delegate { closingSettings = true; dismissTimer.Stop(); };
            dismissTimer.Tick += delegate { CheckSettingsFocus(); };
        }
        internal void RefreshWindowIconPreview()
        { if (!IsDisposed && !DismissedByFocusLoss && previewView != null && previewView.SelectedIndex == 0) QueuePreview(); }
        bool IsOwnedBySettings(IntPtr handle)
        { return OwnerDepth(handle) > 0; }
        int OwnerDepth(IntPtr handle)
        {
            if (!IsHandleCreated || handle == IntPtr.Zero) return 0;
            var seen = new HashSet<IntPtr>();
            for (int depth = 1; depth <= 32 && handle != IntPtr.Zero && seen.Add(handle); depth++)
            {
                if (handle == Handle) return depth;
                handle = Native.GetWindow(handle, 4); // GW_OWNER, not a guessed process/title match.
            }
            return 0;
        }
        void CheckSettingsFocus()
        {
            if (IsDisposed || closingSettings) return;
            if (DismissedByFocusLoss) { FinishOutsideDismissal(); return; }
            if (!Visible) { outsideSince = null; return; }
            IntPtr foreground = Native.GetForegroundWindow();
            uint pid = foreground == IntPtr.Zero ? 0 : WindowNative.ProcessId(foreground);
            bool own = foreground != IntPtr.Zero && (pid == settingsProcess || IsOwnedBySettings(foreground));
            if (own) { dismissalArmed = true; outsideSince = null; return; }
            if (!SettingsFocusPolicy.IsOutside(dismissOnFocusLoss, dismissalArmed, closingSettings,
                foreground != IntPtr.Zero, pid, settingsProcess, own)) { outsideSince = null; return; }
            // Brief null/transition focus and opening an owned file picker must not
            // accidentally discard a draft. Require settled external activation.
            DateTime now = DateTime.UtcNow;
            if (!outsideSince.HasValue) { outsideSince = now; return; }
            if ((now - outsideSince.Value).TotalMilliseconds < 120) return;
            DismissedByFocusLoss = true;
            previewTimer.Stop(); settingHints.RemoveAll();
            FinishOutsideDismissal();
        }
        void FinishOutsideDismissal()
        {
            if (IsDisposed || closingSettings) return;
            // Unwind the deepest owned modal first. This includes the large preview,
            // favourite editor, installed-app picker and native Open/Save dialogs.
            // Waiting for each modal to unwind avoids disposing controls while its
            // ShowDialog callback is still executing.
            var children = new List<Tuple<IntPtr, int>>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                if (h != Handle && Native.IsWindowVisible(h))
                { int depth = OwnerDepth(h); if (depth > 0) children.Add(Tuple.Create(h, depth)); }
                return true;
            }, IntPtr.Zero);
            foreach (var child in children.OrderByDescending(c => c.Item2))
            {
                var form = Control.FromHandle(child.Item1) as Form;
                if (form != null && !form.IsDisposed)
                { form.DialogResult = DialogResult.Cancel; form.Close(); }
                else WindowNative.PostMessage(child.Item1, 0x0010, IntPtr.Zero, IntPtr.Zero); // Normal WM_CLOSE.
                return;
            }
            dismissTimer.Stop();
            DialogResult = DialogResult.Cancel;
            Close();
        }
        void DisposeSettingsDismissal()
        { closingSettings = true; dismissTimer.Stop(); dismissTimer.Dispose(); }
    }
}
