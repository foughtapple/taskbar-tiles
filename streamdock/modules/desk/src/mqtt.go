package main

import (
	"bufio"
	"context"
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"crypto/tls"
	"encoding/binary"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"net"
	"strings"
	"sync"
	"time"
)

const maxMQTTPacket = 2 * 1024 * 1024

// This is the ONLY printer application command this monitor can publish.
// It requests telemetry; there is no path for G-code, start/pause/stop, heat,
// movement, firmware, filesystem, camera, or arbitrary command publishing.
const statusRequest = `{"pushing":{"sequence_id":"0","command":"pushall"}}`

func mqttString(s string) []byte {
	b := make([]byte, 2+len(s))
	binary.BigEndian.PutUint16(b, uint16(len(s)))
	copy(b[2:], s)
	return b
}
func mqttPacket(header byte, body []byte) []byte {
	b := []byte{header}
	n := len(body)
	for {
		digit := byte(n % 128)
		n /= 128
		if n > 0 {
			digit |= 128
		}
		b = append(b, digit)
		if n == 0 {
			break
		}
	}
	return append(b, body...)
}
func mqttConnect(id, password string) []byte {
	body := append(mqttString("MQTT"), 4, 0xc2, 0, 30) // 3.1.1, clean session, user+password, keepalive 30s
	body = append(body, mqttString(id)...)
	body = append(body, mqttString("bblp")...)
	body = append(body, mqttString(password)...)
	return mqttPacket(0x10, body)
}
func mqttSubscribe(serial string) []byte {
	body := []byte{0, 1}
	body = append(body, mqttString("device/"+serial+"/report")...)
	body = append(body, 0)
	return mqttPacket(0x82, body)
}
func snapshotPacket(serial string) []byte {
	b := mqttString("device/" + serial + "/request")
	return mqttPacket(0x30, append(b, []byte(statusRequest)...))
}
func readMQTT(r *bufio.Reader) (byte, []byte, error) {
	h, e := r.ReadByte()
	if e != nil {
		return 0, nil, e
	}
	n, mul := 0, 1
	for i := 0; i < 4; i++ {
		d, e := r.ReadByte()
		if e != nil {
			return 0, nil, e
		}
		n += int(d&127) * mul
		if n > maxMQTTPacket {
			return 0, nil, fmt.Errorf("MQTT packet exceeds monitor size limit")
		}
		if d&128 == 0 {
			b := make([]byte, n)
			_, e = io.ReadFull(r, b)
			return h, b, e
		}
		mul *= 128
	}
	return 0, nil, fmt.Errorf("Invalid MQTT remaining length")
}
func decodePublish(h byte, b []byte) (topic string, payload []byte, id uint16, err error) {
	if len(b) < 2 {
		return "", nil, 0, fmt.Errorf("Short MQTT publication")
	}
	n := int(binary.BigEndian.Uint16(b))
	if n == 0 || n > len(b)-2 {
		return "", nil, 0, fmt.Errorf("Invalid MQTT topic length")
	}
	topic = string(b[2 : 2+n])
	start := 2 + n
	qos := (h >> 1) & 3
	if qos > 1 {
		return "", nil, 0, fmt.Errorf("Unsupported MQTT QoS from report subscription")
	}
	if qos == 1 {
		if len(b) < start+2 {
			return "", nil, 0, io.ErrUnexpectedEOF
		}
		id = binary.BigEndian.Uint16(b[start:])
		if id == 0 {
			return "", nil, 0, fmt.Errorf("Invalid MQTT packet identifier")
		}
		start += 2
	}
	return topic, b[start:], id, nil
}

type certReview struct {
	Fingerprint, Subject string
	Changed              bool
}

func (e *certReview) Error() string {
	if e.Changed {
		return "The printer TLS certificate has changed. Verify the IP and approve it in the display settings."
	}
	return "Confirm this printer's TLS certificate in the display settings before the LAN code is sent."
}

var errPrinterAuth = errors.New("The printer rejected the LAN access code. Check its IP, serial number and current access code.")

