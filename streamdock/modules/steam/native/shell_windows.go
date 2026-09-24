//go:build windows

package main

import("fmt";"os";"os/exec";"path/filepath";"strings";"syscall";"unsafe")
func openSteam()error{return openSteamURI("steam://open/main")}
func openSteamURI(uri string)error{
 if uri!="steam://open/main"&&uri!="steam://run/252950"{return fmt.Errorf("Unsupported Steam command")}
 s:=nativeSnapshot();if s.SteamPath==""{return fmt.Errorf("Steam installation not found")}
 file:=filepath.Join(s.SteamPath,"steam.exe");if _,e:=os.Stat(file);e!=nil{return e}
 c:=exec.Command(file,uri);c.SysProcAttr=&syscall.SysProcAttr{HideWindow:true};if e:=c.Start();e!=nil{return e};return c.Process.Release()
}
// Identify only RocketLeague.exe in this Windows session; issue WM_CLOSE, never TerminateProcess.
func rocketRunning(closeWindow bool)(bool,error){
 h,e:=syscall.CreateToolhelp32Snapshot(syscall.TH32CS_SNAPPROCESS,0);if e!=nil{return false,e};defer syscall.CloseHandle(h)
 me,e:=sessionID(uint32(os.Getpid()));if e!=nil{return false,e};ids:=map[uint32]bool{}
 var p syscall.ProcessEntry32;p.Size=uint32(unsafe.Sizeof(p));e=syscall.Process32First(h,&p)
 for e==nil{if strings.EqualFold(syscall.UTF16ToString(p.ExeFile[:]),"RocketLeague.exe"){sid,er:=sessionID(p.ProcessID);if er==nil&&sid==me{ids[p.ProcessID]=true}};e=syscall.Process32Next(h,&p)}
 if e!=syscall.Errno(18){return false,e};if !closeWindow||len(ids)==0{return len(ids)>0,nil}
 u:=syscall.NewLazyDLL("user32.dll");getpid:=u.NewProc("GetWindowThreadProcessId");visible:=u.NewProc("IsWindowVisible");post:=u.NewProc("PostMessageW")
 callback:=syscall.NewCallback(func(hwnd,lparam uintptr)uintptr{var pid uint32;getpid.Call(hwnd,uintptr(unsafe.Pointer(&pid)));v,_,_:=visible.Call(hwnd);if ids[pid]&&v!=0{post.Call(hwnd,0x0010,0,0)};return 1})
 r,_,er:=u.NewProc("EnumWindows").Call(callback,0);if r==0{return true,er};return true,nil
}
