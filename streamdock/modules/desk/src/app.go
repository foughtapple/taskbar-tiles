package main

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

type DisplaySettings struct {
	Interval int `json:"interval"`
}

// Legacy one/two-second settings are promoted to five seconds without changing
// account choices or printer credentials. Longer refresh choices remain available.
func normalSettings(s DisplaySettings) DisplaySettings {
	if s.Interval != 5 && s.Interval != 10 && s.Interval != 30 {
		s.Interval = 5
	}
	return s
}

const displayInterval = 5 * time.Second

type HostEvent struct {
	Event   string `json:"event"`
	Action  string `json:"action"`
	Context string `json:"context"`
	Device  string `json:"device"`
	Payload struct {
		Settings    DisplaySettings `json:"settings"`
		Command     string          `json:"command"`
		Host        string          `json:"host"`
		Serial      string          `json:"serial"`
		AccessCode  string          `json:"accessCode"`
		Fingerprint string          `json:"fingerprint"`
		Snapshot    bool            `json:"requestStatusSnapshot"`
		Interval    int             `json:"interval"`
	} `json:"payload"`
}
type Display struct {
	Device                  string
	Action                  string
	Settings                DisplaySettings
	Inspector               bool
	LastImage               string
	LastSent, LastInspector time.Time
}
type PrinterUpdate struct {
	Generation int
	Snapshot   PrinterSnapshot
}
type WatchFunc func(context.Context, PrinterConfig, string, *SnapshotGate, func(PrinterSnapshot))
type App struct {
	Instances   map[string]*Display
	Config      PrinterConfig
	Printer     PrinterSnapshot
	PC          PCSample
	Sample      func() PCSample
	ResetSample func()
	Send        func(interface{}) error
	Log         func(string)
	Persist     func(PrinterConfig) error
	Protect     func(string) (string, error)
	Reveal      func(string) (string, error)
	Watch       WatchFunc
	Updates     chan PrinterUpdate
	WatchEnded  chan int
	LastWake    time.Time
	WatchRetry  time.Time
	LastPC      time.Time
	Generation  int
	Cancel      context.CancelFunc
	Gate        SnapshotGate
}

