'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),path=require('node:path'),{EventEmitter}=require('node:events');
const pkg=process.env.UNIFIED_PACKAGE;
if(pkg)test('forwarded Buffer JSON stays text-framed to worker and Stream Dock',async t=>{
 const bridge=require(path.join(pkg,'plugin','bridge.cjs'));
 const {WebSocket}=require(path.join(pkg,'workers','steam','plugin','dependencies','ws'));
 const action='com.foughtapple.controls.clipboard';let worker,child,workerFrame,hostFrame;
 class Real extends EventEmitter{constructor(){super();this.readyState=1;this.bufferedAmount=0;}send(raw,options){assert.equal(options.binary,false);const m=JSON.parse(raw.toString());if(m.event==='setTitle')hostFrame=m;}close(){this.readyState=3;}}
 const real=new Real();
 const spawn=(file,args)=>{
  child=new EventEmitter();child.kill=()=>{worker.terminate();child.emit('exit',0,null);};
  const get=k=>args[args.indexOf(k)+1];worker=new WebSocket('ws://127.0.0.1:'+get('-port'));
  worker.on('error',()=>{});worker.on('close',()=>child.emit('exit',0,null));
  worker.on('open',()=>worker.send(JSON.stringify({event:'registerPlugin',uuid:get('-pluginUUID')})));
  worker.on('message',(raw,isBinary)=>{workerFrame={isBinary,msg:JSON.parse(raw.toString())};worker.send(JSON.stringify({event:'setTitle',context:'frame',payload:{title:'OK'}}));});return child;
 };
 const app=bridge.start(['-port','1234','-pluginUUID','test','-registerEvent','registerPlugin'],{realSocket:real,manifest:{Actions:[{UUID:action}]},noSignals:true,spawn,stopMs:200});
 t.after(()=>app.shutdown());real.emit('open');
 const wait=async f=>{const until=Date.now()+5000;while(!f()){if(Date.now()>until)throw Error('framing timeout');await new Promise(r=>setTimeout(r,10));}};
 await wait(()=>app.states.get('controls')?.socket);
 real.emit('message',Buffer.from(JSON.stringify({event:'willAppear',action,context:'frame',payload:{}})));
 await wait(()=>workerFrame&&hostFrame);
 assert.equal(workerFrame.isBinary,false,'Go/C# SDK clients require a text frame');assert.equal(workerFrame.msg.context,'frame');assert.equal(hostFrame.context,'frame');
});
