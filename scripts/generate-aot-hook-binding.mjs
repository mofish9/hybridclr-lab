// Generate the declarative DHE extension boundary. Originals remain directly
// called when HYBRIDCLR_ENABLE_AOT_SELECTION=0. Fail on unmapped new signatures.
import fs from 'node:fs';
import path from 'node:path';
const root=process.argv[2];
if(!root) throw new Error('hybridclr worktree required');
const read=p=>fs.readFileSync(path.join(root,p),'utf8').replaceAll('\r\n','\n');
const write=(p,s)=>fs.writeFileSync(path.join(root,p),s);
const split=s=>{let depth=0,start=0,out=[];for(let n=0;n<s.length;n++){if(s[n]==='<')depth++;if(s[n]==='>')depth--;if(s[n]===','&&!depth){out.push(s.slice(start,n).trim());start=n+1;}} if(s.trim())out.push(s.slice(start).trim());return out;};
const metadataValues={
  ResolveDheTypeHandleClass:'klass',
  GetUnderlyingInterpreterImage:'LegacyInterpreterImage(methodInfo)',GetInterpreterResolveImage:'LegacyInterpreterImage(methodInfo)',
  GetDheMethodMetadataImage:'method->klass->image',GetDheCurrentMethodMetadata:'method',GetDheClassInitializationOwner:'klass',GetDheReferenceAllocationClass:'klass',
  GetDheExecutionClass:'klass',GetDhePublicReferenceType:'type',ResolveDheReferenceInstanceField:'const_cast<FieldInfo*>(field)',ResolveDheSupplementalField:'field',
  GetDheLogicalFieldParent:'field ? field->parent : nullptr',ResolveDheMethod:'method',GetDheCustomAttributeProperty:'LegacyProperty(klass, index)'
};
const runtimeValues={ResolveReferenceAllocationClass:'klass',GetPublicationIdentity:'nullptr',ResolvePublicAssemblyImage:'image',SelectReferenceInterfaceIterationClass:'requested',
  CanEnterWithBaseAbi:'true',ResolveInterpreterMethod:'baseMethod',ResolveCurrentExecutionMethod:'method',ResolveNativeReferenceInvokeMethod:'method',ResolveCurrentReceiverMethod:'method',ResolveInterpreterVirtualMethod:'method'};
const runtimeNames=new Set(['ResolveReferenceAllocationClass','GetPublicationIdentity','ResolvePublicAssemblyImage','SelectReferenceInterfaceIterationClass',
  'IsDheAssembly','IsMutableDheAssembly','IsDheModuleInitializationReady','IsFrozenAotExecutionSource','TryGetVirtualInvokeData','TryGetVirtualBaseMethod',
  'TryGetVirtualReflectionIdentity','TryGetInterfaceInvokeData','IsChangedMethod','IsRemovedMethod','IsRemovedType','ShouldDispatchToInterpreter','CanEnterWithBaseAbi',
  'ResolveAotGuardMethodByToken','ResolveMethodByToken','ResolveMethodByNameAndToken','ResolveInterpreterMethod','ResolveCurrentExecutionMethod',
  'ResolveNativeReferenceInvokeMethod','ResolveCurrentReceiverMethod','ResolveInterpreterVirtualMethod']);
