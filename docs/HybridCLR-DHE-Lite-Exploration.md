# HybridCLR DHE-Lite 工作流边界

这个 worktree 用于探索不依赖旗舰版的差分混合执行。本文前半部分保留了早期 Unity 2021
实验记录；当前正式 demo 已锁定为 Tuanjie 1.10 / Unity-compatible 2022.3，基线程序集进入 AOT，运行时加载 current DLL 和 `mv`，变化的方法
进入解释器，未变化的方法继续执行 AOT。实现仍然只覆盖下文列出的严格兼容和 ABI 子集；
正式入口、输出目录和发布门禁以 `HybridCLR-DHE-Formal-Project-Validation.md` 为准。

## Worktree 基线

正式 workflow 不依赖下面的 dirty research worktree；它使用
`manifests/repo-lock.json` 的干净提交，再应用 `manifests/dhe-runtime-lock.json`
中的 patch。下面的 worktree 只保留作历史审计和 patch 生成来源。

| 仓库 | 路径 | 分支 |
|---|---|---|
| `hybridclr` | `worktrees/dhe-experiment-hybridclr` | `research/dhe-lite-v8.13.0` |
| `hybridclr_unity` | `worktrees/dhe-experiment-hybridclr_unity` | `research/dhe-lite-package-v8.13.0` |
| `il2cpp_plus` | `worktrees/dhe-experiment-il2cpp_plus-unity2021` | `research/dhe-lite-il2cpp-unity2021-v8.13.0` |
| `lab` | `worktrees/dhe-experiment-lab` | `research/dhe-lite-lab-v8.13.0` |

这些 worktree 都从当前主工作副本的 `HEAD` 创建。主工作副本中的未提交改动没有被
复制、覆盖或清理。

## 当前闭环

```text
baseline DLL + current DLL
    -> generate-dhe-mv.ps1
    -> method identity / IL hash / type layout diff
    -> strict *.mv.json + *.mv.bytes
    -> Unity AOT Player guard injection
    -> LoadDifferentialHybridAssemblyWithMetaVersion(...)
    -> changed method = interpreter, unchanged method = AOT
```

运行：

```powershell
./scripts/generate-dhe-mv.ps1 `
    -BaselineAssembly C:/path/to/baseline/HybridCLR.ManagedCases.dll `
    -CurrentAssembly C:/path/to/current/HybridCLR.ManagedCases.dll `
    -DnlibPath C:/path/to/project/Packages/com.code-philosophy.hybridclr/Plugins/dnlib.dll `
    -Output staging/dhe/HybridCLR.ManagedCases.mv.json
```

在第一阶段应使用严格门禁：

```powershell
./scripts/generate-dhe-mv.ps1 `
    -BaselineAssembly C:/path/to/baseline/HybridCLR.ManagedCases.dll `
    -CurrentAssembly C:/path/to/current/HybridCLR.ManagedCases.dll `
    -DnlibPath C:/path/to/project/Packages/com.code-philosophy.hybridclr/Plugins/dnlib.dll `
    -Output staging/dhe/HybridCLR.ManagedCases.mv.json `
    -BinaryOutput staging/dhe/HybridCLR.ManagedCases.mv.bytes `
    -StrictCompatibility
```

严格模式只接受已有方法的 IL body 变化，并要求方法 token、调用形状（static/virtual/
abstract/PInvoke）、完整 method attributes 和所有类型布局保持不变。命令会始终写出报告，但发现不兼容项时以非零
状态退出；`-BinaryOutput` 只有在严格门禁通过时才会生成，可以直接交给实验 runtime API。
这份报告可以作为 CI 的发布门禁。

低层 `generate-dhe-mv.ps1` 要求显式传入 `-DnlibPath`，不会从 Demo 或 sibling
checkout 猜测依赖。批处理入口可以通过 `-ProjectRoot` 从项目自己的 embedded
package 解析 dnlib；registry/external package 仍需显式传入路径。

## mv 格式

当前实验同时输出可读的 JSON 和首版二进制 `mv.bytes`：

- `baseline` 和 `current` 保存 DLL SHA-256 与 MVID；
- 方法身份使用 `declaringTypeFullName + methodName + MethodSig`，不依赖 token；
- 每个方法保存基线 token、当前 token、IL body hash 和变化类型；
- `compatibility` 保存严格模式的状态、原因和门禁模式；`tokenStable`、`shapeStable`
  用于审计方法入口是否仍可复用；
