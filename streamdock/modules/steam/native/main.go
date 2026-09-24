// Small on-demand Windows session probe. No polling loop or background service.
package main

import (
 "encoding/json"
 "fmt"
 "os"
 "strconv"
)

const steamBase uint64 = 76561197960265728

type Snapshot struct {
 Running bool `json:"running"`
 LoggedIn bool `json:"loggedIn"`
 SteamID string `json:"steamId"`
 PID uint32 `json:"pid"`
 ProcessStart string `json:"processStart"`
 SteamPath string `json:"steamPath"`
 AutoLogin string `json:"autoLogin"`
 AutoLoginPresent bool `json:"autoLoginPresent"`
 RunningApps []string `json:"runningApps"`
 GamesKnown bool `json:"gamesKnown"`
 RegistryView uint32 `json:"registryView"`
 Reason string `json:"reason"`
 ObservedAt int64 `json:"observedAt"`
}
func accountSteamID(id uint32) string {
 if id==0{return ""};return strconv.FormatUint(steamBase+uint64(id),10)
}
func main() {
 var err error
 if len(os.Args)<2 {err=fmt.Errorf("Use --once, --open, or --validate")} else {
  switch os.Args[1] {
  case "--validate":fmt.Println(`{"probe":"SteamSession","version":"1.1.0"}`);return
  case "--once":err=json.NewEncoder(os.Stdout).Encode(nativeSnapshot())
  case "--open":err=openSteam()
  case "--rocket-status":var running bool;running,err=rocketRunning(false);if err==nil{err=json.NewEncoder(os.Stdout).Encode(map[string]bool{"running":running})}
  case "--close-rocket":_,err=rocketRunning(true)
  case "--launch-rocket":err=openSteamURI("steam://run/252950")
  case "--autologin":
   if len(os.Args)!=4{err=fmt.Errorf("Missing login or registry view")}else{var v uint64;v,err=strconv.ParseUint(os.Args[3],10,32);if err==nil{err=setAutoLogin(os.Args[2],false,uint32(v))}}
  case "--clear-autologin":
   if len(os.Args)!=3{err=fmt.Errorf("Missing registry view")}else{var v uint64;v,err=strconv.ParseUint(os.Args[2],10,32);if err==nil{err=setAutoLogin("",true,uint32(v))}}
  default:err=fmt.Errorf("Unknown operation")
  }
 }
 if err!=nil{fmt.Fprintln(os.Stderr,err);os.Exit(1)}
}