// Exact certificate pinning is deliberate: Bambu LAN certificates may not
// chain to Windows' public root store. InsecureSkipVerify disables that root
// check ONLY; VerifyConnection below enforces the user-approved SHA256 pin.
// Without a matching pin the TLS handshake fails before MQTT credentials.
func printerTLS(c PrinterConfig) *tls.Config {
	return &tls.Config{MinVersion: tls.VersionTLS12, ServerName: c.Serial, InsecureSkipVerify: true, VerifyConnection: func(s tls.ConnectionState) error {
		if len(s.PeerCertificates) == 0 {
			return fmt.Errorf("Printer did not supply a TLS certificate")
		}
		leaf := s.PeerCertificates[0]
		fp := sha256.Sum256(leaf.Raw)
		got := hex.EncodeToString(fp[:])
		expected, e := hex.DecodeString(c.Fingerprint)
		if e != nil || len(expected) != 32 || subtle.ConstantTimeCompare(expected, fp[:]) != 1 {
			return &certReview{got, leaf.Subject.String(), c.Fingerprint != ""}
		}
		return nil
	}}
}

type SnapshotGate struct {
	mu   sync.Mutex
	last map[string]time.Time
}

func (g *SnapshotGate) allow(key string, now time.Time) bool {
	return g.allowAfter(key, now, 5*time.Minute)
}
func (g *SnapshotGate) allowAfter(key string, now time.Time, interval time.Duration) bool {
	g.mu.Lock()
	defer g.mu.Unlock()
	if g.last == nil {
		g.last = map[string]time.Time{}
	}
	if t, ok := g.last[key]; ok && now.Sub(t) < interval {
		return false
	}
	g.last[key] = now
	return true
}
func newClientID() string {
	b := make([]byte, 7)
	_, _ = rand.Read(b)
	return "FAView_" + hex.EncodeToString(b)
}

type mqttWriter struct {
	conn net.Conn
	mu   sync.Mutex
}

func (w *mqttWriter) write(p []byte) error {
	w.mu.Lock()
	defer w.mu.Unlock()
	w.conn.SetWriteDeadline(time.Now().Add(6 * time.Second))
	return writeAll(w.conn, p)
}

// Every MQTT read belongs to one cancellable connection. Socket liveness and
// printer telemetry freshness are separate: PINGRESP is not printer status.
type mqttTimings struct {
	Pulse, Handshake, Read, PingEvery, PingWait time.Duration
	Quiet, RepairAfter, RecoveryRequest         time.Duration
}

func defaultMQTTTimings() mqttTimings {
	return mqttTimings{Pulse: 5 * time.Second, Handshake: 12 * time.Second,
		Read: 45 * time.Second, PingEvery: 15 * time.Second, PingWait: 30 * time.Second,
		Quiet: 60 * time.Second, RepairAfter: 180 * time.Second, RecoveryRequest: 30 * time.Second}
}
func incompletePrinterData(d PrintData) bool {
	if strings.TrimSpace(d.State) == "" {
		return true
	}
	// Other states (idle, preparing, finished) need not carry progress values.
	return d.State == "RUNNING" && (d.Percent == nil || d.Remaining == nil)
}
func describeLive(s *PrinterSnapshot, requests bool) {
	if incompletePrinterData(s.Data) {
		s.Phase = "syncing"
		s.Reason = "Receiving partial printer reports. Recovering the full status automatically; the reported state is not yet complete."
		if !requests {
			s.Reason = "Receiving partial printer reports. Waiting for the state; automatic status snapshots are disabled."
		}
	} else {
		s.Phase = "online"
		s.Reason = "Receiving live local printer reports. Automatic reconnection is enabled."
	}
}

type mqttRead struct {
	header byte
	body   []byte
	err    error
}

