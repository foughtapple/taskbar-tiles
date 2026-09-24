'use strict';
(function(){
 let socket=null,uuid='',action='',started=false,printerDirty=false,pcDirty=false,latest=null,received=false;
 const $=id=>document.getElementById(id);
 function send(o){if(socket&&socket.readyState===1){socket.send(JSON.stringify(o));return true;}return false;}
 function command(cmd,extra){return send({event:'sendToPlugin',action:action,context:uuid,payload:Object.assign({command:cmd},extra||{})});}
 function enable(yes){for(const id of ['savePC','savePrinter','reconnect','diagnostic','forgetPrinter'])$(id).disabled=!yes;}
 function safeDate(v){const d=new Date(v);return Number.isFinite(d.getTime())&&d.getFullYear()>2000?d.toLocaleString():'Not yet received';}
 function percentage(v,ok){return ok?Math.round(v)+'%':'Warming up / unavailable';}
 function normalInterval(v){return [5,10,30].includes(Number(v))?Number(v):5;}
 function fill(p){
  received=true;latest=p;$('runtimeVersion').textContent=p.version||'1.2.1';
  if(p.image&&p.image.startsWith('data:image/svg+xml;'))$('preview').src=p.image;
  if(p.kind==='pc'){
   const s=p.sample||{};if(!pcDirty)$('interval').value=String(normalInterval((p.settings||{}).interval));
   $('reason').textContent=s.reason||'CPU needs two readings, so its first value appears after one interval.';
   const gib=n=>(Number(n||0)/1073741824).toFixed(1);
   $('ramDetail').textContent=s.ramValid?gib(s.usedRAMBytes)+' / '+gib(s.totalRAMBytes)+' GiB RAM in use':'Waiting for Windows physical-memory reading.';
   $('detail').textContent='CPU: '+percentage(s.cpuPercent,s.cpuValid)+'\nRAM: '+percentage(s.ramPercent,s.ramValid)+'\nObserved: '+safeDate(s.observedAt)+'\n\nCPU uses Windows busy-time counters, not a temperature sensor. It can differ from Task Manager\'s frequency-adjusted utilisation.\nRAM means physical memory in use, not virtual/commit memory.';
  }else{
   const c=p.config||{},s=p.printer||{},d=s.data||{};
   if(!printerDirty){$('host').value=c.host||'';$('serial').value=c.serial||'';$('snapshot').checked=c.requestStatusSnapshot!==false;}
   $('codeNote').textContent=c.hasAccessCode?'A Windows-protected LAN code is saved. Leave blank to keep it; enter a new code to replace it.':'Stored encrypted for your Windows user. Never included in diagnostic reports.';
   $('accessCode').placeholder=c.hasAccessCode?'Saved - leave blank to keep':'8 characters from the printer';
   $('reason').textContent=s.reason||'Add the printer details above.';
   const needsTrust=s.phase==='trust'&&!!s.candidateFingerprint;
   $('trustPanel').hidden=!needsTrust;$('trustPrinter').disabled=!needsTrust;
   $('certificate').textContent='Printer: '+(c.host||'')+'\nSerial entered: '+(c.serial||'')+'\nCertificate subject: '+(s.certificateSubject||'')+'\nSHA-256: '+(s.candidateFingerprint||'');
   const val=(x,suffix)=>x===undefined||x===null?'Not received':String(x)+(suffix||'');
   $('detail').textContent='Connection: '+(s.phase||'setup')+'\nConnected: '+safeDate(s.connectedAt)+'\nLast snapshot request: '+safeDate(s.lastSnapshotRequest)+'\nLast keepalive reply: '+safeDate(s.lastPingResponse)+'\nReported state: '+(d.state||'Not received')+'\nLast status: '+safeDate(d.lastReport)+'\nProgress: '+val(d.percent,'%')+'\nRemaining: '+val(d.remainingMinutes,' min')+'\nLayer: '+val(d.layer)+' / '+val(d.totalLayers)+'\nBed: '+val(d.bedCelsius,' C')+'\nNozzle: '+val(d.nozzleCelsius,' C')+'\nPrint error: '+(d.errorCode?'0x'+Number(d.errorCode).toString(16).toUpperCase():'0')+'\nHMS messages: '+(d.hmsMessages||0)+'\nJob: '+(d.jobName||'Not supplied')+'\n\nDONE means the printer reported FINISH. It does not mean the plate has been cleared.\n\nLogs: %LOCALAPPDATA%\\FoughtApple\\DeskStatus\\desk-status.log';
  }
 }
 function connect(port,id,registration,info,actionInfo){
  if(started)return;started=true;
  try{
   uuid=id;const data=typeof actionInfo==='string'?JSON.parse(actionInfo):actionInfo;action=data.action||'';const isPrinter=action.endsWith('.p1s');
   $('heading').textContent=isPrinter?'P1S Print Status':'PC CPU + RAM';$('intro').textContent=isPrinter?'Local print progress, remaining time and attention states.':'Two clear readings. No heavyweight sensor software.';
   $('printerSection').hidden=!isPrinter;$('pcSection').hidden=isPrinter;$('forgetPrinter').hidden=!isPrinter;$('printerHelp').hidden=!isPrinter;$('preview').src=isPrinter?'../images/p1s.png':'../images/pc.png';
   if(!isPrinter)$('interval').value=String(normalInterval((data.payload&&data.payload.settings||{}).interval));
   socket=new WebSocket('ws://127.0.0.1:'+port);
   socket.onopen=()=>{send({event:registration,uuid:uuid});enable(true);command('inspectorOpen');};
   socket.onmessage=e=>{try{const m=JSON.parse(e.data);if(m.event==='sendToPropertyInspector'&&m.payload){const p=m.payload;if(p.type==='status')fill(p);else if(p.type==='notice'){$('message').textContent=p.message||'';$('message').style.color=p.error?'#ff9ead':'#87eac6';}}
    else if(m.event==='didReceiveSettings'&&!isPrinter){pcDirty=false;$('interval').value=String(normalInterval((m.payload&&m.payload.settings||{}).interval));}
   }catch(err){$('message').textContent='Could not read a plugin response: '+err.message;}};
   socket.onerror=()=>{$('reason').textContent='Cannot connect to Stream Dock. Fully exit and reopen the application.';};
   socket.onclose=()=>{enable(false);$('reason').textContent='Stream Dock connection closed.';};
   setTimeout(()=>{if(!received)$('reason').textContent='The plugin has not responded. Fully exit and reopen Stream Dock, then select this display. Check that the action was added from FoughtApple, not Toolbox > Open.';},10000);
  }catch(err){$('reason').textContent='Display setup error: '+err.message;}
 }
 for(const id of ['host','serial','accessCode','snapshot'])$(id).addEventListener('input',()=>{printerDirty=true;});
 $('interval').addEventListener('change',()=>{pcDirty=true;});
 $('savePrinter').onclick=()=>{if(command('savePrinter',{host:$('host').value.trim(),serial:$('serial').value.trim(),accessCode:$('accessCode').value,requestStatusSnapshot:$('snapshot').checked})){printerDirty=false;$('accessCode').value='';$('message').textContent='Saving locally...';}};
 $('savePC').onclick=()=>{if(command('savePC',{interval:Number($('interval').value)})){pcDirty=false;$('message').textContent='Saving refresh rate...';}};
 $('reconnect').onclick=()=>{command('reconnect');};
 $('trustPrinter').onclick=()=>{const s=latest&&latest.printer;if(s&&s.candidateFingerprint){command('trustPrinter',{fingerprint:s.candidateFingerprint});$('trustPrinter').disabled=true;}};
 $('diagnostic').onclick=()=>{command('diagnostic');};
 $('forgetPrinter').onclick=()=>{if(window.confirm('Forget this plugin\'s printer address, protected LAN code and certificate? Your printer, Bambu apps and Steam display are not changed.')){printerDirty=false;command('forgetPrinter');}};
 window.connectElgatoStreamDeckSocket=connect;window.connectStreamDockSocket=connect;
 window.addEventListener('pagehide',()=>command('inspectorClose'));
 window.addEventListener('beforeunload',()=>command('inspectorClose'));
 if(Array.isArray(window.argv)&&window.argv.length>=5)setTimeout(()=>connect.apply(null,window.argv),0);
})();
