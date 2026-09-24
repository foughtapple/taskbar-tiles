package main

import (
	"bytes"
	"encoding/json"
	"encoding/xml"
	"fmt"
	"image/png"
	"io"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"time"
)

var logMutex sync.Mutex

func dataRoot() string {
	p := os.Getenv("LOCALAPPDATA")
	if p == "" {
		p = os.TempDir()
	}
	return filepath.Join(p, "FoughtApple", "DeskStatus")
}
func logger(s string) {
	logMutex.Lock()
	defer logMutex.Unlock()
	if os.MkdirAll(dataRoot(), 0700) != nil {
		return
	}
	p := filepath.Join(dataRoot(), "desk-status.log")
	if st, e := os.Stat(p); e == nil && st.Size() > 256000 {
		_ = os.Remove(p + ".previous")
		_ = os.Rename(p, p+".previous")
	}
	f, e := os.OpenFile(p, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0600)
	if e == nil {
		defer f.Close()
		fmt.Fprintf(f, "%s %s\r\n", time.Now().Format(time.RFC3339), s)
	}
}
func recentLog() string {
	f, e := os.Open(filepath.Join(dataRoot(), "desk-status.log"))
	if e != nil {
		return "No connection log yet."
	}
	defer f.Close()
	st, e := f.Stat()
	if e != nil {
		return "Could not read log metadata."
	}
	start := st.Size() - 16384
	if start < 0 {
		start = 0
	}
	_, _ = f.Seek(start, io.SeekStart)
	b, e := io.ReadAll(io.LimitReader(f, 16384))
	if e != nil {
		return "Could not read connection log."
	}
	if start > 0 {
		if n := bytes.IndexByte(b, '\n'); n >= 0 {
			b = b[n+1:]
		}
	}
	return string(b)
}
func parseArgs(argv []string) (map[string]string, error) {
	m := map[string]string{}
	for i := 0; i < len(argv); i++ {
		k := strings.TrimLeft(argv[i], "-")
		if p := strings.SplitN(k, "=", 2); len(p) == 2 {
			m[p[0]] = p[1]
			continue
		}
		if i+1 >= len(argv) {
			return nil, fmt.Errorf("Missing Stream Dock argument value")
		}
		i++
		m[k] = argv[i]
	}
	return m, nil
}
func packageRoot() string { exe, _ := os.Executable(); return filepath.Dir(filepath.Dir(exe)) }
func validateAssets(root string) error {
	for _, name := range []string{"p1s", "pc", "plugin", "category"} {
		f, e := os.Open(filepath.Join(root, "images", name+".png"))
		if e != nil {
			return e
		}
		cfg, e := png.DecodeConfig(f)
		f.Close()
		if e != nil {
			return e
		}
		if cfg.Width != 256 || cfg.Height != 256 {
			return fmt.Errorf("%s.png is not 256 x 256", name)
		}
	}
	for _, s := range []string{renderPC(PCSample{CPU: 42, CPUValid: true, RAM: 58, RAMValid: true}), renderPrinter(PrinterSnapshot{Phase: "setup"}, time.Now())} {
		d := xml.NewDecoder(strings.NewReader(s))
		for {
			_, e := d.Token()
			if e == io.EOF {
				break
			}
			if e != nil {
				return e
			}
		}
	}
	return nil
}
func writeDiagnostic(report interface{}) error {
	if e := os.MkdirAll(dataRoot(), 0700); e != nil {
		return e
	}
	b, e := json.MarshalIndent(report, "", "  ")
	if e != nil {
		return e
	}
	p := filepath.Join(dataRoot(), "diagnostic.txt")
	if e = os.WriteFile(p, b, 0600); e != nil {
		return e
	}
	return openReport(p)
}
func runHost(port int, uuid, registration, root string) error {
	allowed, e := loadActionGate(root)
	if e != nil {
		return e
	}
	ws, e := connectSocket(port)
	if e != nil {
		return e
	}
	defer ws.close()
	send := func(v interface{}) error {
		b, e := json.Marshal(v)
		if e != nil {
			return e
		}
		return ws.sendText(b)
	}
	if e = send(map[string]string{"event": registration, "uuid": uuid}); e != nil {
		return e
	}
	cfg, err := loadConfig()
	if err != nil {
		logger("Saved printer settings could not be read; setup is required.")
		cfg = PrinterConfig{RequestSnapshot: true}
	}
	a := newApp(cfg, send, logger)
	defer a.stopWatch()
	logger("Connected to Stream Dock; Desk Status " + Version)
	incoming := make(chan []byte, 16)
	errs := make(chan error, 1)
	done := make(chan struct{})
	defer close(done)
	go func() {
		for {
			b, e := ws.receive()
			if e != nil {
				select {
				case errs <- e:
				case <-done:
				}
				return
			}
			select {
			case incoming <- b:
			case <-done:
				return
			}
		}
	}()
	// No periodic wakeups with zero visible views. The SDK socket stays idle so
	// Stream Dock can notify us when a display becomes visible again.
	var ticker *time.Ticker
	var ticks <-chan time.Time
	defer func() {
		if ticker != nil {
			ticker.Stop()
		}
	}()
	for {
		select {
		case raw := <-incoming:
			if !allowed.allows(raw) {
				continue
			}
			if e = a.handle(raw, time.Now()); e != nil {
				return e
			}
			if len(a.Instances) > 0 && ticker == nil {
				ticker = time.NewTicker(displayInterval)
				ticks = ticker.C
			}
			if len(a.Instances) == 0 && ticker != nil {
				ticker.Stop()
				ticker = nil
				ticks = nil
			}
		case gen := <-a.WatchEnded:
			a.watcherEnded(gen, time.Now())
		case u := <-a.Updates:
			a.observe(u)
		case t := <-ticks:
			if e = a.tick(t); e != nil {
				return e
			}
		case e := <-errs:
			return e
		}
	}
}
func main() {
	if len(os.Args) == 2 && os.Args[1] == "--version" {
		fmt.Println(Version)
		return
	}
	if len(os.Args) == 2 && os.Args[1] == "--validate-assets" {
		if e := validateAssets(packageRoot()); e != nil {
			logger("Package check: " + e.Error())
			os.Exit(2)
		}
		return
	}
	if len(os.Args) == 2 && os.Args[1] == "--diagnose" {
		c, e := loadConfig()
		s := &NativeSampler{}
		_ = s.Sample()
		time.Sleep(2 * time.Second)
		p := s.Sample()
		note := "Printer settings: not configured."
		if e != nil {
			note = "Printer configuration could not be read."
		} else if c.Host != "" {
			note = "For the live printer connection reason, select P1S Print Status in Stream Dock. No access code is included here."
		}
		if er := writeDiagnostic(map[string]interface{}{"version": Version, "time": time.Now(), "pc": p, "config": c.Public(), "recentConnectionLog": recentLog(), "note": note}); er != nil {
			notify(er.Error())
		}
		return
	}
	if len(os.Args) == 3 && os.Args[1] == "--render-previews" {
		renderPreviews(os.Args[2])
		return
	}
	if len(os.Args) == 1 {
		notify("This is the Desk Status display plugin, not a button launcher.\n\nUse INSTALL-DESK-DISPLAYS.cmd, reopen Stream Dock, then drag FoughtApple > P1S Print Status or PC CPU + RAM from Info board onto a display slot.\n\nYour Steam display and seven buttons are separate and unchanged.")
		return
	}
	args, e := parseArgs(os.Args[1:])
	if e != nil {
		logger(e.Error())
		return
	}
	port, e := strconv.Atoi(args["port"])
	if e != nil || port < 1 || port > 65535 || args["pluginUUID"] == "" || args["registerEvent"] == "" {
		logger("Missing Stream Dock registration arguments. Add the plugin action, not Toolbox > Open.")
		return
	}
	if e = runHost(port, args["pluginUUID"], args["registerEvent"], packageRoot()); e != nil && e != io.EOF {
		logger("Stream Dock connection ended: " + e.Error())
	}
}
func renderPreviews(dir string) {
	_ = os.MkdirAll(dir, 0700)
	n, t := 64, 83
	now := time.Now()
	views := map[string]string{"pc": renderPC(PCSample{CPU: 32, CPUValid: true, RAM: 58, RAMValid: true}), "pc-busy": renderPC(PCSample{CPU: 96, CPUValid: true, RAM: 91, RAMValid: true}), "pc-waiting": renderPC(PCSample{}), "p1s": renderPrinter(PrinterSnapshot{Phase: "online", Connected: true, Data: PrintData{State: "RUNNING", Percent: &n, Remaining: &t, LastReport: now}}, now), "p1s-ready": renderPrinter(PrinterSnapshot{Phase: "online", Connected: true, Data: PrintData{State: "IDLE", LastReport: now}}, now)}
	for name, state := range map[string]string{"done": "FINISH", "paused": "PAUSE", "error": "FAILED"} {
		views["p1s-"+name] = renderPrinter(PrinterSnapshot{Phase: "online", Connected: true, Data: PrintData{State: state, LastReport: now}}, now)
	}
	for _, phase := range []string{"setup", "offline", "trust", "connecting", "auth"} {
		views["p1s-"+phase] = renderPrinter(PrinterSnapshot{Phase: phase}, now)
	}
	for name, s := range views {
		_ = os.WriteFile(filepath.Join(dir, name+".svg"), []byte(s), 0600)
	}
}
