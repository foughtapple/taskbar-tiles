//go:build !windows

package main
import("errors";"os";"strings")
func protectSecret(s string)(string,error){return "",errors.New("Windows DPAPI is required to save credentials.")}
func revealSecret(s string)(string,error){return "",errors.New("Windows DPAPI is required to read saved credentials.")}
func replaceFile(a,b string)error{return os.Rename(a,b)}
func environmentToken()string{return strings.TrimSpace(os.Getenv("NICKNACKSAU_MCP_TOKEN"))}
func acquireInstance()(func(),bool){return func(){},true}
func notify(s string){}
func openReport(p string)error{return nil}
