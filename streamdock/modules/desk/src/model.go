package main

import (
	"encoding/hex"
	"encoding/json"
	"fmt"
	"math"
	"net"
	"regexp"
	"strconv"
	"strings"
	"time"
)

const Version = "1.2.1"
const PluginID = "com.foughtapple.deskstatus"
const PrinterAction = PluginID + ".p1s"
const PCAction = PluginID + ".pc"

// Only the current Windows user's encrypted access code is persisted.
// It is never saved in Stream Dock action settings, logs, or diagnostics.
type PrinterConfig struct {
	Host            string `json:"host"`
	Serial          string `json:"serial"`
	Secret          string `json:"protectedAccessCode"`
	Fingerprint     string `json:"certificateSHA256"`
	RequestSnapshot bool   `json:"requestStatusSnapshot"`
}
type PublicConfig struct {
	Host            string `json:"host"`
	Serial          string `json:"serial"`
	HasCode         bool   `json:"hasAccessCode"`
	Fingerprint     string `json:"certificateSHA256"`
	RequestSnapshot bool   `json:"requestStatusSnapshot"`
}

func (c PrinterConfig) Public() PublicConfig {
	return PublicConfig{c.Host, c.Serial, c.Secret != "", c.Fingerprint, c.RequestSnapshot}
}

var serialPattern = regexp.MustCompile(`^[A-Za-z0-9_-]{5,40}$`)
var codePattern = regexp.MustCompile(`^[A-Za-z0-9]{8}$`)

func validPrinterAddress(host string) bool {
	ip := net.ParseIP(host)
	if ip == nil {
		return false
	}
	_, cg, _ := net.ParseCIDR("100.64.0.0/10")
	return ip.IsPrivate() || ip.IsLinkLocalUnicast() || cg.Contains(ip)
}
func validateConfig(c PrinterConfig) error {
	if !validPrinterAddress(c.Host) {
		return fmt.Errorf("Enter the printer's local IP address, for example 192.168.1.50; not a website or public IP.")
	}
	if !serialPattern.MatchString(c.Serial) {
		return fmt.Errorf("Enter the printer serial number (not the AMS serial or its 3DP display name).")
	}
	if c.Secret == "" {
		return fmt.Errorf("Enter the printer's 8-character LAN access code.")
	}
	if c.Fingerprint != "" {
		if len(c.Fingerprint) != 64 {
			return fmt.Errorf("The saved certificate fingerprint is invalid.")
		}
		if _, e := hex.DecodeString(c.Fingerprint); e != nil {
			return e
		}
	}
	return nil
}

type Times struct{ Idle, Kernel, User uint64 }
type PCSample struct {
	CPU        float64   `json:"cpuPercent"`
	CPUValid   bool      `json:"cpuValid"`
	RAM        float64   `json:"ramPercent"`
	RAMValid   bool      `json:"ramValid"`
	UsedBytes  uint64    `json:"usedRAMBytes"`
	TotalBytes uint64    `json:"totalRAMBytes"`
	Reason     string    `json:"reason"`
	At         time.Time `json:"observedAt"`
}

func cpuBusy(old, cur Times) (float64, bool) {
	if cur.Idle < old.Idle || cur.Kernel < old.Kernel || cur.User < old.User {
		return 0, false
	}
	idle, kernel, user := cur.Idle-old.Idle, cur.Kernel-old.Kernel, cur.User-old.User
	total := kernel + user
	if total == 0 || idle > total {
		return 0, false
	}
	return 100 * float64(total-idle) / float64(total), true
}
func memoryUse(total, available uint64) (float64, uint64, bool) {
	if total == 0 || available > total {
		return 0, 0, false
	}
	used := total - available
	return 100 * float64(used) / float64(total), used, true
}

type PrintData struct {
	State       string    `json:"state"`
	Percent     *int      `json:"percent,omitempty"`
	Remaining   *int      `json:"remainingMinutes,omitempty"`
	ErrorCode   uint64    `json:"errorCode"`
	HMSCount    int       `json:"hmsMessages"`
	JobID       string    `json:"jobId,omitempty"`
	JobName     string    `json:"jobName,omitempty"`
	Layer       *int      `json:"layer,omitempty"`
	TotalLayers *int      `json:"totalLayers,omitempty"`
	Bed         *float64  `json:"bedCelsius,omitempty"`
	Nozzle      *float64  `json:"nozzleCelsius,omitempty"`
	LastReport  time.Time `json:"lastReport"`
}
type PrinterSnapshot struct {
	Phase               string    `json:"phase"`
	Reason              string    `json:"reason"`
	Candidate           string    `json:"candidateFingerprint,omitempty"`
	CertSubject         string    `json:"certificateSubject,omitempty"`
	Data                PrintData `json:"data"`
	Connected           bool      `json:"connected"`
	ConnectedAt         time.Time `json:"connectedAt"`
	LastSnapshotRequest time.Time `json:"lastSnapshotRequest"`
	LastPingResponse    time.Time `json:"lastPingResponse"`
	RetryInSeconds      int       `json:"retryInSeconds,omitempty"`
}

