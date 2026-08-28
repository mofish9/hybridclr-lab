# HybridCLR Assembly.Load 优化正式方案

文档状态：源码候选已冻结并具备正式评审材料；生产发布仍需完成同身份 Player 重采样和 Android ARM64 门禁。

更新时间：2026-08-28

## 1. 结论摘要

本方案针对热更新程序集的 `Assembly.Load` 冷加载路径。核心做法是把不属于加载必需路径的
metadata 构建拆成按需阶段，并在类、方法和类型缓存层减少重复分配、锁竞争和跨边界调用。
它不减少 DLL 的读取字节，也不依赖完全泛型共享（FGS）；FGS、Interpreter 和 DHE 均保持独立
工作区和独立验收。提交候选使用 `hybridclr` commit
`f40c6f08ccd0391ad9285276b4cc21ada3a180ab`、package commit
`04dd2067f2b52624b778913afae2a4e627d2c4af`；三个 engine hook 的正式分支和 commit
见 `manifests/repo-lock.json`。

冻结组合：

| 仓库 | ref | commit |
|---|---|---|
| `hybridclr_unity` | `optimize/v8.13.0` | `04dd2067f2b52624b778913afae2a4e627d2c4af` |
| `hybridclr` | `optimize/assembly-load-metadata-v8.13.0` | `f40c6f08ccd0391ad9285276b4cc21ada3a180ab` |
| `il2cpp_plus` / Tuanjie 2022 | `optimize/assembly-load-metadata-tuanjie-1.10-v8.13.0` | `82162cb681f1564807d2ab5e7f3a9312271d0a73` |
| `il2cpp_plus` / Unity 2022 | `optimize/assembly-load-metadata-unity2022-v8.11.0` | `bf15337e189ae7da5876aa51c9b896a36c52a155` |
| `il2cpp_plus` / Unity 2021 | `optimize/assembly-load-metadata-unity2021-v8.1.0` | `3e38f1b77304c40e45c89bb583d417602e0bc44d` |

在 Windows x64 的正式 workload（2051 个类型、45065 个成员、27653 个 CustomAttribute，
每端 100 个独立进程）上，历史稳定候选得到如下结果：

| 指标 | Tuanjie 2022 | Unity 2022 | Unity 2021 |
|---|---:|---:|---:|
| `Assembly.Load` median | 11.994 -> 2.306 ms（-80.77%） | 待同身份重采样 | 10.053 -> 2.106 ms（-79.05%） |
| `Assembly.Load` P95 | -80.07% | 待同身份重采样 | -79.57% |
| `Assembly.Load` P99 | -82.46% | 待同身份重采样 | -79.75% |
| Load-only private bytes | -42.80% | 待同身份重采样 | -31.14% |
| Load + Entry median | -9.97% | 待同身份重采样 | -9.21% |
| Load + Reflection median | -9.59% | 待同身份重采样 | -9.20% |
| Reflection touch median | +1.97% | 待同身份重采样 | -0.01% |
| Entry execute median | -17.53% | 待同身份重采样 | -9.79% |

三端 native compile gate 和 native tests 通过；Unity 2022 冻结候选已完成 Player
correctness `220/220`、differential `0`，团结和 Unity 2021 的同项记录也均为
`220/220`、differential `0`，但属于较早产物身份。上表是已完成的历史实测证据，不是冻结候选的最终发布证明；
候选源码已提交，但仍必须使用这些 commit 重新构建并重新采样。Android ARM64 尚未完成同一身份的 Player correctness、
PSS 和尾延迟门禁，因此当前结论是“方案可提交评审，发布有条件通过”。

## 2. 目标与边界

### 2.1 目标

- 降低热更新程序集首次 `Assembly.Load` 的 CPU 时间和 load-only 内存峰值。
- 保持 `System.Reflection`、泛型约束、属性/事件、CustomAttribute、接口、嵌套类型、VTable
  和解释器入口的语义一致性。