- 类型单独保存基类、接口、字段和 value-type 信息；
- 类型布局发生变化时输出 `layoutChanged`，第一版运行时应拒绝这种更新；
- 新增和删除的方法分别输出 `added`、`removed`，后续再决定是否允许。
- 二进制首版使用 `DHEMVLT1` magic，携带严格模式下 changed 方法的当前 token，以及
  baseline/current DLL 的 SHA-256；runtime 会先校验 current DLL，三参数 loader 还会校验
  实际 AOT Snapshot 的 baseline hash。

## 已验证结果

使用现有 `HybridCLR.ManagedCases.dll` 自比较：

```text
methods changed: 0/567
type changes: 0
```

使用 `StandaloneWindows64` 与 `DefineProbe` 两份现有构建比较，可以识别方法和类型的
变化，但也会得到大量结构变化。这证明结构兼容性必须成为独立门禁，不能只按方法 hash
决定是否切换解释器。

受控 `DheProbe` 双版本实验只把 `Calculator.Add` 的 body 从 `left + right` 改为
`left + right + 1`：

```text
methods changed: 1/2
type changes: 0
binary: magic=DHEMVLT1, schema=1, flags=1, changedTokens=[0x06000001]
```

官方 DHE 的总体语义与本实验一致：包内预置被标记程序集的 AOT 代码，启动后加载最新
热更 DLL，再依据离线生成的差分文件决定每个类/函数走 AOT 还是解释器。官方的 `dhao`
文件承担的角色就是这里的 `mv`；本实验先用 JSON 便于审计，运行时稳定后再压缩为二进制。
由于社区版没有官方 DHE 的入口改写和完整元数据版本工作流，第一阶段必须收窄为方法体
差分，不能宣称支持官方文档中的任意增删改。

MetaVersion 工作流还说明了两个工程约束：AOT Snapshot 必须来自已经导出/构建的主包，
不能拿普通 `Generate/All` 产物冒充；不同平台要维护独立 Snapshot。官方通过为每个类型和
函数生成版本号，并用 `signature-mapper` 解决 token 漂移，才可以让一份热更新元数据服务
多个主包。我们的后续扩展应沿用这个分层：先做平台/构建身份校验，再做稳定签名映射，
最后才放开新增、删除和类型变化。

## 当前 native 原型

实验 runtime 新增了 `DheRuntime` 状态和带 Snapshot hash 的
`RuntimeApi.LoadDifferentialHybridAssemblyWithMetaVersion(dllBytes, mvBytes, snapshotHash)`
接口。旧的双参数重载仅为 ABI 兼容保留，调用会返回
`DHE_MV_BAD_SNAPSHOT_HASH`，不会发布 DHE 状态：

1. 解析并校验二进制 `mv`，并校验最新 DLL 的 current SHA-256；
2. 校验实际 AOT Snapshot 的 baseline SHA-256；
3. 复用 `LoadMetadataForAOTAssembly(..., CONSISTENT)` 加载最新 DLL；严格模式保持
   metadata row set 稳定，确保普通非泛型方法也能读取最新 IL；
4. 按程序集注册 changed token 集合和已解析的 `MethodInfo*`；状态发布后使用只读无锁查表，
   生成入口的正常调用不再扫描整个程序集，只有 null method context 才走缓存解析；
5. `MetadataModule::IsImplementedByInterpreter` 对 DHE 程序集只为 changed 方法返回 true。

这已经覆盖“元数据加载 + 方法级选择”的 native 侧原型。普通 AOT direct call 通过生成后
guard 接入；当前由构建后转换器在 Unity 生成 C++ 后、Bee 编译前注入，因此不需要修改
Unity 安装目录中的 `Unity.IL2CPP.dll`。

早期 Unity 2021 兼容 runtime 的组装和 native compile/test gate 已通过；当前正式
Tuanjie 2022 lane 使用同一套 DHE runtime patch lock：

```text
当前 clean checkout + DHE patch staging tree 的 SHA-256 由
`staging/runtime/<profile>/runtime-manifest.json` 记录，并同时绑定
`manifests/dhe-runtime-lock.json` 的 patch 集哈希。
100% tests passed, 0 tests failed out of 1
```

早期真实 Unity 2021 Player 证据（由历史版本 `run-dhe-demo-workflow.ps1` 生成，报告见
`artifacts/dhe-demo-workflow/dhe-player-result.json`）：

