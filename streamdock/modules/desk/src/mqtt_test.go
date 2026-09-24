package main

import (
	"bufio"
	"bytes"
	"context"
	"crypto/sha256"
	"crypto/tls"
	"crypto/x509"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func TestMQTTPacketLengthRoundTrips(t *testing.T) {
	for _, n := range []int{0, 1, 127, 128, 16383, 16384, 100000} {
		want := bytes.Repeat([]byte{'x'}, n)
		h, b, e := readMQTT(bufio.NewReader(bytes.NewReader(mqttPacket(0x30, want))))
		if e != nil || h != 0x30 || !bytes.Equal(b, want) {
			t.Fatal(n, e)
		}
	}
}
func TestMQTTRejectsMalformedLengths(t *testing.T) {
	for _, b := range [][]byte{{0x30, 128, 128, 128, 128, 0}, {0x30, 255, 255, 255, 127}, {0x30, 10, 1}} {
		if _, _, e := readMQTT(bufio.NewReader(bytes.NewReader(b))); e == nil {
			t.Fatal(b)
		}
	}
}
func TestMQTTQoS1Parsing(t *testing.T) {
	body := append(mqttString("device/ABC/report"), 0, 9)
	body = append(body, []byte(`{"print":{}}`)...)
	topic, p, id, e := decodePublish(0x32, body)
	if e != nil || topic != "device/ABC/report" || id != 9 || string(p) != `{"print":{}}` {
		t.Fatal(topic, id, e)
	}
	if _, _, _, e = decodePublish(0x34, body); e == nil {
		t.Fatal("accepted QoS2")
	}
}
func TestCertificatePinAndFirstUse(t *testing.T) {
	srv := httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {}))
	defer srv.Close()
	cert := srv.Certificate()
	state := tls.ConnectionState{PeerCertificates: []*x509.Certificate{cert}}
	err := printerTLS(PrinterConfig{Serial: "TESTSERIAL"}).VerifyConnection(state)
	var review *certReview
	if !errors.As(err, &review) || len(review.Fingerprint) != 64 || review.Changed {
		t.Fatal(err)
	}
	fp := sha256.Sum256(cert.Raw)
	cfg := PrinterConfig{Serial: "TESTSERIAL", Fingerprint: hex.EncodeToString(fp[:])}
	if err = printerTLS(cfg).VerifyConnection(state); err != nil {
		t.Fatal(err)
	}
	cfg.Fingerprint = strings.Repeat("0", 64)
	err = printerTLS(cfg).VerifyConnection(state)
	if !errors.As(err, &review) || !review.Changed {
		t.Fatal(err)
	}
	// Real TLS handshake is rejected before HTTP/MQTT application data on first use.
	conn, err := tls.Dial("tcp", srv.Listener.Addr().String(), printerTLS(PrinterConfig{Serial: "TESTSERIAL"}))
	if err == nil {
		conn.Close()
		t.Fatal("first-use certificate bypass")
	}
	cfg.Fingerprint = hex.EncodeToString(fp[:])
	conn, err = tls.Dial("tcp", srv.Listener.Addr().String(), printerTLS(cfg))
	if err != nil {
		t.Fatal(err)
	}
	conn.Close()
}
func TestSnapshotThrottleAndPayload(t *testing.T) {
	g := &SnapshotGate{}
	n := time.Now()
	if !g.allow("P1S", n) || g.allow("P1S", n.Add(299*time.Second)) || !g.allow("P1S", n.Add(300*time.Second)) {
		t.Fatal("snapshot not rate limited")
	}
	h, b, e := readMQTT(bufio.NewReader(bytes.NewReader(snapshotPacket("ABCDE"))))
	topic, p, _, e2 := decodePublish(h, b)
	if e != nil || e2 != nil || topic != "device/ABCDE/request" || string(p) != statusRequest || strings.Contains(string(p), "gcode") {
		t.Fatal(e, e2, string(p))
	}
}
func fakeBroker(t *testing.T, auth byte, snapshots bool) ([]PrinterSnapshot, error) {
	t.Helper()
	client, server := net.Pipe()
	ctx, cancel := context.WithTimeout(context.Background(), 4*time.Second)
	defer cancel()
	done := make(chan error, 1)
	go func() {
		defer server.Close()
		server.SetDeadline(time.Now().Add(3 * time.Second))
		r := bufio.NewReader(server)
		h, b, e := readMQTT(r)
		if e != nil || h != 0x10 || !bytes.Contains(b, []byte("bblp")) {
			done <- fmt.Errorf("missing CONNECT %v", e)
			return
		}
		if e = writeAll(server, []byte{0x20, 2, 0, auth}); e != nil {
			done <- e
			return
		}
		if auth != 0 {
			done <- nil
			return
		}
		h, b, e = readMQTT(r)
		if e != nil || h != 0x82 || !bytes.Contains(b, []byte("device/TESTSERIAL/report")) {
			done <- fmt.Errorf("missing SUBSCRIBE %v", e)
			return
		}
		if e = writeAll(server, []byte{0x90, 3, 0, 1, 0}); e != nil {
			done <- e
			return
		}
		if snapshots {
			h, b, e = readMQTT(r)
			if e != nil {
				done <- e
				return
			}
			topic, p, _, e := decodePublish(h, b)
			if e != nil || topic != "device/TESTSERIAL/request" || string(p) != statusRequest {
				done <- fmt.Errorf("unexpected printer command")
				return
			}
		}
		for _, r := range []struct {
			h           byte
			topic, json string
		}{{0x31, "device/TESTSERIAL/report", `{"print":{"gcode_state":"FINISH","mc_percent":100}}`}, {0x30, "device/OTHER/report", `{"print":{"gcode_state":"FINISH"}}`}, {0x30, "device/TESTSERIAL/report", `{"print":{"gcode_state":"RUNNING","mc_percent":64,"mc_remaining_time":83}}`}, {0x30, "device/TESTSERIAL/report", `{"print":{"mc_percent":65}}`}, {0x30, "device/TESTSERIAL/report", `{"print":{"gcode_state":"FINISH"}}`}} {
			frame := mqttPacket(r.h, append(mqttString(r.topic), []byte(r.json)...))
			for _, part := range [][]byte{frame[:2], frame[2:]} {
				if e = writeAll(server, part); e != nil {
					done <- e
					return
				}
			}
		}
		done <- nil
	}()
	var states []PrinterSnapshot
	e := runMQTT(ctx, client, PrinterConfig{Host: "192.168.1.40", Serial: "TESTSERIAL", RequestSnapshot: snapshots}, "TESTCODE", &SnapshotGate{}, func(s PrinterSnapshot) { states = append(states, s) })
	if serverErr := <-done; serverErr != nil {
		t.Fatal(serverErr)
	}
	return states, e
}
func TestLiveMQTTSubscribeAndDeltaStates(t *testing.T) {
	states, e := fakeBroker(t, 0, true)
	if e != io.EOF {
		t.Fatal(e)
	}
	if len(states) != 4 {
		t.Fatal(len(states))
	}
	if states[0].Phase != "waiting" || states[1].Data.State != "RUNNING" || *states[1].Data.Percent != 64 || *states[2].Data.Percent != 65 || *states[2].Data.Remaining != 83 || states[3].Data.State != "FINISH" {
		t.Fatal(states)
	}
}
func TestPassiveMQTTDoesNotPublishRequests(t *testing.T) {
	states, e := fakeBroker(t, 0, false)
	if e != io.EOF || len(states) != 4 {
		t.Fatal(len(states), e)
	}
}
func TestAuthenticationFailureStopsBeforeSubscribe(t *testing.T) {
	states, e := fakeBroker(t, 5, true)
	if !errors.Is(e, errPrinterAuth) || len(states) != 0 {
		t.Fatal(states, e)
	}
}