- 支持 Tuanjie 2022（基于 Unity 2022）、Unity 2022 和 Unity 2021，并让业务可以在 Loading/Lobby/切场景
  阶段预算化预热可预测的首用路径。
- 让 correctness、性能、内存和产物身份可以重复验证和审计。

### 2.2 非目标

- 不复制商业版闭源实现，不改变 DLL 格式或托管 API 语义。
- 不把 FGS、Interpreter 指令优化、DHE 或 AOT metadata packaging 的收益计入本方案。
- 不承诺一次预热覆盖动态反射、字符串类型查找、资源回调、动态代理或未知接口实现。
- 不以 Windows private bytes 直接替代 Android PSS/RSS 结论。

### 2.3 优化边界

`Assembly.Load` 优化与 metadata 优化是同一条运行时路径的两个观察面：加载阶段决定哪些
metadata 立即物化，metadata 的延迟程度直接决定 `Assembly.Load` 时间；但 Reflection touch
和 Entry execute 观察的是被推迟的工作。因此验收必须同时看：

```text
Load-only       -> 加载阶段是否变短、变省内存
Load + Reflection -> 延迟工作是否只是后移，是否有尾延迟回退
Load + Entry     -> 首次入口是否承接了过多成本
steady-state     -> 后续重复调用是否保持原有行为和性能
```

FGS 可以在 Tuanjie/Unity 2022 的生产 workflow 中单独启用，但本方案的 metadata comparison
关闭 FGS diagnostics；Unity 2021 workflow 明确关闭 FGS。这样不会把“无 AOT metadata”或 FGS
收益误算为 `Assembly.Load` 优化收益。

## 3. 运行时架构

### 3.1 加载生命周期

基线在加载时倾向于建立完整的类型成员、签名、布局、属性和虚表信息。候选实现把生命周期拆为：

```text
Assembly.Load
  ├─ 读取并校验 image/table 头部
  ├─ 建立 TypeDef/MethodDef/FieldDef 的轻量索引
  ├─ 建立可发布的地址稳定 cache 容器
  └─ 不执行完整成员签名、属性数据、布局和 VTable 物化

首次 Type/Reflection/Entry 触达
  ├─ 在 metadata lock 下按类型批量补齐所需层
  ├─ 发布完成标志（release）；读侧 acquire 后才能使用
  └─ 失败保持可观察，不伪装成已完成
```

延迟不是删除 metadata。任何真实访问仍会付出对应成本；预热接口只是把这些成本放到业务
可控的非首帧时间窗口。

### 3.2 Metadata lazy 分层

当前保留的实现面如下：

| 层 | 加载阶段 | 首次触达行为 | 主要实现位置 |
|---|---|---|---|
| Image/table | 头部、行数、范围和边界校验 | 按 token/行解析 | [`InterpreterImage.cpp`](/C:/hybridclr_optimize/worktrees/hybridclr-metadata-v8.13.0/hybridclr/metadata/InterpreterImage.cpp) |
| Method | MethodDef 头和范围 | 签名、返回值、参数、默认值、参数特性按类型批量初始化 | `EnsureMethodMetadataInitialized*` |
| Field | field 范围和轻量索引 | 字段类型、static/thread-static 相关信息按需解析 | `EnsureFieldMetadataInitialized*` |
| Generic | 轻量约束索引 | 约束类型真正需要时解析 | `InterpreterImage.cpp` |
| CustomAttribute | parent/token/data 索引 | data blob 和 ctor 分层解析；ThreadStatic ctor 走 raw TypeRef | `EnsureCustomAttributesInitialized` |
| Property/Event | 范围索引 | Property、Event、MethodSemantics 一次性建立 | `EnsurePropertyEventMetadataInitialized` |
| Layout | 必要计数 | 有字段时计算布局；无静态字段时跳过对应布局 | [`ClassFieldLayoutCalculator.cpp`](/C:/hybridclr_optimize/worktrees/hybridclr-metadata-v8.13.0/hybridclr/metadata/ClassFieldLayoutCalculator.cpp) |
| VTable | 不在 Load 阶段完整构建 | 首次类型物化时建立，可复用父树/跨 image 快照 | [`VTableSetup.cpp`](/C:/hybridclr_optimize/worktrees/hybridclr-metadata-v8.13.0/hybridclr/metadata/VTableSetup.cpp)、`EnsureVTableInitializedLocked` |
| Type cache | 分段地址稳定存储 | eager 阶段冻结为紧凑排序索引，避免指针失效 | `FreezeIl2CppTypeCache` |

