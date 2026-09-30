'use strict';
const {test}=require('node:test'),assert=require('node:assert/strict');
const {EventEmitter}=require('node:events');
const {PassThrough,Writable}=require('node:stream');
const {acquireTransition}=require('../package/plugin/transition.cjs');
function fixture(){const child=new EventEmitter();child.stdout=new PassThrough();child.stderr=new PassThrough();child.stdin=new Writable({write(b,e,next){next();},final(next){next();queueMicrotask(()=>child.emit('exit',0));}});child.kill=()=>queueMicrotask(()=>child.emit('exit',1));return child;}
test('lock helper stays alive until release and uses fixed operation',async()=>{const child=fixture();let args;const p=acquireTransition('helper',{spawnChild:(file,a)=>{args=a;return child;}});child.stdout.write('ACQUIRED\n');const release=await p;assert.deepEqual(args,['--hold-transition']);assert.equal(child.stdin.writableEnded,false);await release();assert.equal(child.stdin.writableEnded,true);});
test('busy helper rejects instead of starting another operation',async()=>{const child=fixture(),p=acquireTransition('helper',{spawnChild:()=>child});child.stderr.write('Another Steam/game operation is running.');child.emit('exit',1);await assert.rejects(p,/Another Steam/);});
test('cancelled acquisition closes helper',async()=>{const child=fixture(),stop=new AbortController(),p=acquireTransition('helper',{signal:stop.signal,spawnChild:()=>child});stop.abort();await assert.rejects(p,/Cancelled/);assert.equal(child.stdin.writableEnded,true);});
test('unexpected lock-holder exit cancels its owner',async()=>{const child=fixture();let lost=0;const p=acquireTransition('helper',{spawnChild:()=>child,onLost:()=>lost++});child.stdout.write('ACQUIRED\n');const release=await p;child.emit('exit',1);assert.equal(lost,1);await release();});
