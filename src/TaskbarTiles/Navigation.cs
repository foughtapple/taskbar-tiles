using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed partial class Switcher
    {
        readonly TextBox searchBox = new TextBox();
        readonly WindowMover mover = new WindowMover();
        readonly List<Rectangle> cardCloseRects = new List<Rectangle>();
        List<WindowItem> allWindows = new List<WindowItem>();
        List<AppButton> allApps = new List<AppButton>();
        Rectangle gearRect, undoRect, previewDown, previewUp;
        Form transient;
        Guid desktopAtOpen;
        bool updatingSearch;
        readonly System.Windows.Forms.Timer windowTimer = new System.Windows.Forms.Timer { Interval = 650 };
        ZoneDestination pendingDestination;
        LaunchPlacement launchPlacement;
        int headerHeight;

        WindowHeaderIcons headerIcons;
        void SetupFeatures()
        {
            headerIcons = new WindowHeaderIcons(delegate
            {
                if (closing || IsDisposed) return;
                Invalidate();
                var settings = transient as SettingsWindow;
                if (settings != null) settings.RefreshWindowIconPreview();
            });
            searchBox.BorderStyle = BorderStyle.FixedSingle; searchBox.BackColor = Color.FromArgb(30, 40, 54); searchBox.ForeColor = ForeColor;
            searchBox.Visible = false; searchBox.TextChanged += delegate { if (!updatingSearch) ApplyFilter(true); };
            Controls.Add(searchBox);
            WindowNative.SendMessage(searchBox.Handle, 0x1501, IntPtr.Zero, "Search windows or apps...   /   F5 refreshes");
            windowTimer.Tick += delegate
            {
                if (Visible && transient == null && allWindows.Any(w => !Native.IsWindow(w.Handle))) RefreshWindows();
            };
            windowTimer.Start();
        }
        string Query { get { return options.EnableSearch ? searchBox.Text.Trim() : ""; } }
        bool MatchesQuery(string value)
        {
            string text = value ?? "";
            return Query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(word => text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        void ApplyFilter(bool reset)
        {
            windows = allWindows.Where(w => MatchesQuery(w.Title) && (!options.CurrentMonitorOnly || Screen.FromHandle(w.Handle).DeviceName == Screen.FromPoint(monitorPoint).DeviceName)).ToList();
            apps = allApps.Where(a => MatchesQuery(a.DisplayName)).ToList();
            if (reset) { selected = 0; windowPage = appPage = 0; }
            selected = Math.Max(0, Math.Min(selected, windows.Count - 1));
            if (Visible && area.Width > 0) LayoutMenu();
        }
        void RefreshWindows()
        { allWindows = GetWindows(); ApplyFilter(false); }
        static string MonitorLabel(IntPtr handle)
        {
            string device = Screen.FromHandle(handle).DeviceName;
            var number = Regex.Match(device, @"DISPLAY(\d+)", RegexOptions.IgnoreCase);
            return number.Success ? "Monitor " + number.Groups[1].Value : device;
        }
        void ChangePreviewSize(int delta)
        {
            try { Options.SaveValue("PreviewScale", Math.Max(70, Math.Min(220, options.PreviewScale + delta)).ToString()); ReloadSettings(true); }
            catch (Exception ex) { Notify("Could not save preview size: " + ex.Message); }
        }
        void ShowSettings()
        {
            CancelPassiveLaunchObservation();
            if (transient != null) { transient.Activate(); return; }
            bool reopen = Visible; Dismiss();
            bool returnToMenu = SettingsCore();
            if (reopen && returnToMenu && !closing) OpenOrCycle(true, false, false);
        }
        bool SettingsCore(string initialTab = null)
        {
            allWindows = GetWindows();
            using (var form = new SettingsWindow(options, delegate(Options changed)
            {
                options = changed; ReloadSettings(true); startupItem.Checked = System.IO.File.Exists(StartupPath);
            }, CaptureMenuPreview, allApps, initialTab))
            {
                form.LayoutSummary = delegate { return lastMenuPreviewSummary; };
                transient = form;
                try { form.ShowDialog(); }
                finally { transient = null; }
                return !form.DismissedByFocusLoss && !closing;
            }
        }
        void ChooseZone(WindowItem window, AppButton app)
        {
            CancelPassiveLaunchObservation();
            if (!options.RightClickZones || transient != null) return;
            if (pending != null || launchPlacement != null || mover.Busy) { Notify("Finish the current launch or window move first."); return; }
            Point point = monitorPoint; Dismiss();
            ZoneDestination destination = null;
            while (!closing)
            {
                bool settings;
                using (var form = new ZonePicker(options, desktopAtOpen, point, app != null ? app.DisplayName : window.Title, app != null))
                {
                    transient = form;
                    try { form.ShowDialog(); }
                    finally { transient = null; }
                    destination = form.Destination; settings = form.SettingsRequested;
                }
                if (!settings) break;
                if (!SettingsCore()) return;
            }
            if (closing) return;
            if (destination == null) { OpenOrCycle(true, false, false); return; }
            if (app != null) QueueLaunch(app, destination);
            else MoveTo(window.Handle, destination, true);
        }
        void MoveTo(IntPtr window, ZoneDestination destination, bool existing)
        {
            mover.Place(window, destination, options, delegate(string error)
            {
                if (closing) return;
                if (error != null) Notify(error);
                if (existing && options.KeepOpenAfterMove) OpenOrCycle(true, false, false);
            });
        }
        void UndoMove()
        {
            if (!options.EnableUndoMove) { Notify("Undo is turned off in Settings > Navigation."); return; }
            Dismiss(); string error = mover.Undo(); if (error != null) Notify(error);
        }
        void QueueLaunch(AppButton app, ZoneDestination destination)
        {
            CancelPassiveLaunchObservation();
            if (pending != null || launchPlacement != null) { Notify("An app launch is already in progress."); return; }
            pending = new AppButton { Id=app.Id, Name=app.Name, DisplayName=app.DisplayName, ClassName=app.ClassName,
                LaunchExe=app.LaunchExe, ShortcutPath=app.ShortcutPath, VerifiedShortcut=app.VerifiedShortcut,
                Favourite=app.Favourite == null ? null : app.Favourite.Clone(), Taskbar=app.Taskbar, Bounds=app.Bounds };
            pendingDestination = destination;
            launchStart = DateTime.UtcNow; Dismiss(); launchTimer.Start();
        }
        void CloseWindowCard(int pageIndex)
        {
            int index = windowPage * perWindowPage + pageIndex;
            if (index < 0 || index >= windows.Count) return;
            IntPtr target = windows[index].Handle;
            if (!Native.IsWindow(target)) { RefreshWindows(); return; }
            // Normal close request only: apps retain their Save/Don't Save/Cancel prompts.
            // Do not remove the card until the window has actually disappeared.
            if (!WindowNative.PostMessage(target, 0x0010, IntPtr.Zero, IntPtr.Zero))
                Notify("Windows would not send the close request. Close an elevated/protected app using its own title-bar X.");
        }
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (integratedSearch != null && integratedSearch.Visible) { base.OnKeyPress(e); return; }
            if (options.EnableSearch && !searchBox.Focused && !char.IsControl(e.KeyChar) && !Native.Down(0x11) && !Native.Down(0x12))
            { searchBox.Focus(); searchBox.AppendText(e.KeyChar.ToString()); e.Handled = true; }
            base.OnKeyPress(e);
        }
        void ShutdownFeatures()
        {
            windowTimer.Stop(); windowTimer.Dispose(); mover.Dispose();
            if (headerIcons != null) headerIcons.Dispose();
            if (launchPlacement != null) { launchPlacement.Dispose(); launchPlacement = null; }
            if (transient != null) transient.Close();
        }
    }
}
