// Recover the completed 300-pair run rejected by the former all-PIDs-distinct
// assertion. Include the preceding 100 pairs and every outlier, never resample.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {processCounts,summarizePerformance} from './aot-mode-performance-statistics.mjs';
const root=path.resolve(process.argv[2]), output=path.resolve(process.argv[3]);
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const hash=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
const assert=(v,m)=>{if(!v)throw Error(m);};
const build=read(path.join(root,'build.json'));
const seedPath=path.join(root,'performance-dhe-100-direct/summary.json'), seed=read(seedPath);
assert(JSON.stringify(seed.build)===JSON.stringify(build),'Build changed since the first sample block');
for(const [key,profile] of Object.entries(build.profiles)) {
  assert(hash(path.join(root,key,'player/GameAssembly.dll'))===profile.gameAssemblySha256,'Binary identity changed');
  assert(hash(path.join(root,key,'player/StartupPlayer_Data/il2cpp_data/Metadata/global-metadata.dat'))===profile.metadataSha256,'Metadata identity changed');
}
for(const side of ['baseline','candidate'])for(const [name,sha] of Object.entries(build.payloads))
  assert(hash(path.join(root,side,'shared',name+'.dll'))===sha,'Workload changed');
const completionLog=path.join(root,'performance-tail-validation.log'), log=fs.readFileSync(completionLog,'utf8');
assert(log.includes('PID reuse: cannot count these as unique processes'),'Unexpected recovery cause');
const runs=[], inputs={[seedPath]:hash(seedPath),[completionLog]:hash(completionLog)};
for(const [directory,pairs,offset] of [['performance-dhe-100-direct',100,0],['performance-dhe-300-tail-validation',300,100]]) {
  const referencePath=path.join(root,directory,'clr.json'), reference=read(referencePath);
  inputs[referencePath]=hash(referencePath);
  assert(fs.readdirSync(path.join(root,directory)).filter(n=>/^\d+-.*-benchmark\.json$/.test(n)).length===pairs*2,'Unexpected raw report count');
  for(let i=0;i<pairs;i++)for(const group of ['baseline','candidate']) {
    const stem=`${i}-${group}-DHE-dhe-benchmark`, file=path.join(root,directory,stem+'.json'), r=read(file);
    inputs[file]=hash(file);
    assert(r.scenario==='benchmark'&&r.mode===1&&r.passed&&!r.diagnostics&&r.differential===0&&r.caseCount===reference.caseCount,'Invalid raw run');
    assert(r.unityResult===236&&r.identityMatches&&JSON.stringify(r.actual)===JSON.stringify(reference.actual),'CLR/Unity differential');
    for(const [field,name] of [['currentSha256','StartupHotfix'],['consumerSha256','StartupConsumer'],['unitySha256','StartupUnityHotfix'],['supportSha256','StartupAotSupport']])assert(r[field]===build.payloads[name],'Loaded payload drift');
    assert(Number.isInteger(r.pid)&&r.pid>0,'Invalid PID');
    if(offset)assert(log.includes(`PASS ${stem}, pid=${r.pid}`),'Missing launch completion');
    else {
      const original=seed.runs.find(x=>x.index===i&&x.comparisonGroup===group);
      assert(original&&Object.entries(r).every(([k,v])=>JSON.stringify(original[k])===JSON.stringify(v)),'Seed/raw report drift');
    }
    runs.push({side:group,profile:'DHE',requestedMode:'dhe',...r,index:i+offset,sourceIndex:i,sourceDirectory:directory,comparisonGroup:group});
  }
}
const processes=processCounts(runs);
const report={...seed,format:'hybridclr.aot-mode.release-gate.v2',runnerSha256:hash(new URL(import.meta.url)),statisticsSha256:hash(new URL('./aot-mode-performance-statistics.mjs',import.meta.url)),recovery:{reason:'Windows recycled exited numeric PIDs; original collector rejected the completed 300 pairs at its final global-uniqueness assertion.',allSamplesRetained:true,includesPrior100PairTailFailure:true,originalCollectorSha256:seed.runnerSha256,provenance:'First block has its original build-bound summary. Second block is reconstructed from all 600 raw reports, the sequential collector completion log, and unchanged binaries/workloads in the same build root. Parent launch timestamps were not recorded by the old collector.',inputs},pairs:400,independentProcesses:runs.length,processCounts:processes,p99SampleQualified:Object.values(processes).every(g=>g.uniquePids>=100),metrics:summarizePerformance(runs),runs};
fs.mkdirSync(path.dirname(output),{recursive:true});
fs.writeFileSync(output,JSON.stringify(report,null,2),{encoding:'utf8',flag:'wx'});
console.log(JSON.stringify({output,processes,metrics:report.metrics},null,2));
