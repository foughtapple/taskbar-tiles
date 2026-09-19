using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

namespace TaskbarTiles
{
    // Run() is offline. RunNetwork() is a separate, explicitly invoked CI/diagnostic mode.
    // Both run inside the built EXE, not inside PowerShell's unrelated runtime/configuration.
    static class UpdateTlsTests
    {
        static int checks;
        static void Require(bool value, string name)
        { checks++; if (!value) throw new InvalidOperationException("FAILED: " + name); }

        internal static void Run(StringBuilder log)
        {
            checks = 0;
            var attribute = Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(TargetFrameworkAttribute)) as TargetFrameworkAttribute;
            Require(attribute != null && attribute.FrameworkName == ".NETFramework,Version=v4.8", "CodeDOM and MSBuild outputs explicitly target .NET Framework 4.8");
            bool disabled;
            Require(AppContext.TryGetSwitch("Switch.System.Net.DontEnableSchUseStrongCrypto", out disabled) && !disabled,
                "installed app configuration enables strong cryptography");
            Require(AppContext.TryGetSwitch("Switch.System.Net.DontEnableSystemDefaultTlsVersions", out disabled) && !disabled,
                "installed app configuration uses OS-selected TLS");
            Require(ServicePointManager.SecurityProtocol == SecurityProtocolType.SystemDefault, "real executable defaults to the OS TLS policy");
            Require(ServicePointManager.ServerCertificateValidationCallback == null, "no certificate-validation bypass");
            string tls = UpdatesWindow.ErrorText(new WebException("test", WebExceptionStatus.SecureChannelFailure));
            Require(tls.Contains("TLS") && tls.Contains("Browser download") && tls.Contains("unchanged"), "TLS failures explain manual recovery");
            string certificate = UpdatesWindow.ErrorText(new WebException("test", WebExceptionStatus.TrustFailure));
            Require(certificate.Contains("certificate") && certificate.Contains("Do not disable"), "certificate failures never suggest bypassing security");
            string folder = Path.Combine(Path.GetTempPath(), "TaskbarTiles-MarkerTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string file = Path.Combine(folder, "test.download"), renamed = Path.Combine(folder, "renamed.bin");
                string source = ReleaseInfo.AssetUrl("v0.7.1", ReleaseInfo.SetupName("v0.7.1"));
                File.WriteAllText(file, "Non-executable marker regression fixture.");
                string hash = ReleaseInfo.Hash(file);
                InternetDownload.Mark(file, source);
                Require(InternetDownload.HasMark(file, source), "native stream retains ZoneId=3 and source");
                Require(ReleaseInfo.Hash(file) == hash, "Internet marker does not change payload checksum");
                File.Move(file, renamed);
                Require(InternetDownload.HasMark(renamed, source), "Internet marker survives final rename");
            }
            finally { Directory.Delete(folder, true); }
            log.AppendLine("PASS: " + checks + " updater TLS/runtime/Internet-marker/error-message regressions. No network requests in these helper tests.");
        }

        // GETs GitHub metadata, the checksum list and an installer using production transport.
        // Downloads into a unique temporary directory, verifies SHA-256, then deletes it.
        // Never installs, starts an app, changes user settings or supplies GitHub credentials.
        internal static int RunNetwork()
        {
            var log = new StringBuilder();
            string root = Path.Combine(Path.GetTempPath(), "TaskbarTiles-HttpsTest-" + Guid.NewGuid().ToString("N"));
            int result = 1;
            try
            {
                Run(log);
                log.AppendLine("Executable: Taskbar Tiles " + Program.Version + "; TLS policy: " + ServicePointManager.SecurityProtocol);
                using (var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
                {
                    AvailableUpdate update = UpdateTransport.Check(stop.Token);
                    log.AppendLine("PASS: actual GitHub release metadata retrieved and validated (" + update.Tag + ").");
                    string expected;
                    string file = UpdateTransport.DownloadTo(update, stop.Token, null, out expected, root);
                    if (!File.Exists(file) || new FileInfo(file).Length == 0 || ReleaseInfo.Hash(file) != expected)
                        throw new InvalidDataException("Downloaded installer failed final verification.");
                    if (!InternetDownload.HasMark(file, ReleaseInfo.AssetUrl(update.Tag, update.AssetName)))
                        throw new InvalidDataException("Downloaded installer lost its Internet security marker.");
                    log.AppendLine("PASS: final installer retains its Internet security marker.");
                    log.AppendLine("PASS: checksum list, HTTPS redirect and installer download through production updater transport.");
                    log.AppendLine("PASS: downloaded installer SHA-256 verified; installer NOT executed.");
                }
                result = 0;
            }
            catch (Exception ex)
            {
                var web = ex as WebException;
                log.AppendLine("FAIL: " + ex.GetType().Name + (web == null ? "" : " / " + web.Status) + ": " + ex.Message);
                log.AppendLine(ex.StackTrace);
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                catch (Exception ex) { log.AppendLine("FAIL: temporary download cleanup: " + ex.GetType().Name); result = 1; }
            }
            try { File.WriteAllText(Path.Combine(Program.Home, "update-network-test.log"), log.ToString()); }
            catch { result = 1; }
            return result;
        }
    }
}
