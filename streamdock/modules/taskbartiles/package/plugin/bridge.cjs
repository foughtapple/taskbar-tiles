'use strict';
const fs=require('node:fs');
const path=require('node:path');
const crypto=require('node:crypto');
const cp=require('node:child_process');

const ROOT=path.resolve(__dirname,'..');
const WORKERS=path.join(ROOT,'workers');
const WSROOT=path.join(WORKERS,'steam','plugin','dependencies','ws');
const {WebSocket,WebSocketServer}=require(WSROOT);

const ACTION_WORKER=Object.freeze({
  'com.foughtapple.controls.rocket':'controls',
  'com.foughtapple.controls.overwatch':'controls',
  'com.foughtapple.controls.screenshot':'controls',
  'com.foughtapple.controls.clipboard':'controls',
  'com.foughtapple.controls.voice':'controls',
  'com.foughtapple.steamsmarttoggle.toggle':'steam',
  'com.foughtapple.steamsmarttoggle.play':'steam',
  'com.foughtapple.deskstatus.p1s':'desk',
  'com.foughtapple.deskstatus.pc':'desk',
  'com.foughtapple.nicknacksorders.processing':'orders'
});
const OUTPUT_EVENTS=new Set(['setImage','setTitle','setSettings','showAlert','showOk','sendToPropertyInspector']);
const BROADCAST_EVENTS=new Set(['deviceDidDisconnect','systemDidWakeUp','applicationDidLaunch','applicationDidTerminate']);

function parseArgs(argv){
  const out={};
  for(let i=0;i<argv.length;i++){
    let k=argv[i];
    if(!/^--?[A-Za-z]/.test(k)) continue;
    k=k.replace(/^-+/,'').toLowerCase();
    if(i+1<argv.length&&!/^--?[A-Za-z]/.test(argv[i+1])) out[k]=argv[++i];
    else out[k]='';
  }
  return out;
}
function readManifest(){return JSON.parse(fs.readFileSync(path.join(ROOT,'manifest.json'),'utf8'));}
function allowedActions(manifest){return new Set((manifest.Actions||[]).map(a=>a.UUID));}
function workersFor(actions){return new Set([...actions].map(a=>ACTION_WORKER[a]).filter(Boolean));}
function workerSpec(id,port,token){
  const common=['-port',String(port),'-pluginUUID',`com.foughtapple.taskbartiles.worker.${id}.${token}`,'-registerEvent','registerPlugin'];
  if(id==='controls') return {id,uuid:common[3],file:path.join(WORKERS,'controls','plugin','ControlsHost.exe'),args:common,cwd:path.join(WORKERS,'controls','plugin')};
  if(id==='desk') return {id,uuid:common[3],file:path.join(WORKERS,'desk','plugin','DeskStatus.exe'),args:common,cwd:path.join(WORKERS,'desk','plugin')};
  if(id==='orders') return {id,uuid:common[3],file:path.join(WORKERS,'orders','plugin','NickNacksOrders.exe'),args:common,cwd:path.join(WORKERS,'orders','plugin')};
  if(id==='steam') return {id,uuid:common[3],file:process.execPath,args:[path.join(WORKERS,'steam','plugin','index.js'),...common],cwd:path.join(WORKERS,'steam','plugin')};
  throw new Error('Unknown worker '+id);
}
function validate(manifest){
  const actions=allowedActions(manifest);
  if(actions.size===0) throw new Error('No Taskbar Tiles actions are enabled.');
  for(const action of actions) if(!ACTION_WORKER[action]) throw new Error('Unknown action in unified manifest: '+action);
  const needed=workersFor(actions);
  for(const id of needed){
    const spec=workerSpec(id,1,'validate');
    if(!fs.existsSync(spec.file)) throw new Error('Missing '+id+' worker runtime.');
    if(!fs.existsSync(path.join(WORKERS,id,'manifest.json'))) throw new Error('Missing '+id+' worker manifest.');
  }
  if(!fs.existsSync(path.join(ROOT,'images','taskbar.svg'))) throw new Error('Missing Taskbar Tiles category icon.');
  return {actions:actions.size,workers:[...needed]};
}

