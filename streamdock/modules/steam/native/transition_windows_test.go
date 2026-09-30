//go:build windows

package main

import (
	"bufio"
	"fmt"
	"io"
	"strings"
	"testing"
	"time"
)

func TestTransitionExcludesConcurrentHolderAndReleasesOnEOF(t *testing.T) {
	// Unique fixture mutex: never contend with the owner's actual Steam controls.
	name := fmt.Sprintf(`Local\TaskbarTiles.TransitionFixture.%d`, time.Now().UnixNano())
	input, end := io.Pipe()
	output, writer := io.Pipe()
	done := make(chan error, 1)
	go func() { err := holdTransitionNamed(name, input, writer); writer.Close(); done <- err }()
	t.Cleanup(func() { end.Close(); output.Close() })
	ready := make(chan string, 1)
	go func() { line, _ := bufio.NewReader(output).ReadString('\n'); ready <- line }()
	select {
	case line := <-ready:
		if line != "ACQUIRED\n" {
			t.Fatalf("unexpected handshake %q", line)
		}
	case <-time.After(3 * time.Second):
		t.Fatal("mutex helper did not acquire")
	}
	if err := holdTransitionNamed(name, strings.NewReader(""), io.Discard); err == nil {
		t.Fatal("concurrent holder acquired the mutex")
	}
	end.Close()
	select {
	case err := <-done:
		if err != nil {
			t.Fatal(err)
		}
	case <-time.After(3 * time.Second):
		t.Fatal("mutex did not release after EOF")
	}
	if err := holdTransitionNamed(name, strings.NewReader(""), io.Discard); err != nil {
		t.Fatal("mutex remained held", err)
	}
}
