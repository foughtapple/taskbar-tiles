'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'..','package');
const manifest=JSON.parse(fs.readFileSync(path.join(root,'manifest.json'),'utf8'));
test('one Taskbar Tiles plugin exposes all final actions',()=>{
 assert.equal(manifest.Name,'Taskbar Tiles');assert.equal(manifest.Category,'Taskbar Tiles');
 assert.equal(manifest.Actions.length,11);
 assert.equal(new Set(manifest.Actions.map(a=>a.UUID)).size,11);
 assert.ok(manifest.Actions.every(a=>a.Icon.startsWith('workers/')));
 assert.ok(manifest.Actions.every(a=>a.PropertyInspectorPath.startsWith('workers/')));
});

test('Audio Control exposes a single manual-state microphone action',()=>{
 const a=manifest.Actions.find(a=>a.UUID==='com.foughtapple.audiocontrol.microphone');
 assert.ok(a);assert.equal(a.Name,'Audio Control - Microphone');
 assert.equal(a.DisableAutomaticStates,true);assert.equal(a.SupportedInMultiActions,false);
 assert.deepEqual(a.Controllers,['Keypad']);
 assert.equal(a.States[0].Image,'workers/audio/images/mic-unavailable.png');
});
