# HybridCLR 完全泛型共享正式合并设计

> 历史 opt2/opt3 记录。文中的 PowerShell helper 已移除；当前 DHE 验证
> 使用 `HybridCLR-DHE-Toolchain.md` 记录的 C# host 命令。

更新时间：2026-08-25

## 1. 决策

完全泛型共享兼容代码进入 `optimize/v8.13.0`，由同一份 HybridCLR runtime
同时支持三条工作流：

| 工作流 | 引擎 | FGS | IL2CPP codegen | 补充 AOT 元数据 |
|---|---|---|---|---|
| `Tuanjie2022Fgs` | 团结 1.10 / Unity 2022.3.62t12 | 开启 | `OptimizeSize` | 不打包、不加载 |
| `Unity2022Fgs` | Unity 2022.3 | 开启 | `OptimizeSize` | 不打包、不加载 |
| `Unity2021Standard` | Unity 2021.3.45f2 | 关闭 | `OptimizeSpeed` | 打包并加载 |

运行时代码不通过项目开关维护两份实现。它只根据实际 `MethodInfo` 是否具有
full-generic-sharing signature 选择 ABI；引擎版本、IL2CPP 代码生成模式和元数据
打包方式由构建工作流负责。

运行时逻辑位于 HybridCLR，但每个固定 `il2cpp_plus` 输入都必须在 `MethodInfo`
的 HybridCLR 扩展区增加 `hasFullGenericSharingAotInvoker` 与
`fullGenericSharingPreparationState`。前者记录 IL2CPP redirect invoker 包装前是否
以 missing sentinel 标记 AOT 实例缺失，后者记录该方法的一次性准备状态；它们
属于两侧必须同步的内部 ABI，不能只更新 HybridCLR 源码。

## 2. 兼容目标与边界

正式兼容目标为：

- 团结 `1.10.0 / 2022.3.62t12`，`il2cpp_plus v2022-tuanjie-8.13.0`；
- Unity `2022.3`，`il2cpp_plus v2022-8.11.0`；
- Unity `2021.3.45f2`，`il2cpp_plus v2021-8.1.0`；
- Windows x64 Release Player；
- 同一份 `optimize/v8.13.0` HybridCLR 源码。

Windows Player 是当前合并门禁。Android ARM64 已有构建和测试通道，但真机
correctness、native test 与 benchmark 仍是移动端发布门禁，不能用本文件的
Windows 结果替代。

## 3. ABI 契约

### 3.1 普通方法

当 `IsFullGenericSharingMethod(method) == false` 时保持既有路径：

1. `InitAndGetInterpreterDirectlyCallMethodPointer` 准备
   `methodPointerCallByInterp`；
2. transform 阶段按签名选择生成的 managed-to-native bridge；
3. reflection fallback 调用 `invoker_method(methodPointerCallByInterp, ...)`。

Unity 2021 在关闭 FGS 时全部落在这条路径，因此现有补充元数据行为不变。

### 3.2 FGS 方法

当 `has_full_generic_sharing_signature` 为真时，普通签名桥与实际 fully-shared ABI
不兼容。正确契约为：

1. 首次使用时执行一次解释器 lazy initializer，区分 IL2CPP redirect invoker、
   missing invoker 和解释器实现；
2. 使用最终的 `invoker_method`；
3. 向 invoker 传入 `method->methodPointer`，而不是
   `methodPointerCallByInterp`；
4. 使用 `Managed2NativeCallByReflectionInvoke` 完成参数和返回值封送；
5. 运行期解析出的实际方法带 FGS signature 时，覆盖 transform 阶段缓存的普通
   签名桥。

IL2CPP 构造 FGS `MethodInfo` 时必须在安装 redirect invoker 前记录原始 AOT
invoker 是否存在。补充元数据只在原始 AOT invoker 缺失时启用解释器回退；已有
有效 AOT invoker 的 FGS 方法即使所属程序集加载了补充元数据，也保持原来的 AOT
指针和 invoker，不允许整体降级到解释器。

