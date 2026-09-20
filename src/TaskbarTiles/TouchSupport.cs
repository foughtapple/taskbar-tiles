// Deterministic Touch Return policy. No input injection, app settings or product-name rules.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;

namespace TaskbarTiles
{
    sealed class TouchMonitorRule
    {
        public string Key = "", Nickname = "", TouchDevice = "", PenDevice = "";
        public bool Enabled, Touch, Pen, TouchVerified, PenVerified;
        public int TouchDelay = -1, PenDelay = -1;
        public override string ToString() { return string.IsNullOrWhiteSpace(Nickname) ? Key : Nickname; }
    }
    static class TouchRules
    {
        internal static List<TouchMonitorRule> Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<TouchMonitorRule>();
            if (text.Length > 100000) throw new ArgumentException("Touch monitor settings are too large.");
            var result = new JavaScriptSerializer().Deserialize<List<TouchMonitorRule>>(text) ?? new List<TouchMonitorRule>();
            if (result.Count > 32) throw new ArgumentException("Too many touch monitor rules.");
            foreach (var r in result)
            {
                r.Key = r.Key ?? ""; r.Nickname = r.Nickname ?? ""; r.TouchDevice = r.TouchDevice ?? ""; r.PenDevice = r.PenDevice ?? "";
                r.TouchDelay = Math.Max(-1, Math.Min(60000, r.TouchDelay)); r.PenDelay = Math.Max(-1, Math.Min(60000, r.PenDelay));
            }
            if (result.Select(r => r.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count) throw new ArgumentException("Duplicate monitor identities must be reassociated.");
            return result;
        }
        internal static string Save(List<TouchMonitorRule> rules) { return new JavaScriptSerializer().Serialize(rules); }
    }
    sealed class TouchReturnPoint
    {
        internal IntPtr Window;
        internal uint Pid;
        internal long Start;
        internal Point Cursor;
        internal string Layout;
        internal bool Verified;
    }
    sealed class TouchContact
    {
        internal int Id;
        internal bool Down;
        internal double X, Y;
    }
    sealed class TouchFrame
    {
        internal string Device = "";
        internal bool Pen, Hover, HoverKnown, Complete = true, Valid = true;
        internal List<TouchContact> Contacts = new List<TouchContact>();
        internal uint Tick;
        internal int Down { get { return Contacts.Count(c => c.Down); } }
    }
    // Frames can span several HID reports. Never turn an incomplete frame into UP.
    sealed class TouchFrameAssembler
    {
        readonly Dictionary<int, bool> prior = new Dictionary<int, bool>();
        readonly List<TouchContact> collecting = new List<TouchContact>();
        int expected;
        internal bool Partial { get { return expected != 0; } }
        internal void Reset() { prior.Clear(); collecting.Clear(); expected = 0; }
        internal List<TouchContact> Feed(int count, IList<TouchContact> slots)
        {
            if (count < 0 || count > 256) throw new InvalidOperationException("Invalid contact count.");
            if (count > 0)
            {
                if (expected != 0) throw new InvalidOperationException("A contact frame was lost.");
                expected = count; collecting.Clear();
            }
            if (expected == 0)
            {
                if (prior.Any(p => p.Value)) throw new InvalidOperationException("No explicit contact release was observed.");
                return new List<TouchContact>();
            }
            foreach (var c in slots.Take(expected - collecting.Count)) collecting.Add(c);
            if (collecting.Count < expected) return null;
            if (collecting.Select(c => c.Id).Distinct().Count() != collecting.Count) throw new InvalidOperationException("Duplicate contact IDs.");
            foreach (var p in prior.Where(p => p.Value))
                if (!collecting.Any(c => c.Id == p.Key)) throw new InvalidOperationException("An active contact disappeared without UP.");
            var complete = collecting.ToList(); prior.Clear();
            foreach (var c in complete) prior[c.Id] = c.Down;
            expected = 0; collecting.Clear(); return complete;
        }
    }
    sealed class TouchReturnEngine
    {
        sealed class ContactState { internal int Down; internal bool Hover, Partial; }
        readonly Dictionary<string, ContactState> contacts = new Dictionary<string, ContactState>();
        internal TouchReturnPoint Saved { get; private set; }
        internal string Status = "Disabled";
        internal bool Stay, Enabled, WaitHover = true;
        internal int Delay = 1000;
        double lastActivity;
        bool contacted;
        internal bool Busy { get { return contacts.Values.Any(c => c.Down != 0 || c.Partial); } }
        internal bool InHover { get { return WaitHover && contacts.Values.Any(c => c.Hover); } }
        internal void Cancel(string why) { Saved = null; contacted = false; Status = "Cancelled: " + why; }
        internal void ClearInput(string why) { Cancel(why); contacts.Clear(); }
        internal void SetStay(bool value, double now)
        { Stay = value; lastActivity = now; Status = value ? "Stay here" : Saved == null ? "Ready" : "Waiting: fresh idle countdown"; }
        internal void Activity(TouchFrame f, bool allowed, TouchReturnPoint before, int delay, double now)
        {
            if (!f.Valid) { ClearInput("input report not reliable"); return; }
            ContactState old; if (!contacts.TryGetValue(f.Device, out old)) old = new ContactState();
            bool entering = (f.Down > 0 && old.Down == 0) || (f.Hover && !old.Hover && old.Down == 0);
            contacts[f.Device] = new ContactState { Down = f.Down, Hover = f.Pen && f.Hover && f.HoverKnown, Partial = !f.Complete };
            if (!Enabled || !allowed) { if (f.Down > 0) Cancel("input is outside validated enabled screens"); return; }
            if (Saved == null && entering)
            {
                if (before == null || !before.Verified) { Status = "Skipped: pre-touch state could not be verified"; return; }
                Saved = before; Delay = delay; contacted = f.Down > 0;
            }
            if (Saved == null) return;
            Delay = Math.Max(Delay, delay); contacted |= f.Down > 0; lastActivity = now;
            Status = Busy ? "Waiting: contact still down / frame incomplete" : InHover ? "Waiting: pen still in range" : Stay ? "Stay here" : "Waiting: idle countdown";
        }
        internal TouchReturnPoint Take(double now, bool manual, bool blocked)
        {
            if (!Enabled || Saved == null || !contacted || Busy || InHover || blocked || (!manual && (Stay || now - lastActivity < Delay))) return null;
            var result = Saved; Saved = null; contacted = false; Status = "Returning"; return result;
        }
    }
    sealed class TouchDeviceEvidence
    {
        internal string Key, Name, Kind, Problem = "Waiting for input";
        internal bool Supported, HoverKnown, SawHoverExit, SawUp;
        internal int Frames, MaxContacts, HeldMilliseconds, Contacts;
        internal long DownSince;
        internal uint LastTick;
        internal void Observe(TouchFrame frame, long now)
        {
            Frames++; Contacts = frame.Down; LastTick = frame.Tick;
            if (frame.Down > 0 && DownSince == 0) DownSince = now;
            if (frame.Down == 0 && DownSince != 0) { SawUp = true; HeldMilliseconds = Math.Max(HeldMilliseconds, (int)(now - DownSince)); DownSince = 0; }
            MaxContacts = Math.Max(MaxContacts, frame.Down); HoverKnown |= frame.HoverKnown;
            if (frame.Pen && frame.HoverKnown && !frame.Hover && frame.Down == 0) SawHoverExit = true;
            Problem = !frame.Valid ? "Invalid report; automatic return blocked" : !frame.Complete ? "Waiting for complete contact frame" : frame.Down > 0 ? "Contact down" : frame.Hover ? "Pen hovering" : "All observed contacts released";
        }
        internal bool Ready { get { return Supported && SawUp && HeldMilliseconds >= 2500 && (Kind == "Pen" || MaxContacts >= 2); } }
        public override string ToString() { return Kind + " " + Name + " | " + (Supported ? "supported report format" : "detection only") + " | " + Problem; }
    }
}
