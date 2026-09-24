//go:build windows

package main

import (
 "encoding/binary"
 "fmt"
 "strings"
 "syscall"
 "unsafe"
)

// Reads only the current Windows user's Steam preferences; never credentials.
func regValue(sub, name string, view uint32) (uint32, []byte, error) {
 k, e := syscall.UTF16PtrFromString(sub); if e != nil { return 0, nil, e }
 var h syscall.Handle
 e = syscall.RegOpenKeyEx(syscall.HKEY_CURRENT_USER,k,0,syscall.KEY_QUERY_VALUE|view,&h)
 if e != nil { return 0,nil,e }; defer syscall.RegCloseKey(h)
 n,e:=syscall.UTF16PtrFromString(name); if e!=nil{return 0,nil,e}
 var typ,size uint32
 e=syscall.RegQueryValueEx(h,n,nil,&typ,nil,&size);if e!=nil{return 0,nil,e}
 if size>65536{return 0,nil,fmt.Errorf("registry value too large")};if size==0{return typ,nil,nil}
 b:=make([]byte,size);e=syscall.RegQueryValueEx(h,n,nil,&typ,&b[0],&size)
 if e!=nil{return 0,nil,e};return typ,b[:size],nil
}
func regDWORD(sub,name string,view uint32)(uint32,error){
 t,b,e:=regValue(sub,name,view);if e!=nil{return 0,e}
 if t!=syscall.REG_DWORD||len(b)!=4{return 0,fmt.Errorf("expected DWORD %s",name)}
 return binary.LittleEndian.Uint32(b),nil
}
func regString(sub,name string,view uint32)string{
 t,b,e:=regValue(sub,name,view)
 if e!=nil||(t!=syscall.REG_SZ&&t!=syscall.REG_EXPAND_SZ)||len(b)%2!=0{return ""}
 u:=make([]uint16,len(b)/2);for i:=range u{u[i]=binary.LittleEndian.Uint16(b[2*i:])};return syscall.UTF16ToString(u)
}
// This selects the remembered login requested by the owner, in HKCU only.
// Steam itself performs authentication. No passwords or tokens are read or written.
func setAutoLogin(login string,remove bool,view uint32)error{
 if view!=0&&view!=syscall.KEY_WOW64_32KEY&&view!=syscall.KEY_WOW64_64KEY{return fmt.Errorf("Unsupported registry view")}
 if !remove&&(login==""||len(login)>128||strings.ContainsAny(login,"\x00\r\n")){return fmt.Errorf("Invalid saved login name")}
 k,_:=syscall.UTF16PtrFromString(`Software\Valve\Steam`);var h syscall.Handle
 e:=syscall.RegOpenKeyEx(syscall.HKEY_CURRENT_USER,k,0,syscall.KEY_SET_VALUE|view,&h);if e!=nil{return e};defer syscall.RegCloseKey(h)
 n,_:=syscall.UTF16PtrFromString("AutoLoginUser");adv:=syscall.NewLazyDLL("advapi32.dll");var r uintptr
 if remove{
  r,_,_=adv.NewProc("RegDeleteValueW").Call(uintptr(h),uintptr(unsafe.Pointer(n)))
  if r==uintptr(syscall.ERROR_FILE_NOT_FOUND){return nil}
 }else{
  u,_:=syscall.UTF16FromString(login)
  r,_,_=adv.NewProc("RegSetValueExW").Call(uintptr(h),uintptr(unsafe.Pointer(n)),0,syscall.REG_SZ,uintptr(unsafe.Pointer(&u[0])),uintptr(len(u)*2))
 }
 if r!=0{return syscall.Errno(r)};return nil
}
