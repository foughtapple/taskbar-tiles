//go:build !windows

package main

import (
	"fmt"
	"time"
)

type NativeSampler struct{}

func (s *NativeSampler) Sample() PCSample {
	return PCSample{At: time.Now(), Reason: "Live CPU/RAM sampling requires Windows. Tests use explicit fixtures."}
}
func protectSecret(s string) (string, error) {
	return "", fmt.Errorf("Windows DPAPI is required; no plaintext fallback")
}
func revealSecret(s string) (string, error) {
	return "", fmt.Errorf("Windows DPAPI is required; no plaintext fallback")
}
func notify(s string)              { fmt.Println(s) }
func openReport(path string) error { fmt.Println(path); return nil }
