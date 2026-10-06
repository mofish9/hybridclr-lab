import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {spawnSync} from 'node:child_process';

const root = path.resolve(process.argv[2] || 'artifacts/ar1');
const pairs = Number(process.argv[3] || 0);
if (!Number.isInteger(pairs) || pairs < 0) throw Error('Invalid pair count');
const build = JSON.parse(fs.readFileSync(path.join(root, 'build.json')));
const label = process.argv[4] || '';
if (label && !/^[a-z0-9-]+$/.test(label)) throw Error('Invalid run label');
const output = path.join(root, (pairs ? `performance-${pairs}` : 'correctness') + (label ? '-' + label : ''));
fs.mkdirSync(output); // Preserve existing evidence rather than overwrite it.
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const assert = (test, message) => { if (!test) throw Error(message); };
for (const [key, profile] of Object.entries(build.profiles)) {
  assert(hash(path.join(root, key, 'player/GameAssembly.dll')) === profile.gameAssemblySha256, `Runtime identity drift: ${key}`);
  assert(hash(path.join(root, key, 'player/StartupPlayer_Data/il2cpp_data/Metadata/global-metadata.dat')) === profile.metadataSha256, `Metadata identity drift: ${key}`);
}
for (const side of ['candidate', 'baseline'])
  for (const [name, expected] of Object.entries(build.payloads))
    assert(hash(path.join(root, side, 'shared', name + '.dll')) === expected, `Payload identity drift: ${side}/${name}`);

const referencePath = path.join(output, 'clr.json');
const referenceRun = spawnSync('C:/Program Files/dotnet/dotnet.exe', [path.join(root, 'reference/Reference.dll'), path.join(root, 'candidate'), referencePath], {encoding:'utf8', windowsHide:true, timeout:120000});
assert(referenceRun.status === 0, `CLR reference failed: ${referenceRun.stderr} ${referenceRun.stdout}`);
const reference = read(referencePath);
const runs = [];
function run(side, profile, mode, scenario, index) {
  const name = `${index}-${side}-${profile}-${mode}-${scenario}`;
  const report = path.join(output, name + '.json');
  const exe = path.join(root, side, profile, 'player/StartupPlayer.exe');
  const child = spawnSync(exe, ['-batchmode','-nographics','-scenario',scenario,'-mode',mode,'-startupPairRoot',path.join(root,side),'-startupReport',report,'-logFile',path.join(output,name+'.log')], {encoding:'utf8', windowsHide:true, timeout:120000});
  assert(child.status === 0 && fs.existsSync(report), `Player failed: ${name}; exit=${child.status}; ${child.error || ''}`);
  const result = read(report);
  assert(result.passed && result.differential === 0 && result.caseCount === reference.caseCount, `Correctness failed: ${name}: ${result.error}`);
  assert(JSON.stringify(result.actual) === JSON.stringify(reference.actual), `CLR differential: ${name}`);
  for (const [field, assembly] of [['currentSha256','StartupHotfix'],['consumerSha256','StartupConsumer'],['unitySha256','StartupUnityHotfix'],['supportSha256','StartupAotSupport']])
    assert(result[field] === build.payloads[assembly], `Loaded payload mismatch: ${name}/${field}`);
  assert(!result.diagnostics && result.unityResult === 236 && result.identityMatches, `Production/Unity boundary failed: ${name}`);
  if (scenario === 'concurrent') assert(result.concurrentSuccesses === 1 && result.concurrentResults.filter(n=>n===1).length === 11, 'Concurrent selection failed');
  else assert(result.mode === (mode === 'dhe' ? 1 : 2), `Wrong mode: ${name}`);
  runs.push({side, profile, requestedMode:mode, index, ...result});
  console.log(`PASS ${name}, pid=${result.pid}`);
}
if (!pairs) {
  const failures=[];
  for (const args of [
    ['candidate','DHE','dhe','correctness',0],
    ['candidate','DHE','legacy','correctness',1],
    ['candidate','DHE','dhe','concurrent',2],
    ['baseline','DHE','dhe','correctness',3],
    ['candidate','LegacyInterpreter','legacy','correctness',4]
  ]) { try { run(...args); } catch(error) { failures.push(error.message); console.error(error.message); } }
  if (failures.length) {
    fs.writeFileSync(path.join(output,'failures.json'),JSON.stringify({build,failures},null,2));
    throw Error(`${failures.length} correctness profiles failed`);
  }
  const assetGate = read(path.join(root,'candidate/DHE/asset-gate.json'));
  assert(['sceneRejected','preloadedRejected','resourcesRejected','ordinaryAssetsAccepted'].every(k=>assetGate[k]===true), 'Base asset boundary failed');
} else {
  for (let i=0; i<pairs; ++i)
    for (const side of i % 2 ? ['candidate','baseline'] : ['baseline','candidate'])
      run(side,'DHE','dhe','benchmark',i);
}
assert(new Set(runs.map(r=>r.pid)).size === runs.length, 'PID reuse: cannot count these as unique processes');
const quantile = (xs,q) => { const a=[...xs].sort((a,b)=>a-b); const i=(a.length-1)*q; return a[Math.floor(i)]+(a[Math.ceil(i)]-a[Math.floor(i)])*(i%1); };
const stats = xs => { const p50=quantile(xs,.5); return {count:xs.length,p50,p95:quantile(xs,.95),mad:quantile(xs.map(x=>Math.abs(x-p50)),.5)}; };
const metrics = {};
if (pairs) {
  const values = run => ({load:run.loadMilliseconds,loadAndEntry:run.loadMilliseconds+run.firstEntryMilliseconds,privateBytes:run.privateBytesAfter,privateBytesDelta:run.privateBytesAfter-run.privateBytesBefore,...Object.fromEntries(run.samples.map(s=>[s.name,quantile(s.milliseconds,.5)]))});
  for (const run of runs) for (const sample of run.samples) {
    const counterpart=runs.find(r=>r.index===run.index && r.side!==run.side).samples.find(s=>s.name===sample.name);
    assert(sample.checksum===counterpart.checksum && sample.iterations===counterpart.iterations, 'Benchmark checksum/workload mismatch');
  }
  for (const name of Object.keys(values(runs[0]))) {
    const baseline=runs.filter(r=>r.side==='baseline').map(r=>values(r)[name]);
    const candidate=runs.filter(r=>r.side==='candidate').map(r=>values(r)[name]);
    metrics[name]={baseline:stats(baseline),candidate:stats(candidate),pairedPercent:stats(candidate.map((n,i)=>(n/baseline[i]-1)*100)),medianPercent:(quantile(candidate,.5)/quantile(baseline,.5)-1)*100};
  }
}
const summary={format:'hybridclr.aot-mode.release-gate.v1',passed:true,build,caseCount:reference.caseCount,unityExpected:236,independentProcesses:runs.length,pairs,diagnostics:false,p99HardGate:false,metrics,runs};
fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(summary,null,2));
console.log(JSON.stringify({passed:true,processes:runs.length,metrics},null,2));
