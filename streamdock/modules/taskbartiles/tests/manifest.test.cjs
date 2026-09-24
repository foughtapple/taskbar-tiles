'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'..','package');
const manifest=JSON.parse(fs.readFileSync(path.join(root,'manifest.json'),'utf8'));
test('one Taskbar Tiles plugin exposes all final actions',()=>{
 assert.equal(manifest.Name,'Taskbar Tiles');assert.equal(manifest.Category,'Taskbar Tiles');
 assert.equal(manifest.Actions.length,10);
 assert.equal(new Set(manifest.Actions.map(a=>a.UUID)).size,10);
 assert.ok(manifest.Actions.every(a=>a.Icon.startsWith('workers/')));
 assert.ok(manifest.Actions.every(a=>a.PropertyInspectorPath.startsWith('workers/')));
});