func newApp(cfg PrinterConfig, send func(interface{}) error, log func(string)) *App {
	sampler := &NativeSampler{}
	return &App{Instances: map[string]*Display{}, Config: cfg, Printer: PrinterSnapshot{Phase: "setup", Reason: "Add your printer's local IP, serial and LAN access code in this display's settings."}, Sample: sampler.Sample, ResetSample: func() { *sampler = NativeSampler{} }, Send: send, Log: log, Persist: saveConfig, Protect: protectSecret, Reveal: revealSecret, Watch: watchPrinter, Updates: make(chan PrinterUpdate, 32), WatchEnded: make(chan int, 8)}
}
func (a *App) has(action string) bool {
	for _, d := range a.Instances {
		if d.Action == action {
			return true
		}
	}
	return false
}
func (a *App) stopWatch() {
	if a.Cancel != nil {
		a.Cancel()
		a.Cancel = nil
		a.Generation++
		a.Log("Printer monitoring stopped.")
	}
}
func (a *App) startWatch() {
	a.stopWatch()
	if !a.has(PrinterAction) {
		return
	}
	if e := validateConfig(a.Config); e != nil {
		a.Printer = PrinterSnapshot{Phase: "setup", Reason: e.Error()}
		return
	}
	code, e := a.Reveal(a.Config.Secret)
	if e != nil {
		a.Printer = PrinterSnapshot{Phase: "auth", Reason: e.Error()}
		return
	}
	if !codePattern.MatchString(code) {
		a.Printer = PrinterSnapshot{Phase: "auth", Reason: "The stored LAN code is invalid. Enter its current 8 characters again."}
		return
	}
	ctx, cancel := context.WithCancel(context.Background())
	a.Cancel = cancel
	a.Printer = PrinterSnapshot{Phase: "connecting", Reason: "Connecting to the printer on your local network."}
	a.WatchRetry = time.Time{}
	a.Generation++
	gen, cfg := a.Generation, a.Config
	a.Log("Printer monitoring started: visible P1S display.")
	go func() {
		defer func() {
			if recover() != nil {
				a.Log("Printer watcher ended unexpectedly; recovery will be attempted while visible.")
			}
			select {
			case a.WatchEnded <- gen:
			case <-ctx.Done():
			}
		}()
		a.Watch(ctx, cfg, code, &a.Gate, func(s PrinterSnapshot) {
			select {
			case a.Updates <- PrinterUpdate{gen, s}:
			case <-ctx.Done():
			}
		})
	}()
}
func (a *App) watcherEnded(gen int, now time.Time) {
	if gen != a.Generation {
		return
	}
	for {
		select {
		case u := <-a.Updates:
			a.observe(u)
		default:
			goto drained
		}
	}
drained:
	if a.Cancel != nil {
		a.Cancel()
		a.Cancel = nil
	}
	if !a.has(PrinterAction) {
		return
	}
	if a.Printer.Phase == "auth" || a.Printer.Phase == "trust" || a.Printer.Phase == "setup" {
		return
	}
	a.Printer = PrinterSnapshot{Phase: "offline", Reason: "Printer watcher stopped. Restarting automatically while the display is visible.", RetryInSeconds: 5}
	a.WatchRetry = now.Add(displayInterval)
}
func (a *App) ensureWatch(now time.Time) {
	if !a.has(PrinterAction) || a.Cancel != nil || a.WatchRetry.IsZero() || now.Before(a.WatchRetry) {
		return
	}
	switch a.Printer.Phase {
	case "auth", "trust", "setup":
		return
	}
	a.startWatch()
}
func (a *App) Notice(ctx, msg string, bad bool) error {
	d := a.Instances[ctx]
	if d == nil {
		return nil
	}
	return a.Send(map[string]interface{}{"event": "sendToPropertyInspector", "action": d.Action, "context": ctx, "payload": map[string]interface{}{"type": "notice", "message": msg, "error": bad}})
}
func (a *App) updateStatus(ctx string, now time.Time) error {
	d := a.Instances[ctx]
	if d == nil || !d.Inspector {
		return nil
	}
	payload := map[string]interface{}{"type": "status", "version": Version, "kind": "pc", "settings": d.Settings, "sample": a.PC, "image": svgURI(renderPC(a.PC))}
	if d.Action == PrinterAction {
		payload = map[string]interface{}{"type": "status", "version": Version, "kind": "p1s", "config": a.Config.Public(), "printer": a.Printer, "view": printerView(a.Printer, now), "image": svgURI(renderPrinter(a.Printer, now))}
	}
	d.LastInspector = now
	return a.Send(map[string]interface{}{"event": "sendToPropertyInspector", "action": d.Action, "context": ctx, "payload": payload})
}
func (a *App) refresh(now time.Time, force bool) error {
	for ctx, d := range a.Instances {
		// Hidden actions are absent from Instances. One initial image, then at
		// most one changed image every five seconds. No periodic repaint.
		if d.LastSent.IsZero() || now.Sub(d.LastSent) >= displayInterval {
			var img string
			if d.Action == PrinterAction {
				img = renderPrinter(a.Printer, now)
			} else {
				img = renderPC(a.PC)
			}
			if img != d.LastImage {
				if e := a.Send(map[string]interface{}{"event": "setImage", "context": ctx, "payload": map[string]interface{}{"image": svgURI(img), "target": 0}}); e != nil {
					return e
				}
				d.LastImage = img
				d.LastSent = now
			}
		}
		if d.Inspector && (force || now.Sub(d.LastInspector) >= displayInterval) {
			if e := a.updateStatus(ctx, now); e != nil {
				return e
			}
		}
	}
	return nil
}
func (a *App) tick(now time.Time) error {
	a.ensureWatch(now)
	if a.has(PCAction) {
		interval := 30
		for _, d := range a.Instances {
			if d.Action == PCAction && d.Settings.Interval < interval {
				interval = d.Settings.Interval
			}
		}
		if a.LastPC.IsZero() || now.Sub(a.LastPC) >= time.Duration(interval)*time.Second {
			first := a.LastPC.IsZero()
			a.PC = a.Sample()
			if first {
				a.PC.CPUValid = false
			}
			a.LastPC = now
		}
	}
	return a.refresh(now, false)
}
func (a *App) observe(u PrinterUpdate) {
	if u.Generation != a.Generation || !a.has(PrinterAction) {
		return
	}
	old := a.Printer.Phase
	a.Printer = u.Snapshot
	if old != a.Printer.Phase {
		a.Log("Printer state: " + a.Printer.Phase + " - " + a.Printer.Reason)
	}
}

