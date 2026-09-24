package main

import (
	"context"
	"encoding/json"
	"sync/atomic"
	"testing"
	"time"
)

func deskEvent(name, action, id, device string) []byte {
	b, _ := json.Marshal(map[string]interface{}{"event": name, "action": action, "context": id, "device": device})
	return b
}
func deskCounter() (*App, map[string]int) {
	c := map[string]int{}
	a := newApp(PrinterConfig{}, func(v interface{}) error {
		b, _ := json.Marshal(v)
		var m map[string]interface{}
		_ = json.Unmarshal(b, &m)
		c[m["event"].(string)]++
		return nil
	}, func(string) {})
	a.Sample = func() PCSample { return PCSample{CPU: 32, CPUValid: true, RAM: 58, RAMValid: true} }
	return a, c
}
func TestNoSamplingOrRenderingBeforeAssignment(t *testing.T) {
	a, c := deskCounter()
	calls := 0
	a.Sample = func() PCSample { calls++; return PCSample{} }
	n := time.Now()
	for i := 0; i < 100; i++ {
		_ = a.tick(n.Add(time.Duration(i) * time.Second))
	}
	if calls != 0 || len(c) != 0 {
		t.Fatal(calls, c)
	}
}
func TestDeskUnchangedImagesNotResentAndInspectorClosed(t *testing.T) {
	a, c := deskCounter()
	n := time.Now()
	_ = a.handle(hostMsg("willAppear", PCAction, "pc", nil), n)
	for i := 1; i < 100; i++ {
		_ = a.tick(n.Add(time.Duration(i) * 5 * time.Second))
	}
	if c["setImage"] != 2 || c["setTitle"] != 1 || c["sendToPropertyInspector"] != 0 {
		t.Fatal(c)
	}
	_ = a.handle(hostMsg("didReceiveSettings", PCAction, "pc", nil), n.Add(500*time.Second))
	if c["setImage"] != 2 {
		t.Fatal("settings forced duplicate image", c)
	}
}
func TestDeskInspectorUpdatesStopWhenClosed(t *testing.T) {
	a, c := deskCounter()
	n := time.Now()
	_ = a.handle(hostMsg("willAppear", PCAction, "pc", nil), n)
	_ = a.handle(hostMsg("propertyInspectorDidAppear", PCAction, "pc", nil), n)
	if c["sendToPropertyInspector"] != 1 {
		t.Fatal(c)
	}
	_ = a.handle(hostMsg("propertyInspectorDidDisappear", PCAction, "pc", nil), n)
	_ = a.tick(n.Add(5 * time.Second))
	_ = a.tick(n.Add(10 * time.Second))
	if c["sendToPropertyInspector"] != 1 {
		t.Fatal("closed inspector updated", c)
	}
}
func TestFiveSecondMinimumAndLegacyMigration(t *testing.T) {
	for _, v := range []int{0, 1, 2, 3, 4, 999} {
		if normalSettings(DisplaySettings{v}).Interval != 5 {
			t.Fatal(v)
		}
	}
	for _, v := range []int{5, 10, 30} {
		if normalSettings(DisplaySettings{v}).Interval != v {
			t.Fatal(v)
		}
	}
	a, _ := deskCounter()
	n := time.Now()
	calls := 0
	a.Sample = func() PCSample { calls++; return PCSample{} }
	_ = a.handle(hostMsg("willAppear", PCAction, "pc", map[string]interface{}{"settings": DisplaySettings{1}}), n)
	for i := 1; i < 5; i++ {
		_ = a.tick(n.Add(time.Duration(i) * time.Second))
	}
	if calls != 1 {
		t.Fatal("sample interval under 5 sec", calls)
	}
	_ = a.tick(n.Add(5 * time.Second))
	if calls != 2 {
		t.Fatal(calls)
	}
}
func TestPrinterWatchStopsOnLastVisibilityAndDeviceDisconnect(t *testing.T) {
	a, _ := deskCounter()
	a.Config = PrinterConfig{Host: "192.168.1.8", Serial: "TESTSERIAL", Secret: "mock"}
	a.Reveal = func(string) (string, error) { return "TESTCODE", nil }
	var started, stopped atomic.Int32
	a.Watch = func(ctx context.Context, _ PrinterConfig, _ string, _ *SnapshotGate, _ func(PrinterSnapshot)) {
		started.Add(1)
		<-ctx.Done()
		stopped.Add(1)
	}
	defer a.stopWatch()
	n := time.Now()
	_ = a.handle(deskEvent("willAppear", PrinterAction, "one", "dock"), n)
	_ = a.handle(deskEvent("willAppear", PrinterAction, "two", "dock"), n)
	time.Sleep(20 * time.Millisecond)
	if started.Load() != 1 {
		t.Fatal("duplicate workers", started.Load())
	}
	_ = a.handle(deskEvent("willDisappear", PrinterAction, "one", "dock"), n)
	if a.Cancel == nil {
		t.Fatal("stopped while second display visible")
	}
	_ = a.handle(deskEvent("deviceDidDisconnect", "", "", "dock"), n)
	time.Sleep(20 * time.Millisecond)
	if a.Cancel != nil || stopped.Load() != 1 || len(a.Instances) != 0 {
		t.Fatal("watch not canceled", stopped.Load())
	}
	gen := a.Generation
	a.observe(PrinterUpdate{gen - 1, PrinterSnapshot{Phase: "online"}})
	if a.Printer.Phase == "online" {
		t.Fatal("old update accepted")
	}
}
func TestCPUAndPrinterVisibilityIndependent(t *testing.T) {
	a, _ := deskCounter()
	calls := 0
	a.Sample = func() PCSample { calls++; return PCSample{} }
	n := time.Now()
	_ = a.handle(deskEvent("willAppear", PCAction, "pc", "pcDock"), n)
	_ = a.handle(deskEvent("willAppear", PrinterAction, "p1s", "p1sDock"), n)
	_ = a.handle(deskEvent("deviceDidDisconnect", "", "", "pcDock"), n)
	for i := 1; i < 10; i++ {
		_ = a.tick(n.Add(time.Duration(i) * 5 * time.Second))
	}
	if calls != 1 || len(a.Instances) != 1 || !a.has(PrinterAction) {
		t.Fatal(calls, a.Instances)
	}
}
func TestPrinterReportBurstDoesNotFloodDisplay(t *testing.T) {
	a, c := deskCounter()
	n := time.Now()
	_ = a.handle(hostMsg("willAppear", PrinterAction, "p1s", nil), n)
	for i := 0; i < 100; i++ {
		v := i
		a.observe(PrinterUpdate{a.Generation, PrinterSnapshot{Phase: "online", Connected: true, Data: PrintData{State: "RUNNING", Percent: &v, LastReport: n}}})
		_ = a.refresh(n.Add(2*time.Second), false)
	}
	if c["setImage"] != 1 {
		t.Fatal("report burst repainted", c)
	}
	_ = a.tick(n.Add(5 * time.Second))
	if c["setImage"] != 2 {
		t.Fatal(c)
	}
}
