package main

import (
	"encoding/json"
	"encoding/xml"
	"io"
	"math"
	"strings"
	"testing"
	"time"
)

func TestCPUBusyIncludesIdleInKernel(t *testing.T) {
	v, ok := cpuBusy(Times{100, 300, 100}, Times{150, 450, 150})
	if !ok || math.Abs(v-75) > .00001 {
		t.Fatal(v, ok)
	}
}
func TestCPUCounterInvalidCases(t *testing.T) {
	for _, p := range [][2]Times{{{1, 2, 3}, {1, 2, 3}}, {{3, 3, 3}, {2, 4, 4}}, {{0, 0, 0}, {100, 10, 10}}} {
		if _, ok := cpuBusy(p[0], p[1]); ok {
			t.Fatal(p)
		}
	}
}
func TestPhysicalRAM(t *testing.T) {
	v, u, ok := memoryUse(32<<30, 8<<30)
	if !ok || v != 75 || u != 24<<30 {
		t.Fatal(v, u, ok)
	}
	for _, p := range [][2]uint64{{0, 0}, {10, 11}} {
		if _, _, ok := memoryUse(p[0], p[1]); ok {
			t.Fatal(p)
		}
	}
}
func TestTelemetryDeltasKeepPreviousKnownValues(t *testing.T) {
	var d PrintData
	n := time.Now()
	_, e := d.Apply([]byte(`{"print":{"gcode_state":"RUNNING","mc_percent":64,"mc_remaining_time":83,"subtask_id":"111","layer_num":42,"total_layer_num":100}}`), n)
	if e != nil {
		t.Fatal(e)
	}
	_, e = d.Apply([]byte(`{"print":{"mc_percent":65,"nozzle_temper":240.5}}`), n.Add(time.Second))
	if e != nil || d.State != "RUNNING" || *d.Percent != 65 || *d.Remaining != 83 || *d.Nozzle != 240.5 {
		t.Fatal(d, e)
	}
}
func TestNewJobClearsMissingProgressAndError(t *testing.T) {
	var d PrintData
	n := time.Now()
	_, _ = d.Apply([]byte(`{"print":{"gcode_state":"FINISH","subtask_id":"111","mc_percent":100,"mc_remaining_time":0,"print_error":9}}`), n)
	_, _ = d.Apply([]byte(`{"print":{"gcode_state":"PREPARE","subtask_id":"112"}}`), n)
	if d.Percent != nil || d.Remaining != nil || d.ErrorCode != 0 {
		t.Fatal(d)
	}
}
func TestInvalidAndMissingFields(t *testing.T) {
	var d PrintData
	n := time.Now()
	if ok, _ := d.Apply([]byte(`{"info":{"command":"get_version"}}`), n); ok {
		t.Fatal("info was telemetry")
	}
	if ok, _ := d.Apply([]byte(`{"print":{"command":"ack"}}`), n); ok {
		t.Fatal("ack was telemetry")
	}
	_, _ = d.Apply([]byte(`{"print":{"mc_percent":101,"mc_remaining_time":-1}}`), n)
	if d.Percent != nil || d.Remaining != nil {
		t.Fatal(d)
	}
	if _, e := d.Apply([]byte(`{`), n); e == nil {
		t.Fatal("accepted malformed JSON")
	}
}
func TestStatusTransitionsAndFreshness(t *testing.T) {
	n := time.Now()
	p, m := 75, 100
	for _, tc := range []struct{ state, main string }{{"RUNNING", "75%"}, {"PAUSE", "PAUSED"}, {"PREPARE", "PREP"}, {"FINISH", "DONE"}, {"IDLE", "READY"}, {"FAILED", "STOPPED"}, {"STOP", "STOPPED"}, {"other", "WAIT"}} {
		s := PrinterSnapshot{Phase: "online", Connected: true, Data: PrintData{State: tc.state, Percent: &p, Remaining: &m, LastReport: n}}
		if v := printerView(s, n); v.Main != tc.main {
			t.Fatal(tc, v)
		}
	}
	s := PrinterSnapshot{Phase: "online", Connected: true, Data: PrintData{State: "RUNNING", Percent: &p, LastReport: n.Add(-91 * time.Second)}}
	v := printerView(s, n)
	if v.Main != "NO DATA" || v.Percent != nil {
		t.Fatal(v)
	}
	s.Phase = "offline"
	s.Connected = false
	v = printerView(s, n)
	if v.Main != "OFFLINE" || v.Percent != nil {
		t.Fatal(v)
	}
}
func TestReportWithoutStateIsNotReady(t *testing.T) {
	s := PrinterSnapshot{Phase: "online", Connected: true}
	_, _ = s.Data.Apply([]byte(`{"print":{"nozzle_temper":32}}`), time.Now())
	if printerView(s, time.Now()).Main != "NO DATA" {
		t.Fatal(s)
	}
}
func TestPrinterErrorHexAndClear(t *testing.T) {
	s := PrinterSnapshot{Phase: "online", Connected: true}
	n := time.Now()
	_, _ = s.Data.Apply([]byte(`{"print":{"gcode_state":"RUNNING","print_error":"0x07008003"}}`), n)
	if s.Data.ErrorCode != 0x07008003 || printerView(s, n).Main != "CHECK" {
		t.Fatal(s)
	}
	_, _ = s.Data.Apply([]byte(`{"print":{"print_error":0}}`), n)
	if printerView(s, n).State != "running" {
		t.Fatal(s)
	}
}
func TestTimeFormatting(t *testing.T) {
	for _, tc := range []struct {
		n    int
		want string
	}{{0, "<1m left"}, {1, "1m left"}, {59, "59m left"}, {60, "1h 00m"}, {83, "1h 23m"}, {1440, "1d 0h"}} {
		if v := remainingLabel(&tc.n); v != tc.want {
			t.Fatal(v)
		}
	}
}
func TestNetworkAndSerialValidation(t *testing.T) {
	for _, host := range []string{"192.168.1.42", "10.0.0.3", "172.20.5.3", "100.80.0.3", "fd00::22"} {
		if !validPrinterAddress(host) {
			t.Fatal(host)
		}
	}
	for _, host := range []string{"8.8.8.8", "steam.com", "https://192.168.1.1", "127.0.0.1", "0.0.0.0", "192.168.1.4:8883"} {
		if validPrinterAddress(host) {
			t.Fatal(host)
		}
	}
	for _, serial := range []string{"x/#", "../x", "#", "hello+"} {
		if serialPattern.MatchString(serial) {
			t.Fatal(serial)
		}
	}
}
func TestPublicConfigExcludesSecret(t *testing.T) {
	c := PrinterConfig{Secret: "TOP-SECRET-EXAMPLE"}
	b, _ := json.Marshal(c.Public())
	if strings.Contains(string(b), "SECRET") || strings.Contains(string(b), "protectedAccessCode") {
		t.Fatal(string(b))
	}
}
func TestAllSVGStatesValidAndBlack(t *testing.T) {
	n := time.Now()
	list := []string{renderPC(PCSample{}), renderPC(PCSample{CPU: 100, CPUValid: true, RAM: 100, RAMValid: true})}
	for _, s := range []string{"RUNNING", "PAUSE", "FINISH", "IDLE", "FAILED"} {
		list = append(list, renderPrinter(PrinterSnapshot{Phase: "online", Connected: true, Data: PrintData{State: s, LastReport: n}}, n))
	}
	for _, s := range []string{"setup", "trust", "connecting", "offline", "auth", "error"} {
		list = append(list, renderPrinter(PrinterSnapshot{Phase: s}, n))
	}
	for _, s := range list {
		if !strings.Contains(s, `width="256" height="256"`) || !strings.Contains(s, `fill="#000000"`) {
			t.Fatal(s)
		}
		d := xml.NewDecoder(strings.NewReader(s))
		for {
			_, e := d.Token()
			if e == io.EOF {
				break
			}
			if e != nil {
				t.Fatal(e)
			}
		}
	}
}