校验覆盖 MethodImpl、PropertyMap、EventMap、MethodSemantics、NestedClass、ClassLayout、
FieldLayout、FieldRVA、Constant、ImplMap、Param ownership/sequence、InterfaceImpl 以及
CustomAttribute parent/constructor 范围。NestedClass 循环、重复参数序列和非法 coded index
均在 runtime 侧拒绝。

### 3.3 IL2CPP 接入

两端共用 metadata lazy 契约，但不能外推完全相同的首次入口性能：

- **Tuanjie 2022.3.62t12**：`Il2CppClass` 增加 method-table 完成标志；支持完整 method table
  批量建立，也支持普通 interpreter class 按槽懒构建（`GetOrSetupOneMethod`）。发布完成位前
  使用 full barrier，读侧使用 acquire barrier，覆盖 ARM64 弱内存序。
- **Unity 2022.3.62f3**：复用同一 metadata hook 契约；通过 `SetupVTable` public wrapper 接入
  metadata vtable snapshot，保留 Unity 2022 的批量 method setup 和原有 FGS ABI。
- **Unity 2021.3.45f2**：保留与 lazy metadata 兼容的 `SetupFieldsLocked`、`SetupMethodsLocked`
  和布局/虚表修复；`Class::Init` 仍主要批量建立 method table，因此不能承诺 Tuanjie 的 per-method
  时间收益。

相关源码：

- Tuanjie：[`Class.cpp`](/C:/hybridclr_optimize/worktrees/il2cpp-plus-metadata-tuanjie-v8.13.0/libil2cpp/vm/Class.cpp)，正式分支 `optimize/assembly-load-metadata-tuanjie-1.10-v8.13.0`
- Unity 2022：[`Class.cpp`](/C:/hybridclr_optimize/worktrees/il2cpp-plus-metadata-unity2022-v8.11.0/libil2cpp/vm/Class.cpp)，正式分支 `optimize/assembly-load-metadata-unity2022-v8.11.0`
- Unity 2021：[`Class.cpp`](/C:/hybridclr_optimize/worktrees/il2cpp-plus-metadata-unity2021-v8.1.0/libil2cpp/vm/Class.cpp)，正式分支 `optimize/assembly-load-metadata-unity2021-v8.1.0`
- metadata：[`InterpreterImage.cpp`](/C:/hybridclr_optimize/worktrees/hybridclr-metadata-v8.13.0/hybridclr/metadata/InterpreterImage.cpp)

## 4. 运行时 API 与预热方案

### 4.1 直接 API

托管包位于 [`repos/hybridclr_unity/Runtime`](/C:/hybridclr_optimize/repos/hybridclr_unity/Runtime)：

```csharp
// 类级：准备类、fields、methods、interfaces、nested types、property/event、
// Tuanjie vtable，以及可解释执行方法的 method info；不会执行 static constructor。
bool ok = HybridCLR.RuntimeApi.PrewarmClass(typeof(Gameplay.Entry));

// 批量类级调用：减少 managed/native 边界，最多由队列提交 16 个类。
bool batchOk = HybridCLR.RuntimeApi.PrewarmClassBatch(types, types.Length);

// 精确入口；MethodBase 版本同时覆盖 .ctor。
HybridCLR.RuntimeApi.PrewarmMethod(entryMethod);
HybridCLR.RuntimeApi.PrewarmMethodBase(entryConstructor);

// 已生成 metadata token 的非泛型入口；不枚举完整 MethodInfo[]。
HybridCLR.RuntimeApi.PrewarmMethodToken(declaringType, metadataToken);
HybridCLR.RuntimeApi.PrewarmMethodTokenBatch(declaringTypes, metadataTokens, count);
```

