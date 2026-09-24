'use strict';
const {parse}=require('./vendor.cjs');
const BASE=76561197960265728n;
function isID(s){try{return /^\d{17}$/.test(s)&&BigInt(s)>BASE&&BigInt(s)<=BASE+4294967295n;}catch{return false;}}
function accountsFromText(text){
 if(typeof text!=='string'||text.length>2*1024*1024)throw new Error('Saved Steam account list is too large or unreadable.');
 let doc;try{doc=parse(text.replace(/^\uFEFF/,''),{types:false,arrayify:false});}catch{throw new Error('Steam is updating its saved account list, or its format is unreadable. Try again shortly.');}
 const users=doc.users||doc.Users||doc;
 return Object.entries(users).filter(([id,u])=>isID(id)&&u&&typeof u==='object'&&!Array.isArray(u)&&typeof u.AccountName==='string'&&u.AccountName.length>0&&u.AccountName.length<=128&&!/[\0\r\n]/.test(u.AccountName))
 .map(([id,u])=>({id,login:u.AccountName,name:String(u.PersonaName||u.AccountName).slice(0,160),remembered:String(u.RememberPassword)==='1'}));
}
const normal=s=>String(s||'').toLowerCase().replace(/[^a-z0-9]/g,'');
function pairFor(accounts,settings={}){
 if(settings.firstId||settings.secondId){
  const first=accounts.find(x=>x.id===settings.firstId),second=accounts.find(x=>x.id===settings.secondId);
  if(first&&second&&first.id!==second.id)return {first,second,automatic:false};
  return {error:'Select two different saved accounts in this button\'s settings.'};
 }
 const remembered=accounts.filter(x=>x.remembered);
 const apple=remembered.filter(x=>normal(x.login)==='foughtapple');
 const banana=remembered.filter(x=>normal(x.login)==='foughtbanana');
 if(apple.length===1&&banana.length===1)return {first:apple[0],second:banana[0],automatic:true};
 if(remembered.length===2)return {first:remembered[0],second:remembered[1],automatic:true};
 return {error:remembered.length<2?'Sign into both accounts in Steam with Remember me enabled, then refresh accounts.':'More than two accounts are saved. Choose the two accounts in this button\'s settings.'};
}
function decide(snapshot,accounts,settings){
 const pair=pairFor(accounts,settings);
 const current=snapshot?.running&&snapshot?.loggedIn&&isID(snapshot.steamId)?accounts.find(x=>x.id===snapshot.steamId):undefined;
 if(!current)return {kind:'open',pair,current:null};
 if(pair.error)return {kind:'configure',pair,current};
 if(current.id===pair.first.id)return {kind:'switch',pair,current,target:pair.second};
 if(current.id===pair.second.id)return {kind:'switch',pair,current,target:pair.first};
 return {kind:'open',pair,current:null};
}
function cleanSettings(value={}){
 return {firstId:isID(String(value.firstId||''))?String(value.firstId):'',secondId:isID(String(value.secondId||''))?String(value.secondId):'',showName:value.showName!==false,webAvatars:value.webAvatars!==false};
}
module.exports={isID,accountsFromText,pairFor,decide,cleanSettings};