```text
loadError=OK
changedToken=100663351, changedMethod=interpreter, addResult=101
unchangedToken=100663352, unchangedMethod=aot, stableResult=4
changedCallingUnchangedMethod=interpreter + AOT callee, addViaStableResult=104
changedMultiArgumentMethod=interpreter, addPairResult=107
changedInt64Method=interpreter, wideResult=1005
changedVoidMethod=interpreter, touchValue=705
changedInstanceMethod=interpreter, instanceAddResult=201
unchangedInstanceMethod=aot, instanceStableResult=6
changedInstanceCallingUnchangedMethod=interpreter + AOT callee, instanceAddViaStableResult=206
interpreterEntryCount=10, aotBridgeCallCount=0, aotEntryCount=10
capabilityDirectPassed=true, capabilityDirectInterpreterEntryCount=30
secondaryAssemblyChangedValidated=true, secondaryAssemblyDirectValidated=true
currentHashValidated=true, baselineHashValidated=true, snapshotHashValidated=true
embeddedSnapshotHashValidated=true
passed=true
```

`aotEntryCount=10` 和 `aotBridgeCallCount=0` 是本次无 probe 构建主路径的调用计数，不能作为性能结论；
开启显式 AOT probe 后计数会随 probe 方法增加。
报告同时覆盖了 changed caller、unchanged static/instance AOT callee、双参数 ABI、`int64` 返回值、
`void` 副作用以及 capability runner 的 generic/async/iterator 结果。resolver/ABI v2 已为
27 个 changed managed token 生成完整 guard；真实 IL2CPP 实例化产生 34 个 native entries。
Exploratory 报告仍固定 `releaseReady=false`，原因是它没有正式 Git/runtime 发布身份，而不是
缺少 generic guard。

## 生成代码实验入口

Unity 2021 的 `MethodWriter.WritePrologue` 位于部署的 `Unity.IL2CPP.dll`，不在
`il2cpp_plus` 源码树中。当前提供一个不修改 Unity 安装目录的生成后实验路径：

```powershell
./scripts/apply-dhe-generated-cpp.ps1 `
    -MvJson staging/dhe/HybridCLR.ManagedCases.mv.json `
    -GeneratedCppRoot path/to/il2cppOutput `
    -InPlace
```

Unity 2021 Player 的正式构建入口是确定性脚本：

```powershell
./scripts/run-dhe-deterministic-player-build.ps1 `
    -UnityExe "C:/Program Files/Unity/Hub/Editor/2021.3.45f2/Editor/Unity.exe" `
    -ProjectPath ./unity2021-dhe-demo `
    -BuildPath ./unity2021-dhe-demo/Builds/DHE-Demo-Workflow/HybridCLRLab.exe `
    -MvJson ./artifacts/dhe-demo-workflow/HybridCLR.ManagedCasesAot.mv.json `
    -TransformerScript ./scripts/apply-dhe-generated-cpp.ps1 `
    -DheAotAssemblies ./artifacts/dhe-demo-workflow/capability/baseline/HybridCLR.ManagedCasesAot.dll `
    -BuildIdentity ./artifacts/dhe-demo-workflow/build-identity.json
```

流程可先用 `resolve-dhe-native-manifest.ps1` 从 IL2CPP 生成代码的托管方法注释解析
native 函数名、返回类型和参数，发布/重复构建时通过 `-ResolvedManifest` 复用这份结果，
让转换器在 C++ 生成后快速注入。`inject-dhe-guard.ps1` 会按 ABI 生成 guard；除普通静态/实例、
virtual、state-machine 和常见值类型形状外，v2 invoke-args bridge 还覆盖 generic concrete、
gshared concrete、引用参数、合法 null 引用和 FGS hidden return。一个 managed generic token
可以对应多个 native entry。managed ref/out、unsafe pointer 和 byref return 继续 fail-closed。
找不到唯一生成定义或遇到未声明形状会直接失败；重复执行是幂等的。真实 Player 已覆盖
direct/reflection/null/generic virtual 组合，fixture 另验证 3 个 managed generic token 到
10 个 native entries 的分组契约。

Unity 2021 的 demo 使用按 ABI 分派的直接解释器 helper，避免社区版
`methodPointerCallByInterp` 在这个生成入口场景下回落到 AOT entry 的递归问题：

```cpp
if (hybridclr::dhe::ShouldDispatchToInterpreter(dheMethod))
    return hybridclr::dhe::ExecuteInterpreterI4I4(dheMethod, ___0_value);
```

