'use strict';
// Field-only editing: preserve unrelated settings, comments, escaping and formatting.
function tree(text){
 if(typeof text!=='string'||text.length>8*1024*1024)throw new Error('Steam configuration is too large.');
 let i=0,count=0;
 function next(){
  for(;;){while(i<text.length&&/\s|\uFEFF/.test(text[i]))i++;
   if(text[i]==='['){const j=text.indexOf(']',i+1);if(j<0)throw new Error('Incomplete configuration condition.');i=j+1;continue;}
   if(text.slice(i,i+2)==='//'){let j=text.indexOf('\n',i+2);i=j<0?text.length:j+1;continue;}
   if(text.slice(i,i+2)==='/*'){let j=text.indexOf('*/',i+2);if(j<0)throw new Error('Incomplete configuration comment.');i=j+2;continue;}break;}
  if(i===text.length)return null;
  if(++count>300000)throw new Error('Configuration has too many tokens.');
  const start=i,c=text[i++];
  if(c==='{'||c==='}')return {kind:c,start,end:i};
  if(c==='"'){let value='';while(i<text.length){let a=text[i++];if(a==='"')return{kind:'word',value,start,end:i};if(a==='\\'&&i<text.length){const b=text[i++];value+=(b==='"'||b==='\\')?b:'\\'+b;}else value+=a;}throw new Error('Incomplete configuration string.');}
  while(i<text.length&&!/[\s{}]/.test(text[i]))i++;
  return {kind:'word',value:text.slice(start,i),start,end:i};
 }
 function block(path,depth){if(depth>48)throw new Error('Configuration nesting is too deep.');let nodes=[];
  for(;;){const key=next();if(!key)return{nodes,close:text.length,ended:false};if(key.kind==='}')return{nodes,close:key.start,ended:true};if(key.kind!=='word')throw new Error('Invalid configuration key.');
   const val=next();if(!val)throw new Error('Incomplete configuration entry.');
   if(val.kind==='{'){const body=block([...path,key.value],depth+1);if(!body.ended)throw new Error('Unclosed configuration section.');nodes.push({key:key.value,path:[...path,key.value],...body});}
   else if(val.kind==='word')nodes.push({key:key.value,path:[...path,key.value],value:val.value,start:val.start,end:val.end});else throw new Error('Invalid configuration value.');
  }
 }
 const root=block([],0);if(root.ended)throw new Error('Extra configuration closing brace.');return root.nodes;
}
function flatten(nodes){return nodes.flatMap(n=>n.nodes?[n,...flatten(n.nodes)]:[n]);}
function setAutoLoginFlag(text,id){
 const nodes=flatten(tree(text));const user=nodes.find(n=>n.nodes&&n.path.length===2&&n.path[0].toLowerCase()==='users'&&n.key===id);
 if(!user)throw new Error('Target account disappeared from Steam\'s saved accounts.');
 const existing=user.nodes.filter(n=>n.key.toLowerCase()==='allowautologin'&&!n.nodes);
 if(existing.length>1)throw new Error('Ambiguous auto-login flag in Steam account file.');
 if(existing.length){const n=existing[0];return text.slice(0,n.start)+'"1"'+text.slice(n.end);}
 const eol=text.includes('\r\n')?'\r\n':'\n';return text.slice(0,user.close)+`\t\t"AllowAutoLogin"\t\t"1"${eol}\t`+text.slice(user.close);
}
function chooser(text,value){
 const found=flatten(tree(text)).filter(n=>n.key.toLowerCase()==='alwaysshowuserchooser'&&!n.nodes);
 if(found.length>1)throw new Error('Multiple Steam account-picker preferences found. No configuration changed.');
 if(!found.length)return {changed:false,previous:null,output:text};
 const n=found[0];if(!['0','1'].includes(n.value))throw new Error('Unexpected Steam account-picker preference.');
 if(!['0','1'].includes(value))throw new Error('Invalid account-picker preference.');
 return {changed:n.value!==value,previous:n.value,output:text.slice(0,n.start)+'"'+value+'"'+text.slice(n.end)};
}
module.exports={tree,flatten,setAutoLoginFlag,chooser};
