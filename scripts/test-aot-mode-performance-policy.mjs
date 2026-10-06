import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import crypto from 'node:crypto';
const directory=fs.mkdtempSync(path.join(os.tmpdir(),'aot-performance-policy-'));
const metrics=['BenchNative','BenchChanged','BenchVirtual','selection','selectionToEntry','processToEntry','load','loadAndEntry','privateBytes','privateBytesDelta'];
const sample={count:100,p50:10,p95:10,p99:10,mad:0};
function report(comparison) {
  return {passed:true,pairs:100,p99SampleQualified:true,comparison,
    runs:['baseline','candidate'].flatMap((comparisonGroup,g)=>Array.from({length:100},(_,index)=>({comparisonGroup,index,pid:1+g*100+index}))),
    metrics:Object.fromEntries(metrics.map(name=>[name,{baseline:{...sample},candidate:{...sample}}]))};
}
const checker=fileURLToPath(new URL('./check-aot-mode-performance.mjs',import.meta.url));
const cases=[
  ['complete',()=>{},true],
  ['empty-metrics',r=>{r[0].metrics={};},false],
  ['missing-hotspot',r=>{delete r[0].metrics.BenchVirtual;},false],
  ['missing-startup',r=>{delete r[0].metrics.processToEntry;},false],
  ['missing-memory',r=>{delete r[0].metrics.privateBytes;},false],
  ['missing-percentile',r=>{delete r[0].metrics.BenchNative.candidate.p99;},false],
  ['insufficient-metric-samples',r=>{r[0].metrics.BenchNative.candidate.count=99;},false],
  ['null-measurement',r=>{r[0].metrics.load.candidate.p50=null;},false],
  ['legacy-missing-metrics',r=>{r[1].metrics={};},false],
  ['legacy-disclosed-cost',r=>{r[1].metrics.BenchVirtual.candidate.p50=20;},true],
  ['dhe-regression',r=>{r[0].metrics.BenchVirtual.candidate.p50=20;},false],
  ['fixed-regression',r=>{r[2].metrics.BenchVirtual.candidate.p50=20;},false]
];
const results=[];
for(const [name,mutate,expected] of cases){
  const reports=['dhe','legacy','fixed'].map(report);mutate(reports);
  const files=reports.map(r=>{const file=path.join(directory,name+'-'+r.comparison+'.json');fs.writeFileSync(file,JSON.stringify(r));return file;});
  const child=spawnSync(process.execPath,[checker,...files],{encoding:'utf8',windowsHide:true});
  const actual=JSON.parse(child.stdout);
  results.push({name,expected,actual:actual.passed});
  assert.equal(actual.passed,expected,`${name}: ${child.stdout}`);
  assert.equal(child.status,expected?0:1,name);
}
console.log(JSON.stringify({passed:true,cases:results,evidenceDirectory:directory,checkerSha256:crypto.createHash('sha256').update(fs.readFileSync(checker)).digest('hex')},null,2));