// dropInactive cancels only the no-longer-visible subsystem; the other view
// may stay active. CPU baselines do not span a hidden period.
func (a *App) dropInactive() {
	if !a.has(PrinterAction) {
		a.stopWatch()
	}
	if !a.has(PCAction) {
		a.LastPC = time.Time{}
		a.PC = PCSample{}
		if a.ResetSample != nil {
			a.ResetSample()
		}
	}
}
func (a *App) handle(raw []byte, now time.Time) error {
	var e HostEvent
	if er := json.Unmarshal(raw, &e); er != nil {
		return nil
	}
	if e.Event == "deviceDidDisconnect" {
		for id, d := range a.Instances {
			if e.Device != "" && d.Device == e.Device {
				delete(a.Instances, id)
			}
		}
		a.dropInactive()
		return nil
	}
	if e.Event == "systemDidWakeUp" {
		// Stream Dock may deliver the same wake event repeatedly. Restart
		// once, rather than tearing down a recovering MQTT link each time.
		if !a.LastWake.IsZero() && now.Sub(a.LastWake) < 10*time.Second {
			return nil
		}
		a.LastWake = now
		a.LastPC = time.Time{}
		a.PC = PCSample{}
		if a.ResetSample != nil {
			a.ResetSample()
		}
		if a.has(PrinterAction) {
			a.startWatch()
		}
		return a.tick(now)
	}
	if e.Action != PrinterAction && e.Action != PCAction {
		return nil
	}
	d := a.Instances[e.Context]
	switch e.Event {
	case "willAppear":
		if d != nil {
			d.Settings = normalSettings(e.Payload.Settings)
			if e.Device != "" {
				d.Device = e.Device
			}
			a.ensureWatch(now)
			return nil
		}
		was := a.has(PrinterAction)
		a.Instances[e.Context] = &Display{Action: e.Action, Settings: normalSettings(e.Payload.Settings), Device: e.Device}
		if !was && e.Action == PrinterAction {
			a.startWatch()
		}
		if er := a.Send(map[string]interface{}{"event": "setTitle", "context": e.Context, "payload": map[string]interface{}{"title": "", "target": 0}}); er != nil {
			return er
		}
		return a.tick(now)
	case "willDisappear":
		delete(a.Instances, e.Context)
		a.dropInactive()
	case "propertyInspectorDidAppear":
		if d != nil {
			d.Inspector = true
			return a.updateStatus(e.Context, now)
		}
	case "propertyInspectorDidDisappear":
		if d != nil {
			d.Inspector = false
		}
	case "didReceiveSettings":
		if d != nil {
			d.Settings = normalSettings(e.Payload.Settings)
			return a.refresh(now, true)
		}
	case "sendToPlugin":
		if d == nil {
			return nil
		}
		switch e.Payload.Command {
		case "inspectorClose":
			d.Inspector = false
			return nil
		case "inspectorOpen":
			d.Inspector = true
			return a.updateStatus(e.Context, now)
		case "status":
			return a.updateStatus(e.Context, now)
		case "savePC":
			if d.Action != PCAction {
				return nil
			}
			d.Settings = normalSettings(DisplaySettings{Interval: e.Payload.Interval})
			if er := a.Send(map[string]interface{}{"event": "setSettings", "context": e.Context, "payload": d.Settings}); er != nil {
				return er
			}
			_ = a.Notice(e.Context, "Saved. CPU and RAM will update at the selected interval.", false)
		case "savePrinter":
			if d.Action != PrinterAction {
				return nil
			}
			c := a.Config
			c.Host = strings.TrimSpace(e.Payload.Host)
			c.Serial = strings.TrimSpace(e.Payload.Serial)
			c.RequestSnapshot = e.Payload.Snapshot
			if c.Host != a.Config.Host || c.Serial != a.Config.Serial {
				c.Fingerprint = ""
				c.Secret = ""
			}
			if e.Payload.AccessCode != "" {
				v := strings.TrimSpace(e.Payload.AccessCode)
				if !codePattern.MatchString(v) {
					return a.Notice(e.Context, "The LAN access code must be 8 letters/numbers from the printer, not your Bambu account password.", true)
				}
				secret, er := a.Protect(v)
				if er != nil {
					return a.Notice(e.Context, er.Error(), true)
				}
				c.Secret = secret
			}
			if er := validateConfig(c); er != nil {
				return a.Notice(e.Context, er.Error(), true)
			}
			if er := a.Persist(c); er != nil {
				return a.Notice(e.Context, "Could not save printer settings: "+er.Error(), true)
			}
			a.Config = c
			a.startWatch()
			_ = a.Notice(e.Context, "Saved. The LAN code is protected by Windows for your user account. Confirm the printer certificate when it appears below.", false)
		case "trustPrinter":
			if d.Action != PrinterAction {
				return nil
			}
			if a.Printer.Phase != "trust" || a.Printer.Candidate == "" || e.Payload.Fingerprint != a.Printer.Candidate {
				return a.Notice(e.Context, "The certificate waiting for confirmation changed. Use Reconnect and review it again.", true)
			}
			c := a.Config
			c.Fingerprint = a.Printer.Candidate
			if er := a.Persist(c); er != nil {
				return a.Notice(e.Context, "Could not save certificate approval: "+er.Error(), true)
			}
			a.Config = c
			a.startWatch()
			_ = a.Notice(e.Context, "Printer certificate saved. Connecting securely to read its status.", false)
		case "reconnect":
			if d.Action == PrinterAction {
				a.startWatch()
			}
		case "forgetPrinter":
			if d.Action != PrinterAction {
				return nil
			}
			c := PrinterConfig{RequestSnapshot: true}
			if er := a.Persist(c); er != nil {
				return a.Notice(e.Context, "Could not clear printer settings: "+er.Error(), true)
			}
			a.Config = c
			a.startWatch()
			_ = a.Notice(e.Context, "Printer details and saved access code cleared. PC and Steam displays are unchanged.", false)
		case "diagnostic":
			report := map[string]interface{}{"version": Version, "sample": a.PC, "printer": a.Printer, "config": a.Config.Public(), "time": now, "recentConnectionLog": recentLog(), "note": "Read-only. Access code is excluded. Contains local printer IP/serial and job name."}
			if er := writeDiagnostic(report); er != nil {
				return a.Notice(e.Context, er.Error(), true)
			}
		}
		return a.refresh(now, true)
	}
	return nil
}
func configPath() string { return filepath.Join(dataRoot(), "printer.json") }
func loadConfig() (PrinterConfig, error) {
	c := PrinterConfig{RequestSnapshot: true}
	b, e := os.ReadFile(configPath())
	if os.IsNotExist(e) {
		return c, nil
	}
	if e != nil {
		return c, e
	}
	if len(b) > 32768 {
		return c, fmt.Errorf("Saved printer configuration is too large")
	}
	e = json.Unmarshal(b, &c)
	return c, e
}
func saveConfig(c PrinterConfig) error {
	if e := os.MkdirAll(dataRoot(), 0700); e != nil {
		return e
	}
	b, e := json.MarshalIndent(c, "", "  ")
	if e != nil {
		return e
	}
	f, e := os.CreateTemp(dataRoot(), "printer-*.tmp")
	if e != nil {
		return e
	}
	p := f.Name()
	defer os.Remove(p)
	if _, e = f.Write(b); e != nil {
		f.Close()
		return e
	}
	if e = f.Close(); e != nil {
		return e
	}
	return os.Rename(p, configPath())
}
