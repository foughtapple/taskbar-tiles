// Seven separately compiled, directly runnable Windows applications.
// C# 5 / .NET Framework 4.x. No shell-shortcut arguments or custom icons.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DockUtilities
{
    internal static class DirectButtons
    {
#if BUTTON01
        internal const int Button = 1;
        internal const string Title = "01 - Rocket League - Open Close";
        internal const string GameMode = "RocketToggle";
#elif BUTTON02
        internal const int Button = 2;
        internal const string Title = "02 - Overwatch - Open Close";
        internal const string GameMode = "OverwatchToggle";
#elif BUTTON03
        internal const int Button = 3;
        internal const string Title = "03 - Switch profiles";
        internal const string GameMode = "Switch";
#elif BUTTON04
        internal const int Button = 4;
        internal const string Title = "04 - Switch profiles and Rocket League";
        internal const string GameMode = "SwitchPlay";
#elif BUTTON05
        internal const int Button = 5;
        internal const string Title = "05 - FancyZone Screenshot";
        internal const string GameMode = "";
#elif BUTTON06
        internal const int Button = 6;
        internal const string Title = "06 - Clipboard History";
        internal const string GameMode = "";
#elif BUTTON07
        internal const int Button = 7;
        internal const string Title = "07 - GPT Voice - Pet";
        internal const string GameMode = "";
#else
#error A BUTTON01 through BUTTON07 compile constant is required.
#endif
        internal const string VoiceKeys = "Ctrl+Alt+Shift+F";
        internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        internal static readonly string Logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"FoughtApple\StreamDockControls\Logs");
        internal static readonly string Games = Path.Combine(Root, @"Engine\Games");
        private static string logFile;
        private static readonly object logLock = new object();

        private static void Log(string text)
        {
            lock (logLock)
            {
                try { if (File.Exists(logFile) && new FileInfo(logFile).Length > 262144) File.WriteAllText(logFile, ""); File.AppendAllText(logFile, DateTimeOffset.Now.ToString("o") + " " + text + Environment.NewLine, new UTF8Encoding(false)); }
                catch { }
            }
        }
        private static string PowerShellPath()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string normal = Path.Combine(windows, @"System32\WindowsPowerShell\v1.0\powershell.exe");
            string native = Path.Combine(windows, @"Sysnative\WindowsPowerShell\v1.0\powershell.exe");
            if (!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem && File.Exists(native)) return native;
            if (File.Exists(normal)) return normal;
            throw new FileNotFoundException("Windows PowerShell could not be found at: " + normal);
        }
        private static string Quote(string value)
        {
            if (value.IndexOf('"') >= 0) throw new ArgumentException("A file path contains a quote.");
            return "\"" + value + "\"";
        }
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                // Read-only regression checks for the updater. No Windows display,
                // clipboard, process launch, game or Steam action is used here.
                if (args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
                {
                    string testFile = Path.Combine(Root, "fallback-selftest.txt");
                    try { File.WriteAllText(testFile, CaptureSelectionTests.Run(), new UTF8Encoding(false)); return 0; }
                    catch (Exception testError) { File.WriteAllText(testFile, "FAILED: " + testError, new UTF8Encoding(false)); return 1; }
                }
                Directory.CreateDirectory(Logs);
                foreach (var old in new DirectoryInfo(Logs).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTimeUtc).Skip(49)) { try { old.Delete(); } catch { } }
                logFile = Path.Combine(Logs, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Button.ToString("00") + ".txt");
                Log("START " + Title + "; Screenshot05 1.1 monitor fallback; process bits=" + (IntPtr.Size * 8));
                if (args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase)))
                {
                    CheckOnly();
                    Log("CHECK PASSED: startup, fixed action and local dependencies only. No live action executed.");
                    return 0;
                }
                if (args.Any(a => a.Equals("--diagnostics", StringComparison.OrdinalIgnoreCase)))
                {
                    WriteDiagnostics(); return 0;
                }
                if (args.Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase))) return RunGame("Setup");
                if (args.Any(a => a.Equals("--status", StringComparison.OrdinalIgnoreCase))) return RunGame("Status");
                // This executable's compile-time action is authoritative; its filename
                // and Stream Dock's stored command parameters are not used to choose it.
                if (Button <= 4) return RunGame(GameMode);
                Native.Dpi();
                bool owns;
                using (Mutex mutex = new Mutex(true, @"Local\FoughtApple.StreamDock.Plain7." + Button, out owns))
                {
                    if (!owns) { Log("BUSY: previous utility press still running; not queued."); return 0; }
                    try
                    {
                        if (Button == 5)
                        {
                            bool test = args.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase));
                            if (test) Thread.Sleep(3000);
                            Native.POINT point;
                            if (!Native.GetCursorPos(out point)) throw new InvalidOperationException("Windows did not return the mouse position.");
                            Capture(new Point(point.X, point.Y), Native.GetForegroundWindow(), test);
                        }
                        else if (Button == 6)
                        {
                            Thread.Sleep(200);
                            Native.Hotkey("Win+V");
                            Log("SENT Win+V. Windows clipboard-history UI is not programmatically verified.");
                        }
                        else if (Button == 7)
                        {
                            Thread.Sleep(200);
                            Native.Hotkey(VoiceKeys);
                            Log("SENT " + VoiceKeys + ". The receiving app must already be running. No text or Enter key sent.");
                        }
                        Log("FINISHED");
                        return 0;
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex)
            {
                Log("ERROR " + ex);
                try
                {
                    Application.EnableVisualStyles();
                    MessageBox.Show(ex.Message + "\r\n\r\nLog file:\r\n" + (logFile ?? Logs) +
                        "\r\n\r\nKeep this error text for troubleshooting.", Title + " - needs attention",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch { }
                return 1;
            }
        }
        private static void CheckOnly()
        {
            if (Button < 1 || Button > 7) throw new InvalidOperationException("Invalid built-in action.");
            int expectedSize = IntPtr.Size == 8 ? 40 : 28;
            if (Marshal.SizeOf(typeof(Native.INPUT)) != expectedSize) throw new InvalidOperationException("Unexpected Windows INPUT structure size.");
            if (!Native.ParseHotkey(VoiceKeys).SequenceEqual(new ushort[] { 0x11, 0x12, 0x10, 0x46 }))
                throw new InvalidOperationException("Voice keys are not Ctrl+Alt+Shift+F.");
            if (Button <= 4)
            {
                PowerShellPath();
                foreach (string name in new[] { "DockGames.ps1", "DockGames.Core.ps1", "DockGames.UI.ps1", "DockGames.Steam.ps1", "DockGames.Actions.ps1" })
                    if (!File.Exists(Path.Combine(Games, name))) throw new FileNotFoundException("Missing engine file: " + name);
            }
            File.WriteAllText(Path.Combine(Logs, "check-" + Button.ToString("00") + ".txt"),
                Title + Environment.NewLine + "Action=" + (Button <= 4 ? GameMode : (Button == 5 ? "CaptureZoneOrMonitor-v1.1" : (Button == 6 ? "Win+V" : VoiceKeys))) +
                Environment.NewLine + "Startup check passed; no apps, input, clipboard or Steam state were changed.");
        }
        private static int RunGame(string mode)
        {
            string script = Path.Combine(Games, "DockGames.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException("Missing game engine. Repair Desktop Controls from Taskbar Tiles Settings > Stream Dock. Expected: " + script);
            ProcessStartInfo si = new ProcessStartInfo(PowerShellPath());
            si.Arguments = "-NoLogo -NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File " + Quote(script) + " -Mode " + mode + " -PlainRunner";
            si.WorkingDirectory = Games;
            si.UseShellExecute = false;
            si.CreateNoWindow = true;
            si.RedirectStandardOutput = true;
            si.RedirectStandardError = true;
            StringBuilder output = new StringBuilder();
            object outputLock = new object();
            Log("Starting game engine; mode=" + mode + "; script=" + script);
            using (Process process = new Process())
            {
                process.StartInfo = si;
                DataReceivedEventHandler handler = delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (outputLock) { output.AppendLine(e.Data); }
                    Log("ENGINE " + e.Data);
                };
                process.OutputDataReceived += handler;
                process.ErrorDataReceived += handler;
                if (!process.Start()) throw new InvalidOperationException("Windows did not start the game engine.");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit(); // Drain both redirected streams without synchronous pipe deadlock.
                Log("Engine exit=" + process.ExitCode);
                if (process.ExitCode != 0 && process.ExitCode != 2)
                {
                    string detail;
                    lock (outputLock) { detail = output.ToString().Trim(); }
                    if (detail.Length > 7000) detail = detail.Substring(detail.Length - 7000);
                    if (detail.Length == 0) detail = "The game engine exited with code " + process.ExitCode + ". See Engine\\Games\\UserData for its operation log.";
                    throw new InvalidOperationException(detail);
                }
                return process.ExitCode;
            }
        }
        private static void Capture(Point pointer, IntPtr foreground, bool preview)
        {
            // Freeze the target monitor at the pointer position for this press.
            // Bounds includes its taskbar. Never use VirtualScreen/AllScreens here.
            Screen screen = Screen.FromPoint(pointer);
            CaptureSelection target = CaptureSelection.Select(pointer, screen.Bounds, screen.DeviceName,
                delegate(out string detail) { return LayoutResolver.Resolve(pointer, foreground, out detail); });
            string report = target.Report;
            Rectangle r = target.Bounds;
            if ((long)r.Width * r.Height > 80000000) throw new InvalidOperationException("Unexpectedly large capture region; no image copied.");
            using (Bitmap image = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics graphics = Graphics.FromImage(image))
                    graphics.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
                using (MemoryStream png = new MemoryStream())
                {
                    image.Save(png, ImageFormat.Png); png.Position = 0;
                    DataObject data = new DataObject();
                    data.SetData(DataFormats.Bitmap, true, image);
                    data.SetData("PNG", false, png);
                    Clipboard.SetDataObject(data, true, 10, 100); // Persist after this process exits.
                }
                Log("COPIED " + (target.FullMonitor ? "whole monitor" : "visible zone") + " to clipboard. " + report);
                if (preview)
                {
                    Application.EnableVisualStyles();
                    using (Form form = new Form { Text = target.FullMonitor ? "05 - Whole monitor copied" : "05 - This zone was copied", Width = 900, Height = 650, StartPosition = FormStartPosition.CenterScreen })
                    using (PictureBox picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = image })
                    using (Label label = new Label { Dock = DockStyle.Bottom, Height = 75, Text = report + "\r\nClose this preview, then paste with Ctrl+V. No image file was saved.", Padding = new Padding(10) })
                    { form.Controls.Add(picture); form.Controls.Add(label); form.ShowDialog(); }
                }
            }
        }
        private static void WriteDiagnostics()
        {
            string file = Path.Combine(Root, "Diagnostic-report.txt");
            StringBuilder b = new StringBuilder();
            b.AppendLine("Stream Dock Plain7 diagnostic report - local only");
            b.AppendLine(DateTimeOffset.Now.ToString("o"));
            b.AppendLine("Folder: " + Root);
            b.AppendLine("Windows: " + Environment.OSVersion);
            b.AppendLine("Process bits: " + (IntPtr.Size * 8));
            b.AppendLine("Voice keys: " + VoiceKeys);
            b.AppendLine("Copied game settings present: " + File.Exists(Path.Combine(Games, @"UserData\settings.json")));
            b.AppendLine("The report does NOT include saved passwords or the contents of Steam login files.");
            foreach (FileInfo log in new DirectoryInfo(Logs).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTimeUtc).Take(16))
            {
                b.AppendLine("\r\n=== " + log.Name + " ===");
                try { string text = File.ReadAllText(log.FullName); b.AppendLine(text.Length > 12000 ? text.Substring(text.Length - 12000) : text); }
                catch (Exception ex) { b.AppendLine(ex.Message); }
            }
            string gameLogs = Path.Combine(Games, "UserData");
            if (Directory.Exists(gameLogs))
                foreach (FileInfo log in new DirectoryInfo(gameLogs).GetFiles("operation-*.log").OrderByDescending(f => f.LastWriteTimeUtc).Take(4))
                {
                    b.AppendLine("\r\n=== GAME " + log.Name + " ===");
                    try { string text = File.ReadAllText(log.FullName); b.AppendLine(text.Length > 16000 ? text.Substring(text.Length - 16000) : text); }
                    catch (Exception ex) { b.AppendLine(ex.Message); }
                }
            File.WriteAllText(file, b.ToString(), new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo("notepad.exe", Quote(file)) { UseShellExecute = true });
        }
    }
}
