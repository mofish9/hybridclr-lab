import fs from 'node:fs';
import crypto from 'node:crypto';
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const policyPath=new URL('../manifests/aot-mode-performance-policy.json',import.meta.url);
const policy=read(policyPath), failures=[], checked=[], disclosedRegressions=[];
const requiredMetrics=['BenchNative','BenchChanged','BenchVirtual','selection','selectionToEntry','processToEntry','load','loadAndEntry','privateBytes','privateBytesDelta'];
for(const reportPath of process.argv.slice(2)) {
  const report=read(reportPath);
  if(!report.passed || report.pairs<policy.minimumIndependentProcessesPerSide || !report.p99SampleQualified ||
    ['baseline','candidate'].some(group=>new Set(report.runs.filter(r=>r.comparisonGroup===group).map(r=>r.pid)).size<policy.minimumIndependentProcessesPerSide)) failures.push({reportPath,reason:'Insufficient valid independent processes'});
  if(!policy.comparisonAcceptance[report.comparison]) failures.push({reportPath,reason:'Unknown comparison'});
  for(const name of requiredMetrics)
    if(!report.metrics?.[name])failures.push({reportPath,name,reason:'Missing required metric'});
  for(const [name,metric] of Object.entries(report.metrics??{})) {
    let valid=true;
    for(const group of ['baseline','candidate']) {
      const measurement=metric?.[group];
      if(!measurement || measurement.count!==report.runs.filter(r=>r.comparisonGroup===group).length ||
        measurement.count<policy.minimumIndependentProcessesPerSide ||
        ['p50','p95','p99'].some(p=>!Number.isFinite(measurement[p]))) {
        failures.push({reportPath,name,group,reason:'Invalid measurement or metric sample count'});valid=false;
      }
    }
    if(!valid)continue;
    for(const percentile of ['p50','p95','p99']) {
      const b=metric.baseline[percentile], c=metric.candidate[percentile];
      const percent=b>0?(c/b-1)*100:null, delta=c-b;
      if(!Number.isFinite(b)||!Number.isFinite(c)){failures.push({reportPath,name,percentile,reason:'Invalid measurement'});continue;}
      let failed=false;
      if(name.startsWith('Bench'))failed=percent>(percentile==='p99'?policy.steadyState.maximumP99RegressionPercent:policy.steadyState[percentile==='p50'?'maximumP50RegressionPercent':'maximumP95RegressionPercent']);
      else if(['selectionToEntry','processToEntry','load','loadAndEntry'].includes(name))
        failed=percent>(percentile==='p99'?policy.startup.maximumP99RegressionPercent:policy.startup.maximumP50P95RegressionPercent)&&delta>policy.startup.absoluteRegressionFloorMilliseconds;
      else if(name==='privateBytes'&&percentile==='p50')
        failed=percent>policy.memory.maximumP50RegressionPercent&&delta>policy.memory.absoluteRegressionFloorBytes;
      if(failed)(policy.comparisonAcceptance[report.comparison]==='measure-and-disclose'?disclosedRegressions:failures).push({reportPath,name,percentile,percent,delta});
    }
  }
  checked.push({reportPath,comparison:report.comparison,pairs:report.pairs,sha256:crypto.createHash('sha256').update(fs.readFileSync(reportPath)).digest('hex')});
}
if(new Set(checked.map(r=>r.comparison)).size!==3)failures.push({reason:'Require dhe, legacy and fixed comparisons'});
const result={passed:failures.length===0,policy,policySha256:crypto.createHash('sha256').update(fs.readFileSync(policyPath)).digest('hex'),checkerSha256:crypto.createHash('sha256').update(fs.readFileSync(new URL(import.meta.url))).digest('hex'),checked,failures,disclosedRegressions};
console.log(JSON.stringify(result,null,2));
if(failures.length)process.exitCode=1;