func number(v interface{}) (float64, bool) {
	var f float64
	var e error
	switch x := v.(type) {
	case json.Number:
		f, e = x.Float64()
	case float64:
		f = x
	case string:
		f, e = strconv.ParseFloat(x, 64)
	default:
		return 0, false
	}
	return f, e == nil && !math.IsNaN(f) && !math.IsInf(f, 0)
}
func boundedInt(v interface{}, max int) *int {
	n, ok := number(v)
	if !ok || n < 0 || n > float64(max) {
		return nil
	}
	x := int(math.Round(n))
	return &x
}
func temperature(v interface{}) *float64 {
	n, ok := number(v)
	if !ok || n < -40 || n > 500 {
		return nil
	}
	return &n
}
func errCode(v interface{}) uint64 {
	switch x := v.(type) {
	case json.Number:
		n, _ := strconv.ParseUint(x.String(), 10, 64)
		return n
	case string:
		base := 10
		if strings.HasPrefix(strings.ToLower(x), "0x") {
			base = 0
		}
		n, _ := strconv.ParseUint(x, base, 64)
		return n
	case float64:
		if x > 0 && x < 1<<53 {
			return uint64(x)
		}
	}
	return 0
}
func text(v interface{}) string {
	switch x := v.(type) {
	case string:
		return x
	case json.Number:
		return x.String()
	}
	return ""
}
func activeState(s string) bool {
	return s == "RUNNING" || s == "PREPARE" || s == "SLICING" || s == "PAUSE"
}

// Bambu sends partial/delta reports. Missing values must not erase unchanged
// values. New jobs and reconnects, however, must never inherit old progress.
func (d *PrintData) Apply(raw []byte, now time.Time) (bool, error) {
	var root map[string]interface{}
	dec := json.NewDecoder(strings.NewReader(string(raw)))
	dec.UseNumber()
	if e := dec.Decode(&root); e != nil {
		return false, e
	}
	p, ok := root["print"].(map[string]interface{})
	if !ok {
		return false, nil
	}
	// Acknowledgements and empty objects are not fresh telemetry.
	meaningful := false
	for _, k := range []string{"gcode_state", "mc_percent", "mc_remaining_time", "print_error", "hms", "subtask_id", "subtask_name", "gcode_file", "layer_num", "total_layer_num", "bed_temper", "nozzle_temper"} {
		if _, yes := p[k]; yes {
			meaningful = true
			break
		}
	}
	if !meaningful {
		return false, nil
	}
	nextState := strings.ToUpper(text(p["gcode_state"]))
	nextID := text(p["subtask_id"])
	newJob := (nextID != "" && nextID != "0" && d.JobID != "" && d.JobID != nextID) || (activeState(nextState) && !activeState(d.State) && d.State != "")
	if newJob {
		d.Percent = nil
		d.Remaining = nil
		d.Layer = nil
		d.TotalLayers = nil
		d.ErrorCode = 0
		d.HMSCount = 0
		d.JobName = ""
	}
	if nextState != "" {
		d.State = nextState
	}
	if nextID != "" && nextID != "0" {
		d.JobID = nextID
	}
	if v, yes := p["subtask_name"]; yes {
		d.JobName = text(v)
	}
	if v, yes := p["mc_percent"]; yes {
		d.Percent = boundedInt(v, 100)
	}
	if v, yes := p["mc_remaining_time"]; yes {
		d.Remaining = boundedInt(v, 525600)
	}
	if v, yes := p["print_error"]; yes {
		d.ErrorCode = errCode(v)
	}
	if v, yes := p["hms"].([]interface{}); yes {
		d.HMSCount = len(v)
	}
	if v, yes := p["layer_num"]; yes {
		d.Layer = boundedInt(v, 1000000)
	}
	if v, yes := p["total_layer_num"]; yes {
		d.TotalLayers = boundedInt(v, 1000000)
	}
	if v, yes := p["bed_temper"]; yes {
		d.Bed = temperature(v)
	}
	if v, yes := p["nozzle_temper"]; yes {
		d.Nozzle = temperature(v)
	}
	if d.State == "IDLE" {
		d.Percent = nil
		d.Remaining = nil
		d.Layer = nil
		d.TotalLayers = nil
	}
	d.LastReport = now
	return true, nil
}

