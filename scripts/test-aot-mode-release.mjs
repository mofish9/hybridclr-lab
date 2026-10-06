import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {spawnSync} from 'node:child_process';
import {processCounts,summarizePerformance} from './aot-mode-performance-statistics.mjs';

const root = path.resolve(process.argv[2] || 'artifacts/ar1');
const pairs = Number(process.argv[3] || 0);
if (!Number.isInteger(pairs) || pairs < 0) throw Error('Invalid pair count');
const build = JSON.parse(fs.readFileSync(path.join(root, 'build.json')));
const label = process.argv[4] || '';
const comparison = process.argv[5] || 'dhe';
assertComparison();
function assertComparison() { if (!['dhe','legacy','fixed'].includes(comparison)) throw Error('Unknown performance comparison'); }
if (label && !/^[a-z0-9-]+$/.test(label)) throw Error('Invalid run label');
const output = path.join(root, (pairs ? `performance-${comparison}-${pairs}` : 'correctness') + (label ? '-' + label : ''));
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
for (const [name, expected] of Object.entries(build.bundlePayloads))
  assert(hash(path.join(root,'candidate/shared/bundles',name)) === expected, `Bundle identity drift: ${name}`);
const referenceRun = spawnSync('C:/Program Files/dotnet/dotnet.exe', [path.join(root, 'reference/Reference.dll'), path.join(root, 'candidate'), referencePath], {encoding:'utf8', windowsHide:true, timeout:120000});
assert(referenceRun.status === 0, `CLR reference failed: ${referenceRun.stderr} ${referenceRun.stdout}`);
const reference = read(referencePath);
const runs = [];
function run(side, profile, mode, scenario, index) {
  const name = `${index}-${side}-${profile}-${mode}-${scenario}`;
  const report = path.join(output, name + '.json');
  const exe = path.join(root, side, profile, 'player/StartupPlayer.exe');
  const launchedAtUtc = new Date().toISOString();
  const child = spawnSync(exe, ['-batchmode','-nographics','-scenario',scenario,'-mode',mode,'-selectMode',String(side==='candidate' && profile==='DHE'),'-bundleRoot',path.join(root,'candidate/shared/bundles'),'-startupPairRoot',path.join(root,side),'-startupReport',report,'-logFile',path.join(output,name+'.log')], {encoding:'utf8', windowsHide:true, timeout:120000});
  assert(child.status === 0 && fs.existsSync(report), `Player failed: ${name}; exit=${child.status}; ${child.error || ''}`);
  const result = read(report);
  assert(result.pid === child.pid, `Parent/Player PID mismatch: ${name}`);
  const launch = {pid:child.pid,launchedAtUtc,exitedAtUtc:new Date().toISOString(),exitCode:child.status};
  fs.writeFileSync(path.join(output,name+'.launch.json'),JSON.stringify(launch));
  assert(result.passed && result.differential === 0 && result.caseCount === reference.caseCount, `Correctness failed: ${name}: ${result.error}`);
  assert(JSON.stringify(result.actual) === JSON.stringify(reference.actual), `CLR differential: ${name}`);
  for (const [field, assembly] of [['currentSha256','StartupHotfix'],['consumerSha256','StartupConsumer'],['unitySha256','StartupUnityHotfix'],['supportSha256','StartupAotSupport']])
    assert(result[field] === build.payloads[assembly], `Loaded payload mismatch: ${name}/${field}`);
  assert(!result.diagnostics && result.unityResult === 236 && result.identityMatches, `Production/Unity boundary failed: ${name}`);
  if (scenario !== 'benchmark') assert(result.duplicateSupplemental === 5, `Wrong duplicate-metadata result: ${name}`);
  if (scenario !== 'benchmark') assert(result.bundleResult === 1298 && result.nativeTypeLookup === 63, `Bundle serialization/native Current type lookup failed: ${name}`);
  if (side === 'candidate' && profile === 'DHE' && scenario !== 'benchmark' && result.mode === 1)
    assert(result.rejectedLoadAttempts === 512, `Rejected loads exhausted metadata indices: ${name}`);
  if (scenario === 'concurrent') assert(result.concurrentSuccesses === 1 && result.concurrentResults.filter(n=>n===1).length === 11, 'Concurrent selection failed');
  else assert(result.mode === (mode === 'dhe' ? 1 : 2), `Wrong mode: ${name}`);
  runs.push({side, profile, requestedMode:mode, index, launch, ...result});
  console.log(`PASS ${name}, pid=${result.pid}`);
}
if (!pairs) {
  const failures=[];
  const profiles=[
    ['candidate','DHE','dhe','correctness',0],
    ['candidate','DHE','legacy','correctness',1],
    ['candidate','DHE','dhe','concurrent',2],
    ['baseline','DHE','dhe','correctness',3],
    ['candidate','LegacyInterpreter','legacy','correctness',4]
  ];
  if(build.profiles['fixed/DHE'])profiles.push(['fixed','DHE','dhe','correctness',5]);
  for (const args of profiles) { try { run(...args); } catch(error) { failures.push(error.message); console.error(error.message); } }
  if (failures.length) {
    fs.writeFileSync(path.join(output,'failures.json'),JSON.stringify({build,failures},null,2));
    throw Error(`${failures.length} correctness profiles failed`);
  }
  const assetGate = read(path.join(root,'candidate/DHE/asset-gate.json'));
  assert(['sceneRejected','preloadedRejected','resourcesRejected','ordinaryAssetsAccepted'].every(k=>assetGate[k]===true), 'Base asset boundary failed');
} else {
  const control = comparison==='legacy' ? ['candidate','LegacyInterpreter','legacy'] : ['baseline','DHE','dhe'];
  const candidate = comparison==='legacy' ? ['candidate','DHE','legacy'] : [comparison==='fixed'?'fixed':'candidate','DHE','dhe'];
  for (let i=0; i<pairs; ++i)
    for (const group of i % 2 ? ['candidate','baseline'] : ['baseline','candidate']) {
      run(...(group==='candidate'?candidate:control),'benchmark',i);
      runs[runs.length-1].comparisonGroup=group;
    }
}
const processes=processCounts(runs);
const metrics=pairs?summarizePerformance(runs):{};
const summary={format:'hybridclr.aot-mode.release-gate.v2',passed:true,correctnessPassed:true,performanceAcceptance:pairs?'requires-review':'not-measured',comparison,runnerSha256:hash(new URL(import.meta.url)),statisticsSha256:hash(new URL('./aot-mode-performance-statistics.mjs',import.meta.url)),build,caseCount:reference.caseCount,unityExpected:236,independentProcesses:runs.length,pairs,diagnostics:false,processCounts:processes,p99SampleQualified:pairs>=100&&Object.values(processes).every(g=>g.uniquePids>=100),p99HardGate:false,metrics,runs};
fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(summary,null,2));
console.log(JSON.stringify({passed:true,processes:runs.length,metrics},null,2));
