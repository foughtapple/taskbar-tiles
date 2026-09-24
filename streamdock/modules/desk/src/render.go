package main
import("encoding/base64";"fmt";"html";"math";"strings";"time")
const svgStart=`<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256"><rect width="256" height="256" fill="#000000"/><rect x="4" y="4" width="248" height="248" rx="28" fill="#080d12" stroke="#263039" stroke-width="2"/>`
const svgEnd=`</svg>`
func label(x,y,size int,txt,col,anchor string)string{return fmt.Sprintf(`<text x="%d" y="%d" font-family="Segoe UI,Arial,sans-serif" font-size="%d" font-weight="700" fill="%s" text-anchor="%s">%s</text>`,x,y,size,col,anchor,html.EscapeString(txt))}
func pcColor(n float64,base string)string{if n>=97{return "#ff667b"};if n>=90{return "#ffc36b"};return base}
func renderPC(p PCSample)string{
 var b strings.Builder;b.WriteString(svgStart);b.WriteString(`<path d="M22 127H234" stroke="#263039" stroke-width="1.5"/>`)
 for i,r:=range []struct{name string;value float64;valid bool;color string}{{"CPU",p.CPU,p.CPUValid,"#42cfff"},{"RAM",p.RAM,p.RAMValid,"#48e6b0"}}{
  off:=i*116;c:=pcColor(r.value,r.color);value:="--";if r.valid{value=fmt.Sprintf("%.0f",r.value)}
  b.WriteString(label(22,48+off,24,r.name,c,"start"));b.WriteString(label(194,91+off,62,value,"#f4f9ff","end"));b.WriteString(label(232,90+off,28,"%",c,"end"))
  b.WriteString(fmt.Sprintf(`<rect x="22" y="%d" width="212" height="10" rx="5" fill="#1c2631"/>`,104+off))
  if r.valid&&r.value>0{w:=math.Max(1,212*math.Min(100,r.value)/100);b.WriteString(fmt.Sprintf(`<rect x="22" y="%d" width="%.1f" height="10" rx="5" fill="%s"/>`,104+off,w,c))}
 };b.WriteString(svgEnd);return b.String()
}
func renderPrinter(s PrinterSnapshot,now time.Time)string{
 v:=printerView(s,now);var b strings.Builder;b.WriteString(svgStart);b.WriteString(label(23,36,25,"P1S","#d6e2ee","start"));b.WriteString(fmt.Sprintf(`<circle cx="227" cy="27" r="6" fill="%s"/>`,v.Color))
 if v.State=="running"||v.State=="prepare"||v.State=="slicing"{
  b.WriteString(`<circle cx="128" cy="128" r="71" fill="none" stroke="#1c2933" stroke-width="11"/>`)
  if v.Percent!=nil{circ:=2*math.Pi*71;part:=circ*float64(*v.Percent)/100;b.WriteString(fmt.Sprintf(`<circle cx="128" cy="128" r="71" fill="none" stroke="%s" stroke-width="11" stroke-linecap="round" stroke-dasharray="%.2f %.2f" transform="rotate(-90 128 128)"/>`,v.Color,part,circ))}
  size:=59;if len(v.Main)>3{size=52};b.WriteString(label(128,143,size,v.Main,"#f4f9ff","middle"))
 }else{
  switch v.State{
  case "finish":b.WriteString(fmt.Sprintf(`<circle cx="128" cy="96" r="37" fill="%s" fill-opacity="0.11"/><path d="M107 96l14 14 30-32" stroke="%s" stroke-width="9" stroke-linecap="round" stroke-linejoin="round" fill="none"/>`,v.Color,v.Color))
  case "pause":b.WriteString(fmt.Sprintf(`<rect x="103" y="65" width="16" height="54" rx="5" fill="%s"/><rect x="137" y="65" width="16" height="54" rx="5" fill="%s"/>`,v.Color,v.Color))
  case "error","failed","stop","cancelled","auth":b.WriteString(fmt.Sprintf(`<path d="M128 57l42 72H86Z" fill="none" stroke="%s" stroke-width="7" stroke-linejoin="round"/><path d="M128 81v21" stroke="%s" stroke-width="7" stroke-linecap="round"/><circle cx="128" cy="115" r="4" fill="%s"/>`,v.Color,v.Color,v.Color))
  default:b.WriteString(fmt.Sprintf(`<path d="M92 123V65h72v58M87 123h82M104 110h48M107 65v13h42V65M128 78v10M110 103l18-13 18 13" stroke="%s" stroke-width="6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>`,v.Color))
  };size:=43;if len(v.Main)>6{size=32};b.WriteString(label(128,177,size,v.Main,v.Color,"middle"))
 };size:=25;if len(v.Sub)>11{size=20};b.WriteString(label(128,235,size,v.Sub,"#cad8e5","middle"));b.WriteString(svgEnd);return b.String()
}
func svgURI(s string)string{return "data:image/svg+xml;base64,"+base64.StdEncoding.EncodeToString([]byte(s))}
