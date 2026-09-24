package main
import("encoding/xml";"io";"strings";"testing")
func TestRenderStates(t *testing.T){a,_:=loadArt("");for _,v:=range []int64{0,7,12,137,999999999999999999}{b,e:=a.render(&v);if e!=nil||len(b)>8192{t.Fatal(e)};d:=xml.NewDecoder(strings.NewReader(string(b)));for{_,e=d.Token();if e==io.EOF{break};if e!=nil{t.Fatal(e)}};if !strings.Contains(string(b),viewKey(&v)){t.Fatal("wrong number")};if v==0&&!strings.Contains(string(b),`fill="url(#dark)"`){t.Fatal("zero colour")}};b,_:=a.render(nil);if !strings.Contains(string(b),">?</text>"){t.Fatal("unknown")} }
