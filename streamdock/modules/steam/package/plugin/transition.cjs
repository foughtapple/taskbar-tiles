'use strict';
const {spawn}=require('node:child_process');

// A native helper owns the same Windows mutex as the game controls. Keeping
// stdin open keeps ownership on one OS thread; EOF also releases after a crash.
function acquireTransition(exe,{signal,onLost=()=>{},spawnChild=spawn,timeoutMs=6000}={}){
 return new Promise((resolve,reject)=>{
  if(signal?.aborted)return reject(new Error('Cancelled before the Steam operation.'));
  const child=spawnChild(exe,['--hold-transition'],{windowsHide:true,stdio:['pipe','pipe','pipe']});
  let acquired=false,releasing=false,settled=false,output='',errorText='';
  const exited=new Promise(done=>{child.once('exit',done);child.once('error',done);});
  const cleanup=()=>{clearTimeout(timer);signal?.removeEventListener('abort',abort);};
  const fail=error=>{if(settled)return;settled=true;cleanup();try{child.stdin.end();child.kill();}catch{}reject(error);};
  const abort=()=>{if(!acquired)fail(new Error('Cancelled before the Steam operation.'));};
  const timer=setTimeout(()=>fail(new Error('Steam operation coordination timed out.')),timeoutMs);
  signal?.addEventListener('abort',abort,{once:true});
  child.stdin.on('error',()=>{});
  child.stderr.on('data',data=>{errorText=(errorText+data.toString()).slice(0,500);});
  child.stdout.on('data',data=>{
   if(acquired||settled)return;
   output+=data.toString();if(output.length>128)return fail(new Error('Invalid Steam operation coordinator response.'));
   if(!output.includes('\n'))return;
   if(output.trim()!=='ACQUIRED')return fail(new Error('Steam operation coordinator did not acquire the lock.'));
   acquired=true;settled=true;cleanup();
   resolve(async()=>{
    if(releasing)return;releasing=true;
    child.stdin.end();
    const stop=setTimeout(()=>{try{child.kill();}catch{}},3000);
    try{await exited;}finally{clearTimeout(stop);}
   });
  });
  child.on('error',e=>{if(!acquired)fail(new Error('Could not coordinate the Steam operation: '+e.message));else if(!releasing)onLost();});
  child.on('exit',()=>{
   if(!acquired)fail(new Error(errorText.trim()||'Another Steam/game operation is running. Finish or cancel it, then retry.'));
   else if(!releasing)onLost();
  });
 });
}
module.exports={acquireTransition};