// runMQTT accepts an already TLS-authenticated connection. Byte-level tests
// use a fake broker; they never connect to the user's actual printer.
func runMQTT(ctx context.Context, conn net.Conn, c PrinterConfig, password string, gate *SnapshotGate, emit func(PrinterSnapshot)) error {
	return runMQTTWithTimings(ctx, conn, c, password, gate, emit, defaultMQTTTimings())
}
func runMQTTWithTimings(ctx context.Context, conn net.Conn, c PrinterConfig, password string, gate *SnapshotGate, emit func(PrinterSnapshot), tm mqttTimings) error {
	ctx, cancel := context.WithCancel(ctx)
	defer cancel()
	defer conn.Close()
	done := make(chan struct{})
	defer close(done)
	go func() {
		select {
		case <-ctx.Done():
			conn.Close()
		case <-done:
		}
	}()
	w := &mqttWriter{conn: conn}
	r := bufio.NewReader(conn)
	if e := w.write(mqttConnect(newClientID(), password)); e != nil {
		return e
	}
	conn.SetReadDeadline(time.Now().Add(tm.Handshake))
	h, b, e := readMQTT(r)
	if e != nil {
		return e
	}
	if h != 0x20 || len(b) != 2 {
		return fmt.Errorf("Printer did not return a valid MQTT CONNACK")
	}
	if b[1] == 4 || b[1] == 5 {
		return errPrinterAuth
	}
	if b[1] != 0 {
		return fmt.Errorf("Printer refused MQTT connection (code %d)", b[1])
	}
	if b[0] != 0 {
		return fmt.Errorf("Unexpected session-present flag for a clean MQTT session")
	}
	if e = w.write(mqttSubscribe(c.Serial)); e != nil {
		return e
	}

	// A bounded reader lets maintenance continue while a printer sends a slow
	// or fragmented packet. Cancellation always closes and joins this reader.
	incoming := make(chan mqttRead, 8)
	readerDone := make(chan struct{})
	go func() {
		defer close(readerDone)
		for {
			conn.SetReadDeadline(time.Now().Add(tm.Read))
			h, b, e := readMQTT(r)
			select {
			case incoming <- mqttRead{h, b, e}:
			case <-ctx.Done():
				return
			}
			if e != nil {
				return
			}
		}
	}()
	defer func() { cancel(); conn.Close(); <-readerDone }()
	now := time.Now()
	s := PrinterSnapshot{Phase: "waiting", Connected: true, ConnectedAt: now,
		Reason: "Connected securely. Waiting for the printer's first status report."}
	subscribed := false
	syncSince := now
	lastPing := now
	awaitingPing := false
	pendingAt := time.Time{}
	pulse := time.NewTicker(tm.Pulse)
	defer pulse.Stop()
	ackTimeout := time.NewTimer(tm.Handshake)
	defer ackTimeout.Stop()
	ackTick := ackTimeout.C
	request := func(at time.Time, recovery bool) error {
		if !c.RequestSnapshot {
			return nil
		}
		interval := 5 * time.Minute
		if recovery {
			interval = tm.RecoveryRequest
		}
		if !gate.allowAfter(c.Host+"/"+c.Serial, at, interval) {
			return nil
		}
		if e := w.write(snapshotPacket(c.Serial)); e != nil {
			return e
		}
		s.LastSnapshotRequest = at
		return nil
	}
	for {
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-ackTick:
			return fmt.Errorf("Printer did not acknowledge the report subscription in time")
		case at := <-pulse.C:
			if !subscribed {
				continue
			}
			if awaitingPing && at.Sub(pendingAt) >= tm.PingWait {
				return fmt.Errorf("Printer MQTT keepalive response timed out; reconnecting")
			}
			if !awaitingPing && at.Sub(lastPing) >= tm.PingEvery {
				if e = w.write([]byte{0xc0, 0}); e != nil {
					return e
				}
				lastPing, pendingAt, awaitingPing = at, at, true
			}
			quiet := s.Data.LastReport.IsZero() || at.Sub(s.Data.LastReport) >= tm.Quiet
			needsSync := quiet || incompletePrinterData(s.Data)
			if needsSync {
				if syncSince.IsZero() {
					syncSince = at
				}
				if c.RequestSnapshot && at.Sub(syncSince) >= tm.RepairAfter {
					return fmt.Errorf("Printer connection is alive but complete fresh status did not recover; resubscribing")
				}
				if quiet {
					s.Phase = "syncing"
					s.Reason = "Printer connection is alive but reports are quiet. Requesting fresh status automatically."
					if !c.RequestSnapshot {
						s.Reason = "Printer connection is alive but reports are quiet. Passive listening is enabled; no status requests are being sent."
					}
				}
			} else {
				syncSince = time.Time{}
			}
			if e = request(at, needsSync); e != nil {
				return e
			}
			// UI sends remain throttled by App. This also exposes recovery reasons
			// without calling a quiet MQTT connection 'offline'.
			emit(s)
		case packet := <-incoming:
			if packet.err != nil {
				return packet.err
			}
			h, b = packet.header, packet.body
			at := time.Now()
			switch h >> 4 {
			case 9:
				if h != 0x90 || len(b) != 3 || binary.BigEndian.Uint16(b) != 1 || b[2] > 1 {
					return fmt.Errorf("Printer did not accept the report subscription. Check the serial number and LAN access settings.")
				}
				if !subscribed {
					subscribed = true
					ackTimeout.Stop()
					ackTick = nil
					// MQTT permits a publication before SUBACK. Do not reset any
					// telemetry that has already arrived on this new connection.
					if !s.Data.LastReport.IsZero() {
						describeLive(&s, c.RequestSnapshot)
					}
					if e = request(at, true); e != nil {
						return e
					}
					emit(s)
				}
			case 3:
				topic, payload, id, er := decodePublish(h, b)
				if er != nil {
					return er
				}
				if id != 0 {
					if e = w.write([]byte{0x40, 2, byte(id >> 8), byte(id)}); e != nil {
						return e
					}
				}
				if topic != "device/"+c.Serial+"/report" || h&1 != 0 {
					continue
				}
				changed, er := s.Data.Apply(payload, at)
				if er != nil || !changed {
					continue
				}
				describeLive(&s, c.RequestSnapshot)
				if !incompletePrinterData(s.Data) {
					syncSince = time.Time{}
				}
				if subscribed {
					emit(s)
				}
			case 13:
				if h != 0xd0 || len(b) != 0 {
					return fmt.Errorf("Invalid MQTT ping response")
				}
				awaitingPing = false
				s.LastPingResponse = at
			case 14:
				return fmt.Errorf("Printer disconnected the MQTT session")
			default:
				return fmt.Errorf("Unexpected MQTT response type %d", h>>4)
			}
		}
	}
}