这些 API 要求 closed/instantiated type 或 method。Editor 实现返回 `false`，不改变编辑器行为；
Player 调用是幂等的。批量 API 适合小批次直接调用，大清单应使用下方增量队列。
`PrewarmMethodToken` 只承诺清单中的方法，不等价于完整 Reflection。

### 4.2 增量队列

大型清单应延迟解析并跨帧推进：

```csharp
using System;
using System.Collections;
using System.Reflection;
using HybridCLR;

IEnumerator WarmScene(Assembly hotUpdateAssembly, string[] typeNames)
{
    RuntimePrewarmManifestQueue queue =
        RuntimePrewarmManifest.CreateIncrementalQueue(hotUpdateAssembly, typeNames);

    while (!queue.IsComplete)
    {
        // 双重门控：单次最多约 1 ms，且最多尝试 8 个类型。
        RuntimePrewarmBatchResult batch = queue.Process(1.0f, 8);
        if (batch.FailedCount != 0)
            UnityEngine.Debug.LogError("HybridCLR prewarm failed: " + batch.FailedCount);
        yield return null;
    }
}
```

方法清单可使用：

```csharp
RuntimePrewarmMethodManifestQueue queue =
    RuntimePrewarmManifest.CreateIncrementalMethodBaseQueue(assembly, descriptors);
// 每帧：queue.Process(1.0f, 8);  // maxMethods 由业务预算决定
```

token 清单可使用 `CreateIncrementalMethodTokenQueue`；它在队列推进时才解析声明类型，并把
解析时间计入预算，适合大清单。若清单较小，可用同步的 `CreateQueue`、`CreateMethodBaseQueue`
或 `CreateMethodTokenQueue`。

队列特性：

- 去重并保留 manifest 顺序；`Process(0)` 也至少推进一个条目。
- 同时受时间预算和 `maxTypes/maxMethods` 限制；首个预算窗口只提交一个条目，并按最近批次
  耗时采用保守估计。
- 失败计入 `FailedCount` 和 `LastFailedType/LastFailedMethod`，异常不会被吞掉；异常条目不会
  被静默标记为成功。
- 队列应由主线程串行推进；预热不运行 static constructor，也不替代业务初始化顺序。
- 单个 native 类型物化不可中断，因此“1 ms”是调度目标而非硬实时上限。生产应记录每批耗时和
  峰值 PSS。

### 4.3 清单选择

建议为每个首场景维护三类清单：

| 清单 | 适用场景 | 完整性含义 |
|---|---|---|
| Class manifest | 需要 `Type.GetMembers`、属性、事件或接口遍历 | 覆盖整个类的 metadata，但成本较高 |
| MethodBase manifest | 已知入口调用图，包含方法和构造函数 | 只覆盖显式列出的成员及其必要声明类 |
| Token manifest | 生成器已知非泛型入口 token | 最轻量；不覆盖未知 Reflection、接口实现或动态代理 |

实验脚本 [`generate-prewarm-manifest.ps1`](/C:/hybridclr_optimize/lab/scripts/generate-prewarm-manifest.ps1) 可从根方法递归生成 JSON 和 C# 清单。
反射、字符串拼接类型名、运行时虚调用、资源回调和动态代理必须由业务手工补充。若首场景
有 exhaustive Reflection，应使用完整 `PrewarmClass` 清单或完整 method-base manifest，不能
用 token 清单宣称已覆盖。

## 5. 正确性与并发保证

1. **初始化阶段隔离**：`InterpreterImage::Initialize` 只发布已验证的头部和索引；每个延迟层有
   独立完成状态，重复进入直接复用结果。
2. **锁边界**：metadata 构建在 `g_MetadataLock` 下完成；`SetupFieldsLocked`、`SetupMethodsLocked`
   等函数要求调用方持锁，并在 debug 构建中断言。
3. **发布顺序**：写入完整对象后执行 release/full barrier，再发布完成位；读侧 acquire 后才读取
   fields、methods、vtable 和 method info，防止 ARM64 上观察到半初始化状态。