const groups=[
  {name:'Metadata',header:'hybridclr/metadata/MetadataModule.h',cpp:'hybridclr/metadata/MetadataModule.cpp',owner:'hybridclr::metadata::MetadataModule::',definition:'MetadataModule::',values:metadataValues},
  {name:'Runtime',header:'hybridclr/DheRuntime.h',cpp:'hybridclr/DheRuntime.cpp',owner:'hybridclr::dhe::',definition:'',values:runtimeValues}
];
const hooks=[];
const unroute=s=>s.replace(/HCLR_AOT_IMPL\((\w+)\)/g,'$1').replace(/^[ \t]*HCLR_AOT_OBSERVE\("[^"\n]+"\);\n/gm,'');
const sources=new Map(groups.map(g=>[g.cpp,unroute(read(g.cpp))]));
const normalized=s=>s.replace(/\s+/g,' ').trim();
for(const group of groups){
  let header=read(group.header).replace(/\n#if HYBRIDCLR_ENABLE_AOT_SELECTION\n[^\n]*DheImpl_\w+\([^\n]*\);\n#endif/g,'');
  const pattern=group.name==='Metadata'?/^([ \t]*)static ([\w:*<> ]+?)\s+(\w+)\(([^;{}]*?)\);/gm:/^([ \t]+)([\w:*<> ]+?)\s+(\w+)\(([^;{}]*?)\);/gm;
  header=header.replace(pattern,(full,indent,ret,name,raw)=>{
    if(group.name==='Runtime'&&!runtimeNames.has(name))return full;
    if(group.name==='Metadata'&&!name.includes('Dhe')&&!metadataValues[name])return full;
    if(name==='RegisterDheSupplementalInstanceField')return full; // DHE metadata preparation, loader is gated.
    const params=split(raw.replaceAll('\n',' ').replace(/\s+/g,' ')).map(p=>p.replace(/\s*=.*$/,''));
    const args=params.map(p=>{const m=p.match(/(\w+)$/);if(!m)throw new Error(p);return m[1];});
    ret=ret.trim();
    let fallback=group.values[name];
    if(fallback===undefined){
      if(ret==='bool')fallback='false';
      else if(ret==='size_t')fallback='0';
      else if(ret.endsWith('*'))fallback='nullptr';
      else if(ret==='void'&&name==='GetDheSupplementalTypes')fallback='';
      else throw new Error('Review fallback for '+name);
    }
    const key=`${group.name}_${name}_${hooks.filter(h=>h.name===name&&h.group===group.name).length}`;
    const hook={key,group:group.name,name,ret,params,args,fallback,owner:group.owner};hooks.push(hook);
    const signature=`${ret} ${group.definition}${name}`;
    const escaped=signature.replace(/[.*+?^${}()|[\]\\]/g,'\\$&').replace(/ /g,'\\s+');
    // Replace one overload per declaration, matching normalized parameter list.
    const re=new RegExp(`(^[ \\t]*${escaped}\\s*\\()([^)]*)(\\)\\s*\\{)`,'gm');
    let replaced=0;
    for(const [file,source] of sources) sources.set(file,source.replace(re,(body,prefix,actual,suffix)=>{
      if(normalized(actual)!==normalized(params.join(', ')))return body;
      replaced++;
      return prefix.replace(name+'(',`HCLR_AOT_IMPL(${name})(`) + actual + suffix + `\n    HCLR_AOT_OBSERVE("${key}");`;
    }));
    if(replaced!==1)throw new Error(`Expected one definition: ${key}, got ${replaced}; params=${params}`);
    return full+`\n#if HYBRIDCLR_ENABLE_AOT_SELECTION\n${indent}${group.name==='Metadata'?'static ':''}${ret} DheImpl_${name}(${params.join(', ')});\n#endif`;
  });
  if(!header.includes('AotModeConfig.h'))header=header.replace('#pragma once','#pragma once\n#include "'+(group.name==='Metadata'?'../':'')+'AotModeConfig.h"');
  group.headerResult=header;
}
// Generation is transactional with respect to signature validation.
for(const group of groups){write(group.header,group.headerResult);write(group.cpp,sources.get(group.cpp));}
let cpp=`// Generated by lab/scripts/generate-aot-hook-binding.mjs; fallback map is reviewed there.\n#include "AotModeConfig.h"\n#if HYBRIDCLR_ENABLE_AOT_SELECTION\n#include "metadata/MetadataModule.h"\n#include "metadata/AOTHomologousImage.h"\n#include "DheRuntime.h"\n#include <atomic>\nusing namespace hybridclr::metadata;\nnamespace hybridclr { namespace startup {\nstatic Image* LegacyInterpreterImage(const MethodInfo* methodInfo) {\n    return IsInterpreterMethod(methodInfo) ? MetadataModule::GetImage(methodInfo->klass)\n        : static_cast<Image*>(AOTHomologousImage::FindImageByAssembly(\n            methodInfo->klass->rank ? il2cpp_defaults.corlib->assembly : methodInfo->klass->image->assembly));\n}\nstatic const PropertyInfo* LegacyProperty(Il2CppClass* klass, uint32_t index) {\n    if (index >= klass->property_count) return nullptr;\n#if UNITY_ENGINE_TUANJIE\n    return klass->properties[index];\n#else\n    return &klass->properties[index];\n#endif\n}\nstruct HookTable {\n    int mode;\n`;
for(const h of hooks)cpp+=`    ${h.ret} (*${h.key})(${h.params.join(', ')});\n`;
cpp+='};\n';
for(const h of hooks)cpp+=`static ${h.ret} Legacy_${h.key}(${h.params.join(', ')}) { ${h.ret==='void'?'':`return ${h.fallback};`} }\n`;
cpp+='static const HookTable legacyTable = {\n    2,\n'+hooks.map(h=>`    &Legacy_${h.key}`).join(',\n')+'\n};\n';
cpp+='static const HookTable dheTable = {\n    1,\n'+hooks.map(h=>`    &${h.owner}DheImpl_${h.name}`).join(',\n')+'\n};\n';
cpp+='static const HookTable unselectedTable = {\n    0,\n'+hooks.map(h=>`    &Legacy_${h.key}`).join(',\n')+'\n};\n';
cpp+='static std::atomic<const HookTable*> selectedHooks{&unselectedTable};\nint BindMode(int mode) {\n    if (mode != 1 && mode != 2) return 2;\n    if (selectedHooks.load(std::memory_order_acquire)->mode) return 1;\n    selectedHooks.store(mode == 1 ? &dheTable : &legacyTable, std::memory_order_release);\n    return 0;\n}\nint GetMode() { return selectedHooks.load(std::memory_order_acquire)->mode; }\nstatic const HookTable& GetHooks() { return *selectedHooks.load(std::memory_order_acquire); }\n}}\n';
for(const h of hooks)cpp+=`${h.ret} ${h.owner}${h.name}(${h.params.join(', ')}) { return hybridclr::startup::GetHooks().${h.key}(${h.args.join(', ')}); }\n`;
cpp+='#endif\n';write('hybridclr/AotModeHooks.cpp',cpp);
write('hybridclr/AotModeHooks.inventory.json',JSON.stringify(hooks,null,2)+'\n');
console.log(`Generated ${hooks.length} routed hooks (${hooks.filter(h=>h.group==='Metadata').length} metadata, ${hooks.filter(h=>h.group==='Runtime').length} runtime).`);
