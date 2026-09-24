package main

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"strings"
	"sync/atomic"
	"testing"
	"time"
)

func hostMsg(event, action, ctx string, payload interface{}) []byte {
	b, _ := json.Marshal(map[string]interface{}{"event": event, "action": action, "context": ctx, "payload": payload})
	return b
}
func TestTwoIndependentActionsAndCoalescing(t *testing.T) {
	var sent []map[string]interface{}
	a := newApp(PrinterConfig{}, func(v interface{}) error {
		b, _ := json.Marshal(v)
		var m map[string]interface{}
		_ = json.Unmarshal(b, &m)
		sent = append(sent, m)
		return nil
	}, func(string) {})
	samples := 0
	a.Sample = func() PCSample { samples++; return PCSample{CPU: 32, CPUValid: true, RAM: 58, RAMValid: true} }
	n := time.Now()
	if e := a.handle(hostMsg("willAppear", PCAction, "pc", nil), n); e != nil {
		t.Fatal(e)
	}
	if e := a.handle(hostMsg("willAppear", PrinterAction, "p1s", nil), n); e != nil {
		t.Fatal(e)
	}
	if len(a.Instances) != 2 || a.Printer.Phase != "setup" {
		t.Fatal(a.Printer)
	}
	_ = a.tick(n.Add(5 * time.Second))
	if samples != 2 {
		t.Fatal(samples)
	}
	before := len(sent)
	_ = a.tick(n.Add(6 * time.Second))
	if len(sent) != before {
		t.Fatal("unchanged tile sent too often")
	}
	seen := map[string]bool{}
	for _, m := range sent {
		if m["event"] == "setImage" {
			ctx := m["context"].(string)
			p := m["payload"].(map[string]interface{})
			uri := p["image"].(string)
			raw, e := base64.StdEncoding.DecodeString(strings.TrimPrefix(uri, "data:image/svg+xml;base64,"))
			if e != nil || !strings.Contains(string(raw), "<svg") {
				t.Fatal(uri)
			}
			seen[ctx] = true
		}
	}
	if !seen["pc"] || !seen["p1s"] {
		t.Fatal(seen)
	}
	_ = a.handle(hostMsg("willDisappear", PCAction, "pc", nil), n)
	_ = a.tick(n.Add(10 * time.Second))
	if samples != 2 {
		t.Fatal("hidden PC kept sampling")
	}
}
func TestCredentialSaveNeverUsesSDKSettings(t *testing.T) {
	var sent []string
	var saved PrinterConfig
	var calls atomic.Int32
	a := newApp(PrinterConfig{}, func(v interface{}) error { b, _ := json.Marshal(v); sent = append(sent, string(b)); return nil }, func(string) {})
	a.Protect = func(s string) (string, error) { return "encrypted-value", nil }
	a.Reveal = func(s string) (string, error) { return "TESTCODE", nil }
	a.Persist = func(c PrinterConfig) error { saved = c; return nil }
	a.Watch = func(ctx context.Context, c PrinterConfig, s string, g *SnapshotGate, emit func(PrinterSnapshot)) {
		calls.Add(1)
		<-ctx.Done()
	}
	defer a.stopWatch()
	n := time.Now()
	_ = a.handle(hostMsg("willAppear", PrinterAction, "p1s", nil), n)
	_ = a.handle(hostMsg("sendToPlugin", PrinterAction, "p1s", map[string]interface{}{"command": "savePrinter", "host": "192.168.1.5", "serial": "TESTSERIAL", "accessCode": "TESTCODE", "requestStatusSnapshot": true}), n)
	if saved.Secret != "encrypted-value" || saved.Host != "192.168.1.5" {
		t.Fatal(saved)
	}
	for _, s := range sent {
		if strings.Contains(s, "TESTCODE") || strings.Contains(s, "encrypted-value") || strings.Contains(s, `"event":"setSettings"`) {
			t.Fatal("credential sent back through SDK", s)
		}
	}
}
func TestStaleWorkerUpdatesAreIgnored(t *testing.T) {
	a := newApp(PrinterConfig{}, func(interface{}) error { return nil }, func(string) {})
	a.Generation = 5
	a.Printer.Phase = "offline"
	a.observe(PrinterUpdate{4, PrinterSnapshot{Phase: "online"}})
	if a.Printer.Phase != "offline" {
		t.Fatal("stale watch overwrote status")
	}
}
func TestIntervalSettingsAndOtherPluginsIgnored(t *testing.T) {
	a := newApp(PrinterConfig{}, func(interface{}) error { return nil }, func(string) {})
	n := time.Now()
	_ = a.handle(hostMsg("willAppear", "com.foughtapple.steamstatus.display", "steam", nil), n)
	if len(a.Instances) != 0 {
		t.Fatal("touched steam")
	}
	if normalSettings(DisplaySettings{Interval: 0}).Interval != 5 || normalSettings(DisplaySettings{Interval: 999}).Interval != 5 {
		t.Fatal("bad interval")
	}
}
func TestTrustRequiresCurrentCertificate(t *testing.T) {
	a := newApp(PrinterConfig{}, func(interface{}) error { return nil }, func(string) {})
	n := time.Now()
	_ = a.handle(hostMsg("willAppear", PrinterAction, "p1s", nil), n)
	saved := false
	a.Persist = func(PrinterConfig) error { saved = true; return nil }
	a.Printer = PrinterSnapshot{Phase: "trust", Candidate: strings.Repeat("a", 64)}
	_ = a.handle(hostMsg("sendToPlugin", PrinterAction, "p1s", map[string]interface{}{"command": "trustPrinter", "fingerprint": strings.Repeat("b", 64)}), n)
	if saved {
		t.Fatal("stale certificate accepted")
	}
}
