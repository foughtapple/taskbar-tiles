'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),path=require('node:path'),fs=require('node:fs'),os=require('node:os'),{EventEmitter}=require('node:events');
const pkg=process.env.UNIFIED_PACKAGE;
const wait=async predicate=>{const start=Date.now();while(!predicate()){if(Date.now()-start>10000)throw Error('Timed out waiting for bridge');await new Promise(r=>setTimeout(r,20));}};
const delay=ms=>new Promise(r=>setTimeout(r,ms));
class FakeReal extends EventEmitter{
 constructor(onRegister){super();this.readyState=1;this.bufferedAmount=0;this.sent=[];this.onRegister=onRegister;}
 send(v){const m=JSON.parse(v.toString());this.sent.push(m);if(m.event==='registerPlugin'&&this.onRegister)this.onRegister(this);}
 close(){this.readyState=3;}
}
const PC='com.foughtapple.deskstatus.pc',CLIP='com.foughtapple.controls.clipboard';
const appear=(action,context)=>({event:'willAppear',action,context,device:'test-dock',payload:{settings:{interval:5}}});
function mockSpawn(WebSocket,clients){return(file,args)=>{
 const child=new EventEmitter();child.killed=false;
 const get=k=>args[args.indexOf(k)+1],port=Number(get('-port')),uuid=get('-pluginUUID'),id=/worker\.([^.]+)\./.exec(uuid)[1];
 const ws=new WebSocket('ws://127.0.0.1:'+port);clients.set(id,{ws,received:[],child});
 child.kill=()=>{child.killed=true;ws.terminate();child.emit('exit',0,null);};
 ws.on('error',()=>{});ws.on('open',()=>ws.send(JSON.stringify({event:'registerPlugin',uuid})));
 ws.on('close',()=>child.emit('exit',0,null));ws.on('message',b=>clients.get(id).received.push(JSON.parse(b.toString())));
 return child;
};}
if(!pkg){test('bridge integration requires built package',t=>t.skip('UNIFIED_PACKAGE is set by Build-StreamDock.ps1'));}
else {
 const bridge=require(path.join(pkg,'plugin','bridge.cjs'));
 const {WebSocket}=require(path.join(pkg,'workers','steam','plugin','dependencies','ws'));
 test('startup appearance is queued before worker registration, not lost',async t=>{
  const clients=new Map(),real=new FakeReal(r=>r.emit('message',Buffer.from(JSON.stringify(appear(PC,'pc')))));
  const app=bridge.start(['-port','1234','-pluginUUID','unified','-registerEvent','registerPlugin'],{realSocket:real,noSignals:true,stopMs:200,spawn:mockSpawn(WebSocket,clients)});
  t.after(()=>app.shutdown());real.emit('open');
  await wait(()=>clients.get('desk')?.received.some(x=>x.context==='pc'));
  assert.equal(real.sent.filter(x=>x.event==='registerPlugin').length,1);
  assert.deepEqual([...clients.keys()].sort(),['controls','desk','orders','steam']);
  assert.equal(clients.get('steam').received.length,0);
  clients.get('desk').ws.send(JSON.stringify({event:'setTitle',context:'pc',payload:{title:'CPU'}}));
  await wait(()=>real.sent.some(x=>x.event==='setTitle'&&x.context==='pc'));
  const before=real.sent.length;
  clients.get('steam').ws.send(JSON.stringify({event:'setTitle',context:'pc',payload:{title:'WRONG WORKER'}}));
  await delay(50);assert.equal(real.sent.length,before,'worker cannot write a different worker context');
  real.emit('message',Buffer.from(JSON.stringify({event:'willDisappear',context:'pc'})));
  await wait(()=>clients.get('desk').received.some(x=>x.event==='willDisappear'&&x.action===PC));
  clients.get('desk').ws.send(JSON.stringify({event:'setTitle',context:'pc',payload:{title:'HIDDEN'}}));
  await delay(50);assert.equal(real.sent.length,before,'hidden context cannot be repainted');
  real.emit('message',Buffer.from(JSON.stringify({event:'systemDidWakeUp'})));
  await wait(()=>[...clients.values()].every(x=>x.received.some(e=>e.event==='systemDidWakeUp')));
 });
 test('disabled groups are not launched or routed, shutdown leaves no workers',async t=>{
  const clients=new Map(),real=new FakeReal();
  const app=bridge.start(['-port','1234','-pluginUUID','unified','-registerEvent','registerPlugin'],{realSocket:real,noSignals:true,stopMs:200,manifest:{Actions:[{UUID:CLIP}]},spawn:mockSpawn(WebSocket,clients)});
  t.after(()=>app.shutdown());real.emit('open');await wait(()=>app.states.get('controls')?.socket);
  assert.deepEqual([...clients.keys()],['controls']);
  real.emit('message',Buffer.from(JSON.stringify(appear(PC,'not-enabled'))));await delay(40);
  assert.equal(clients.get('controls').received.length,0);
  real.emit('message',Buffer.from(JSON.stringify(appear(CLIP,'clip'))));await wait(()=>clients.get('controls').received.length===1);
  const unknown=new WebSocket('ws://127.0.0.1:'+app.server.address().port);unknown.on('error',()=>{});
  const closed=new Promise(resolve=>unknown.once('close',resolve));unknown.on('open',()=>unknown.send(JSON.stringify({event:'registerPlugin',uuid:'guessed-worker'})));
  await closed;
  app.shutdown();await wait(()=>[...app.states.values()].every(s=>!s.child));
 });
 test('real Windows workers connect, display and exit without game commands',{skip:process.platform!=='win32'},async t=>{
  const previous=process.env.LOCALAPPDATA,temp=fs.mkdtempSync(path.join(os.tmpdir(),'unified-native-'));
  process.env.LOCALAPPDATA=temp;
  const real=new FakeReal(r=>{for(const [a,c] of [[PC,'pc'],['com.foughtapple.deskstatus.p1s','p1s'],['com.foughtapple.nicknacksorders.processing','orders'],['com.foughtapple.steamsmarttoggle.toggle','steam'],[CLIP,'clip']])r.emit('message',Buffer.from(JSON.stringify(appear(a,c))));});
  const app=bridge.start(['-port','1234','-pluginUUID','native-test','-registerEvent','registerPlugin'],{realSocket:real,noSignals:true});
  t.after(async()=>{app.shutdown();await wait(()=>[...app.states.values()].every(s=>!s.child));if(previous===undefined)delete process.env.LOCALAPPDATA;else process.env.LOCALAPPDATA=previous;fs.rmSync(temp,{recursive:true,force:true});});
  real.emit('open');await wait(()=>[...app.states.values()].length===4&&[...app.states.values()].every(s=>s.socket));
  await wait(()=>['pc','p1s','orders','steam'].every(c=>real.sent.some(m=>m.event==='setImage'&&m.context===c)));
  for(const st of app.states.values())assert.ok(st.child&&!st.failed,'native worker remains alive');
  // No keyDown was emitted: no Steam account switch, game launch, screenshot or input.
  real.emit('message',Buffer.from(JSON.stringify({event:'deviceDidDisconnect',device:'test-dock'})));
 });
}