FGS redirect invoker 会无条件调用保存下来的 raw invoker，因此 raw table invoker
不能为 `nullptr`。构造 FGS `MethodInfo` 时必须把空 invoker 规范化为
`Runtime::GetMissingMethodInvoker()`，并把该实例标记为没有有效 AOT invoker；只有非空且
不等于 missing sentinel 的 raw invoker 才可调用。真正允许为空的是下面所述的
`methodPointer` discriminator。

所有调用 `InitAndGetInterpreterDirectlyCallMethodPointer` 的入口都遵守这项规则。
因此三个固定 IL2CPP 版本中现有的 reflection/delegate hook 不需要分别打补丁：
即使 FGS `MethodInfo` 预置了非空 stub/redirect pointer，统一 helper 仍会先执行
`PrepareFullGenericSharingMethod`。

`method->methodPointer == nullptr` 对 FGS redirect invoker 是合法状态。该参数可被
IL2CPP 用作 direct/virtual 分派判别值，并不必然是待直接调用的函数地址。因此：

- FGS 准备成功的条件是 `invoker_method` 既非空，也不是
  `Runtime::GetMissingMethodInvoker()`；
- 即使初始 invoker 非空，只要 lazy initializer 尚未运行，就必须先初始化；解释器
  实现会安装自己的 invoker，AOT 方法则保留 IL2CPP redirect invoker；
- 不得以 `methodPointer != nullptr` 作为 FGS 可调用性的前置条件；
- nullable discriminator 必须保留原值传给 invoker。

`methodPointerCallByInterp` 同时作为普通方法 lock-free fast path 的完成标志。
initializer 必须先写入 invoker、公开 method pointer、virtual pointer 和
`isInterpterImpl`，最后用 release publish 写入该字段；virtual helper 也必须先观察
direct helper 的完成发布。不得让另一个线程通过非空 pointer 观察到半初始化的
`MethodInfo`。

FGS preparation 是首用不可变决策。状态从未准备进入准备中，最终只发布为已准备；
已经发布的 AOT/解释器选择不会因后续补充元数据加载而失效，也不会运行中改写已被
委托、虚表或 IL2CPP invoker 缓存的指针。`NotifyAOTMetadataLoaded` 只递增注册
epoch，使尚未准备的方法在与元数据注册并发时明确线性化到注册前或注册后；它不会
重新打开已准备方法。`UINT32_MAX` 只表示锁内发布尚未完成。所有可能进入
metadata/type 初始化或抛出异常的解析都必须在获取 method-pointer 锁之前完成，因此
不得形成 `s_methodPointerInitLock -> g_MetadataLock` 的锁边。

因此需要补充元数据的工作流必须在加载热更新程序集、创建相关委托和启动业务线程
之前完成 `LoadMetadataForAOTAssembly`。方法首次准备后再加载元数据仍可服务其他
尚未准备的方法，但不会把该方法从 AOT/missing 状态切换为解释器。这个限制用于
避免对已发布 `MethodInfo`、delegate 和 vtable 执行指针进行不安全的在线替换。

`MethodInfo` 的整体复制必须通过 `CopyMethodInfo` 与 preparation 使用同一把锁，
并把目标对象的 preparation state 重置为未准备。这样数组泛型方法及其他 clone
不会继承源对象的 in-progress sentinel，也不会在源对象改写执行字段时复制到半
初始化快照。

这条规则已有原生回归测试。测试分别覆盖空 raw invoker 到 missing sentinel 的
规范化，以及空 `methodPointer` 仍由有效 redirect invoker 接收的 ABI 契约。

## 4. 代码结构

核心改动位于以下边界：

- `hybridclr/Il2CppCompatibleDef.h/.cpp`
  - `IsFullGenericSharingMethod` 集中隔离版本字段；
  - `PrepareInterpreterManaged2NativeCall` 按普通/FGS ABI 判断可调用性；
  - `GetInterpreterInvokerMethodPointer` 选择传给 invoker 的指针；
  - 解释器方法初始化时同步修正 FGS invoker 和 method pointer。
