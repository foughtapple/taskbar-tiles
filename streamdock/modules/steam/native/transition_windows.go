//go:build windows

package main

import (
	"fmt"
	"io"
	"os"
	"runtime"
	"syscall"
	"unsafe"
)

const transitionMutexName = `Local\FoughtApple.StreamDock.SteamRL.v1`

// Windows mutex ownership belongs to an OS thread, not a goroutine.
func holdTransition() error {
	return holdTransitionNamed(transitionMutexName, os.Stdin, os.Stdout)
}
func holdTransitionNamed(mutexName string, input io.Reader, output io.Writer) error {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()
	name, err := syscall.UTF16PtrFromString(mutexName)
	if err != nil {
		return err
	}
	handle, _, callErr := kernel.NewProc("CreateMutexW").Call(0, 0, uintptr(unsafe.Pointer(name)))
	if handle == 0 {
		return callErr
	}
	defer syscall.CloseHandle(syscall.Handle(handle))
	result, _, callErr := kernel.NewProc("WaitForSingleObject").Call(handle, 0)
	if result != 0 && result != 0x80 {
		if result == 0xffffffff {
			return callErr
		}
		return fmt.Errorf("Another Steam/game operation is running. Finish or cancel it, then retry.")
	}
	defer kernel.NewProc("ReleaseMutex").Call(handle)
	if _, err = fmt.Fprintln(output, "ACQUIRED"); err != nil {
		return err
	}
	_, err = io.Copy(io.Discard, input)
	return err
}
