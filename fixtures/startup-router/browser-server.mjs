import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
const [packageRoot,artifactRoot]=process.argv.slice(2);
if(!packageRoot || !artifactRoot)throw new Error('packageRoot and artifactRoot required');
const fixture=path.dirname(new URL(import.meta.url).pathname.replace(/^\/(\w:)/,'$1'));
const inputs={'/':path.join(fixture,'browser-tests.html'),'/browser-worker.html':path.join(fixture,'browser-worker.html'),'/startup.mjs':path.join(packageRoot,'Tools~/Startup/HybridStartupHost.mjs'),'/native-record.bin':path.join(artifactRoot,'native-record.bin')};
const outputs={'/js-record.bin':path.join(artifactRoot,'js-record.bin'),'/browser-report.json':path.join(artifactRoot,'browser-report.json')};
http.createServer(async(req,res)=>{
  const url=new URL(req.url,'http://localhost');
  if(req.method==='POST' && outputs[url.pathname]) { const chunks=[]; let length=0; for await (const chunk of req) { length+=chunk.length; if(length>65536){res.writeHead(413);res.end();return;} chunks.push(chunk); } fs.writeFileSync(outputs[url.pathname],Buffer.concat(chunks));res.end('ok');return; }
  if(url.pathname==='/test-loader.js'){res.setHeader('Content-Type','text/javascript');res.end('globalThis.createUnityInstance=async(c,config)=>{globalThis.testCreatedConfig=config;return {testDouble:true};};');return;}
  if(!inputs[url.pathname]){res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',url.pathname.endsWith('.mjs')?'text/javascript':url.pathname.endsWith('.bin')?'application/octet-stream':'text/html');
  res.setHeader('Cache-Control','no-store');fs.createReadStream(inputs[url.pathname]).pipe(res);
}).listen(18746,'127.0.0.1',()=>process.stdout.write('Startup browser tests: http://127.0.0.1:18746/\n'));