- 三个固定 `il2cpp_plus` 的 `libil2cpp/il2cpp-class-internals.h`、
  `metadata/GenericMethod.cpp` 与 `metadata/ArrayMetadata.cpp`
  - 在 HybridCLR 扩展字段中保存原始 AOT invoker 可用位与 FGS preparation state；
  - 在 redirect 包装前记录原始 invoker 是否缺失；
  - 所有 `MethodInfo` clone 使用带锁复制并重置 preparation state；
  - 新字段由现有零初始化分配路径初始化为未准备状态。
- `hybridclr/interpreter/InterpreterModule.h/.cpp`
  - FGS 方法固定选择 `Managed2NativeCallByReflectionInvoke`；
  - reflection 与 delegate invoker 使用正确的指针参数；
  - `ResolveRuntimeManaged2NativeMethodPointer` 处理运行期实际目标与 transform
    缓存桥 ABI 不一致的问题。
- `hybridclr/RuntimeApi.cpp`、`hybridclr/interpreter/InterpreterProfile.h`
  - 仅在 `HYBRIDCLR_LAB_FGS_TESTS` 下分别记录实际进入 reflection bridge 的次数和
    实际进入 interpreter invoker 的次数；运行期加载的热更新泛型方法不保证带
    IL2CPP 生成期的 FGS signature bit，因此后一个计数以 invoker 入口处的
    `MethodInfo::is_inflated` 为准；
  - delegate bridge 计数按 `curMethod` 实际目标判断 FGS，不能按外层 delegate
    `Invoke` 方法判断；
  - single delegate 只在最终选中的 static/instance bridge 上解析一次，不能因同时
    预解析两种候选桥把一次调用记为两次；
  - 暴露测试专用 reset/read internal call；生产 runtime 不定义该宏时为空实现，
    不引入原子计数开销。
- `hybridclr/interpreter/Interpreter_Execute.cpp`
  - delegate、动态 `callvirt` 和携带 `MethodInfo` 的 `calli` 在执行时重新选择桥。
- `hybridclr/transform/TransformContext.cpp`
  - direct call/newobj 在 transform 时使用统一准备逻辑；
  - 对可在运行期改变实际目标的方法保留执行期覆盖能力。
  - 数组 bounds-check 消除只接受 entry block 单次赋值且 local 地址未泄露的事实；
    循环中多次赋值的 range 需要 CFG fixed-point，在实现前不得以单遍扫描结果消除
    bounds check。

这些文件还包含独立的解释器性能优化。FGS 合并和回滚必须按上述符号及调用点
操作，不能整文件替换。

## 5. 三套生产工作流

### 5.1 团结引擎 2022 FGS

生产配置：

- 开启完全泛型共享；
- IL2CPP code generation 使用 `OptimizeSize`；
- Player 不打包补充 AOT 元数据；
- 启动时不调用 `LoadMetadataForAOTAssembly`；
- 构建结果必须记录 `aotMetadataPackaging=exclude`，Player 结果必须记录
  `aotMetadataMode=none` 和 `fileCount=0`。

团结 2022.3.62t12 的 Editor 构建管线直接以
`Il2CppCodeGeneration == OptimizeSize` 设置 `EnableFullGenericSharing`；
`OptimizeSpeed` 不开启 FGS，因此不能作为 FGS 无元数据门禁。

`include/none` 只用于证明运行时不依赖包内文件，不能替代 `exclude/none` 的生产
门禁。

### 5.2 Unity 2022 FGS

Unity 2022 与团结使用相同的 FGS ABI 契约，但基线必须来自独立的
`il2cpp_plus v2022-8.11.0`，不能复用 `v2022-tuanjie-8.13.0`。生产配置同样使用
`OptimizeSize/exclude/none`；生产和诊断 profile 分别命名为 `Unity2022-Candidate`
与 `Unity2022-Fgs-Diagnostic`，避免覆盖团结的 staging、Player 和报告。

当前机器没有 Unity 2022 Editor。原生兼容门禁以 Unity 2022 宏、`TUANJIE_VERSION=0`
和 Unity 2022 `libil2cpp` 编译，临时使用团结 2022.3 external/baselib 头并在 runtime
manifest 中记录 `surrogate=true`。该结果只能证明源码接口、FGS 回归和并发契约，不能
替代真实 Unity 2022 Editor 生成、Player correctness、diagnostics 与 differential 门禁。
替代头文件默认被拒绝；仅显式传入 `-AllowSurrogateExternalHeaders` 时才执行这类原生
测试，此时兼容矩阵保留 `passed=true` 表示测试本身通过，但必须输出 `mergeReady=false`。

