// Taskbar Tiles 0.6.2. Header geometry is shared by real cards and live previews.
// Icon extraction never runs in a paint handler, keyboard hook or mouse hook.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace TaskbarTiles
{
    sealed class WindowHeaderGeometry
    {
        internal Rectangle Icon, Title, Close;
        internal static WindowHeaderGeometry Build(Rectangle card, Options o, float scale)
        {
            Func<int, int> s = n => Math.Max(1, (int)Math.Round(n * scale));
            int band = Math.Min(card.Height, s(MenuTextMetrics.TitleBand(o)));
            var g = new WindowHeaderGeometry();
            g.Close = new Rectangle(card.Right - s(31), card.Top + (band - s(24)) / 2, s(24), s(24));
            int left = card.Left + s(12), right = o.ShowCloseButtons ? g.Close.Left - s(6) : card.Right - s(12);
            if (o.ShowWindowTitleIcons)
            {
                int size = Math.Min(s(o.WindowTitleIconSize), Math.Min(band - s(8), right - left - s(30)));
                if (size > 0)
                {
                    g.Icon = new Rectangle(left, card.Top + (band - size) / 2, size, size);
                    left = g.Icon.Right + s(8);
                }
            }
            g.Title = new Rectangle(left, card.Top + s(2), Math.Max(1, right - left), Math.Max(1, band - s(4)));
            return g;
        }
        internal static void PaintFallback(Graphics g, Rectangle box, Color ink)
        {
            // Neutral application-window symbol; never substitute another app's identity.
            float line = Math.Max(1, box.Width / 20f);
            var r = Rectangle.Inflate(box, -Math.Max(1, box.Width / 8), -Math.Max(1, box.Height / 8));
            using (var p = new Pen(ink, line))
            {
                g.DrawRectangle(p, r);
                g.DrawLine(p, r.Left, r.Top + r.Height * .28f, r.Right, r.Top + r.Height * .28f);
            }
        }
    }

    sealed class WindowHeaderIcons : IDisposable
    {
        sealed class RequestItem { internal IntPtr Handle; internal uint Pid; }
        sealed class Result { internal IntPtr Handle; internal uint Pid; internal Bitmap Image; }
        sealed class Entry { internal uint Pid; internal Bitmap Image; internal DateTime Checked, Used; }
        readonly Dictionary<IntPtr, Entry> cache = new Dictionary<IntPtr, Entry>();
        readonly Dictionary<IntPtr, uint> pending = new Dictionary<IntPtr, uint>();
        readonly BlockingCollection<RequestItem> jobs = new BlockingCollection<RequestItem>(128);
        readonly ConcurrentQueue<Result> results = new ConcurrentQueue<Result>();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        readonly object gate = new object();
        readonly Action changed;
        volatile bool stopped;

        internal WindowHeaderIcons(Action onChange)
        {
            changed = onChange;
            var worker = new Thread(ReadJobs) { IsBackground = true, Name = "Window header icon reader" };
            worker.SetApartmentState(ApartmentState.STA); worker.Start();
            timer.Tick += delegate { Drain(); }; timer.Start();
        }
        internal void Request(IEnumerable<WindowItem> windows)
        {
            if (stopped) return;
            foreach (var window in windows)
            {
                IntPtr h = window.Handle; if (h == IntPtr.Zero) continue;
                uint pid = WindowNative.ProcessId(h); if (pid == 0) continue;
                Entry value; DateTime now = DateTime.UtcNow;
                if (cache.TryGetValue(h, out value))
                {
                    value.Used = now;
                    if (value.Pid == pid && (now - value.Checked).TotalSeconds < 15) continue;
                    if (value.Pid != pid) { if (value.Image != null) value.Image.Dispose(); cache.Remove(h); }
                }
                if (pending.ContainsKey(h)) continue;
                if (jobs.TryAdd(new RequestItem { Handle = h, Pid = pid })) pending[h] = pid;
            }
        }
        // Borrowed bitmap: owned by this cache. Called only from the UI thread.
        internal Bitmap Get(IntPtr handle)
        { Entry value; return !stopped && cache.TryGetValue(handle, out value) ? value.Image : null; }
        void ReadJobs()
        {
            try
            {
                foreach (var request in jobs.GetConsumingEnumerable())
                {
                    if (stopped) break;
                    Bitmap image = null;
                    try
                    {
                        if (Native.IsWindow(request.Handle) && WindowNative.ProcessId(request.Handle) == request.Pid)
                        {
                            image = ShellIcons.WindowIcon(request.Handle);
                            if (image == null) image = WindowHeaderNative.ClassIcon(request.Handle);
                            if (image == null)
                            {
                                string appId = ShellIcons.WindowAppId(request.Handle);
                                if (!string.IsNullOrWhiteSpace(appId)) image = ShellIcons.Extract(@"shell:AppsFolder\" + appId, 128);
                            }
                            if (image == null)
                            {
                                string exe = ShellIcons.ProcessFile(request.Handle);
                                if (!string.IsNullOrWhiteSpace(exe)) image = ShellIcons.Extract(exe, 128);
                            }
                        }
                    }
                    catch (Exception ex) { Program.Log("Window header icon: " + ex.Message); }
                    lock (gate)
                    {
                        if (stopped) { if (image != null) image.Dispose(); }
                        else results.Enqueue(new Result { Handle = request.Handle, Pid = request.Pid, Image = image });
                    }
                }
            }
            catch (Exception ex) { if (!stopped) Program.Log("Window icon worker: " + ex.Message); }
            finally { jobs.Dispose(); }
        }
        void Drain()
        {
            if (stopped) return;
            Result result; bool any = false;
            while (results.TryDequeue(out result))
            {
                pending.Remove(result.Handle);
                if (!Native.IsWindow(result.Handle) || WindowNative.ProcessId(result.Handle) != result.Pid)
                { if (result.Image != null) result.Image.Dispose(); continue; }
                Entry old;
                if (cache.TryGetValue(result.Handle, out old) && old.Image != null) old.Image.Dispose();
                cache[result.Handle] = new Entry { Pid = result.Pid, Image = result.Image, Checked = DateTime.UtcNow, Used = DateTime.UtcNow };
                any = true;
            }
            // Bound GDI/image resources across long-running sessions.
            foreach (var key in cache.OrderBy(p => p.Value.Used).Take(Math.Max(0, cache.Count - 128)).Select(p => p.Key).ToArray())
            { if (cache[key].Image != null) cache[key].Image.Dispose(); cache.Remove(key); }
            if (any && changed != null) changed();
        }
        public void Dispose()
        {
            if (stopped) return;
            lock (gate)
            {
                stopped = true; jobs.CompleteAdding();
                Result result;
                while (results.TryDequeue(out result)) if (result.Image != null) result.Image.Dispose();
            }
            timer.Stop(); timer.Dispose();
            foreach (var entry in cache.Values) if (entry.Image != null) entry.Image.Dispose();
            cache.Clear(); pending.Clear();
            // Do not wait for shell extensions or hung external windows on the UI thread.
        }
    }
    static class WindowHeaderNative
    {
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr ClassLong64(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "GetClassLongW")] static extern uint ClassLong32(IntPtr h, int index);
        internal static Bitmap ClassIcon(IntPtr h)
        {
            foreach (int index in new[] { -14, -34 })
            {
                IntPtr icon = IntPtr.Size == 8 ? ClassLong64(h, index) : new IntPtr(unchecked((int)ClassLong32(h, index)));
                if (icon == IntPtr.Zero) continue;
                try
                {
                    // A class icon is borrowed. Dispose only our bitmap and cloned icon.
                    using (var borrowed = Icon.FromHandle(icon))
                    using (var own = (Icon)borrowed.Clone())
                    using (var image = own.ToBitmap()) return ShellIcons.Trim(image);
                }
                catch { }
            }
            return null;
        }
    }
}
