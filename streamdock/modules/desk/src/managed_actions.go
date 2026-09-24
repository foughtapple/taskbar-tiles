package main

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
)

type actionGate map[string]bool

func loadActionGate(root string) (actionGate, error) {
	b, e := os.ReadFile(filepath.Join(root, "manifest.json"))
	if e != nil {
		return nil, e
	}
	if len(b) > 262144 {
		return nil, fmt.Errorf("manifest too large")
	}
	var m struct {
		Actions []struct {
			UUID string `json:"UUID"`
		} `json:"Actions"`
	}
	if e = json.Unmarshal(b, &m); e != nil {
		return nil, e
	}
	g := actionGate{}
	for _, a := range m.Actions {
		if a.UUID == "" || g[a.UUID] {
			return nil, fmt.Errorf("invalid action manifest")
		}
		g[a.UUID] = true
	}
	return g, nil
}
func (g actionGate) allows(raw []byte) bool {
	var e struct {
		Action string `json:"action"`
		Event  string `json:"event"`
	}
	if json.Unmarshal(raw, &e) != nil {
		return false
	}
	if e.Action != "" {
		return g[e.Action]
	}
	switch e.Event {
	case "deviceDidDisconnect", "deviceDidConnect", "systemDidWakeUp", "willDisappear", "propertyInspectorDidDisappear":
		return true
	}
	return false
}
