'use strict';
try{require('./bridge.cjs').start();}catch(e){console.error(e&&e.message?e.message:String(e));process.exitCode=1;}
