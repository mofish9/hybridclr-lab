import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';

const lab = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const root = path.resolve(process.argv[2]);
const label = process.argv[3] || 'full-regression';
if (!/^[a-z0-9-]+$/.test(label)) throw Error('Invalid evidence label');
const output = path.join(root, label);
fs.mkdirSync(output);
const read = f => JSON.parse(fs.readFileSync(f, 'utf8').replace(/^\uFEFF/, ''));
const hash = f => crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex');
const assert = (ok, message) => {if (!ok) throw Error(message);};
const build = read(path.join(root, 'build.json'));
const workload = path.join(root, 'candidate/shared/regression');
for (const [name, sha] of Object.entries(build.regressionPayloads))
  assert(hash(path.join(workload, name)) === sha, 'Regression identity drift: ' + name);
for (const [name, sha] of Object.entries(build.bundlePayloads))
  assert(hash(path.join(root, 'candidate/shared/bundles', name)) === sha, 'Bundle identity drift: ' + name);
const goldenPath = path.join(lab, 'manifests/test-golden.json');
const manifestPath = path.join(lab, 'manifests/test-manifest.json');
const golden = read(goldenPath);
const referencePath = path.join(output, 'reference.json');
const referenceRun = spawnSync('C:/Program Files/dotnet/dotnet.exe', ['run', '--project', path.join(lab, 'runners/dotnet-reference/HybridCLR.ReferenceRunner.csproj'), '-c', 'Release', '-p:IncludeSourceRevisionInInformationalVersion=false', '--', '--manifest', manifestPath, '--golden', goldenPath, '--output', referencePath], {encoding:'utf8', windowsHide:true, timeout:180000});
fs.writeFileSync(path.join(output,'reference.log'), (referenceRun.stdout||'')+(referenceRun.stderr||''));
assert(referenceRun.status === 0, 'CLR reference failed');
const reference = read(referencePath);
assert(reference.summary.total === golden.cases.length && reference.summary.failed === 0, 'Incomplete CLR reference');
assert(reference.managedAssemblySha256.toLowerCase() === build.regressionPayloads['HybridCLR.ManagedCases.dll'], 'CLR/Player workload DLLs differ');
const expected = new Map(golden.cases.map(c => [c.id, c]));
const decode = value => value === '-' ? null : Buffer.from(value,'base64').toString('utf8');
const runs = [], failures = [];
for (const [side, profile, mode] of [['candidate','DHE','dhe'],['candidate','DHE','legacy'],['baseline','DHE','dhe'],['candidate','LegacyInterpreter','legacy']]) {
  for (const metadata of ['none','superset']) {
    const name = `${side}-${profile}-${mode}-${metadata}`;
    try {
      const identity=build.profiles[`${side}/${profile}`];
      const player=path.join(root,side,profile,'player');
      assert(hash(path.join(player,'GameAssembly.dll'))===identity.gameAssemblySha256, 'Runtime identity drift');
      assert(hash(path.join(player,'StartupPlayer_Data/il2cpp_data/Metadata/global-metadata.dat'))===identity.metadataSha256, 'Metadata identity drift');
      const report=path.join(output,name+'.json');
      const child=spawnSync(path.join(player,'StartupPlayer.exe'), ['-batchmode','-nographics','-scenario','correctness','-mode',mode,'-selectMode',String(side==='candidate'&&profile==='DHE'),'-startupPairRoot',path.join(root,side),'-bundleRoot',path.join(root,'candidate/shared/bundles'),'-regressionRoot',workload,'-regressionMetadata',metadata,'-startupReport',report,'-logFile',path.join(output,name+'.log')], {encoding:'utf8',windowsHide:true,timeout:180000});
      assert(child.status===0 && fs.existsSync(report), `Player failure (${child.status}): ${child.error||''}`);
      const result=read(report);
      assert(result.passed && result.differential===0 && result.bundleResult===443, 'Startup/Bundle regression: '+result.error);
      assert(result.mode===(mode==='dhe'?1:2), 'Wrong mode');
      const observations=new Map();
      for (const line of result.regressionRecords) {
        const [id, value, sideEffect, exceptionType]=line.split('\t');
        assert(!observations.has(id), 'Duplicate case: '+id);
        observations.set(id,{returnValue:decode(value),sideEffect:decode(sideEffect),exceptionType:decode(exceptionType)});
      }
      assert(observations.size===expected.size, 'Incomplete managed suite');
      const differences=[];
      for (const [id,contract] of expected) {
        const actual=observations.get(id);
        if (!actual || actual.exceptionType !== (contract.exceptionType??null) ||
            (actual.exceptionType===null && (actual.returnValue!==(contract.returnValue??null) || actual.sideEffect!==(contract.sideEffect??null))))
          differences.push({id,expected:contract,actual});
      }
      const negativeControl=side==='baseline';
      runs.push({name,pid:result.pid,mode:result.mode,metadata,cases:observations.size,differences,negativeControl});
      if (negativeControl) {
        // opt8 predates the ordinary-interpreter/PInvoke lowering fix. Require
        // exactly the reproduced failures, never count them as passing cases.
        assert(identity.sources.hybridclr==='9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071', 'Unexpected negative-control runtime');
        assert(JSON.stringify(differences.map(d=>d.id).sort())===JSON.stringify(['pinvoke_process_id','reverse_pinvoke_qsort']) && differences.every(d=>d.actual.exceptionType==='System.ExecutionEngineException'), 'Baseline failure set changed');
        console.log(`NEGATIVE CONTROL ${name}: ${differences.length} known failures reproduced`);
      } else {
        assert(differences.length===0, `${differences.length} differential failures`);
        console.log(`PASS ${name}: ${observations.size} cases, pid=${result.pid}`);
      }
    } catch(error) {failures.push({name,error:String(error)}); console.error(`FAIL ${name}: ${error}`);}
  }
}
assert(new Set(runs.map(r=>r.pid)).size===runs.length, 'PID reuse');
const summary={passed:failures.length===0,scope:'Unity2022 Windows, existing full managed suite and cross-assembly VTable, both selection modes, with/without supplemental AOT metadata',build,runnerSha256:hash(fileURLToPath(import.meta.url)),goldenSha256:hash(goldenPath),manifestSha256:hash(manifestPath),reference,runs,failures};
fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(summary,null,2));
if (failures.length) throw Error(`${failures.length} full regression profiles failed; see ${output}`);