function start(argv=process.argv.slice(2),overrides={}){
  const options=parseArgs(argv),manifest=readManifest();
  if(argv.includes('--validate')||Object.prototype.hasOwnProperty.call(options,'validate')){
    const result=validate(manifest); console.log(JSON.stringify(result)); return result;
  }
  const port=Number(options.port),pluginUUID=options.pluginuuid,registerEvent=options.registerevent;
  if(!Number.isInteger(port)||port<1||port>65535||!pluginUUID||!registerEvent) throw new Error('Add Taskbar Tiles actions from Stream Dock, not Toolbox > Open.');
  const allowed=allowedActions(manifest),needed=workersFor(allowed);
  const dataDir=path.join(process.env.LOCALAPPDATA||ROOT,'FoughtApple','TaskbarTilesStreamDock');
  fs.mkdirSync(dataDir,{recursive:true});
  function log(msg){try{const p=path.join(dataDir,'bridge.log');if(fs.existsSync(p)&&fs.statSync(p).size>256*1024){try{fs.unlinkSync(p+'.1');}catch{}fs.renameSync(p,p+'.1');}fs.appendFileSync(p,new Date().toISOString()+' '+String(msg).slice(0,900)+'\n');}catch{}}

  const real=overrides.realSocket||new WebSocket('ws://127.0.0.1:'+port,{perMessageDeflate:false,maxPayload:1024*1024,handshakeTimeout:10000});
  const states=new Map();
  let server,closing=false,realReady=false;
  const pendingReal=[];
  function realSendRaw(raw){
    if(closing) return false;
    if(real.readyState===WebSocket.OPEN){real.send(raw);return true;}
    if(pendingReal.length<128) pendingReal.push(raw);return false;
  }
  function realSend(obj){return realSendRaw(JSON.stringify(obj));}
  function flushReal(){while(pendingReal.length&&real.readyState===WebSocket.OPEN) real.send(pendingReal.shift());}
  function safeJson(raw){try{return JSON.parse(Buffer.isBuffer(raw)?raw.toString():String(raw));}catch{return null;}}
  function workerSend(id,raw){
    const st=states.get(id); if(!st) return;
    if(st.socket&&st.socket.readyState===WebSocket.OPEN){st.socket.send(raw);return;}
    if(st.queue.length<128) st.queue.push(raw); else log(id+' queue full; event dropped');
  }
  function routeHost(raw){
    const msg=safeJson(raw); if(!msg) return;
    if(msg.action&&allowed.has(msg.action)){
      const id=ACTION_WORKER[msg.action]; if(id&&needed.has(id)) workerSend(id,raw); return;
    }
    if(BROADCAST_EVENTS.has(msg.event)) for(const id of needed) workerSend(id,raw);
  }
  function childArgs(spec){return spec.args;}
  function spawnWorker(spec){
    const child=(overrides.spawn||cp.spawn)(spec.file,childArgs(spec),{cwd:spec.cwd,windowsHide:true,stdio:['ignore','ignore','ignore']});
    const st=states.get(spec.id); st.child=child;
    child.on('exit',(code,signal)=>{st.child=null;st.socket=null;if(!closing)log(`${spec.id} worker exited (${code===null?signal:code}).`);});
    child.on('error',e=>log(`${spec.id} worker start failed: ${e.message}`));
  }
  function stopWorkers(){
    for(const st of states.values()){
      try{if(st.socket&&st.socket.readyState===WebSocket.OPEN)st.socket.close();}catch{}
      const child=st.child;if(child){setTimeout(()=>{try{if(!child.killed)child.kill();}catch{}},1500).unref();}
    }
  }
  function shutdown(){if(closing)return;closing=true;try{server&&server.close();}catch{}stopWorkers();try{real.close();}catch{};}

  server=new WebSocketServer({host:'127.0.0.1',port:0,perMessageDeflate:false,maxPayload:1024*1024});
  server.on('connection',socket=>{
    let identified=null;
    socket.on('message',raw=>{
      const msg=safeJson(raw); if(!msg){socket.close(1008,'bad json');return;}
      if(!identified){
        if(msg.event!=='registerPlugin'||typeof msg.uuid!=='string'){socket.close(1008,'registration required');return;}
        for(const [id,st] of states){if(st.spec.uuid===msg.uuid&&!st.socket){identified=id;st.socket=socket;while(st.queue.length&&socket.readyState===WebSocket.OPEN)socket.send(st.queue.shift());log(id+' worker connected.');return;}}
        socket.close(1008,'unknown worker');return;
      }
      if(!OUTPUT_EVENTS.has(msg.event)){log(identified+' ignored output event '+String(msg.event));return;}
      if(msg.action&&(!allowed.has(msg.action)||ACTION_WORKER[msg.action]!==identified))return;
      realSendRaw(Buffer.isBuffer(raw)?raw:Buffer.from(String(raw)));
    });
    socket.on('close',()=>{if(identified){const st=states.get(identified);if(st&&st.socket===socket)st.socket=null;}});
  });
  server.on('error',e=>{log('Internal bridge server: '+e.message);shutdown();});
  server.on('listening',()=>{
    const addr=server.address(),internalPort=addr&&addr.port;if(!internalPort){shutdown();return;}
    for(const id of needed){const token=crypto.randomBytes(16).toString('hex'),spec=workerSpec(id,internalPort,token);states.set(id,{spec,socket:null,child:null,queue:[]});spawnWorker(spec);}
  });
  real.on('open',()=>{realReady=true;realSend({event:registerEvent,uuid:pluginUUID});flushReal();log('Registered unified Taskbar Tiles plugin.');});
  real.on('message',routeHost);
  real.on('close',shutdown);
  real.on('error',e=>{log('Stream Dock socket: '+e.message);shutdown();});
  if(!overrides.noSignals){process.on('SIGTERM',shutdown);process.on('SIGINT',shutdown);process.on('uncaughtException',e=>{log('fatal '+e.message);shutdown();});process.on('unhandledRejection',e=>{log('rejection '+String(e));shutdown();});}
  return {real,server,states,shutdown,routeHost,allowed,needed,get realReady(){return realReady;}};
}
module.exports={start,parseArgs,validate,ACTION_WORKER,workersFor,workerSpec,OUTPUT_EVENTS,BROADCAST_EVENTS};
