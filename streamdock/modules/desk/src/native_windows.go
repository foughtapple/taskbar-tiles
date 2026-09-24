//go:build windows

package main

import (
 "encoding/base64"
 "fmt"
 "os/exec"
 "runtime"
 "syscall"
 "time"
 "unsafe"
)
var kernel = syscall.NewLazyDLL("kernel32.dll")
var getTimes = kernel.NewProc("GetSystemTimes")
var getMemory = kernel.NewProc("GlobalMemoryStatusEx")
var localFree = kernel.NewProc("LocalFree")
var crypt = syscall.NewLazyDLL("crypt32.dll")
var protectData = crypt.NewProc("CryptProtectData")
var unprotectData = crypt.NewProc("CryptUnprotectData")
var messageBox = syscall.NewLazyDLL("user32.dll").NewProc("MessageBoxW")
type memStatus struct {
 Length, Load uint32
 TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual uint64
}
type blob struct { Size uint32; Data *byte }
type NativeSampler struct { previous Times; hasPrevious bool }
func (s *NativeSampler) Sample() PCSample {
 p := PCSample{At: time.Now()}; var cur Times
 ok, _, _ := getTimes.Call(uintptr(unsafe.Pointer(&cur.Idle)), uintptr(unsafe.Pointer(&cur.Kernel)), uintptr(unsafe.Pointer(&cur.User)))
 if ok != 0 {
  if s.hasPrevious { p.CPU, p.CPUValid = cpuBusy(s.previous, cur) }
  s.previous = cur; s.hasPrevious = true
 } else { p.Reason = "Windows CPU counters could not be read." }
 var m memStatus; m.Length = uint32(unsafe.Sizeof(m))
 ok, _, _ = getMemory.Call(uintptr(unsafe.Pointer(&m)))
 if ok != 0 { p.RAM, p.UsedBytes, p.RAMValid = memoryUse(m.TotalPhys, m.AvailPhys); p.TotalBytes = m.TotalPhys
 } else { p.Reason += " Windows RAM counters could not be read." }
 if p.Reason == "" { p.Reason = "CPU busy time; physical RAM in use. CPU may differ from Task Manager's frequency-adjusted value." }
 if runtime.NumCPU() > 64 { p.Reason += " CPU reading is for the primary Windows processor group (more than 64 logical processors)." }
 return p
}
func secretTransform(data []byte, decrypt bool) ([]byte, error) {
 if len(data) == 0 || len(data) > 65536 { return nil, fmt.Errorf("Invalid protected credential length") }
 entropy := []byte("FoughtApple.DeskStatus.P1S.v1")
 in := blob{uint32(len(data)), &data[0]}; ent := blob{uint32(len(entropy)), &entropy[0]}; var out blob; var r uintptr
 if decrypt { r, _, _ = unprotectData.Call(uintptr(unsafe.Pointer(&in)), 0, uintptr(unsafe.Pointer(&ent)), 0, 0, 1, uintptr(unsafe.Pointer(&out)))
 } else { r, _, _ = protectData.Call(uintptr(unsafe.Pointer(&in)), 0, uintptr(unsafe.Pointer(&ent)), 0, 0, 1, uintptr(unsafe.Pointer(&out))) }
 runtime.KeepAlive(data); runtime.KeepAlive(entropy)
 if r == 0 { return nil, fmt.Errorf("Windows could not protect/unprotect the LAN code for this user. Re-enter the code in the P1S settings.") }
 defer localFree.Call(uintptr(unsafe.Pointer(out.Data)))
 if out.Size > 65536 || out.Data == nil { return nil, fmt.Errorf("Invalid protected credential response") }
 return append([]byte{}, unsafe.Slice(out.Data, int(out.Size))...), nil
}
func protectSecret(s string) (string, error) { b, e := secretTransform([]byte(s), false); if e != nil { return "", e }; return base64.StdEncoding.EncodeToString(b), nil }
func revealSecret(s string) (string, error) { b, e := base64.StdEncoding.DecodeString(s); if e != nil { return "", fmt.Errorf("Saved LAN code is invalid. Enter it again.") }; v, e := secretTransform(b, true); return string(v), e }
func notify(s string) { t, _ := syscall.UTF16PtrFromString("FoughtApple Desk Displays"); v, _ := syscall.UTF16PtrFromString(s); messageBox.Call(0, uintptr(unsafe.Pointer(v)), uintptr(unsafe.Pointer(t)), 0x40) }
func openReport(path string) error { c := exec.Command("notepad.exe", path); return c.Start() }
