import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {execFileSync} from 'node:child_process';

const buildRoot=path.resolve(process.argv[2]);
const correctnessRoot=path.resolve(process.argv[3]);
const performanceRoot=path.resolve(process.argv[4]);
const compatibilityRoot=path.resolve(process.argv[5]);
const output=path.resolve(process.argv[6]);
const read=file=>JSON.parse(fs.readFileSync(file,'utf8').replace(/^\uFEFF/,''));
const hash=file=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const assert=(value,message)=>{if(!value)throw Error(message)};
const build=read(path.join(buildRoot,'build.json'));
const correct=read(path.join(correctnessRoot,'summary.json'));
const performance=read(path.join(performanceRoot,'summary.json'));
const native=read(path.join(buildRoot,'candidate/native-gate.json'));
const compatibility=read(path.join(compatibilityRoot,'summary.json'));
const identities=build.profiles['candidate/DHE'].sources;
assert(correct.passed&&performance.passed&&native.selectionEnabledCompile&&native.selectionDisabledCompile&&compatibility.passed,'A prerequisite gate failed');
for(const data of [correct,performance])assert(JSON.stringify(data.build)===JSON.stringify(build),'Player evidence has another build identity');
assert(native.sources.hybridclr===identities.hybridclr&&native.sources.il2cpp_plus===identities.il2cpp_plus,'Native evidence is stale');
assert(compatibility.profiles.every(p=>p.hybridclrCommit===identities.hybridclr&&!p.surrogateExternalHeadersUsed),'Other engine evidence is stale');
for(const [key,profile] of Object.entries(build.profiles))assert(hash(path.join(buildRoot,key,'player/GameAssembly.dll'))===profile.gameAssemblySha256,'Player changed after testing');
const lab=path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/(\w:)/,'$1')),'..');
const sourceCommit=execFileSync('git',['-C',lab,'rev-parse','HEAD'],{encoding:'utf8'}).trim();
assert(!execFileSync('git',['-C',lab,'status','--porcelain'],{encoding:'utf8'}).trim(),'Commit source before freezing evidence');
fs.mkdirSync(output);
const inputs={
  'build.json':path.join(buildRoot,'build.json'),
  'correctness.json':path.join(correctnessRoot,'summary.json'),
  'performance.json':path.join(performanceRoot,'summary.json'),
  'native-unity2022.json':path.join(buildRoot,'candidate/native-gate.json'),
  'native-other-engines.json':path.join(compatibilityRoot,'summary.json'),
  'dependency-validator.json':path.join(lab,'artifacts/aot-mode-validator/validator-gate.json'),
  'asset-gate.json':path.join(buildRoot,'candidate/DHE/asset-gate.json')
};
const evidence={};
for(const [name,source] of Object.entries(inputs)){fs.copyFileSync(source,path.join(output,name));evidence[name]={source,sha256:hash(source)}}
const lock={format:'hybridclr.aot-mode.candidate-lock.v1',createdAtUtc:new Date().toISOString(),labSourceCommit:sourceCommit,sources:identities,evidence,windowsCorrectnessPassed:true,performanceRequiresReview:true,mergeReady:false,releaseReady:false,androidPlayerVerified:false,webglPlayerVerified:false,iosPlayerVerified:false,otherEngineSelectionVerified:false};
fs.writeFileSync(path.join(output,'lock.json'),JSON.stringify(lock,null,2)+'\n');
console.log(JSON.stringify({output,sources:identities,labSourceCommit:sourceCommit},null,2));
