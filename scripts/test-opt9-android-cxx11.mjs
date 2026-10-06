import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {execFileSync, spawnSync} from 'node:child_process';

const [candidateRoot, mergedOpt9Root, outputRoot] = process.argv.slice(2);
if (!outputRoot) throw Error('Expected candidate HybridCLR root, frozen merged opt9 IL2CPP root, and new output directory');
fs.mkdirSync(outputRoot);
const editor = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Data';
const compiler = editor + '/PlaybackEngines/AndroidPlayer/NDK/toolchains/llvm/prebuilt/windows-x86_64/bin/clang++.exe';
const hash = p => crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
const git = (...args) => execFileSync('git', ['-C', candidateRoot, ...args], {encoding:'utf8'}).trim();
if (git('status','--porcelain')) throw Error('Commit candidate before validation');
const baseline = path.join(outputRoot, 'opt9-RuntimeApi.cpp');
fs.writeFileSync(baseline, execFileSync('git', ['-C', candidateRoot, 'show', 'v8.13.0-opt9:hybridclr/RuntimeApi.cpp']));
const common = ['--target=aarch64-linux-android22', '-std=c++11', '-fPIC', '-O2', '-c',
  '-I'+candidateRoot, '-I'+path.join(candidateRoot,'hybridclr'), '-I'+path.join(mergedOpt9Root,'libil2cpp'),
  '-I'+editor+'/il2cpp/external/baselib/Include', '-I'+editor+'/il2cpp/external/baselib/Platforms/Android/Include',
  '-I'+editor+'/il2cpp/external/bdwgc/include', '-DNET_4_0', '-DIL2CPP_GC_BOEHM=1',
  '-DBASELIB_INLINE_NAMESPACE=il2cpp_baselib', '-DHYBRIDCLR_UNITY_VERSION=20220362',
  ...['2019_OR_NEW','2020_OR_NEW','2021_OR_NEW','2022','2022_OR_NEW'].map(v=>'-DHYBRIDCLR_UNITY_'+v+'=1')];
const cases=[];
for (const selection of [0,1]) for (const side of ['baseline','candidate']) {
  const name=`${side}-selection-${selection}`;
  const source=side==='baseline'?baseline:path.join(candidateRoot,'hybridclr/RuntimeApi.cpp');
  const object=path.join(outputRoot,name+'.o');
  const args=[...common,'-DHYBRIDCLR_ENABLE_AOT_SELECTION='+selection,source,'-o',object];
  const result=spawnSync(compiler,args,{encoding:'utf8',windowsHide:true});
  const diagnostic=(result.stdout||'')+(result.stderr||'');
  const log=path.join(outputRoot,name+'.log');fs.writeFileSync(log,diagnostic);
  const expected=side==='baseline'?'PendingDheImage C++11 constructor rejection':'object compilation succeeds';
  const passed=side==='baseline'?result.status!==0&&/no matching constructor for initialization of '[^'\r\n]*PendingDheImage'/.test(diagnostic):result.status===0&&fs.existsSync(object);
  const evidence={name,sourceSha256:hash(source),args,exitCode:result.status,expected,passed,logSha256:hash(log)};
  if(result.status===0)evidence.objectSha256=hash(object);
  cases.push(evidence);console.log(`${passed?'PASS':'FAIL'} ${name}: ${expected}`);
}
const report={passed:cases.every(c=>c.passed),candidateCommit:git('rev-parse','HEAD'),baselineCommit:git('rev-parse','v8.13.0-opt9^{}'),il2cppCommit:'135f18fd5a26906e7800e0c4313826eaf2a7756c',compiler,compilerSha256:hash(compiler),compilerVersion:execFileSync(compiler,['--version'],{encoding:'utf8'}).trim(),engine:'Unity2022.3.62f3',target:'Android ARM64 API22',standard:'c++11',scope:'RuntimeApi.cpp cross-compilation only; explicit reproducible flags, not a recovered project build command; no full APK/link/runtime qualification',cases};
fs.writeFileSync(path.join(outputRoot,'summary.json'),JSON.stringify(report,null,2)+'\n');
if(!report.passed)process.exitCode=1;