// Exact pinned TLS remains mandatory before any credentials are sent.
func dialPrinter(ctx context.Context, c PrinterConfig) (net.Conn, error) {
	d := &tls.Dialer{NetDialer: &net.Dialer{Timeout: 7 * time.Second}, Config: printerTLS(c)}
	return d.DialContext(ctx, "tcp", net.JoinHostPort(c.Host, "8883"))
}
func watchPrinter(ctx context.Context, c PrinterConfig, password string, gate *SnapshotGate, emit func(PrinterSnapshot)) {
	watchPrinterWith(ctx, c, password, gate, emit, dialPrinter, runMQTT, 3*time.Second, 30*time.Second)
}

type printerDialFunc func(context.Context, PrinterConfig) (net.Conn, error)
type printerRunFunc func(context.Context, net.Conn, PrinterConfig, string, *SnapshotGate, func(PrinterSnapshot)) error

func watchPrinterWith(ctx context.Context, c PrinterConfig, password string, gate *SnapshotGate, emit func(PrinterSnapshot), dial printerDialFunc, run printerRunFunc, minBackoff, maxBackoff time.Duration) {
	backoff := minBackoff
	for ctx.Err() == nil {
		emit(PrinterSnapshot{Phase: "connecting", Reason: "Connecting to the P1S on its local TLS/MQTT port (8883)."})
		conn, e := dial(ctx, c)
		if e == nil {
			connectedAt := time.Now()
			e = run(ctx, conn, c, password, gate, emit)
			conn.Close()
			if time.Since(connectedAt) > time.Minute {
				backoff = minBackoff
			}
		}
		if ctx.Err() != nil {
			return
		}
		var review *certReview
		if errors.As(e, &review) {
			emit(PrinterSnapshot{Phase: "trust", Reason: review.Error(), Candidate: review.Fingerprint, CertSubject: review.Subject})
			return
		}
		if errors.Is(e, errPrinterAuth) {
			emit(PrinterSnapshot{Phase: "auth", Reason: errPrinterAuth.Error()})
			return
		}
		reason := "Printer disconnected or unavailable; retrying automatically. Check power/network/IP only if this persists."
		if e != nil {
			reason += " " + sanitizeNetworkError(e.Error(), password)
		}
		emit(PrinterSnapshot{Phase: "offline", Reason: reason, RetryInSeconds: int(backoff.Seconds())})
		timer := time.NewTimer(backoff)
		select {
		case <-ctx.Done():
			timer.Stop()
			return
		case <-timer.C:
		}
		backoff *= 2
		if backoff > maxBackoff {
			backoff = maxBackoff
		}
	}
}
func sanitizeNetworkError(s, secret string) string {
	if secret != "" {
		s = strings.ReplaceAll(s, secret, "[redacted]")
	}
	if len(s) > 600 {
		s = s[:600]
	}
	return s
}
