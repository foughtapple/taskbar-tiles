//go:build !windows

package main

import "errors"

func nativeSnapshot() Snapshot { return Snapshot{Reason:"Windows only"} }
func openSteam() error { return errors.New("Windows only") }
func setAutoLogin(string,bool,uint32) error { return errors.New("Windows only") }
func rocketRunning(bool)(bool,error) { return false,errors.New("Windows only") }
func openSteamURI(string) error { return errors.New("Windows only") }
