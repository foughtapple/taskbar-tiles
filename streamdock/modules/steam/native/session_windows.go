//go:build windows

package main

import (
 "fmt"
 "os"
 "path/filepath"
 "strings"
 "syscall"
 "time"
 "unsafe"
)
var kernel=syscall.NewLazyDLL("kernel32.dll")
var queryProcessImage=kernel.NewProc("QueryFullProcessImageNameW")
var processSession=kernel.NewProc("ProcessIdToSessionId")
var processExit=kernel.NewProc("GetExitCodeProcess")
func sessionID(pid uint32)(uint32,error){var sid uint32;r,_,e:=processSession.Call(uintptr(pid),uintptr(unsafe.Pointer(&sid)));if r==0{return 0,e};return sid,nil}
func inspectProcess(h syscall.Handle,pid uint32)(bool,string,string,string){
 var code uint32;r,_,_:=processExit.Call(uintptr(h),uintptr(unsafe.Pointer(&code)));if r==0||code!=259{return false,"","","Steam has exited."}
 current,e1:=sessionID(uint32(os.Getpid()));remote,e2:=sessionID(pid)
 if e1!=nil||e2!=nil||current!=remote{return false,"","","Steam could not be confirmed in this Windows session."}
 buf:=make([]uint16,32768);n:=uint32(len(buf));r,_,_=queryProcessImage.Call(uintptr(h),0,uintptr(unsafe.Pointer(&buf[0])),uintptr(unsafe.Pointer(&n)))
 if r==0||n==0{return false,"","","Steam's executable path could not be read. Run Steam and Stream Dock as the same normal user."}
 exe:=syscall.UTF16ToString(buf[:n]);if !strings.EqualFold(filepath.Base(exe),"steam.exe"){return false,"","","The registry's process is not steam.exe."}
 var created,exited,kernelTime,userTime syscall.Filetime
 if syscall.GetProcessTimes(h,&created,&exited,&kernelTime,&userTime)!=nil{return false,"","","Steam process start time could not be checked."}
 return true,exe,fmt.Sprintf("%08x%08x",created.HighDateTime,created.LowDateTime),""
}
func nativeSnapshot()Snapshot{
 v:=Snapshot{ObservedAt:time.Now().UnixMilli(),Reason:"Steam is closed or signed out.",RunningApps:[]string{}}
 const key=`Software\Valve\Steam`;const active=key+`\ActiveProcess`
 for _,view:=range []uint32{0,syscall.KEY_WOW64_32KEY,syscall.KEY_WOW64_64KEY}{
  dir:=regString(key,"SteamPath",view);if dir==""{ex:=regString(key,"SteamExe",view);if ex!=""{dir=filepath.Dir(strings.Trim(ex,`"`))}}
  if dir!=""{v.SteamPath=filepath.Clean(strings.ReplaceAll(dir,"/",`\`));v.RegistryView=view}
  pid,_:=regDWORD(active,"pid",view);uid,_:=regDWORD(active,"ActiveUser",view);if pid==0{continue}
  h,e:=syscall.OpenProcess(0x1000,false,pid);if e!=nil{continue};live,exe,start,reason:=inspectProcess(h,pid);syscall.CloseHandle(h)
  if !live{v.Reason=reason;continue}
  if v.SteamPath!=""&&!strings.EqualFold(filepath.Clean(exe),filepath.Join(v.SteamPath,"steam.exe")){continue}
  v.SteamPath=filepath.Dir(exe);v.Running=true;v.PID=pid;v.ProcessStart=start;v.RegistryView=view
  pid2,e1:=regDWORD(active,"pid",view);uid2,e2:=regDWORD(active,"ActiveUser",view)
  if e1==nil&&e2==nil&&pid==pid2&&uid==uid2&&uid!=0{v.LoggedIn=true;v.SteamID=accountSteamID(uid);v.Reason="Steam reports this active account in this Windows session."}else{v.Reason="Steam is open; no stable active account is reported."};break
 }
 if !v.Running&&v.SteamPath!=""{
  h,e:=syscall.CreateToolhelp32Snapshot(syscall.TH32CS_SNAPPROCESS,0)
  if e==nil{defer syscall.CloseHandle(h);var p syscall.ProcessEntry32;p.Size=uint32(unsafe.Sizeof(p));e=syscall.Process32First(h,&p)
   for e==nil{
    if strings.EqualFold(syscall.UTF16ToString(p.ExeFile[:]),"steam.exe"){
     ph,er:=syscall.OpenProcess(0x1000,false,p.ProcessID)
     if er==nil{live,ex,st,_:=inspectProcess(ph,p.ProcessID);syscall.CloseHandle(ph)
      if live&&strings.EqualFold(filepath.Clean(ex),filepath.Join(v.SteamPath,"steam.exe")){v.Running=true;v.PID=p.ProcessID;v.ProcessStart=st;v.Reason="Steam is open, but no signed-in account is confirmed.";break}
     }
    };e=syscall.Process32Next(h,&p)
   }
  }
 }
 v.AutoLogin=regString(key,"AutoLoginUser",v.RegistryView);_,_,e:=regValue(key,"AutoLoginUser",v.RegistryView);v.AutoLoginPresent=e==nil
 if v.Running{v.RunningApps,v.GamesKnown=runningApps(v.RegistryView)}else{v.GamesKnown=true};return v
}
func runningApps(view uint32)([]string,bool){
 const key=`Software\Valve\Steam\Apps`;out:=[]string{};k,_:=syscall.UTF16PtrFromString(key);var h syscall.Handle
 e:=syscall.RegOpenKeyEx(syscall.HKEY_CURRENT_USER,k,0,syscall.KEY_READ|view,&h)
 if e==syscall.ERROR_FILE_NOT_FOUND{return out,true};if e!=nil{return out,false};defer syscall.RegCloseKey(h)
 for i:=uint32(0);i<20000;i++{
  buf:=make([]uint16,512);n:=uint32(len(buf));e=syscall.RegEnumKeyEx(h,i,&buf[0],&n,nil,nil,nil,nil)
  if e==syscall.Errno(259){return out,true};if e!=nil{return out,false}
  name:=syscall.UTF16ToString(buf[:n]);good:=name!="";for _,c:=range name{if c<'0'||c>'9'{good=false}}
  if good{running,er:=regDWORD(key+`\`+name,"Running",view);if er==nil&&running!=0{out=append(out,name)}}
 };return out,false
}
