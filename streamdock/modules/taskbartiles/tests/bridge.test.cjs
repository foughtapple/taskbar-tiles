'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),path=require('node:path'),{EventEmitter}=require('node:events');
const pkg=process.env.UNIFIED_PACKAGE;
if(!pkg){test('bridge integration runs in the built package',t=>t.skip('UNIFIED_PACKAGE is set by Build-StreamDock.ps1'));}
else test('one SDK connection routes actions to the correct internal worker',async()=>{
 const bridge=require(path.join(pkg,'plugin','bridge.cjs'));
 const {WebSocket}=require(path.join(pkg,'workers','steam','plugin','dependencies','ws'));
 class FakeReal extends EventEmitter{constructor(){super();this.readyState=1;this.sent=[];}send(v){this.sent.push(Buffer.isBuffer(v)?v.toString():String(v));}close(){this.readyState=3;}}
 const real=new FakeReal(),clients=new Map(),children=[];
 const spawn=(file,args)=>{
   const child=new EventEmitter();child.killed=false;child.kill=()=>{child.killed=true;child.emit('exit',0,null);};children.push(child);
   const get=k=>args[args.indexOf(k)+1],port=Number(get('-port')),uuid=get('-pluginUUID'),id=/worker\.([^.]+)\./.exec(uuid)[1];
   const ws=new WebSocket('ws://127.0.0.1:'+port);clients.set(id,{ws,received:[]});
   ws.on('open',()=>ws.send(JSON.stringify({event:'registerPlugin',uuid})));
   ws.on('message',b=>clients.get(id).received.push(JSON.parse(b.toString())));
   return child;
 };
 const app=bridge.start(['-port','1234','-pluginUUID','com.foughtapple.taskbartiles','-registerEvent','registerPlugin'],{realSocket:real,noSignals:true,spawn});
 real.emit('open');await new Promise(r=>setTimeout(r,150));
 assert.ok(real.sent.some(s=>JSON.parse(s).event==='registerPlugin'));
 assert.deepEqual([...clients.keys()].sort(),['controls','desk','orders','steam']);
 const host={event:'willAppear',action:'com.foughtapple.deskstatus.pc',context:'ctx-pc',payload:{settings:{interval:5}}};
 real.emit('message',Buffer.from(JSON.stringify(host)));await new Promise(r=>setTimeout(r,30));
 assert.equal(clients.get('desk').received.at(-1).context,'ctx-pc');
 assert.equal(clients.get('steam').received.length,0);
 clients.get('desk').ws.send(JSON.stringify({event:'setTitle',context:'ctx-pc',payload:{title:'CPU'}}));await new Promise(r=>setTimeout(r,30));
 assert.ok(real.sent.some(s=>{const x=JSON.parse(s);return x.event==='setTitle'&&x.context==='ctx-pc';}));
 real.emit('message',Buffer.from(JSON.stringify({event:'systemDidWakeUp'})));await new Promise(r=>setTimeout(r,30));
 assert.ok([...clients.values()].every(x=>x.received.some(e=>e.event==='systemDidWakeUp')));
 app.shutdown();for(const x of clients.values())try{x.ws.close();}catch{}
});
