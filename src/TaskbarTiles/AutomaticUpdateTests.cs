using System;
using System.IO;
using System.Text;
namespace TaskbarTiles
{
    static class AutomaticUpdateTests
    {
        static int checks;
        static void Require(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("FAILED: " + label); }
        static bool Refuses(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } catch (IOException) { return true; } }
        internal static void Run(StringBuilder log)
        {
            checks = 0;
            Require(!new Options().AutoUpdateApp, "automatic app updates remain opt-in");
            var state = new AutomaticUpdateSnapshot { Enabled = true, Installed = true, Hidden = true, IdleSeconds = 300 };
            Require(AutomaticUpdatePolicy.CanInstall(state), "installed idle hidden app can update");
            state.Enabled = false; Require(!AutomaticUpdatePolicy.CanInstall(state), "disabled blocks installation"); state.Enabled = true;
            state.Installed = false; Require(!AutomaticUpdatePolicy.CanInstall(state), "development copies never install"); state.Installed = true;
            state.Hidden = false; Require(!AutomaticUpdatePolicy.CanInstall(state), "visible switcher blocks installation"); state.Hidden = true;
            state.Busy = true; Require(!AutomaticUpdatePolicy.CanInstall(state), "transient launch activation mover and profile work block installation"); state.Busy = false;
            state.IdleSeconds = 299.99; Require(!AutomaticUpdatePolicy.CanInstall(state), "five minute boundary respected");
            Require(!AutomaticUpdatePolicy.CanInstall(null), "missing state does not grant install permission");
            Require(AutomaticUpdatePolicy.InstalledHome(@"C:\Users\Test\AppData\Local\TaskbarTiles\", @"C:\Users\Test\AppData\Local"), "per-user installed location recognised");
            foreach (string home in new[] { "", "TaskbarTiles", @"C:\Dev\TaskbarTiles", @"C:\Users\Test\AppData\Local\TaskbarTiles-other", @"C:\Users\Test\AppData\Local\TaskbarTiles\..\Other" })
                Require(!AutomaticUpdatePolicy.InstalledHome(home, @"C:\Users\Test\AppData\Local"), "noninstalled home refused");
            Require(AutomaticUpdatePolicy.Newer(AvailableUpdate.FromTag("v1.2.4", ""), "1.2.3"), "newer stable update accepted");
            Require(!AutomaticUpdatePolicy.Newer(AvailableUpdate.FromTag("v1.2.3", ""), "1.2.3"), "same version not reinstalled");
            Require(!AutomaticUpdatePolicy.Newer(AvailableUpdate.FromTag("v1.2.2", ""), "1.2.3"), "older version never downgraded");
            Require(!AutomaticUpdatePolicy.Newer(null, "1.2.3"), "unavailable release ignored");
            var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc); var schedule = new AutomaticUpdateSchedule(now);
            Require(!schedule.Due(now) && schedule.Due(now.AddMinutes(10)), "startup does not immediately poll network");
            schedule.Success(now); Require(schedule.Next == now.AddHours(24), "stable check interval one day");
            schedule.Failure(now); Require(schedule.Next == now.AddMinutes(15), "first error backs off fifteen minutes");
            schedule.Failure(now); Require(schedule.Next == now.AddMinutes(30), "second error doubles retry delay");
            for (int i = 0; i < 20; i++) schedule.Failure(now);
            Require(schedule.Next == now.AddHours(24), "repeated errors retry at most once daily");
            schedule.Success(now); schedule.Failure(now); Require(schedule.Next == now.AddMinutes(15), "successful attempt resets failure backoff");
            using (var gate = new AutomaticUpdateGate())
            {
                Require(gate.Begin() && !gate.Begin(), "one update attempt at a time"); gate.End(); Require(gate.Begin(), "completed attempt releases gate");
                gate.Dispose(); gate.End(); Require(!gate.Begin(), "disposed gate cannot be revived by late completion");
            }
            Require(AutomaticUpdatePolicy.SilentArguments.Contains("/NORESTART") && !AutomaticUpdatePolicy.SilentArguments.Contains("/TASKS") &&
                !AutomaticUpdatePolicy.SilentArguments.Contains("/MERGETASKS"), "silent setup never rewrites user task choices or restarts Windows");
            string directory = Path.Combine(Path.GetTempPath(), "TaskbarTiles-AutoUpdateTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var update = AvailableUpdate.FromTag("v1.2.4", ""); string file = Path.Combine(directory, update.AssetName);
                File.WriteAllText(file, "Non-executable test fixture. Never launch this file."); string hash = ReleaseInfo.Hash(file);
                InternetDownload.Mark(file, ReleaseInfo.AssetUrl(update.Tag, update.AssetName));
                UpdateTransport.VerifyInstaller(file, update, hash); Require(true, "verified payload and marker accepted without executing it");
                File.AppendAllText(file, "changed"); Require(Refuses(delegate { UpdateTransport.VerifyInstaller(file, update, hash); }), "changed checksum blocks dispatch");
                hash = ReleaseInfo.Hash(file); InternetDownload.Mark(file, ReleaseInfo.AssetUrl("v1.2.3", ReleaseInfo.SetupName("v1.2.3")));
                Require(Refuses(delegate { UpdateTransport.VerifyInstaller(file, update, hash); }), "wrong Internet source blocks dispatch");
            }
            finally { Directory.Delete(directory, true); }
            log.AppendLine("PASS: " + checks + " automatic-update opt-in, idle, installation-location, version, backoff, concurrency, checksum and Internet-marker assertions. No network access or installer execution.");
        }
    }
}
