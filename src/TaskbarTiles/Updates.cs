// GitHub updates are opt-in per operation. No startup polling, telemetry or credentials.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class ReleaseInfo
    {
        internal const string Repository = "foughtapple/taskbar-tiles";
        internal const string ProjectUrl = "https://github.com/" + Repository;
        internal const string LatestUrl = ProjectUrl + "/releases/latest";
        internal const string ApiUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
        internal static string ReleaseTagUrl(string tag)
        {
            Version version;
            if (!TryVersion(tag, out version)) throw new InvalidDataException("Unexpected release tag.");
            return ProjectUrl + "/releases/tag/" + tag;
        }
        internal static bool TryReleaseTagUri(Uri uri, out string tag, out Version version)
        {
            tag = null; version = null;
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
                !uri.DnsSafeHost.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
            string prefix = "/" + Repository + "/releases/tag/";
            if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
            string candidate = Uri.UnescapeDataString(uri.AbsolutePath.Substring(prefix.Length)).TrimEnd('/');
            if (candidate.Contains("/")) return false;
            Version parsed;
            if (!TryVersion(candidate, out parsed)) return false;
            tag = candidate; version = parsed; return true;
        }
        internal static bool TryVersion(string tag, out Version version)
        {
            version = null;
            if (tag == null || !Regex.IsMatch(tag, @"^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z")) return false;
            return Version.TryParse(tag.Substring(1), out version) && version.Major <= 65535 && version.Minor <= 65535 && version.Build <= 65535;
        }
        internal static string SetupName(string tag)
        { Version v; if (!TryVersion(tag, out v)) throw new InvalidDataException("Unexpected release tag."); return "TaskbarTiles-" + v.ToString(3) + "-Setup.exe"; }
        internal static string AssetUrl(string tag, string name)
        {
            if (name != SetupName(tag) && name != "SHA256SUMS.txt") throw new InvalidDataException("Unexpected release asset.");
            return ProjectUrl + "/releases/download/" + tag + "/" + name;
        }
        internal static bool AllowedDownloadUri(Uri uri)
        {
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) return false;
            string host = uri.DnsSafeHost.ToLowerInvariant();
            if (host == "github.com") return uri.AbsolutePath.StartsWith("/" + Repository + "/releases/", StringComparison.Ordinal);
            if (host == "api.github.com") return uri.AbsolutePath.StartsWith("/repos/" + Repository + "/releases/", StringComparison.Ordinal);
            return host == "release-assets.githubusercontent.com" || host == "objects.githubusercontent.com" || host == "github-releases.githubusercontent.com";
        }
        internal static string ExpectedHash(string text, string file)
        {
            string result = null;
            foreach (string raw in (text ?? "").Split('\n'))
            {
                var m = Regex.Match(raw.TrimEnd('\r'), @"^([a-fA-F0-9]{64}) [ *](.+)$");
                if (!m.Success || m.Groups[2].Value != file) continue;
                if (result != null) throw new InvalidDataException("The checksum list contains duplicate installer entries.");
                result = m.Groups[1].Value.ToLowerInvariant();
            }
            if (result == null) throw new InvalidDataException("The release has no valid checksum for its installer.");
            return result;
        }
        internal static string Hash(string file)
        { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        internal static void Open(string url) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    }
    sealed class AvailableUpdate
    {
        internal string Tag, Notes, AssetName;
        internal Version Version;
        internal static AvailableUpdate FromTag(string tag, string notes)
        {
            Version version;
            if (!ReleaseInfo.TryVersion(tag, out version)) throw new InvalidDataException("Unsupported version format in release.");
            return new AvailableUpdate { Tag = tag, Version = version, AssetName = ReleaseInfo.SetupName(tag), Notes = notes ?? "" };
        }
        internal static AvailableUpdate Parse(string json)
        {
            var obj = new JavaScriptSerializer { MaxJsonLength = 2097152 }.Deserialize<Dictionary<string, object>>(json);
            if (obj == null || !obj.ContainsKey("tag_name")) throw new InvalidDataException("GitHub did not return a release.");
            object flag;
            if ((obj.TryGetValue("draft", out flag) && object.Equals(flag, true)) || (obj.TryGetValue("prerelease", out flag) && object.Equals(flag, true)))
                throw new InvalidDataException("This is not a published stable release.");
            string tag = obj["tag_name"] as string; Version version;
            if (!ReleaseInfo.TryVersion(tag, out version)) throw new InvalidDataException("Unsupported version format in release.");
            string setup = ReleaseInfo.SetupName(tag); bool hasSetup = false, hasHash = false;
            object assets;
            if (obj.TryGetValue("assets", out assets))
            {
                var array = assets as System.Collections.IEnumerable;
                if (array != null) foreach (object raw in array)
                {
                    var a = raw as Dictionary<string, object>; object name;
                    if (a == null || !a.TryGetValue("name", out name)) continue;
                    if (object.Equals(name, setup)) hasSetup = true;
                    if (object.Equals(name, "SHA256SUMS.txt")) hasHash = true;
                }
            }
            if (!hasSetup || !hasHash) throw new InvalidDataException("The latest release does not yet have a complete installer and checksum. Try again after its build finishes.");
            object body; obj.TryGetValue("body", out body);
            return FromTag(tag, body as string ?? "");
        }
    }
    static class UpdateTransport
    {
        static void Get(Uri url, Stream output, long limit, CancellationToken token, Action<long, long> progress)
        {
            for (int redirects = 0; redirects < 6; redirects++)
            {
                if (!ReleaseInfo.AllowedDownloadUri(url)) throw new InvalidDataException("The update redirected outside the trusted GitHub hosts.");
                token.ThrowIfCancellationRequested();
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.AllowAutoRedirect = false; request.Timeout = 25000; request.ReadWriteTimeout = 25000;
                request.UserAgent = "TaskbarTiles/" + Program.Version;
                request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
                // Keep OS TLS and certificate validation intact. No credentials are sent.
                using (token.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    int code = (int)response.StatusCode;
                    if (code >= 300 && code <= 399)
                    {
                        string location = response.Headers["Location"];
                        Uri next;
                        if (string.IsNullOrEmpty(location) || !Uri.TryCreate(url, location, out next)) throw new InvalidDataException("Invalid update redirect.");
                        url = next; continue;
                    }
                    if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("GitHub did not provide the requested asset.");
                    if (response.ContentLength > limit) throw new InvalidDataException("The update response is larger than expected.");
                    using (var input = response.GetResponseStream())
                    {
                        if (input == null) throw new IOException("Empty update response.");
                        byte[] buffer = new byte[32768]; long total = 0; int n;
                        var watch = Stopwatch.StartNew();
                        while ((n = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            token.ThrowIfCancellationRequested(); total += n;
                            if (total > limit || watch.Elapsed.TotalMinutes > 5) throw new InvalidDataException("The download exceeded its size/time limit.");
                            output.Write(buffer, 0, n); if (progress != null) progress(total, response.ContentLength);
                        }
                        if (response.ContentLength >= 0 && total != response.ContentLength) throw new IOException("The download ended early.");
                    }
                    return;
                }
            }
            throw new InvalidDataException("Too many update redirects.");
        }
        internal static string Text(string url, int limit, CancellationToken token)
        { using (var memory = new MemoryStream()) { Get(new Uri(url), memory, limit, token, null); return Encoding.UTF8.GetString(memory.ToArray()).TrimStart((char)0xFEFF); } }

        // Version discovery intentionally uses GitHub's normal public /releases/latest
        // redirect rather than the unauthenticated REST API. The REST API has a low
        // shared-IP rate limit and can return HTTP 403 even while public release files
        // remain fully available. No credentials or machine-wide proxy/TLS changes.
        internal static AvailableUpdate CheckFromPublicRelease(CancellationToken token)
        {
            Uri current = new Uri(ReleaseInfo.LatestUrl);
            for (int redirects = 0; redirects < 6; redirects++)
            {
                if (!ReleaseInfo.AllowedDownloadUri(current)) throw new InvalidDataException("The update redirected outside the trusted GitHub hosts.");
                token.ThrowIfCancellationRequested();
                var request = (HttpWebRequest)WebRequest.Create(current);
                request.AllowAutoRedirect = false; request.Timeout = 25000; request.ReadWriteTimeout = 25000;
                request.UserAgent = "TaskbarTiles/" + Program.Version;
                using (token.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    int code = (int)response.StatusCode;
                    if (code >= 300 && code <= 399)
                    {
                        string location = response.Headers["Location"]; Uri next;
                        if (string.IsNullOrEmpty(location) || !Uri.TryCreate(current, location, out next))
                            throw new InvalidDataException("GitHub returned an invalid latest-release redirect.");
                        string tag; Version version;
                        if (ReleaseInfo.TryReleaseTagUri(next, out tag, out version))
                        {
                            var update = AvailableUpdate.FromTag(tag,
                                "Taskbar Tiles " + version.ToString(3) + " is the latest published GitHub release.\r\n\r\n" +
                                "The updater found it through GitHub's public release redirect and verified that its checksum list contains this installer's SHA-256.\r\n\r\n" +
                                "Use Browser download to view the full release notes.");
                            string sums = Text(ReleaseInfo.AssetUrl(tag, "SHA256SUMS.txt"), 65536, token);
                            ReleaseInfo.ExpectedHash(sums, update.AssetName);
                            return update;
                        }
                        current = next; continue;
                    }
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string tag; Version version;
                        if (ReleaseInfo.TryReleaseTagUri(current, out tag, out version))
                        {
                            var update = AvailableUpdate.FromTag(tag, "Taskbar Tiles " + version.ToString(3) + " is the latest published GitHub release.");
                            string sums = Text(ReleaseInfo.AssetUrl(tag, "SHA256SUMS.txt"), 65536, token);
                            ReleaseInfo.ExpectedHash(sums, update.AssetName);
                            return update;
                        }
                        throw new InvalidDataException("GitHub did not identify its latest release.");
                    }
                    throw new InvalidDataException("GitHub did not provide its latest release.");
                }
            }
            throw new InvalidDataException("Too many latest-release redirects.");
        }

        internal static AvailableUpdate Check(CancellationToken token)
        { return CheckFromPublicRelease(token); }
        internal static string Download(AvailableUpdate update, CancellationToken token, Action<long, long> progress, out string verifiedHash)
        { return DownloadTo(update, token, progress, out verifiedHash, Path.Combine(Program.Home, "Updates")); }
        // The online integration test uses a unique temporary root; normal updates keep their existing location.
        internal static string DownloadTo(AvailableUpdate update, CancellationToken token, Action<long, long> progress, out string verifiedHash, string updateRoot)
        {
            string sums = Text(ReleaseInfo.AssetUrl(update.Tag, "SHA256SUMS.txt"), 65536, token);
            verifiedHash = ReleaseInfo.ExpectedHash(sums, update.AssetName);
            string folder = Path.Combine(updateRoot, update.Tag);
            Directory.CreateDirectory(folder);
            string partial = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".download");
            string destination = Path.Combine(folder, update.AssetName);
            try
            {
                using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    Get(new Uri(ReleaseInfo.AssetUrl(update.Tag, update.AssetName)), stream, 134217728, token, progress);
                if (ReleaseInfo.Hash(partial) != verifiedHash) throw new InvalidDataException("Installer checksum mismatch. Nothing will be run.");
                token.ThrowIfCancellationRequested();
                InternetDownload.Mark(partial, ReleaseInfo.AssetUrl(update.Tag, update.AssetName));
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(partial, destination);
                // Renaming within the same folder preserves the verified Internet marker.
                return destination;
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
    }
    sealed class UpdatesWindow : Form
    {
        readonly Label status = new Label();
        readonly TextBox notes = new TextBox();
        readonly Button check, install;
        readonly ProgressBar progress = new ProgressBar();
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        AvailableUpdate available;
        bool busy;
        internal UpdatesWindow(bool checkOnOpen = false)
        {
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Taskbar Tiles - Updates"; BackColor = Theme.Background; ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(700, 490); MinimumSize = new Size(680, 460); MaximizeBox = false;
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            body.Controls.Add(new Label { Text = "Taskbar Tiles  " + Program.Version, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true }, 0, 0);
            status.Text = "Check GitHub for a newer version. Nothing is downloaded or installed without your action.";
            status.Dock = DockStyle.Fill; body.Controls.Add(status, 0, 1);
            notes.Multiline = true; notes.ReadOnly = true; notes.ScrollBars = ScrollBars.Vertical; notes.Dock = DockStyle.Fill;
            notes.BackColor = Theme.Card; notes.ForeColor = Theme.Text;
            notes.Text = "Source: " + ReleaseInfo.Repository + "\r\n\r\nDownloads are checked against the release's SHA-256 list. This detects corruption; it is not an independent publisher signature. The installer may be unsigned.\r\n\r\nYour settings and favourites are retained when upgrading. No Git or developer tools are needed to install a published release.";
            body.Controls.Add(notes, 0, 2); progress.Dock = DockStyle.Fill; progress.Visible = false; body.Controls.Add(progress, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var close = Theme.Button("Close", 82); close.Click += delegate { Close(); };
            install = Theme.Button("Download && install", 165); install.Enabled = false; install.Click += delegate { Download(); };
            check = Theme.Button("Check GitHub", 125); check.Click += delegate { Check(); };
            var releases = Theme.Button("Browser download", 145); releases.Click += delegate { try { ReleaseInfo.Open(ReleaseInfo.LatestUrl); } catch (Exception ex) { status.Text = ex.Message; } };
            actions.Controls.Add(close); actions.Controls.Add(install); actions.Controls.Add(check); actions.Controls.Add(releases); body.Controls.Add(actions, 0, 4);
            Controls.Add(body); CancelButton = close; FormClosing += delegate { stop.Cancel(); };
            if (checkOnOpen) Shown += delegate { Check(); };
        }
        void UI(Action action) { if (IsDisposed || Disposing || !IsHandleCreated) return; try { BeginInvoke(action); } catch (InvalidOperationException) { } }
        void SetBusy(bool value) { busy = value; check.Enabled = !value; install.Enabled = !value && available != null; progress.Visible = value; }
        void Check()
        {
            if (busy) return; available = null; SetBusy(true); progress.Style = ProgressBarStyle.Marquee; status.Text = "Checking the public GitHub release...";
            Task.Factory.StartNew(delegate
            {
                try
                {
                    var found = UpdateTransport.Check(stop.Token);
                    UI(delegate
                    {
                        if (IsDisposed) return;
                        if (found.Version.CompareTo(new Version(Program.Version)) > 0)
                        { available = found; status.Text = "Version " + found.Version.ToString(3) + " is available. Download & install starts its setup wizard."; }
                        else status.Text = "You already have version " + Program.Version + " (latest published: " + found.Version.ToString(3) + ").";
                        notes.Text = found.Notes; SetBusy(false);
                    });
                }
                catch (Exception ex) { UI(delegate { status.Text = ErrorText(ex); SetBusy(false); }); }
            });
        }
        internal static string ErrorText(Exception ex)
        {
            var web = ex as WebException; var response = web == null ? null : web.Response as HttpWebResponse;
            if (web != null && web.Status == WebExceptionStatus.SecureChannelFailure)
                return "Windows could not establish a secure TLS connection to GitHub. Use Browser download to update manually. Certificate checks remain enabled; your installed version is unchanged.";
            if (web != null && web.Status == WebExceptionStatus.TrustFailure)
                return "Windows could not verify GitHub's certificate. Check the PC clock or your network's certificate policy. Do not disable certificate checks. Browser download is available; your installed version is unchanged.";
            if (response != null && response.StatusCode == HttpStatusCode.Forbidden)
                return "GitHub refused this request (HTTP 403). Taskbar Tiles no longer needs GitHub's rate-limited API for version checks; use Browser download if this was an asset request. Your installed version is unchanged.";
            if (response != null && response.StatusCode == HttpStatusCode.NotFound) return "No public release is available at the configured repository yet, or a release asset is missing. Browser download is available; your installed version is unchanged.";
            return "Update check/download did not complete. Your installed version is unchanged. " + ex.Message;
        }
        void Download()
        {
            if (busy || available == null) return;
            AvailableUpdate update = available; SetBusy(true); progress.Style = ProgressBarStyle.Continuous; progress.Value = 0; status.Text = "Downloading and verifying " + update.Tag + "...";
            Task.Factory.StartNew(delegate
            {
                try
                {
                    string expected;
                    string file = UpdateTransport.Download(update, stop.Token, delegate(long done, long total)
                    { if (total > 0) UI(delegate { if (!IsDisposed) progress.Value = (int)Math.Min(100, done * 100 / total); }); }, out expected);
                    UI(delegate
                    {
                        if (stop.IsCancellationRequested || IsDisposed) return;
                        try
                        {
                            if (ReleaseInfo.Hash(file) != expected) throw new InvalidDataException("Installer changed after verification. Not running it.");
                            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                            status.Text = "Setup opened. Follow its wizard; it will close this copy safely when ready.";
                        }
                        catch (Exception ex) { status.Text = ErrorText(ex); }
                        SetBusy(false);
                    });
                }
                catch (Exception ex) { UI(delegate { status.Text = ErrorText(ex); SetBusy(false); }); }
            });
        }
    }
    sealed partial class Switcher
    {
        void ShowUpdates()
        {
            CancelPassiveLaunchObservation();
            if (transient != null) { transient.Activate(); return; }
            CancelActivation(); Dismiss();
            using (var form = new UpdatesWindow())
            { transient = form; try { form.ShowDialog(); } finally { transient = null; } }
        }
        void ShowAbout()
        {
            MessageBox.Show("Taskbar Tiles " + Program.Version + "\n\nSwitch to the right window. Launch into the right place.\n\n" + ReleaseInfo.ProjectUrl + "\nMIT licence. No telemetry. Updates are checked only when requested.\n\nRight-click the tray icon for updates, settings and diagnostics.",
                "About Taskbar Tiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
