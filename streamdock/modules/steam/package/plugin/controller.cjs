'use strict';
const {decide,cleanSettings,pairFor}=require('./model.cjs');
const ACTION='com.foughtapple.steamsmarttoggle.toggle',PLAY='com.foughtapple.steamsmarttoggle.play',VERSION='1.1.0';
class Controller{
 constructor(steam,{send=()=>{},log=()=>{},saveStatus=()=>{},now=Date.now,timers=globalThis,interval=5000,allowed=[ACTION,PLAY]}={}){
  Object.assign(this,{steam,send,log,saveStatus,now,timers,interval});this.allowed=new Set(allowed);this.contexts=new Map();this.inspectors=new Set();this.current=null;this.accounts=[];this.lastSample=0;this.stable='';this.candidate='';this.hits=0;this.candidateAt=0;this.busy=false;this.phase='';this.message='Waiting for a visible button.';this.timer=null;this.readPromise=null;this.closed=false;this.lastPress=-Infinity;this.renderID=0;this.generation=0;
 }
 async observe(){if(this.readPromise)return this.readPromise;
  this.readPromise=(async()=>{if(!this.busy&&this.steam.recoverChooser)await this.steam.recoverChooser();const s=await this.steam.snapshot();const a=await this.steam.accounts(s.steamPath);this.current=s;this.accounts=a;this.lastSample=this.now();
   const key=s.running&&s.loggedIn&&s.steamId?(s.steamId+'|'+s.pid+'|'+s.processStart):'';
   if(!key){this.stable='';this.candidate='';this.hits=0;}
   else if(key!==this.candidate){this.candidate=key;this.hits=1;this.candidateAt=this.now();this.stable='';}
   else if(this.now()-this.candidateAt>=750){this.hits++;this.candidateAt=this.now();if(this.hits>=2)this.stable=s.steamId;}
   if(!this.busy){this.phase='';this.message=s.reason||'Steam session checked.';}
   return s;
  })();try{return await this.readPromise;}finally{this.readPromise=null;}
 }
 schedule(){if(this.closed||!this.contexts.size||this.timer!==null)return;this.timer=this.timers.setTimeout(()=>{this.timer=null;void this.tick();},this.interval);}
 async tick(){const generation=this.generation;if(this.closed||!this.contexts.size)return;
  try{await this.observe();if(this.closed||generation!==this.generation)return;this.repaint();this.report();}
  catch(e){this.stable='';this.current=null;if(!this.busy)this.message=e.message;this.log('observation',e.message);this.repaint();this.report();}
  finally{this.schedule();}
 }
 repaint(){for(const ctx of this.contexts.keys())void this.render(ctx).catch(e=>this.log('render',e.message));}
 async render(ctx){const item=this.contexts.get(ctx);if(!item)return;const serial=++this.renderID;item.renderID=serial;
  const s=this.current||{},plan=decide(s,this.accounts,item.settings);
  const current=plan.kind==='switch'&&this.stable===plan.current?.id&&this.now()-this.lastSample<=15000?plan.current:null;
  let title=current&&item.settings.showName?current.name:'';
  if(this.busy)title=this.phase==='wait-game'?'CLOSE GAME':this.phase==='closing'?'CLOSING':this.phase==='opening'?'OPENING':'SWITCHING';
  else if(plan.kind==='configure')title='SET ACCOUNTS';
  else if(item.action===PLAY&&current)title=(title?title+'\n':'')+'SWITCH + RL';
  const image=await this.steam.image(current,s,item.settings).catch(()=>this.steam.staticImage('steam'));
  if(this.closed||this.contexts.get(ctx)!==item||item.renderID!==serial)return;
  item.shownId=current?.id||'';
  if(item.lastImage!==image){if(this.send({event:'setImage',context:ctx,payload:{target:0,image}})!==false)item.lastImage=image;}
  if(item.lastTitle!==title){if(this.send({event:'setTitle',context:ctx,payload:{target:0,title}})!==false)item.lastTitle=title;}
 }
 status(ctx){const item=this.contexts.get(ctx),settings=item?.settings||{};const pair=pairFor(this.accounts,settings),s=this.current||{};
  return {event:'status',version:VERSION,busy:this.busy,phase:this.phase,message:this.message,visible:this.contexts.size>0,currentId:this.stable,detectedId:s.steamId||'',currentName:this.accounts.find(a=>a.id===this.stable)?.name||'',accounts:this.accounts,settings:cleanSettings(settings),firstId:pair.first?.id||'',secondId:pair.second?.id||'',pairError:pair.error||'',automatic:pair.automatic||false,intervalSeconds:this.interval/1000};
 }
 report(){for(const ctx of this.inspectors){const item=this.contexts.get(ctx);if(!item)continue;const status=this.status(ctx),serialized=JSON.stringify(status);if(item.lastPI===serialized)continue;item.lastPI=serialized;this.send({event:'sendToPropertyInspector',action:item.action,context:ctx,payload:status});}
  this.saveStatus({version:VERSION,observedAt:new Date(this.now()).toISOString(),message:this.message,phase:this.phase,visible:this.contexts.size,busy:this.busy,steamRunning:!!this.current?.running,confirmedAccountId:this.stable});
 }
 async handle(data){if(this.closed||!data)return;if(data.action&&!this.allowed.has(data.action))return;const ctx=data.context;
  switch(data.event){
   case 'willAppear':if(!this.allowed.has(data.action)||typeof ctx!=='string')return;this.contexts.set(ctx,{action:data.action,settings:cleanSettings(data.payload?.settings),device:data.device,lastImage:null,lastTitle:null});void this.render(ctx).catch(()=>{});await this.tick();return;
   case 'willDisappear':this.contexts.delete(ctx);this.inspectors.delete(ctx);this.generation++;if(!this.contexts.size&&this.timer!==null){this.timers.clearTimeout(this.timer);this.timer=null;}return;
   case 'deviceDidDisconnect':for(const [c,item]of this.contexts)if(item.device===data.device){this.contexts.delete(c);this.inspectors.delete(c);}this.generation++;if(!this.contexts.size&&this.timer!==null){this.timers.clearTimeout(this.timer);this.timer=null;}return;
   case 'didReceiveSettings':if(this.contexts.has(ctx)){this.contexts.get(ctx).settings=cleanSettings(data.payload?.settings);this.repaint();this.report();}return;
   case 'propertyInspectorDidAppear':if(!this.contexts.has(ctx))return;this.inspectors.add(ctx);this.contexts.get(ctx).lastPI='';this.report();return;
   case 'propertyInspectorDidDisappear':this.inspectors.delete(ctx);return;
   case 'sendToPlugin':{
    if(!this.contexts.has(ctx))return;const p=data.payload||{};
    if(p.event==='hello'||p.event==='refresh'){this.inspectors.add(ctx);this.contexts.get(ctx).lastPI='';try{await this.observe();}catch(e){this.message=e.message;}this.repaint();this.report();}
    if(p.event==='save'){
     if(this.busy){this.message='Wait until the current operation finishes before changing the account pair.';this.report();return;}
     const settings=cleanSettings(p.settings);this.contexts.get(ctx).settings=settings;this.send({event:'setSettings',context:ctx,payload:settings});this.repaint();this.report();
    }
    if(p.event==='cancel'){this.abort?.abort();this.message='Cancel requested. Any already-started Steam launch will be left running.';this.report();}
    return;
   }
   case 'keyDown':if(this.contexts.get(ctx)?.action===data.action)await this.press(ctx);return;
   case 'keyUp':return;
   case 'systemDidWakeUp':if(this.contexts.size)await this.tick();return;
  }
 }
 async press(ctx){const item=this.contexts.get(ctx);if(!item||this.busy||this.now()-this.lastPress<1500)return;this.lastPress=this.now();this.busy=true;this.abort=new AbortController();this.phase='checking';this.message='Checking the active Steam account.';this.repaint();this.report();
  const wasSteam=!item.shownId;
  this.activeTask=(async()=>{try{
   const s=await this.observe();const plan=decide(s,this.accounts,item.settings);
   if(wasSteam||plan.kind==='open'){
    this.phase='opening';this.message='Opening Steam. No account selection is changed.';this.repaint();this.report();await this.steam.open();await this.steam.wait(3000,this.abort.signal);return;
   }
   if(plan.kind==='configure')throw new Error(plan.pair.error);
   if(!plan.target.remembered)throw new Error('The other account must be remembered by Steam. Sign in through Steam once first.');
   if(item.action===PLAY){this.phase='closing';this.message='Closing Rocket League normally before switching.';this.repaint();this.report();await this.steam.closeRocket(this.abort.signal);}
   await this.steam.switchTo(plan.target,s,{signal:this.abort.signal,onStage:(phase,message)=>{this.phase=phase;this.message=message;this.repaint();this.report();}});
   if(item.action===PLAY){if(this.abort.signal.aborted)throw new Error('Cancelled before launching Rocket League.');await this.steam.launchRocket(plan.target.id);}
   if(this.contexts.size)await this.observe();
  }catch(e){this.message=e.message;this.phase='attention';this.log('action',e.message);if(!this.closed&&this.contexts.has(ctx))this.send({event:'showAlert',context:ctx});}
  finally{this.busy=false;this.abort=null;this.repaint();this.report();this.schedule();}})();
  try{await this.activeTask;}finally{this.activeTask=null;}
 }
 async close(){this.closed=true;this.generation++;if(this.timer!==null)this.timers.clearTimeout(this.timer);this.timer=null;this.contexts.clear();this.inspectors.clear();this.abort?.abort();if(this.activeTask)await this.activeTask.catch(()=>{});}
}
module.exports={Controller,ACTION,PLAY,VERSION};
