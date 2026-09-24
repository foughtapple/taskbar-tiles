'use strict';
const path=require('node:path'),fs=require('node:fs');
const {WebSocket}=require('./vendor.cjs');
const {Steam}=require('./steam.cjs');
const {Controller,ACTION,PLAY,VERSION}=require('./controller.cjs');
const ROOT=path.resolve(__dirname,'..');
function args(argv){const out={};for(let i=0;i<argv.length;i++){if(/^--?[A-Za-z]/.test(argv[i]))out[argv[i].replace(/^-+/,'').toLowerCase()]=argv[++i];}return out;}
function start(argv=process.argv.slice(2),overrides={}){
 const options=args(argv),manifest=JSON.parse(fs.readFileSync(path.join(ROOT,'manifest.json'),'utf8'));
 const allowed=manifest.Actions.map(a=>a.UUID);if(!allowed.length||allowed.some(a=>![ACTION,PLAY].includes(a)))throw new Error('Unsupported Steam action manifest.');
 if(options.validate!==undefined||argv.includes('--validate')){if(!fs.existsSync(path.join(ROOT,'images','steam.png')))throw new Error('Missing Steam image');console.log(JSON.stringify({plugin:ACTION,version:VERSION}));return;}
 const port=Number(options.port),uuid=options.pluginuuid,event=options.registerevent;
 if(!Number.isInteger(port)||port<1||port>65535||typeof uuid!=='string'||!event)throw new Error('Add Steam Smart Switch from Stream Dock Key, not Toolbox Open.');
 const dataDir=path.join(process.env.LOCALAPPDATA||ROOT,'FoughtApple','SteamSmartSwitch');fs.mkdirSync(dataDir,{recursive:true});
 let lastLog='',lastLogAt=0;
 function log(kind,message){const now=Date.now(),line=kind+': '+String(message).slice(0,600);if(line===lastLog&&now-lastLogAt<30000)return;lastLog=line;lastLogAt=now;
  try{const p=path.join(dataDir,'plugin.log');if(fs.existsSync(p)&&fs.statSync(p).size>512*1024){try{fs.unlinkSync(p+'.1');}catch{}fs.renameSync(p,p+'.1');}fs.appendFileSync(p,new Date().toISOString()+' '+line+'\n',{mode:0o600});}catch{}}
 const socket=new WebSocket('ws://127.0.0.1:'+port,{perMessageDeflate:false,maxPayload:1024*1024,handshakeTimeout:10000});
 let closing=false,controller,lastStatus='',lastSaved=0;
 function send(obj){if(!closing&&socket.readyState===WebSocket.OPEN&&socket.bufferedAmount<1024*1024){socket.send(JSON.stringify(obj));return true;}return false;}
 const steam=overrides.steam||new Steam(ROOT,{dataDir});
 controller=new Controller(steam,{send,log,allowed,saveStatus:(status)=>{const signature=JSON.stringify({...status,observedAt:''});if(signature===lastStatus&&Date.now()-lastSaved<60000)return;lastStatus=signature;lastSaved=Date.now();try{fs.writeFileSync(path.join(dataDir,'status.json'),JSON.stringify(status,null,2),{mode:0o600});}catch{}},...overrides.controller});
 const shutdown=async()=>{if(closing)return;closing=true;const deadline=setTimeout(()=>{if(!overrides.noExit)process.exit(0);},10000);deadline.unref();await controller.close();clearTimeout(deadline);socket.terminate();if(!overrides.noExit)process.exit(0);};
 socket.on('open',()=>{send({event,uuid});log('lifecycle','Registered; Steam monitoring runs only while visible.');});
 socket.on('message',b=>{let data;try{data=JSON.parse(b.toString());}catch{log('protocol','Ignored invalid host JSON.');return;}void controller.handle(data).catch(e=>log('event',e.message));});
 socket.on('close',()=>void shutdown());socket.on('error',e=>{log('socket',e.message);void shutdown();});
 if(!overrides.noSignals){process.on('SIGTERM',()=>void shutdown());process.on('SIGINT',()=>void shutdown());process.on('uncaughtException',e=>{log('fatal',e.message);void shutdown();});process.on('unhandledRejection',e=>{log('rejection',String(e));void shutdown();});}
 return {socket,controller,shutdown};
}
module.exports={start,args};if(require.main===module){try{start();}catch(e){console.error(e.message);process.exitCode=1;}}
