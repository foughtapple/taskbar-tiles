// NickNacks Orders: read-only count display. No AI calls or store writes.
package main
import("bytes";"encoding/json";"errors";"fmt";"io";"net";"net/url";"os";"path/filepath";"strconv";"strings";"time")
const Version="1.0.1"
const ActionID="com.foughtapple.nicknacksorders.processing"
const PluginFolder="com.foughtapple.nicknacksorders.sdPlugin"
const SnapshotTool="nn_storeops_woocommerce_snapshot"
const SitesTool="nn_storeops_multisite_sites_snapshot"
const DefaultEndpoint="https://nicknacksau.com.au/wp-json/mcp/v1/http"
const PollInterval=5*time.Minute
const MaxResponse=4*1024*1024
type Config struct{Endpoint string `json:"endpoint"`;BlogID int64 `json:"blogID"`;SiteURL string `json:"siteURL"`;Confirmed bool `json:"confirmed"`;UseEnvironment bool `json:"useEnvironment"`;ProtectedToken string `json:"protectedToken,omitempty"`}
type PublicConfig struct{Endpoint string `json:"endpoint"`;BlogID int64 `json:"blogID"`;SiteURL string `json:"siteURL"`;Confirmed bool `json:"confirmed"`;UseEnvironment bool `json:"useEnvironment"`;HasSavedToken bool `json:"hasSavedToken"`;HasEnvironmentToken bool `json:"hasEnvironmentToken"`}
func defaults()Config{return Config{Endpoint:DefaultEndpoint,UseEnvironment:true}}
func(c Config)public()PublicConfig{return PublicConfig{c.Endpoint,c.BlogID,c.SiteURL,c.Confirmed,c.UseEnvironment,c.ProtectedToken!="",environmentToken()!=""}}
func validateEndpoint(raw string)(string,error){
 if len(raw)>2048{return "",errors.New("MCP endpoint URL is too long.")};u,e:=url.Parse(strings.TrimSpace(raw));if e!=nil||u.Hostname()==""{return "",errors.New("Enter a complete MCP HTTP endpoint URL.")}
 if i:=strings.Index(u.Path,"/wp-json/mcp/v1/");i>=0{if strings.Trim(u.Path[i+len("/wp-json/mcp/v1/"):],"/")!="http"{return "",errors.New("Use /wp-json/mcp/v1/http, not an old SSE endpoint or a token embedded in the URL.")}}
 if u.User!=nil||u.RawQuery!=""||u.Fragment!=""{return "",errors.New("Put credentials in the separate token field, not the URL.")}
 if u.Scheme!="https"{ip:=net.ParseIP(u.Hostname());local:=strings.EqualFold(u.Hostname(),"localhost")||(ip!=nil&&ip.IsLoopback());if u.Scheme!="http"||!local{return "",errors.New("Use HTTPS, or HTTP to localhost only.")}}
 if u.Port()!=""{p,e:=strconv.Atoi(u.Port());if e!=nil||p<1||p>65535{return "",errors.New("Invalid endpoint port.")}};return u.String(),nil
}
func validateToken(t string)error{if strings.ContainsAny(t,"\r\n\x00")||len(t)>16384{return errors.New("Invalid token format.")};return nil}
func tokenFor(c Config)(string,error){var t string;var e error;if c.UseEnvironment{t=environmentToken();if t==""{return "",errors.New("NICKNACKSAU_MCP_TOKEN is unavailable. Enter your token privately in settings or restart Stream Dock after setting the variable.")}}else if c.ProtectedToken!=""{t,e=revealSecret(c.ProtectedToken);if e!=nil{return "",e}}else{return "",errors.New("Enter the existing MCP bearer token or use NICKNACKSAU_MCP_TOKEN.")};if e=validateToken(t);e!=nil{return "",e};return strings.TrimSpace(t),nil}
func dataRoot()string{r:=os.Getenv("LOCALAPPDATA");if r==""{r=os.TempDir()};return filepath.Join(r,"FoughtApple","NickNacksOrders")}
func readConfig(path string)(Config,error){c:=defaults();b,e:=os.ReadFile(path);if os.IsNotExist(e){return c,nil};if e!=nil{return c,e};if len(b)>65536{return c,errors.New("Settings file is too large.")};if e=json.Unmarshal(b,&c);e!=nil{return defaults(),errors.New("Saved settings cannot be read. Re-save them in plugin settings.")};if _,e=validateEndpoint(c.Endpoint);e!=nil{return defaults(),e};return c,nil}
func saveConfig(path string,c Config)error{if _,e:=validateEndpoint(c.Endpoint);e!=nil{return e};if c.BlogID<0{return errors.New("Blog ID must be positive or blank.")};b,e:=json.MarshalIndent(c,"","  ");if e!=nil{return e};if e=os.MkdirAll(filepath.Dir(path),0700);e!=nil{return e};f,e:=os.CreateTemp(filepath.Dir(path),"settings-*.tmp");if e!=nil{return e};tmp:=f.Name();defer os.Remove(tmp);_=f.Chmod(0600);if _,e=f.Write(b);e!=nil{f.Close();return e};if e=f.Sync();e!=nil{f.Close();return e};if e=f.Close();e!=nil{return e};return replaceFile(tmp,path)}
func decodeJSON(b []byte)(any,error){if len(b)>MaxResponse{return nil,errors.New("Response exceeds safe size limit.")};d:=json.NewDecoder(bytes.NewReader(b));d.UseNumber();var v any;if e:=d.Decode(&v);e!=nil{return nil,errors.New("Server did not return valid JSON.")};var extra any;if d.Decode(&extra)!=io.EOF{return nil,errors.New("Multiple JSON values in response.")};return v,nil}
// Recognized envelopes only: never guess from arbitrary numbers or order arrays.
func unwrap(v any,depth int)(map[string]any,error){
 if depth>10{return nil,errors.New("Too many nested response envelopes.")};if s,ok:=v.(string);ok{n,e:=decodeJSON([]byte(s));if e!=nil{return nil,e};return unwrap(n,depth+1)}
 m,ok:=v.(map[string]any);if !ok{return nil,errors.New("Unexpected MCP result structure.")};if b,ok:=m["isError"].(bool);ok&&b{return nil,errors.New("MCP tool reported an error.")};if b,ok:=m["success"].(bool);ok&&!b{return nil,errors.New("The NickNacks tool could not read this store.")};if m["error"]!=nil{return nil,errors.New("Server reported a tool error.")}
 for _,k:=range []string{"order_counts","sites","current_site"}{if _,ok:=m[k];ok{return m,nil}};if active,ok:=m["woocommerce_active"].(bool);ok&&!active{return nil,errors.New("WooCommerce is inactive in the selected store.")}
 for _,k:=range []string{"structuredContent","data","result"}{if x,ok:=m[k];ok&&x!=nil{return unwrap(x,depth+1)}}
 if a,ok:=m["content"].([]any);ok{var found map[string]any;for _,item:=range a{p,ok:=item.(map[string]any);if !ok||p["type"]!="text"{continue};s,ok:=p["text"].(string);if !ok{continue};r,e:=unwrap(s,depth+1);if e!=nil{return nil,e};if found!=nil{return nil,errors.New("Ambiguous MCP result blocks.")};found=r};if found!=nil{return found,nil}}
 return nil,errors.New("No supported count data. Server must expose nn_storeops_woocommerce_snapshot.")
}
func integer(v any)(int64,bool){var s string;switch x:=v.(type){case json.Number:s=string(x);case string:s=strings.TrimSpace(x);case int:return int64(x),x>=0;case int64:return x,x>=0;default:return 0,false};n,e:=strconv.ParseInt(s,10,64);return n,e==nil&&n>=0}
type Store struct{BlogID int64 `json:"blogID"`;URL string `json:"url"`}
type CountReading struct{Count int64 `json:"count"`;Store Store `json:"store"`;CheckedAt time.Time `json:"checkedAt"`}
func parseStore(v any)(Store,error){m,ok:=v.(map[string]any);if !ok{return Store{},errors.New("Response does not identify the store.")};id,ok:=integer(m["blog_id"]);if !ok||id==0{return Store{},errors.New("Invalid store ID in response.")};s,_:=m["site_url"].(string);if s==""{s,_=m["home_url"].(string)};if len(s)>2048{return Store{},errors.New("Store URL too long.")};u,e:=url.Parse(s);if e!=nil||u.Hostname()==""||(u.Scheme!="https"&&u.Scheme!="http")||u.User!=nil||u.RawQuery!=""{return Store{},errors.New("Invalid store URL in response.")};return Store{id,s},nil}
func siteKey(s string)string{u,e:=url.Parse(s);if e!=nil{return s};return strings.ToLower(u.Host)+strings.TrimRight(u.EscapedPath(),"/")}
func parseCount(raw any,c Config,now time.Time)(CountReading,error){
 m,e:=unwrap(raw,0);if e!=nil{return CountReading{},e};if active,ok:=m["woocommerce_active"].(bool);ok&&!active{return CountReading{},errors.New("WooCommerce is inactive in this site.")};store,e:=parseStore(m["context"]);if e!=nil{return CountReading{},e}
 if c.BlogID>0&&c.BlogID!=store.BlogID{return CountReading{},errors.New("Server returned a different store ID; count rejected.")};if c.Confirmed&&siteKey(c.SiteURL)!=siteKey(store.URL){return CountReading{},errors.New("Store URL changed. Test and confirm the store again.")}
 list,ok:=m["order_counts"].([]any);if !ok{return CountReading{},errors.New("Missing order_counts array.")};found:=false;var count int64
 for _,r:=range list{row,ok:=r.(map[string]any);if !ok{continue};status,_:=row["status"].(string);if status!="processing"&&status!="wc-processing"{continue};if found{return CountReading{},errors.New("Duplicate processing count.")};n,ok:=integer(row["count"]);if !ok{return CountReading{},errors.New("Processing count is null, negative or not an integer.")};count=n;found=true};if !found{return CountReading{},errors.New("Missing processing count is unknown, not zero.")};return CountReading{count,store,now},nil
}
func parseSites(raw any)([]Store,error){m,e:=unwrap(raw,0);if e!=nil{return nil,e};if c,ok:=m["current_site"];ok{s,e:=parseStore(c);if e!=nil{return nil,e};return []Store{s},nil};a,ok:=m["sites"].([]any);if !ok{return nil,errors.New("Server did not return sites.")};if len(a)>1000{return nil,errors.New("Too many stores.")};out:=[]Store{};seen:=map[int64]bool{};for _,r:=range a{p,ok:=r.(map[string]any);if !ok{continue};if p["archived"]==true||p["deleted"]==true||p["spam"]==true{continue};s,e:=parseStore(p);if e!=nil{return nil,e};if seen[s.BlogID]{return nil,errors.New("Duplicate site ID.")};seen[s.BlogID]=true;out=append(out,s)};return out,nil}
func failureDelay(n int)time.Duration{d:=PollInterval;for i:=1;i<n&&d<30*time.Minute;i++{d*=2};if d>30*time.Minute{d=30*time.Minute};return d}
func configKey(c Config)string{return fmt.Sprintf("%s|%d|%s|%t|%t|%s",c.Endpoint,c.BlogID,c.SiteURL,c.Confirmed,c.UseEnvironment,c.ProtectedToken)}