这些路径已经在 Unity 2021 实际生成的 `HybridCLR.ManagedCasesAot.cpp` 上完成注入、编译
和 Player 运行验证。`patch-dhe-method-body.ps1` 和
`unity2021-dhe-demo/run-dhe-player-build.ps1` 保留为历史诊断材料，不属于正式发布入口。
`inject-dhe-aot-probe.ps1` 仅在显式传入
`-EnableDispatchDiagnostics -AotProbeDeclaringType ... -AotProbeMethodName ...` 时调用，
用于验证 AOT 入口计数；普通构建不会注入 probe，也不会改变发布 runtime 的语义。
整个方案仍是构建后转换，不是对 Unity 官方生成器的永久补丁；Unity 升级后需要重新确认
生成代码签名和构建时序。

### AOT 入口验证

Unity 2021 生成的普通方法形如：

```cpp
int64_t PerformanceWorkload_Execute(..., const RuntimeMethod* method)
{
    // 当前生成器直接从这里开始执行旧 AOT body
}
```

实验 native test 中的 `GeneratedLikeDheEntry` 已用一个精确的
`int32_t(int32_t, const MethodInfo*)` ABI 验证状态查表和未变化 AOT 分支。真实 Player 则
额外验证了生成 guard、`ExecuteInterpreterI4I4`、current IL 执行和 snapshot 校验的组合。

## 仍缺失的能力

现有社区运行时已经具备 `AOTHomologousImage`、解释器 method pointer 和
`methodPointerCallByInterp`；本工作流仍缺少以下生产能力：

1. 将生成后 guard 收敛为稳定的 IL2CPP/Unity 生成器补丁，避免依赖生成后转换时序；当前
   转换器已支持预解析 manifest，但仍属于构建后路径；
2. 在目标平台继续验证尚未进入 Demo 的 managed ref/out、unsafe pointer/byref return、
   任意 struct/adjustor thunk 等 ABI；当前 Windows x64 Demo 的 27 个 changed token 已完整
   覆盖，但不能外推到 Android、小游戏或其它 IL2CPP 生成器版本；
3. 放开新增/删除方法、token 漂移和类型布局变化前，先实现 signature mapping 与更完整的
   metadata version；
4. 为每个平台和每个主包 build identity 生成、保存并校验独立的 AOT Snapshot。

特别是第 3 项不能用普通 `Assembly.Load` 或修改 `MethodInfo.methodPointer` 替代。AOT
函数之间的 direct call 通常是 C++ 静态调用，必须在生成函数入口处检查
`isInterpterImpl`，才可能跳转到 `methodPointerCallByInterp`。

## 建议的最小运行时契约

第一版可以把加载接口收敛为：

```text
LoadDheAssemblyWithMv(dllBytes, mvBytes)
  1. 校验 mv magic/schema、程序集名、current DLL hash、基线 AOT hash 和平台/build identity
  2. 以 CONSISTENT homologous metadata 加载最新 DLL；严格模式保持 metadata row set 稳定
  3. 将 mv 中的 changed method token 注册到该程序集的 DHE 状态
  4. 仅对 changed 方法准备 interpreter method pointer
```

`MetadataModule::IsImplementedByInterpreter` 已改为“程序集是 DHE 且方法在 changed 集合中”
时才返回 true。未变化方法仍保留原生 `methodPointer`。当前生成后转换为 DHE 方法生成入口
检查，概念上类似：

```cpp
if (hybridclr::dhe::ShouldDispatchToInterpreter(method)) {
    return hybridclr::dhe::CallInterpreter(method, ...);
}
```

这段代码必须按返回值、参数、实例/静态、value type adjustor、virtual/delegate 等 ABI
分别生成，不能用一个无类型的 `void*` 转发函数覆盖全部方法。

这里选择 `CONSISTENT` 不是最终 DHE 的限制，而是首版的安全边界。官方 MetaVersion
通过 signature mapping 和更完整的 metadata version 处理 token 漂移、新增/删除类型；
那一层完成前，不能把 `SUPERSET` 当成普通方法体差分的替代品。

## 下一步

当前可继续沿着 ABI 矩阵扩展：每加入一种签名，必须同时增加 native function-pointer
compile gate、guard fixture 和真实 Player 断言。任何放宽兼容范围的改动都必须更新 `mv`
严格检查、类型布局门禁和平台 snapshot 校验；在此之前不要把 `SUPERSET` 或 token 漂移当作
方法体差分的等价替代。

## 参考

- [差分混合执行](https://www.hybridclr.cn/docs/business/differentialhybridexecution)
- [MetaVersion 工作流](https://www.hybridclr.cn/docs/business/ultimate/metaversionworkflow)