### 5.3 Unity 2021 standard

生产配置：

- 关闭完全泛型共享；
- IL2CPP code generation 使用 `OptimizeSpeed`；
- Player 打包裁剪后的补充 AOT 元数据；
- 启动时、加载热更新程序集和启动业务线程前逐个调用
  `LoadMetadataForAOTAssembly`；
- 构建结果必须记录 `aotMetadataPackaging=include`，Player 结果必须记录
  `aotMetadataMode=supplemental` 和至少一个已加载文件。

Unity Editor API 存在版本差异：2022+ 使用
`PlayerSettings.SetIl2CppCodeGeneration`，2021 使用
`EditorUserBuildSettings.il2CppCodeGeneration`。预处理条件为
`UNITY_2022_1_OR_NEWER`。Standalone 构建入口还必须显式清除持久化在 `Library`
中的 `CreateSolution` 平台设置，并在 `BuildPipeline` 报告成功后验证目标 Player
文件真实存在，避免交互式导出设置造成只生成 Visual Studio solution 的假成功。

Unity 2021 使用由 `prepare-engine-test-project.ps1` 生成的独立工程。复制团结工程
资源后删除 `.meta`，由 Unity 2021 重新生成 GUID，避免团结格式的 GUID 被旧版
Editor 拒绝；同时移除 `com.unity.modules.infinity` 和当前工程未使用、但版本不兼容的
`com.unity.ai.navigation`，避免旧版 Package Manager 或编译器在运行时安装前失败。

## 6. 合并门禁

每次修改 FGS 调用桥、MethodInfo 初始化、动态调用或 delegate 路径后必须执行：

```powershell
./scripts/run-runtime-compatibility-matrix.ps1 `
  -HybridClrSource C:/hybridclr_optimize/worktrees/hybridclr-fgs-compatibility-v8.13.0 `
  -AllowDirty

./scripts/run-engine-workflow-gate.ps1 `
  -EngineWorkflow Unity2022Fgs `
  -HybridClrSource C:/hybridclr_optimize/worktrees/hybridclr-fgs-compatibility-v8.13.0 `
  -AllowDirty

./scripts/run-engine-workflow-gate.ps1 `
  -EngineWorkflow Unity2021Standard `
  -HybridClrSource C:/hybridclr_optimize/worktrees/hybridclr-fgs-compatibility-v8.13.0 `
  -AllowDirty

./scripts/run-engine-workflow-gate.ps1 `
  -EngineWorkflow Tuanjie2022Fgs `
  -HybridClrSource C:/hybridclr_optimize/worktrees/hybridclr-fgs-compatibility-v8.13.0 `
  -AllowDirty