type PrinterView struct {
	State, Title, Main, Sub, Color string
	Percent                        *int
}

func remainingLabel(n *int) string {
	if n == nil {
		return "TIME --"
	}
	if *n == 0 {
		return "<1m left"
	}
	if *n < 60 {
		return fmt.Sprintf("%dm left", *n)
	}
	if *n < 1440 {
		return fmt.Sprintf("%dh %02dm", *n/60, *n%60)
	}
	return fmt.Sprintf("%dd %dh", *n/1440, (*n%1440)/60)
}
func printerView(s PrinterSnapshot, now time.Time) PrinterView {
	v := PrinterView{State: s.Phase, Title: "P1S", Main: "SETUP", Sub: "ADD PRINTER", Color: "#748096"}
	switch s.Phase {
	case "setup", "hidden", "":
		return v
	case "connecting":
		v.Main = "LINKING"
		v.Sub = "PLEASE WAIT"
		return v
	case "trust":
		v.Main = "TRUST"
		v.Sub = "SEE SETTINGS"
		v.Color = "#ffc36b"
		return v
	case "auth":
		v.Main = "ACCESS"
		v.Sub = "CHECK CODE"
		v.Color = "#ff667b"
		return v
	case "offline":
		v.Main = "OFFLINE"
		v.Sub = "RECONNECTING"
		return v
	case "error":
		v.Main = "CHECK"
		v.Sub = "SEE SETTINGS"
		v.Color = "#ff667b"
		return v
	}
	if !s.Connected {
		v.Main = "OFFLINE"
		v.Sub = "NO CONNECTION"
		return v
	}
	d := s.Data
	ageLimit := 6 * time.Minute
	if activeState(d.State) || d.State == "" {
		ageLimit = 90 * time.Second
	}
	if d.LastReport.IsZero() || now.Sub(d.LastReport) > ageLimit {
		v.State = "waiting"
		v.Main = "NO DATA"
		v.Sub = "WAITING"
		return v
	}
	if d.ErrorCode != 0 {
		v.State = "error"
		v.Main = "CHECK"
		v.Sub = "PRINTER ERROR"
		v.Color = "#ff667b"
		return v
	}
	// Progress is useful even when the initial state snapshot has not arrived.
	// Do NOT infer RUNNING or DONE from a percentage. Explicitly mark it unknown.
	if strings.TrimSpace(d.State) == "" {
		v.State = "partial"
		if d.Percent != nil {
			v.Main = fmt.Sprintf("%d%%", *d.Percent)
			v.Sub = "STATE UNKNOWN"
			v.Color = "#ffc36b"
			v.Percent = d.Percent
		} else {
			v.Main = "NO DATA"
			v.Sub = "SYNCING"
		}
		return v
	}
	v.State = strings.ToLower(d.State)
	switch d.State {
	case "RUNNING":
		v.Main = "--%"
		if d.Percent != nil {
			v.Main = fmt.Sprintf("%d%%", *d.Percent)
		}
		v.Sub = remainingLabel(d.Remaining)
		v.Color = "#48e6b0"
		v.Percent = d.Percent
	case "PREPARE", "SLICING":
		v.Main = "PREP"
		v.Sub = remainingLabel(d.Remaining)
		v.Color = "#42cfff"
		v.Percent = d.Percent
	case "PAUSE":
		v.Main = "PAUSED"
		v.Sub = "CHECK PRINTER"
		v.Color = "#ffc36b"
		v.Percent = d.Percent
	case "FINISH":
		v.Main = "DONE"
		v.Sub = "JOB FINISHED"
		v.Color = "#48e6b0"
	case "FAILED", "STOP", "CANCELLED":
		v.Main = "STOPPED"
		v.Sub = "CHECK PRINTER"
		v.Color = "#ff667b"
	case "IDLE":
		v.Main = "READY"
		v.Sub = "NO ACTIVE JOB"
		v.Color = "#73a79f"
	case "INIT":
		v.Main = "STARTING"
		v.Sub = "PLEASE WAIT"
	default:
		v.Main = "WAIT"
		v.Sub = "STATE UNKNOWN"
	}
	return v
}
