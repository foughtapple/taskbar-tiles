using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TaskbarTilesAudio
{
    static class AudioTests
    {
        sealed class FakeEndpoint : IAudioEndpoint
        {
            public string Id { get; set; }
            internal bool Muted, ReadFails, WriteFails, IgnoreWrite;
            internal int Writes;
            internal Action OnRead;
            public bool ReadMute() { if (ReadFails) throw new IOException("read"); if (OnRead != null) OnRead(); return Muted; }
            public void WriteMute(bool muted) { if (WriteFails) throw new IOException("write"); Writes++; if (!IgnoreWrite) Muted = muted; }
        }
        sealed class FakeBackend : IAudioBackend
        {
            internal FakeEndpoint Default;
            internal long Epoch;
            internal int Starts, Stops, Opens;
            internal bool Running, Disposed;
            internal Action OnOpen;
            public long Revision { get { return Epoch; } }
            public void Start() { if (!Running) { Running = true; Starts++; } }
            public void Stop() { if (Running) { Running = false; Stops++; } }
            public void Invalidate() { Epoch++; Stop(); }
            public IAudioEndpoint OpenDefault() { Opens++; if (OnOpen != null) OnOpen(); return Default; }
            public void Dispose() { Stop(); Disposed = true; }
        }
        internal static void Run(string file)
        {
            var log = new List<string>(); int checks = 0;
            Action<bool, string> check = (ok, label) => { if (!ok) throw new Exception(label); checks++; log.Add("PASS " + label); };
            var a = new FakeEndpoint { Id = "default-input" };
            var other = new FakeEndpoint { Id = "other-input" };
            var speaker = new FakeEndpoint { Id = "speaker" };
            var backend = new FakeBackend { Default = a };
            var events = new List<string>();
            using (var session = new AudioSession(backend, (ev, ctx, state) => events.Add(ev + ":" + ctx + ":" + state))) {
                Action<string, string> emit = (ev, ctx) => session.Handle(ev, AudioSession.ActionId, ctx, "dock", session.Current);
                Action<string> click = ctx => { emit("keyDown", ctx); emit("keyUp", ctx); };
                session.Refresh(); check(backend.Opens == 0, "hidden worker does not inspect endpoints");
                emit("willAppear", "one"); check(session.Current.State == "live" && backend.Starts == 1, "appearance reads real mute state");
                emit("willAppear", "two"); check(events.Contains("state:two:live"), "duplicate placement gets state");
                click("one"); check(a.Muted && a.Writes == 1 && session.Current.State == "muted", "one press mutes only default input");
                check(events.Contains("state:one:muted") && events.Contains("state:two:muted"), "all placements reflect readback");
                check(other.Writes == 0 && speaker.Writes == 0, "other microphones and speaker untouched");
                emit("keyDown", "one"); emit("keyDown", "one"); emit("keyUp", "one"); check(!a.Muted && a.Writes == 2, "repeated keyDown during a hold does not toggle twice");
                a.Muted = true; session.Refresh(); check(session.Current.State == "muted", "external mute change reflected");
                a.Muted = false; click("two"); check(a.Muted, "click uses fresh mute state rather than displayed flag");
                var old = session.Current; backend.Default = other; backend.Epoch++;
                session.Handle("keyDown", AudioSession.ActionId, "one", "dock", old); emit("keyUp", "one");
                check(other.Writes == 0 && events.Contains("alert:one:"), "stale queued click skipped after default device swap");
                check(session.Current.Id == other.Id && session.Current.State == "live", "default device swap reads new state");
                click("one"); check(other.Muted && other.Writes == 1, "next deliberate press controls new default");
                old = session.Current; backend.Epoch++; backend.Epoch++;
                session.Handle("keyDown", AudioSession.ActionId, "one", "dock", old); emit("keyUp", "one");
                check(other.Writes == 1, "change away and back invalidates queued click");
                other.OnRead = () => { other.OnRead = null; backend.Default = a; backend.Epoch++; };
                click("one"); check(other.Writes == 1 && session.Current.Id == a.Id, "device change during pre-write read aborts write");
                int writes = a.Writes; a.OnRead = () => { a.OnRead = null; backend.Default = null; backend.Epoch++; };
                click("one"); check(a.Writes == writes && session.Current.State == "unavailable", "device removal during press makes no write");
                click("one"); check(a.Writes == writes && session.Current.Id == null, "no default device cannot toggle stale endpoint");
                backend.Default = a; backend.Epoch++; session.Refresh(); check(session.Current.State == "muted", "endpoint return recovers");
                var queuedBeforeError = session.Current; a.ReadFails = true; session.Refresh(); check(session.Current.State == "error" && session.Current.Id == null, "failed state read never shows live or muted");
                click("one"); check(a.Writes == writes, "failed read cannot change mute");
                a.ReadFails = false; session.Refresh();
                session.Handle("keyDown", AudioSession.ActionId, "one", "dock", queuedBeforeError); emit("keyUp", "one");
                check(a.Writes == writes, "queued click expires after endpoint read failure and recovery");
                a.WriteFails = true; click("one");
                check(a.Writes == writes && session.Current.State == "muted", "failed write refreshes actual state without retry");
                a.WriteFails = false; click("one"); check(!a.Muted, "write failure recovers on a later deliberate press");
                a.IgnoreWrite = true; int alerts = events.Count(x => x.StartsWith("alert:")); click("one");
                check(session.Current.State == "live" && events.Count(x => x.StartsWith("alert:")) == alerts + 1, "ignored write is detected by actual-state readback");
                a.IgnoreWrite = false; writes++;
                int paints = events.Count; session.Refresh(); check(events.Count == paints, "unchanged polling avoids repeated image traffic");
                session.Handle("keyDown", "wrong.action", "one", "dock", session.Current); check(a.Writes == writes + 1, "unowned action cannot toggle");
                emit("keyDown", "unknown"); check(a.Writes == writes + 1, "unknown context cannot toggle");
                int starts = backend.Starts;
                session.Handle("systemDidWakeUp", "", "", "", null); check(backend.Starts == starts + 1, "wake recreates endpoint observation");
                emit("willDisappear", "one"); check(backend.Running, "second visible placement keeps observation alive");
                session.Handle("deviceDidDisconnect", "", "", "dock", null); check(!backend.Running && !session.Visible, "dock disconnect stops monitoring");
                int opens = backend.Opens; session.Refresh(); check(backend.Opens == opens, "hidden lifecycle performs no endpoint polling");
            }
            check(backend.Disposed && !backend.Running, "shutdown disposes backend");
            int callbacks = 0;
            var notifications = new DeviceNotifications(change => { if (change) callbacks++; });
            notifications.OnDefaultDeviceChanged(0, 1, "speaker"); notifications.OnDefaultDeviceChanged(1, 2, "communications");
            check(callbacks == 0, "render and separate communications defaults do not change target role");
            notifications.OnDefaultDeviceChanged(1, 1, "input"); check(callbacks == 1, "capture multimedia default notification handled");
            log.Add("Passed " + checks + " checks; fake endpoints only; no real audio changes.");
            File.WriteAllLines(file, log);
        }
    }
}