```

必须同时满足：

- 三个版本的 merged `libil2cpp` 均可编译，原生 CTest 全部通过；
- Unity 2022：真实 Editor 安装后必须完成与团结相同的三个生产组合和一个诊断组合；
  surrogate external headers 的原生结果不得替代 Player 结果；
- Unity 2021：`OptimizeSpeed/include/supplemental` 全部通过、differential 0，
  FGS diagnostics 必须关闭，两个顶层计数与全部 per-case 增量均为 0；
- 团结引擎：`OptimizeSize/include/supplemental`、`OptimizeSize/include/none`、
  `OptimizeSize/exclude/none` 三项使用未插桩 `Candidate`，全部通过且 differential 0；
- 另以 `Fgs-Diagnostic/OptimizeSize/exclude/none` 运行插桩门禁，要求 AOT bridge
  与 interpreter invoker 顶层计数均大于 0，并要求
  `boundary_fgs_aot_calls_hot_generic_delegate` 的 per-case interpreter invoker
  增量严格等于 1；calli、direct virtual、closed/open delegate 的每个 case 必须
  各记录 1 次实际 bridge，multicast 两个目标必须记录 2 次；
- FGS 边界覆盖 static、interface/virtual、reflection、delegate、calli、enum、
  nullable、ref、数组、AOT 调用热更新泛型目标及 small/large/void return；
- runtime/build manifest 的 `hybridclr.path` 指向
  `worktrees/hybridclr-fgs-compatibility-v8.13.0`，不得指向研究 worktree 或性能优化
  工作区；源码 path、commit 与 `treeSha256` 必须同时匹配；
- 正式脚本默认拒绝脏 HybridCLR 源码；本地开发必须显式使用 `-AllowDirty`，并在
  manifest 中记录 path、commit 和 dirty 状态；
- `-SkipAssembly` 与 `-SkipBuild` 仍必须核对源码 path/commit/tree hash、build
  identity、staged runtime hash、managed assembly hash 和完整运行结果，不能仅凭
  commit 或摘要字段放行；
- `git diff --check` 无错误。

性能不是 ABI 正确性的替代条件。合并前至少保留一个生产组合下的
`interp_generic` 10 独立进程结果，校验 checksum、唯一 PID、零元数据加载时间，
并以策略中的噪声阈值判断是否需要阻断。

## 7. 2026-08-25 证据状态

迁移前 `C:\hybridclr_optimize\repos\hybridclr` 中混合 FGS 与解释器性能优化的 dirty
结果只保留为历史对照。正式证据来自四个隔离且 clean 的分支提交：

- HybridCLR `ae21c37ee25992c4c62c77acffaf74b59f4461a3`；
- 团结 il2cpp_plus `68fbca2345d6c781c3f3d7ea2495942617cde277`；
- Unity 2022 il2cpp_plus `684ad89ea38d08629c3aaac32a048dd05984eff8`；
- Unity 2021 il2cpp_plus `13a30454da9fe3b17c35115fdec1beab7984f4a6`。

对应源码 tree SHA-256 为 HybridCLR
`B30A9903348AA86ED4AFE3B554CB9F1E8A777959ED8A20F1EF6BABC6C0A71647`、团结
`A97F9F08935520F8A14C2CADBF4A90AB15FCE05F3C1AD4613ABEA0565858CA6E`、Unity 2022
`5A1B286EFAA415382B83531432CA3C0A9439F05EEEF00139D2EF1C1E50BFC80A`、Unity 2021
`CD9D5D710B5806111124875FD750AF12365421CACC8B36F12913E0314F4367B6`。下表结果的
runtime/build manifest（适用时）均匹配这些提交和 tree hash，并记录源码 `dirty=false`。

| 当前门禁 | 结果 | diagnostics | runtime SHA-256 |
|---|---:|---:|---|
| Workflow / Metadata 原生兼容矩阵 | 两个 scope、三个引擎均通过 | profile-dependent | 团结 `FE3A10...D4C9B` / `E5886E...4B23`；Unity 2022 `EF1F7A...681A`；Unity 2021 `6EAD3D...15FFF` |
| Unity 2022 production / diagnostic Player | 待真实 Editor | 待验证 | native runtime `EF1F7A...681A`，external headers 为 surrogate |
| Unity 2021 production | 220/220，diff 0，4 个元数据文件 | 关闭，计数 0/0 | `6EAD3D...15FFF` |
| 团结 OptimizeSize/include/supplemental | 220/220，diff 0，4 个元数据文件 | 关闭，计数 0/0 | `E5886E...4B23` |
| 团结 OptimizeSize/include/none | 220/220，diff 0，加载 0 个元数据文件 | 关闭，计数 0/0 | 同上 |
| 团结 OptimizeSize/exclude/none | 220/220，diff 0，包内无元数据文件 | 关闭，计数 0/0 | 同上 |
| Fgs-Diagnostic OptimizeSize/exclude/none | 220/220，diff 0 | FGS dispatch 201，generic interpreter 5 | `FE3A10...D4C9B` |

生产组合 `OptimizeSize/exclude/none` 包内补充元数据为 `0` bytes，Player 记录
0 个文件和 0 ns 加载时间。两个 `include/supplemental` 组合均加载 4 个文件并
通过，因此开启 FGS 后继续补充元数据不会改变 ABI 路径或造成冲突；生产
`exclude/none` 则证明运行时不依赖这些文件。

诊断组合中 `boundary_fgs_aot_calls_hot_generic_delegate` 的 generic interpreter
增量为 1，证明 AOT fully-shared delegate 实际进入了热更新泛型解释器目标，而不
只是命中了 bridge 选择代码。

原生兼容矩阵还包含有效 AOT 不降级、缺失 AOT 首用前回退、首用后状态不可变、
`MethodInfo` clone 状态重置和 8 线程并发首次准备同一个 FGS `MethodInfo` 的回归
测试；所有线程必须观察到完整的解释器 invoker 和 method pointer 发布结果。
Windows/Android 测试 stub 使用各平台原生 semaphore/thread API，不修改业务断言
或预期结果。

当前团结 `Candidate` staging 不定义 `HYBRIDCLR_LAB_FGS_TESTS`，用于三个 FGS 生产等价组合；
`Fgs-Diagnostic` 才写入测试配置头。Unity 2022 使用独立的 `Unity2022-*` profile。
runtime manifest 记录 HybridCLR 与
il2cpp_plus 的源目录 tree SHA，避免 dirty 工作树在同一 commit 下被错误复用。
已执行的两套 Editor 门禁以及已准备的 Unity 2022 工程均使用
`artifacts/engine-projects/<workflow>` 中的生成项目，并将
`hybridclr_unity` 作为不含 `.git` 的 embedded package 快照，编辑器不会再改写共享依赖仓库。

当前 managed reference 为 `220/220`。旧 runtime SHA
`5A366A...A11E9`（团结）和 `CD1D44...FF787`（Unity 2021）的 217-case 结果仅作
历史回归参照，不属于当前源码证据。

当前生产 `Candidate/OptimizeSize/exclude/none` 的 `interp_generic` 烟测使用 10 个
独立 Player 进程（10 个唯一 PID），checksum 为 `2000001000000`，元数据加载时间为
0；中位数 `4.929225 ns/op`，P95 `5.0209 ns/op`，relative MAD `0.13491%`，P95
偏离 `1.85983%`，低于策略建议的 5% 噪声阈值。结果绑定 build identity
`E7F2021298341C728CAB41C71377977BCB2E6EE874BC9C59BFA8C4B0E5FFE2E1`、managed
assembly `663F37028B941B7375E6DF6E722DC6189593FCCE026CD9034DAABBF032A27EFA` 与生产
runtime `E5886EFDFC62E409B950D234090216422E6955DC3EB4347C0F202D4861D24B23`。

主要证据：

- `reports/fgs-unity2022-runtime-compatibility-workflow.json`
- `reports/fgs-unity2022-runtime-compatibility-metadata.json`
- `reports/compatibility-unity2021-standard-build-manifest.json`
- `reports/compatibility-unity2021-standard-player-result.json`
- `reports/compatibility-unity2021-standard-differential-result.json`
- `reports/candidate-optimizesize*-build-manifest.json`
- `reports/candidate-optimizesize*-player-result.json`
- `reports/candidate-optimizesize*-differential-result.json`
- `reports/fgs-diagnostic-optimizesize-nometadata-*.json`
- `reports/fgs-merge-ready-candidate-optimizesize-nometadata-interp-generic-10.json`
- `native-unit-tests/hybridclr_native_tests.cpp`

## 8. 回滚边界

代码回滚只撤销第 4 节列出的 FGS helper 及其调用点，并保留其他解释器优化。
门禁脚本、三版本 manifest 和 FGS 回归用例不回滚，它们用于证明回滚后的预期
能力边界。

运行配置的应急回滚必须成套进行：关闭 FGS、恢复 `OptimizeSpeed`、打包并加载
补充 AOT 元数据。只恢复 supplemental 文件但继续生成 fully-shared 方法，不能
规避错误 ABI 桥，因此不是有效回滚。

出现以下任一情况时阻止合并或回滚 FGS 代码：

- 任一引擎编译失败；
- Unity 2021 普通路径行为变化；
- 任一 Player case 或 differential 失败；
- no-metadata Player 实际加载了元数据，或 exclude 构建仍携带元数据；
- 重新引入 FGS `methodPointer` 非空前置条件；
- 运行期实际方法仍使用 transform 缓存的普通 ABI 桥。
