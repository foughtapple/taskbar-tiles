'use strict';
const fs=require('node:fs/promises'),fss=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {execFile,spawn}=require('node:child_process');
const {accountsFromText,isID}=require('./model.cjs');
const {chooser,setAutoLoginFlag}=require('./keyvalues.cjs');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
function sleep(ms,signal){return new Promise((resolve,reject)=>{if(signal?.aborted)return reject(new Error('Cancelled.'));const t=setTimeout(done,ms);function done(){signal?.removeEventListener('abort',abort);resolve();}function abort(){clearTimeout(t);signal?.removeEventListener('abort',abort);reject(new Error('Cancelled.'));}signal?.addEventListener('abort',abort,{once:true});});}
function run(exe,args,options={}){return new Promise((resolve,reject)=>execFile(exe,args,{encoding:'utf8',windowsHide:true,timeout:6000,maxBuffer:512*1024,...options},(e,stdout,stderr)=>e?reject(new Error(String(stderr||e.message).slice(0,500))):resolve(stdout)));}
function launchExe(exe,args){return new Promise((resolve,reject)=>{const c=spawn(exe,args,{shell:false,windowsHide:true,stdio:'ignore'});c.once('error',reject);c.once('spawn',()=>{c.unref();resolve();});});}
async function textFile(p,limit=8*1024*1024){const st=await fs.stat(p);if(!st.isFile()||st.size>limit)throw new Error('Unexpected Steam configuration file size.');return fs.readFile(p,'utf8');}
async function replaceFile(p,expected,updated){
 if(updated===expected)return;
 const tmp=p+'.foughtapple-'+crypto.randomBytes(6).toString('hex')+'.tmp';
 try{await fs.writeFile(tmp,updated,{flag:'wx',mode:0o600});if(await fs.readFile(p,'utf8')!==expected)throw new Error('Steam settings changed during the switch. No further files were changed.');await fs.rename(tmp,p);}
 finally{await fs.unlink(tmp).catch(()=>{});}
}
function decodeXML(s){return s.replace(/^\s*<!\[CDATA\[([\s\S]*)\]\]>\s*$/,'$1').replace(/&amp;/g,'&').replace(/&quot;/g,'"').replace(/&lt;/g,'<').replace(/&gt;/g,'>').trim();}
function avatarURL(xml){for(const tag of ['avatarFull','avatarMedium','avatarIcon']){const m=xml.match(new RegExp('<'+tag+'>([\\s\\S]*?)</'+tag+'>','i'));if(m){try{const u=new URL(decodeXML(m[1]));if(u.protocol==='https:'&&(/(^|\.)steamstatic\.com$/.test(u.hostname)||/(^|\.)steamcommunity\.com$/.test(u.hostname)||u.hostname==='steamcdn-a.akamaihd.net'))return u.href;}catch{}}}return '';}
function imageType(b){if(b.length>8&&b.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10])))return 'image/png';if(b.length>3&&b[0]===255&&b[1]===216&&b[2]===255)return'image/jpeg';return '';}
async function fetchBounded(url,max){
 const response=await fetch(url,{signal:AbortSignal.timeout(7000),redirect:'error',headers:{'User-Agent':'FoughtApple-Steam-Smart-Switch/1.1'}});
 if(!response.ok)throw new Error('Avatar request was not available.');
 const finalURL=new URL(response.url||url);if(finalURL.protocol!=='https:'||!(/(^|\.)steamcommunity\.com$/.test(finalURL.hostname)||/(^|\.)steamstatic\.com$/.test(finalURL.hostname)||finalURL.hostname==='steamcdn-a.akamaihd.net')){await response.body?.cancel();throw new Error('Unexpected avatar redirect.');}
 const chunks=[];let length=0;const reader=response.body.getReader();try{for(;;){const x=await reader.read();if(x.done)break;length+=x.value.length;if(length>max)throw new Error('Avatar response exceeded its size limit.');chunks.push(Buffer.from(x.value));}}finally{await reader.cancel().catch(()=>{});}return Buffer.concat(chunks);
}
class Steam {
 constructor(root,{dataDir,probe,launch,wait,clock}={}){
  this.now=clock||Date.now;this.root=root;this.dataDir=dataDir||path.join(process.env.LOCALAPPDATA||root,'FoughtApple','SteamSmartSwitch');
  this.probeExe=path.join(root,'plugin','SteamSession.exe');this.probe=probe||((...args)=>run(this.probeExe,args));this.launch=launch||launchExe;this.wait=wait||sleep;
  this.avatars=new Map();this.usersCache=null;this.lastPath='';this.journalPath=path.join(this.dataDir,'pending-chooser.json');
 }
 async snapshot(){const s=JSON.parse(await this.probe('--once'));if(s.steamPath)this.lastPath=s.steamPath;return s;}
 async accounts(steamPath){if(!steamPath)return [];const p=path.join(steamPath,'config','loginusers.vdf');try{const st=await fs.stat(p);const key=p+'|'+st.mtimeMs+'|'+st.size;if(this.usersCache?.key===key)return this.usersCache.items;const items=accountsFromText(await textFile(p,2*1024*1024));this.usersCache={key,items};return items;}catch(e){if(e.code==='ENOENT')return [];throw e;}}
 async open(){await this.probe('--open');}
 async closeRocket(signal){await this.probe('--close-rocket');const until=this.now()+90000;for(;;){if(signal?.aborted)throw new Error('Cancelled.');const s=JSON.parse(await this.probe('--rocket-status'));if(!s.running)return;if(this.now()>until)throw new Error('Rocket League has not closed. Finish its exit prompt.');await this.wait(1000,signal);}}
 async launchRocket(expected){const s=await this.snapshot();if(!s.loggedIn||s.steamId!==expected)throw new Error('Account not confirmed; Rocket League was not launched.');await this.probe('--launch-rocket');}
 async image(account,s,settings){
  if(!account)return this.staticImage('steam');
  const key=account.id+'|'+(settings.webAvatars!==false);const existing=this.avatars.get(key);
  if(existing&&this.now()-existing.at<30000)return existing.promise;
  const promise=this.loadAvatar(account,s,settings).catch(()=>this.fallback(account));this.avatars.set(key,{at:this.now(),promise});
  if(this.avatars.size>12)this.avatars.delete(this.avatars.keys().next().value);return promise;
 }
 staticImage(name){if(!this.static)this.static={};if(!this.static[name]){let p=path.join(this.root,'images',name+'.png'),mime='image/png';if(!fss.existsSync(p)){p=path.join(this.root,'images','steam.svg');mime='image/svg+xml';}this.static[name]='data:'+mime+';base64,'+fss.readFileSync(p).toString('base64');}return this.static[name];}
 fallback(account){const name=account.login.toLowerCase();return this.staticImage(name==='foughtapple'?'apple':name==='foughtbanana'?'banana':'steam');}
 async loadAvatar(account,s,settings){
  const cache=path.join(this.dataDir,'avatars',account.id+'.img');
  const candidates=['png','jpg','jpeg'].map(ext=>path.join(s.steamPath,'config','avatarcache',account.id+'.'+ext));candidates.push(cache);
  for(const p of candidates){try{const st=await fs.stat(p);if(st.size>2*1024*1024||!st.isFile())continue;const b=await fs.readFile(p),mime=imageType(b);if(mime)return 'data:'+mime+';base64,'+b.toString('base64');}catch{}}
  if(settings.webAvatars===false)return this.fallback(account);
  if(this.fetchFailed&&this.now()-(this.fetchFailed.get(account.id)||0)<300000)return this.fallback(account);
  try{const xml=(await fetchBounded('https://steamcommunity.com/profiles/'+account.id+'?xml=1',512*1024)).toString('utf8');const url=avatarURL(xml);if(!url)throw new Error('Avatar is not exposed.');const b=await fetchBounded(url,2*1024*1024),mime=imageType(b);if(!mime)throw new Error('Unexpected avatar format.');await fs.mkdir(path.dirname(cache),{recursive:true});await fs.writeFile(cache,b,{mode:0o600});return 'data:'+mime+';base64,'+b.toString('base64');}
  catch{if(!this.fetchFailed)this.fetchFailed=new Map();this.fetchFailed.set(account.id,this.now());if(this.fetchFailed.size>12)this.fetchFailed.delete(this.fetchFailed.keys().next().value);return this.fallback(account);}
 }
 async recoverChooser(force=false){
  let j;try{j=JSON.parse(await textFile(this.journalPath,65536));}catch(e){if(e.code==='ENOENT')return;throw new Error('The previous switch recovery file is unreadable. See the plugin status log.');}
  if(j.version!==1||j.previous!=='1'||!isID(j.targetId)||!path.isAbsolute(j.steamPath))throw new Error('Invalid switch recovery record. No Steam settings changed.');
  const s=await this.snapshot();if(s.steamPath&&path.resolve(s.steamPath).toLowerCase()!==path.resolve(j.steamPath).toLowerCase())throw new Error('Steam location differs from the previous switch recovery record.');
  if(!force&&!(s.loggedIn&&s.steamId===j.targetId)&&this.now()-j.created<180000)return;
  const p=path.join(j.steamPath,'config','config.vdf'),raw=await textFile(p),edit=chooser(raw,'1');
  if(edit.previous==='0')await replaceFile(p,raw,edit.output);
  await fs.unlink(this.journalPath).catch(()=>{});
 }
 async switchTo(target,initial,{signal,onStage=()=>{}}={}){
  if(!target||!isID(target.id)||!target.remembered)throw new Error('Sign into the other account in Steam with Remember me enabled first.');
  await fs.mkdir(this.dataDir,{recursive:true});
  const lockpath=path.join(this.dataDir,'switch.lock');let lock;
  try{lock=await fs.open(lockpath,'wx',0o600);}catch(e){if(e.code!=='EEXIST')throw e;
   let old;try{old=JSON.parse(await fs.readFile(lockpath,'utf8'));}catch{throw new Error('Another switch lock exists. Close Stream Dock and remove SteamSmartSwitch\\switch.lock only if no switch is running.');}
   let alive=true;try{process.kill(Number(old.pid),0);}catch(x){if(x.code==='ESRCH')alive=false;}
   if(alive)throw new Error('A Steam switch is already running.');await fs.unlink(lockpath);lock=await fs.open(lockpath,'wx',0o600);
  }
  let txn=null,closedByUs=false,launched=false;
  try{
   await lock.writeFile(JSON.stringify({pid:process.pid,created:this.now()}));
   await this.recoverChooser(true);let s=await this.snapshot();const gameUntil=this.now()+15*60*1000;
   if(s.loggedIn&&s.steamId!==initial.steamId)throw new Error('Steam account changed before switching. Press again after the icon updates.');
   while(s.running){if(signal?.aborted)throw new Error('Cancelled before switching.');if(!s.gamesKnown)throw new Error('Running Steam games could not be checked. Close games and Steam manually, then retry.');if(!(s.runningApps||[]).length)break;
    onStage('wait-game','Close the running Steam game to continue.');if(this.now()>gameUntil)throw new Error('Timed out waiting for a Steam game to close.');await this.wait(2000,signal);s=await this.snapshot();
    if(s.loggedIn&&s.steamId!==initial.steamId)throw new Error('Account changed while waiting for the game. No switch was made.');
   }
   const steamPath=s.steamPath||initial.steamPath;if(!steamPath)throw new Error('Steam installation was not found.');
   if(s.running){onStage('closing','Waiting for Steam to exit normally. Finish any Steam Cloud or confirmation prompt.');await this.launch(path.join(steamPath,'steam.exe'),['-shutdown']);closedByUs=true;const until=this.now()+90000;
    do{if(signal?.aborted)throw new Error('Cancelled.');await this.wait(1000,signal);s=await this.snapshot();if(this.now()>until&&s.running)throw new Error('Steam has not exited. Complete its prompt or close it normally. No account files were changed.');}while(s.running);
   }
   if(signal?.aborted)throw new Error('Cancelled before account changes.');
   onStage('selecting','Selecting the other remembered Steam account.');
   const userPath=path.join(steamPath,'config','loginusers.vdf'),configPath=path.join(steamPath,'config','config.vdf');
   const users=await textFile(userPath,2*1024*1024),saved=accountsFromText(users).find(a=>a.id===target.id&&a.login===target.login&&a.remembered);if(!saved)throw new Error('The other account is no longer remembered by Steam.');
   const config=await textFile(configPath),userEdit=setAutoLoginFlag(users,target.id),configEdit=chooser(config,'0');
   const check=await this.snapshot();if(check.running)throw new Error('Steam restarted before the account could be selected. No further changes were made.');
   const backup=path.join(this.dataDir,'Backups',new Date().toISOString().replace(/[:.]/g,'-')+'-'+crypto.randomBytes(3).toString('hex'));await fs.mkdir(backup,{recursive:true});
   await fs.writeFile(path.join(backup,'loginusers.vdf'),users,{mode:0o600});
   await fs.writeFile(path.join(backup,'preferences.json'),JSON.stringify({autoLogin:check.autoLogin,autoLoginPresent:check.autoLoginPresent,registryView:check.registryView,chooser:configEdit.previous},null,2),{mode:0o600});
   txn={userPath,configPath,users,userEdit,config,configEdit,check};
   if(configEdit.previous==='1')await fs.writeFile(this.journalPath,JSON.stringify({version:1,steamPath,previous:'1',targetId:target.id,created:this.now()}),{mode:0o600});
   await replaceFile(userPath,users,userEdit);await replaceFile(configPath,config,configEdit.output);
   await this.probe('--autologin',target.login,String(check.registryView||0));
   onStage('sign-in','Steam is signing in. Complete Steam Guard in Steam if requested.');await this.launch(path.join(steamPath,'steam.exe'),[]);launched=true;
   let hits=0,lastStart='',until=this.now()+180000;
   while(this.now()<until){
    if(signal?.aborted)break;
    await this.wait(2000,signal);s=await this.snapshot();
    if(s.running&&s.loggedIn&&s.steamId===target.id){const signature=s.pid+'|'+s.processStart;hits=signature===lastStart?hits+1:1;lastStart=signature;if(hits>=2){onStage('complete','Steam reports the other account as active.');return s;}}else{hits=0;lastStart='';}
   }
   if(signal?.aborted)throw new Error('Stopped waiting. Steam was opened; the icon will follow its actual account.');
   throw new Error('Steam has not confirmed the selected account yet. Complete its sign-in prompt; the icon will update when it is active.');
  }catch(e){
   if(txn&&!launched){
    const safe=await this.snapshot().catch(()=>null);
    if(safe&&!safe.running){
     for(const [p,modified,original]of [[txn.userPath,txn.userEdit,txn.users],[txn.configPath,txn.configEdit.output,txn.config]]){try{const now=await textFile(p);if(now===modified)await replaceFile(p,now,original);}catch{}}
     try{if(txn.check.autoLoginPresent)await this.probe('--autologin',txn.check.autoLogin,String(txn.check.registryView||0));else await this.probe('--clear-autologin',String(txn.check.registryView||0));}catch{}
    }
   }
   if(closedByUs&&!launched)await this.open().catch(()=>{});
   throw e;
  }finally{
   await this.recoverChooser(true).catch(e=>onStage('warning','Steam opened, but its account-picker preference needs attention: '+e.message));
   await lock.close().catch(()=>{});await fs.unlink(lockpath).catch(()=>{});
   try{const b=path.join(this.dataDir,'Backups'),dirs=(await fs.readdir(b,{withFileTypes:true})).filter(x=>x.isDirectory()).map(x=>x.name).sort();for(const n of dirs.slice(0,-5))await fs.rm(path.join(b,n),{recursive:true,force:true});}catch{}
  }
 }
}
module.exports={Steam,sleep,run,launchExe,replaceFile,avatarURL,imageType,hash};