4. **地址稳定**：`Il2CppType` cache 分段存储，避免延迟扩容导致已有指针失效；冻结后转换为紧凑
   排序索引，降低常驻内存。
5. **语义保持**：不主动触发 static constructor；开放泛型、非法 token、缺失类型和 manifest 歧义
   明确失败；Property/Event、CustomAttribute ctor、参数序列和跨 image VTable 均有范围校验。
6. **验证矩阵**：native CTest、Player correctness、differential、并发首触达和 steady-state 必须
   同时通过。固定线程 checksum probe 已通过，但随机交错、重复回收和长时压力仍是发布前补充项。

## 6. 性能测试和验收

### 6.1 固定实验条件

- workload：`HybridCLR.MetadataStress`，DLL 约 1.6 MB，2051 types、45065 members、27653 attributes。
- Baseline/Candidate 使用同一程序集 SHA、同一 Player 配置和同一外部 headers。
- 每端至少 100 个独立进程才能使 P99 进入硬门禁；进程顺序交替，测试前 full GC，等待 settle 50 ms。
- 记录 engine version、package/runtime/source tree hash、generated C++ hash、程序集 SHA、设备/温度、
  code generation、AOT metadata mode 和 FGS diagnostics。

### 6.2 执行命令

装配和 native gate：

```powershell
./lab/scripts/assemble-runtime.ps1 `
  -LabRoot ./lab -Profile Metadata-Candidate `
  -EngineWorkflow Tuanjie2022Fgs `
  -HybridClrSource ./worktrees/hybridclr-metadata-v8.13.0 `
  -Il2CppPlusSource ./worktrees/il2cpp-plus-metadata-tuanjie-v8.13.0 `
  -AllowDirty
./lab/scripts/run-native-tests.ps1 -LabRoot ./lab `
  -Profile Metadata-Candidate -Configuration Release
```

Unity 2021 只替换为 `-EngineWorkflow Unity2021Standard` 和对应的 Unity 2021 il2cpp worktree。
Unity 2022 使用 `-Profile Metadata-Unity2022`、`-EngineWorkflow Unity2022Fgs` 和
`worktrees/il2cpp-plus-metadata-unity2022-v8.11.0`；团结使用 `Metadata-Tuanjie2022`。
正式 Player comparison 使用 `run-metadata-comparison.ps1`，结果由
`compare-metadata-benchmarks.ps1` 按 policy 和 stage targets 判定。

### 6.3 门禁

策略文件：[`metadata-benchmark-policy.json`](/C:/hybridclr_optimize/lab/manifests/metadata-benchmark-policy.json)；阶段目标：
[`metadata-stage-targets.json`](/C:/hybridclr_optimize/lab/manifests/metadata-stage-targets.json)。

| 类别 | 门禁 |
|---|---|
| Load 时间 | median/P95 回退不超过 10% 且不超过 1 ms；目标改善至少 60% |
| Load P99 | 至少 100 进程；回退不超过 15% 且不超过 1 ms |
| Load + Entry | 目标改善至少 45%；Entry execute/resolve 回退不超过 25% |
| Load + Reflection / Reflection touch | 不回退超过 10%（Reflection P95/P99 同样门禁） |
| load-only memory | private bytes 至少改善 30% |
| 触达后 memory | 回退不超过 5% 且不超过 8 MiB |
| 正确性 | Player `220/220`、differential `0`、native tests 100% |

任何性能改善都必须同时绑定不可变产物身份；profile 名称或可变 manifest 路径不能作为证据。

## 7. 当前证据和解释

正式历史报告：

