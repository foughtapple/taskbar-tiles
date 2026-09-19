using System;
using System.IO;
using System.Text;
namespace TaskbarTiles
{
    static class UpdateTests
    {
        static int checks;
        static void Require(bool ok, string name) { checks++; if (!ok) throw new InvalidOperationException("FAILED: " + name); }
        static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Require(rejected, name); }
        internal static void Run(StringBuilder log)
        {
            UpdateTlsTests.Run(log);
            checks = 0; Version v;
            Require(ReleaseInfo.TryVersion("v0.7.0", out v) && v == new Version(0, 7, 0), "stable version parsed");
            foreach (string tag in new[] { "v0.7.0-beta", "../bad", "v01.2.3", "v1.2", "v1.2.3.4", "v999999999.2.3", "v1.2.3&calc", "v1.2.3\n" })
                Require(!ReleaseInfo.TryVersion(tag, out v), "unexpected release tag rejected: " + tag);
            Require(ReleaseInfo.SetupName("v0.7.0") == "TaskbarTiles-0.7.0-Setup.exe", "installer asset name is fixed");
            Reject(delegate { ReleaseInfo.AssetUrl("v0.7.0", "runme.cmd"); }, "arbitrary executable name refused");
            Require(ReleaseInfo.AllowedDownloadUri(new Uri(ReleaseInfo.ApiUrl)), "public release endpoint allowed");
            Require(ReleaseInfo.AllowedDownloadUri(new Uri(ReleaseInfo.AssetUrl("v0.7.0", "SHA256SUMS.txt"))), "checksums fetched from same release");
            foreach (string url in new[] { "http://github.com/foughtapple/taskbar-tiles/releases/a", "https://github.com.attacker.example/a", "https://github.com/other/repo/releases/a", "https://user@github.com/foughtapple/taskbar-tiles/releases/a", "https://evil.example/run.exe", "https://github.com:444/foughtapple/taskbar-tiles/releases/a" })
                Require(!ReleaseInfo.AllowedDownloadUri(new Uri(url)), "untrusted download URL refused");
            string hash = new string('a', 64), file = ReleaseInfo.SetupName("v0.7.0");
            Require(ReleaseInfo.ExpectedHash(hash + "  " + file + "\r\n", file) == hash, "SHA256 list parsed");
            Reject(delegate { ReleaseInfo.ExpectedHash(hash + "  other.exe", file); }, "missing installer hash refused");
            Reject(delegate { ReleaseInfo.ExpectedHash(hash + "  " + file + "\n" + hash + "  " + file, file); }, "duplicate installer hashes refused");
            string json = "{\"tag_name\":\"v0.7.0\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"" + file + "\"},{\"name\":\"SHA256SUMS.txt\"}],\"body\":\"Test release\"}";
            Require(AvailableUpdate.Parse(json).AssetName == file, "complete release accepted");
            Reject(delegate { AvailableUpdate.Parse(json.Replace("\"prerelease\":false", "\"prerelease\":true")); }, "pre-release not auto-offered");
            Reject(delegate { AvailableUpdate.Parse(json.Replace("SHA256SUMS.txt", "missing.txt")); }, "partially uploaded release refused");
            log.AppendLine("PASS: " + checks + " update metadata, version, trusted-host and checksum-parser assertions. No network traffic or installers executed.");
        }
    }
}
