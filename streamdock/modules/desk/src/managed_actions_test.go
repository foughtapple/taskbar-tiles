package main

import (
	"os"
	"path/filepath"
	"testing"
)

func TestManagedActionGate(t *testing.T) {
	d := t.TempDir()
	os.WriteFile(filepath.Join(d, "manifest.json"), []byte(`{"Actions":[{"UUID":"allowed"}]}`), 0600)
	g, e := loadActionGate(d)
	if e != nil {
		t.Fatal(e)
	}
	for _, x := range []struct {
		s string
		b bool
	}{{`{"event":"willAppear","action":"allowed"}`, true}, {`{"event":"willAppear","action":"disabled"}`, false}, {`{"event":"willAppear"}`, false}, {`{"event":"deviceDidDisconnect"}`, true}} {
		if g.allows([]byte(x.s)) != x.b {
			t.Fatal(x.s)
		}
	}
}
