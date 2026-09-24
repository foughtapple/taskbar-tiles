package main

import (
 "bufio"
 "crypto/rand"
 "crypto/sha1"
 "encoding/base64"
 "encoding/binary"
 "fmt"
 "io"
 "net"
 "net/http"
 "strings"
 "sync"
 "time"
 "unicode/utf8"
)
const maxMessage = 4 * 1024 * 1024
const wsGUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
type socket struct { c net.Conn; reader *bufio.Reader; mu sync.Mutex }
func hasToken(value, token string) bool { for _, t := range strings.Split(value, ",") { if strings.EqualFold(strings.TrimSpace(t), token) { return true } }; return false }
func connectSocket(port int) (*socket, error) {
 c, e := net.DialTimeout("tcp4", fmt.Sprintf("127.0.0.1:%d", port), 5*time.Second)
 if e != nil { return nil, e }
 fail := func(e error) (*socket, error) { c.Close(); return nil, e }
 keyBytes := make([]byte, 16); if _, e = rand.Read(keyBytes); e != nil { return fail(e) }
 key := base64.StdEncoding.EncodeToString(keyBytes)
 req, e := http.NewRequest("GET", fmt.Sprintf("http://127.0.0.1:%d/", port), nil); if e != nil { return fail(e) }
 req.Header.Set("Upgrade", "websocket"); req.Header.Set("Connection", "Upgrade"); req.Header.Set("Sec-WebSocket-Version", "13"); req.Header.Set("Sec-WebSocket-Key", key)
 c.SetDeadline(time.Now().Add(5 * time.Second)); if e = req.Write(c); e != nil { return fail(e) }
 br := bufio.NewReader(c); resp, e := http.ReadResponse(br, req); if e != nil { return fail(e) }
 expected := sha1.Sum([]byte(key + wsGUID))
 if resp.StatusCode != 101 || !hasToken(resp.Header.Get("Upgrade"), "websocket") || !hasToken(resp.Header.Get("Connection"), "upgrade") || resp.Header.Get("Sec-WebSocket-Accept") != base64.StdEncoding.EncodeToString(expected[:]) { return fail(fmt.Errorf("Stream Dock did not accept the WebSocket handshake")) }
 c.SetDeadline(time.Time{}); return &socket{c: c, reader: br}, nil
}
func writeAll(w io.Writer, b []byte) error { for len(b)>0 { n,e:=w.Write(b); if e!=nil{return e};if n<=0{return io.ErrShortWrite};b=b[n:] };return nil }
func (s *socket) sendFrame(op byte, payload []byte) error {
 if len(payload)>maxMessage{return fmt.Errorf("outgoing SDK message too large")};if op>=8&&len(payload)>125{return fmt.Errorf("oversized control frame")}
 s.mu.Lock();defer s.mu.Unlock();s.c.SetWriteDeadline(time.Now().Add(5*time.Second))
 header:=[]byte{0x80|op}; n:=len(payload)
 if n<126{header=append(header,0x80|byte(n))}else if n<=65535{header=append(header,0x80|126,byte(n>>8),byte(n))}else{header=append(header,0x80|127);size:=make([]byte,8);binary.BigEndian.PutUint64(size,uint64(n));header=append(header,size...)}
 mask:=make([]byte,4);if _,e:=rand.Read(mask);e!=nil{return e};header=append(header,mask...)
 frame:=make([]byte,len(header)+n);copy(frame,header);for i,b:=range payload{frame[len(header)+i]=b^mask[i%4]};return writeAll(s.c,frame)
}
func(s *socket)sendText(text []byte)error{return s.sendFrame(1,text)}
func(s *socket)receive()([]byte,error){
 var assembled []byte;fragmented:=false
 for{
  head:=make([]byte,2);if _,e:=io.ReadFull(s.reader,head);e!=nil{return nil,e}
  fin:=head[0]&128!=0;op:=head[0]&15
  if head[0]&0x70!=0||head[1]&128!=0{return nil,fmt.Errorf("invalid server WebSocket flags")}
  n:=uint64(head[1]&127)
  if n==126{b:=make([]byte,2);if _,e:=io.ReadFull(s.reader,b);e!=nil{return nil,e};n=uint64(binary.BigEndian.Uint16(b))}else if n==127{b:=make([]byte,8);if _,e:=io.ReadFull(s.reader,b);e!=nil{return nil,e};n=binary.BigEndian.Uint64(b)}
  if n>maxMessage||uint64(len(assembled))+n>maxMessage{return nil,fmt.Errorf("SDK message exceeds limit")}
  if op>=8&&(!fin||n>125){return nil,fmt.Errorf("invalid control frame")}
  b:=make([]byte,int(n));if _,e:=io.ReadFull(s.reader,b);e!=nil{return nil,e}
  switch op{
  case 8:_=s.sendFrame(8,b);return nil,io.EOF
  case 9:if e:=s.sendFrame(10,b);e!=nil{return nil,e};continue
  case 10:continue
  case 1:if fragmented{return nil,fmt.Errorf("unexpected text frame")};assembled=append(assembled,b...);fragmented=!fin
  case 0:if !fragmented{return nil,fmt.Errorf("unexpected continuation")};assembled=append(assembled,b...)
  default:return nil,fmt.Errorf("unsupported SDK message opcode")
  }
  if fin{if !utf8.Valid(assembled){return nil,fmt.Errorf("invalid UTF-8 SDK message")};return assembled,nil}
 }
}
func(s *socket)close(){s.c.Close()}