- [Tuanjie 2022 100 进程 comparison](/C:/hybridclr_optimize/lab/reports/vtable-share-comparison-tuanjie-100.json)
- [Unity 2021 100 进程 comparison](/C:/hybridclr_optimize/lab/reports/metadata-final-unity2021-100-comparison.json)
- [Tuanjie Player correctness](/C:/hybridclr_optimize/lab/reports/metadata-tuanjie2022-player-result.json)
- [Unity 2021 Player correctness](/C:/hybridclr_optimize/lab/reports/metadata-unity2021-player-result.json)
- Unity 2022：冻结身份的 native compile/CTest、Player correctness `220/220`、differential `0`
  和 lazy metadata probe 已通过；runtime SHA-256 为
  `F4144BBC4EA06A062264DF06A87AB841A3772E40CCEB60AE204209AC6CFFEFAE`。100 进程性能和
  Android ARM64 仍待生成，不能沿用 Tuanjie 或 Unity 2021 数字。

对结果的正确解读：

- `Assembly.Load` 的 79%-81% 改善是真实历史样本中的加载阶段收益。
- `Load + Reflection` 只改善约 9%-10%，说明完整 Reflection 的总工作量仍然存在；lazy 主要改变
  工作发生的时间。
- Reflection touch 的 Tuanjie median +1.97%、P99 +8.64%，Unity 2021 median 基本持平、P99
  +8.77%，均在历史 policy 内，但这是尾延迟重点风险。
- 预热后的 Entry execute 可以显著下降，但预热时间和常驻/峰值内存必须单独计入预算；预热不能
  与 `Assembly.Load` 收益简单相加。
- 历史报告早于最新 metadata 表校验、Tuanjie ARM barrier 和 managed token queue 变更；代码冻结
  后必须重新生成同身份报告。

## 8. 发布、灰度与回滚

### 发布前 checklist

- [ ] 四棵正式源码树（metadata + 三套 il2cpp hook）提交并记录 commit、tree SHA、runtime SHA。
- [ ] 以冻结身份分别构建 Tuanjie 2022 和 Unity 2021 Player；重跑 correctness/differential（Unity 2022 已完成）。
- [ ] 三端 Windows x64 各完成至少 100 进程 comparison，包含 P50/P95/P99、Reflection 和 Entry。
- [ ] 同身份构建 Android ARM64；完成 Player correctness、native tests、PSS、温度和弱核门禁。
- [ ] 完成随机交错、重复首触达、长时并发及回收重建测试。
- [ ] 记录预热每帧耗时、失败数、峰值 PSS；按首场景分别维护入口/反射清单。
- [ ] 生成不可变 build manifest，核对 engine、AOT metadata、FGS diagnostics 和程序集 SHA。

### 灰度策略

默认只对首场景的高置信度类型启用 `CreateIncrementalQueue`，每帧预算从 `0.5-1.0 ms`、
`4-8` 个类型开始。先观测 `Assembly.Load`、Reflection P95/P99、Entry P95/P99 和峰值 PSS，
再逐步扩大清单。动态反射路径保持未预热，出现失败或超预算时记录并降级到原有按需路径。

### 回滚边界

候选由三部分组成，可独立回滚：

1. HybridCLR metadata runtime（`worktrees/hybridclr-metadata-v8.13.0`）；
2. Tuanjie/Unity 2022/Unity 2021 各自的 il2cpp hook；
3. managed prewarm API/manifest queue。

若 Reflection/Entry 尾延迟或 Android PSS 超门禁，先关闭预热清单，再回滚对应 engine hook，
最后才回滚 metadata lazy。FGS、Interpreter 和 DHE 不应随本方案一起回滚。

## 9. 后续工作与责任边界

当前主要卡点不是 `Assembly.Load` median，而是：

- 首次 Entry/Reflection 的尾延迟和单类型不可中断物化；
- Unity 2021 批量 method setup 与 Tuanjie per-method 路径的不对称；
- Android ARM64 同身份 PSS、温度和弱核证据；
- 源码、Player、runtime、manifest 尚未完全冻结到同一个提交身份。

在这些门禁闭环前，不应以“小游戏平台已完成发布验收”对外承诺。通过后，正式提交应包含本
文档、不可变 build manifest、JSON comparison、correctness/differential、Android PSS 报告和
预热清单版本；FGS/Interpreter/DHE 分别提交、分别验收、分别回滚。
