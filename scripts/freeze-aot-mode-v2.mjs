import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

const [buildArg, correctArg, fullArg, acceptanceArg, assetsArg, sceneArg, validatorArg, outputArg]=process.argv.slice(2);
if(!outputArg)throw Error('Expected build, correctness, full regression, performance acceptance, assets, Player scene, dependency validator and output paths');
const root=path.resolve(buildArg), output=path.resolve(outputArg);
const lab=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const hash=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
const assert=(ok,message)=>{if(!ok)throw Error(message);};
const git=(root,...args)=>execFileSync('git',['-C',root,...args],{encoding:'utf8'}).trim();
const build=read(path.join(root,'build.json'));
const identity=build.profiles['candidate/DHE'].sources;
const correct=read(correctArg), full=read(fullArg), acceptance=read(acceptanceArg);
const native=read(path.join(root,'candidate/native-gate.json'));
const assets=read(assetsArg), scene=read(sceneArg), validator=read(validatorArg);
assert(correct.passed&&full.passed&&acceptance.passed&&assets.passed&&scene.passed&&validator.result.passes===7,'Release prerequisite failed');
assert(JSON.stringify(correct.build)===JSON.stringify(build)&&JSON.stringify(full.build)===JSON.stringify(build),'Player evidence identity differs');
assert(correct.runs.length===6&&full.runs.length===10,'Incomplete Player matrix');
assert(full.runs.filter(r=>!r.negativeControl).every(r=>r.cases===full.reference.summary.total&&r.differences.length===0),'Incomplete positive full regression');
assert(full.runs.filter(r=>r.negativeControl).length===2,'Missing opt8 negative controls');
assert(native.selectionEnabledCompile&&native.selectionDisabledCompile&&native.ordinaryNativeCtest&&!native.surrogateExternalHeadersUsed,'Incomplete Unity2022 native gates');
assert(native.sources.hybridclr===identity.hybridclr&&native.sources.il2cpp_plus===identity.il2cpp_plus,'Stale native sources');
assert(assets.packageCommit===identity.hybridclr_unity&&!assets.packageDirty&&validator.packageCommit===identity.hybridclr_unity,'Stale package validators');
for(const [repo,commit] of Object.entries(identity))assert(scene.sources[repo]===commit,'Stale Player scene gate');
for(const [key,profile] of Object.entries(build.profiles)) {
  assert(hash(path.join(root,key,'player/GameAssembly.dll'))===profile.gameAssemblySha256,'Player binary changed');
  assert(hash(path.join(root,key,'player/StartupPlayer_Data/il2cpp_data/Metadata/global-metadata.dat'))===profile.metadataSha256,'Player metadata changed');
}
for(const [name,sha] of Object.entries(build.bundlePayloads))assert(hash(path.join(root,'candidate/shared/bundles',name))===sha,'Bundle changed');
for(const [name,sha] of Object.entries(build.regressionPayloads))assert(hash(path.join(root,'candidate/shared/regression',name))===sha,'Regression payload changed');
assert(new Set(acceptance.checked.map(r=>r.comparison)).size===3,'Incomplete performance comparisons');
const inputs={'build.json':path.join(root,'build.json'),'correctness.json':correctArg,'full-regression.json':fullArg,'performance-acceptance.json':acceptanceArg,'native-unity2022.json':path.join(root,'candidate/native-gate.json'),'asset-regression.json':assetsArg,'player-scene.json':sceneArg,'dependency-validator.json':validatorArg};
for(const entry of acceptance.checked) {
  assert(hash(entry.reportPath)===entry.sha256,'Performance report changed');
  const report=read(entry.reportPath);
  // A later fixed-mode rebuild must not relabel previous samples or invalidate
  // unrelated DHE/legacy binaries. Bind every profile actually measured, plus
  // all shared workload/config identities, instead of the unused profile map.
  for(const key of ['engine','diagnostics','ordinaryAotGuards','supplementalAotMetadata','payloads','fixtureHashes','bundlePayloads','regressionPayloads'])
    assert(JSON.stringify(report.build[key])===JSON.stringify(build[key]),`Performance workload/config differs: ${key}`);
  for(const run of report.runs) {
    const key=`${run.side}/${run.profile}`;
    assert(JSON.stringify(report.build.profiles[key])===JSON.stringify(build.profiles[key]),`Performance binary/source differs: ${key}`);
  }
  inputs[`performance-${entry.comparison}.json`]=entry.reportPath;
}
assert(!git(lab,'status','--porcelain'),'Commit lab sources before freeze');
const labSourceCommit=git(lab,'rev-parse','HEAD');
fs.mkdirSync(output);
const evidence={};
for(const [name,source] of Object.entries(inputs)){fs.copyFileSync(source,path.join(output,name));evidence[name]={source:path.resolve(source),sha256:hash(source)};}
const lock={format:'hybridclr.aot-mode.candidate-lock.v2',createdAtUtc:new Date().toISOString(),scope:'Unity2022 Windows IL2CPP; other engines excluded from this release by user instruction',labSourceCommit,sources:identity,evidence,windowsCorrectnessPassed:true,windowsPerformancePassed:true,sourceCandidateReady:true,formalMaintenanceGatePending:true,releaseReady:false,androidPlayerVerified:false,webglPlayerVerified:false,iosPlayerVerified:false};
fs.writeFileSync(path.join(output,'lock.json'),JSON.stringify(lock,null,2)+'\n');
console.log(JSON.stringify({output,sources:identity,sourceCandidateReady:true,releaseReady:false},null,2));
