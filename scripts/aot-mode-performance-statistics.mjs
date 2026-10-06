export const quantile = (xs,q) => {
  const a=[...xs].sort((a,b)=>a-b), i=(a.length-1)*q;
  return a[Math.floor(i)]+(a[Math.ceil(i)]-a[Math.floor(i)])*(i%1);
};
const stats = xs => {
  const p50=quantile(xs,.5);
  return {count:xs.length,p50,p95:quantile(xs,.95),p99:quantile(xs,.99),mad:quantile(xs.map(x=>Math.abs(x-p50)),.5)};
};
export function processCounts(runs) {
  return Object.fromEntries(['baseline','candidate'].map(group=>{
    const selected=runs.filter(r=>r.comparisonGroup===group);
    return [group,{launches:selected.length,uniquePids:new Set(selected.map(r=>r.pid)).size}];
  }));
}
export function summarizePerformance(runs) {
  const metrics={};
  const values = run => ({selection:run.selectionMilliseconds,selectionToEntry:run.selectionToEntryMilliseconds,processToEntry:run.processToEntryMilliseconds,load:run.loadMilliseconds,loadAndEntry:run.loadMilliseconds+run.firstEntryMilliseconds,privateBytes:run.privateBytesAfter,privateBytesDelta:run.privateBytesAfter-run.privateBytesBefore,...Object.fromEntries(run.samples.map(s=>[s.name,quantile(s.milliseconds,.5)]))});
  const groups=Object.fromEntries(['baseline','candidate'].map(group=>[group,runs.filter(r=>r.comparisonGroup===group).sort((a,b)=>a.index-b.index)]));
  if(groups.baseline.length!==groups.candidate.length || !groups.baseline.length)throw Error('Unpaired runs');
  for(let i=0;i<groups.baseline.length;i++) {
    const a=groups.baseline[i], b=groups.candidate[i];
    if(a.index!==b.index || (i && a.index===groups.baseline[i-1].index))throw Error('Invalid pair identity');
    for(const name of ['BenchNative','BenchChanged','BenchVirtual']) {
      const sample=a.samples.find(s=>s.name===name), counterpart=b.samples.find(s=>s.name===name);
      if(!sample || !counterpart || sample.checksum!==counterpart.checksum || sample.iterations!==counterpart.iterations)throw Error('Benchmark checksum/workload mismatch');
      let expected=0;
      for(let n=0;n<sample.iterations;n++) expected=(expected+(name==='BenchNative'?(n&255)*3+1:name==='BenchChanged'?(n&255)+200:(n&255)*(n%2?5:4)))|0;
      if(name==='BenchChanged')expected=(expected+1)|0;
      if(sample.checksum!==expected)throw Error(`Wrong benchmark result: ${name}`);
    }
  }
  for(const name of Object.keys(values(runs[0]))) {
    const baseline=groups.baseline.map(r=>values(r)[name]), candidate=groups.candidate.map(r=>values(r)[name]);
    if([...baseline,...candidate].some(n=>!Number.isFinite(n)))throw Error(`Invalid measurement: ${name}`);
    const denominator=quantile(baseline,.5);
    metrics[name]={baseline:stats(baseline),candidate:stats(candidate),pairedDelta:stats(candidate.map((n,i)=>n-baseline[i])),pairedPercent:baseline.every(n=>n!==0)?stats(candidate.map((n,i)=>(n/baseline[i]-1)*100)):null,medianPercent:denominator!==0?(quantile(candidate,.5)/denominator-1)*100:null};
  }
  return metrics;
}
