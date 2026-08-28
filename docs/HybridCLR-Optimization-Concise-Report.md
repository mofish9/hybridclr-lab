# HybridCLR 优化总览与 Review

更新时间：2026-08-27

范围：`Assembly.Load`/metadata lazy、首次入口和反射触达；FGS、Interpreter、DHE
分别验收，不把它们的收益计入 `Assembly.Load`。

注：2026-08-28 新增了延迟 token manifest queue，managed package 已冻结为 commit
`04dd2067f2b52624b778913afae2a4e627d2c4af`。下文旧 Player 数字均明确属于变更前身份，
不能直接用于本轮发布门禁；需要重建同身份 Player 后再采样。

## 结论

核心方向成立：把加载期的完整类型成员构建拆成按需初始化，Windows x64 的
`Assembly.Load` 中位数已经稳定超过 60% 目标；新增的预热接口还能把可预测入口的
首次执行成本移到 Loading/Lobby。当前不能宣布为发布候选：最新候选仍有首次入口
执行尾延迟回退，源码、staging runtime 和性能报告也没有冻结到同一个身份；小游戏
平台还缺少同一身份的 Android ARM64 证据。

## 优化清单

| 工作线 | 当前实现 | 结论 |
|---|---|---|
| Metadata lazy | `InterpreterImage` 分阶段初始化；方法签名/参数、字段、泛型约束、`Il2CppType`、CustomAttribute、Property/Event、ClassLayout、VTable 延迟到真实触达 | 主收益来源，保留 |
| ClassLayout/VTable | 首次类型物化时布局；无静态字段时跳过对应布局；简单父树/跨 image 快照复用；触达完成后释放中间树 | 正确性已覆盖，尾延迟需继续门禁 |
| Reflection | fields/methods 容量预留；虚方法去重表使用小型栈 bitset，超大类型 spill 到堆 | 语义通过，收益以完整反射场景为准 |
| Entry lookup | 团结 2022 支持 `GetOrSetupOneMethod` 按槽懒构建，并保留整类批量路径；Unity 2021 仅保留批量 `SetupMethods` 兼容路径 | 两引擎不能互相外推入口性能 |
| 首次使用预热 | `RuntimeApi.PrewarmClass/Method/MethodBase`、token 快路径、按时间+类型/方法数双重门控的队列、根方法静态 IL 候选清单生成器 | 将首用成本移到 Loading/Lobby；token 只适合入口优先契约，反射契约需显式完整预热；反射、字符串查找和动态分派边需由业务 manifest 补充 |
| 类级预热批量化 | `PrewarmClassBatch(Type[], int)` 与队列复用 native batch 缓冲 | 减少 managed/native 边界和逐类型调度；团结尾延迟收益明确，Unity 2021 以 P95/P99 改善为主 |
| 预热清单解析 | 按声明类型缓存 `Type` 与 methods/constructors 候选数组；`CreateIncrementalQueue` 和 `CreateIncrementalMethodBaseQueue` 延迟解析 | 大清单的解析、反射发现和 native 预热均可按帧预算推进；同步 API 保留兼容 |
| MethodInfo 快路径实验 | acquire 读已发布方法槽；普通非泛型方法可按 `methodStart` 直接索引 | 历史实验分支；入口 P95 回退，未纳入冻结候选 |
| 锁与缓存 | GenericInst 读侧共享锁，构建/清理独占；lazy 完成状态用 acquire/release 发布；双引擎 `SetupFieldsLocked/SetupMethodsLocked` 增加 debug 锁断言 | native CTest 与固定并发 probe 已通过；随机并发和长时压力仍需真机 |
| Interpreter | `AddInst` 窥孔、load/copy propagation、常量合并、临时量消除及 UB 修复 | 独立工作线，不计入加载收益 |
| FGS | 按真实 `MethodInfo` 选择 fully-shared ABI；一次性 preparation；支持 callvirt/calli/delegate 和无 supplemental metadata 工作流 | 独立兼容性工作线 |
| DHE | MV/hash/guard/dispatch、artifact validator、项目级 workflow 与 demo | 研究/能力验证，尚非发布方案 |

## 性能证据

正式 workload 为 `reflection-first/exhaustive`，约 2051 个类型、45065 个成员、27653
个特性；负数表示 Candidate 更快或占用更少。

| 结果身份 | 样本 | `Assembly.Load` median / P95 / P99 | Load-only private bytes | 门禁 |
|---|---:|---:|---:|---|
| 团结 2022 历史稳定批次（runtime `B2F5...`） | 100 对 | `-80.77% / -80.07% / -82.46%` | `-42.80%` | 通过 |
| Unity 2021 历史稳定批次（runtime `F694...`） | 100 对 | `-77.08% / -79.57% / -79.75%` | `-31.73%` | 通过 |
| 团结 method-slot 诊断（runtime `0D8B...`） | 100 对 | `-77.34% / -78.86% / -81.08%` | `-42.64%` | 失败：`entryResolveP99 +300.09%` |
| 最新单方法 lazy（runtime `5326...`） | 10 对 | `-78.23%` | `-41.40%` | 失败：`entryExecuteP95 +46.99%` |
| `MetadataUtil` fast path 实验（runtime `FE35...`） | 10 对 | `-78.68%` | `-43.96%` | 失败：`entryExecute +74.22%` |
| 当前批量发布 + atomic fast path（runtime `76BB...`） | 30 对，`entry-first` | `-77.51% / -78.28% / 未纳入 P99 门禁` | `-41.97%` | 失败：`entryExecuteP95 +38.26%`、`entryResolveP95 +28.82%` |

`entryResolve/entryExecute` 是首次入口解析和首次执行，`reflectionTouch` 是首次反射
枚举触发的物化成本，不是每帧 steady-state。`MetadataUtil` fast path 曾在无泛型限制
时造成 `215/220` correctness；加限制后虽恢复正确性，入口中位数仍回退，因此已否决，
不应合入。

门禁补充：P99 只有在双方各有至少 100 个独立进程时才进入硬门禁；当前脚本已将
P99 回退上限统一为 15%，median/P95 仍沿用各自的 10% 接受阈值和 stage target。已通过的
稳定批次不会改变，`atomicfast` 100 对实验的入口 P99 `+3.50%` 也不会改变结论；
但当前 30 对实验不具备 P99 资格，且硬门禁失败来自入口 median/P95（分别约
`+13.17%/+38.26%`，配对 median 为 `+28.92%`），放宽 P99 不能解决问题。

## 首次使用预热接口

已在 `HybridCLR.RuntimeApi` 增加 `PrewarmClass(Type)`、更细粒度的
`PrewarmMethod(MethodInfo)` 和支持构造函数的 `PrewarmMethodBase(MethodBase)`。Player 中类级接口会完成目标类的 `Il2CppClass`、fields、
methods、接口、嵌套类型、property/event、Tuanjie vtable，以及可解释执行方法的
interpreter method info 初始化；调用是幂等的。方法级接口只准备精确的目标方法，以及
声明类进入可安全访问状态所需的元数据，适合团结 2022 的 lazy-init 路径。Unity 2021
的 `Class::Init` 兼容实现仍会批量建立方法表，因此方法级接口不能承诺同样的时间/内存
收益。`PrewarmMethodBase` 允许静态调用图同时覆盖 `.ctor`，解析器对 Unity IL2CPP
的 `ConstructorInfo` 不调用未实现的 `MethodBase.GetGenericArguments`。这些接口都只接受已实例化的闭合类型/方法；Editor 实现返回 `false`，不会改变
编辑器行为。

对于生成的非泛型方法图，`RuntimeApi.PrewarmMethodToken(Type, int)` 与
`PrewarmMethodTokenBatch(Type[], int[], int)` 可以按 metadata token 直接定位方法，
避免在团结 2022 上先物化声明类型的完整 `MethodInfo[]`。Unity 2021 保留兼容实现，
不能假设相同收益。token 路径只保证列出的入口方法；如果后续业务会对同一批类型做
exhaustive `Type.GetMembers`，应改用 `PrewarmMethodBase`/`PrewarmClass` 完整预热，
或者维护独立的反射 manifest，避免把延迟的反射补齐成本误认为已经消失。

类级队列现在通过 `RuntimeApi.PrewarmClassBatch(Type[], int)` 批量提交最多 16 个类型，
并通过失败位图逐项记账，不会在批量部分失败时重跑已成功项。`Process(0)` 仍只处理一个类型；正常预算下可减少
internal-call 和托管调度次数，但不会改变每个类型的初始化顺序或失败语义。

方法图解析会缓存同一声明类型的 `Type`、methods 和 constructors 候选数组，避免
descriptor 数量增长时重复执行相同的反射发现。

Token 方法清单也提供 `CreateIncrementalMethodTokenQueue`。它只在队列推进时解析声明
类型，并把类型解析时间计入批次预算，避免大型 token 清单在构造阶段形成同步峰值；
token 仍只覆盖列出的入口，不等价于完整反射预热。

建议在 Loading、Lobby 或切场景等待期间按业务批次调用。不要把整个程序集作为一个同步批次；
将首场景实际会触达的类型放进 manifest，并按帧预算逐批执行。Runtime 包提供了不依赖
Unity 版本的 `RuntimePrewarmQueue`，业务层只需在协程或 `Update` 中推进它：

```csharp
IEnumerator Prewarm(IReadOnlyList<Type> sceneWarmupTypes)
{
    var queue = new HybridCLR.RuntimePrewarmQueue(sceneWarmupTypes);
    while (!queue.IsComplete)
    {
        // The count cap prevents a burst of very cheap types from monopolizing a frame.
        HybridCLR.RuntimePrewarmBatchResult batch = queue.Process(1.0f, 8);
#if !UNITY_EDITOR
        if (batch.FailedCount != 0)
            UnityEngine.Debug.LogError($"HybridCLR prewarm failed for {batch.FailedCount} type(s); last={queue.LastFailedType}");
#endif
        yield return null;
    }
}

// For a precise large-class entry, call the method-level API directly:
// HybridCLR.RuntimeApi.PrewarmMethod(entryMethod);
```

生成式 manifest 可以只包含根入口的可达类型。若清单较小，可用
`RuntimePrewarmManifest.CreateQueue(assembly, GeneratedManifest.Types)`；大型清单应使用
`CreateIncrementalQueue`，它把名称解析、闭合泛型构造和 native 预热一起放进 `Process`
的预算内。解析失败会立即抛出，避免“清单漏项但看起来已完成”的假成功。方法清单对应
使用 `CreateIncrementalMethodBaseQueue`，避免 `GetMethods`/`GetConstructors` 在构造队列时
一次性执行。

```csharp
var queue = RuntimePrewarmManifest.CreateIncrementalQueue(assembly, GeneratedManifest.Types);
while (!queue.IsComplete)
{
    RuntimePrewarmBatchResult batch = queue.Process(1.0f, 8);
    yield return null;
}
```
实验仓库提供 `scripts/generate-prewarm-manifest.ps1`，通过 dnlib 从根方法递归扫描
同一热更程序集中的方法、字段和类型引用，并同时生成 JSON 校验文件与可直接加入主工程
的 C# 字符串清单。例如 `MetadataStressEntry.Touch` 的清单为 129 个类型/257 个可达
方法，而程序集总量是 2051 个类型；扫描器会保留闭合泛型 MemberRef 的本地方法回退解析。
这只是静态候选集，仍需在目标 Player 上验证实际
入口 checksum 和 P95/P99。

```powershell
./scripts/generate-prewarm-manifest.ps1 `
  -Assembly artifacts/managed-cases/StandaloneWindows64/HybridCLR.MetadataStress.dll `
  -RootType HybridCLR.Lab.MetadataStress.MetadataStressEntry `
  -RootMethod Touch -RootParameterCount 0 `
-OutputJson reports/prewarm-manifest-stress.json `
  -OutputCSharp reports/MetadataStressPrewarmManifest.cs
```

扫描器默认限制调用图深度为 64、可达方法数为 16384，超过上限会失败退出而不是输出
不完整清单；大型项目应按模块拆分根入口或显式提高上限，并重新做 Player correctness。

扫描器只把同一热更程序集内静态可解析的调用、字段和签名引用加入清单；反射、字符串
拼接得到的类型、接口/虚调用的运行时实现、资源驱动回调和动态代理不会被静态证明。
这些边界需要由业务 manifest 手工补充。清单生成不会改变 AOT/FGS 选择，也不要求把 FGS
与 metadata 预热合并验收。

`Process(0)` 也会至少推进一个类型，因此可以由严格的每帧调度器安全调用；带 `maxTypes`
的重载同时限制单帧类型数，避免低分辨率计时器下出现批量突发。队列会去重并
保留 manifest 顺序，`false` 结果会计入 `FailedCount` 后继续处理。它不捕获异常，类型初始化
异常会直接暴露给业务层；异常发生时当前类型不会被消费，业务层可以在处理后重试。队列应由
主线程串行推进。Editor 中底层接口按既有约定返回 `false`，生产代码可在
`UNITY_EDITOR` 下跳过失败门禁。

队列的类型数上限是尾延迟保护，不代表每个类型的成本相同；建议小游戏默认采用
`0.5-1.0 ms` 与 `4-8` 个类型的组合，并在目标 Android ARM64 设备上按 P95/P99 调整。
预热后的峰值 PSS 必须单独纳入内存门禁，不能只看 `Assembly.Load` 的 load-only 数字。

当前两端 `MetadataStressEntry` 的历史 20 进程验证如下；本轮 token 结果见后文。表内
数字来自各自报告绑定的历史身份，不能与新 Player 的数字拼接。最新重建 Player/runtime
身份为团结 build `F4697D75...`、runtime `CC622E9F...`；Unity 2021 build
`D473A523...`、runtime `8383F347...`。

| 引擎 / 模式 | 预热 median / P95 / P99 | Entry execute median / P95 / P99 | 预热 private bytes 增量 median |
|---|---:|---:|---:|
| Tuanjie 2022 `none` | 0 / 0 / 0 ms | 7.82 / 11.18 / 12.12 ms | 0 MiB |
| Tuanjie 2022 `entry` | 6.68 / 9.30 / 10.87 ms | 3.38 / 5.24 / 6.41 ms | 0.53 MiB |
| Tuanjie 2022 `entry-graph` | 14.15 / 22.28 / 22.72 ms | 0.79 / 1.87 / 2.21 ms | 0.75 MiB |
| Tuanjie 2022 `entry-method-graph` | 17.52 / 26.20 / 28.36 ms | 0.62 / 1.85 / 3.30 ms | 0.70 MiB |
| Unity 2021 `none` | 0 / 0 / 0 ms | 5.74 / 7.16 / 8.13 ms | 0 MiB |
| Unity 2021 `entry` | 5.28 / 11.40 / 11.60 ms | 2.92 / 4.97 / 6.06 ms | 1.43 MiB |
| Unity 2021 `entry-graph` | 13.00 / 17.76 / 19.52 ms | 0.22 / 0.71 / 0.86 ms | 1.99 MiB |
| Unity 2021 `entry-method-graph` | 13.82 / 20.83 / 22.22 ms | 0.20 / 0.47 / 0.87 ms | 1.88 MiB |

`entry-graph` 预热后的 full-GC private-bytes 增量中位数约为团结 `5.81 MiB`、Unity 2021
`11.35 MiB`；方法图分别约为 `5.75 MiB` 和 `11.21 MiB`。这是预热产生的常驻/缓存
成本，不应与 `Assembly.Load` 的 load-only 增量混为一谈。

同一批最新 Player 在 `reflection-first/selective(100)` 下的结果如下。该场景先做
100 个类型的反射触达，再执行入口，因此能暴露“入口清单预热扰动未列入反射类型”的风险：

| 引擎 / 模式 | `Assembly.Load` median | `reflectionTouch` median / P95 / P99 | `entryExecute` median / P95 / P99 |
|---|---:|---:|---:|
| Tuanjie 2022 `none` | 3.12 ms | 18.54 / 25.03 / 25.03 ms | 5.04 / 8.78 / 8.78 ms |
| Tuanjie 2022 `entry-graph` | 2.59 ms | 12.45 / 19.81 / 19.81 ms | 0.40 / 0.68 / 0.68 ms |
| Unity 2021 `none` | 2.28 ms | 10.60 / 20.65 / 20.65 ms | 5.97 / 7.45 / 7.45 ms |
| Unity 2021 `entry-graph` | 3.00 ms | 10.09 / 21.08 / 21.08 ms | 0.56 / 1.33 / 1.33 ms |

方法图在反射先行场景下没有稳定降低反射尾延迟，且 `Assembly.Load` 的变化属于运行噪声，
不能宣称由预热带来收益。预热清单不能默认扩大到整程序集来“覆盖一切”：未列出的动态反射、接口实现、
资源回调和代理类型仍可能在预热后出现回退。生产侧应为首场景分别维护入口和反射
manifest，先以小批次灰度，并把 `reflectionTouch` 的 P95/P99 与预热峰值 PSS 纳入门禁。

新增方法级接口的 20 进程结果（与上表同一身份）：

| 引擎 / 模式 | 预热 median / P95 / P99 | Entry execute median / P95 / P99 | 预热新增 private bytes median |
|---|---:|---:|---:|
| Tuanjie 2022 `entry-method-graph` | 17.52 / 26.20 / 28.36 ms | 0.62 / 1.85 / 3.30 ms | 717 KiB |
| Unity 2021 `entry-method-graph` | 13.82 / 20.83 / 22.22 ms | 0.20 / 0.47 / 0.87 ms | 1,925 KiB |

本轮 token 版本同身份 30 进程 `entry-first/selective(100)` 结果如下（Windows x64；团结
使用 `OptimizeSize`，Unity 2021 使用 `OptimizeSpeed`）：

| 引擎 / 模式 | 预热 median / P95 / P99 | Entry execute median / P95 / P99 | Reflection touch median / P95 / P99 | 预热新增 private bytes median |
|---|---:|---:|---:|---:|
| Tuanjie 2022 `none` | 0 / 0 / 0 ms | 3.59 / 7.88 / 8.03 ms | 11.00 / 19.61 / 20.67 ms | 0 MiB |
| Tuanjie 2022 `entry-method-graph` | 11.86 / 20.33 / 21.10 ms | 0.62 / 1.97 / 2.09 ms | 8.87 / 19.31 / 21.34 ms | 0.54 MiB |
| Unity 2021 `none` | 0 / 0 / 0 ms | 2.24 / 4.27 / 5.83 ms | 8.10 / 18.75 / 19.35 ms | 0 MiB |
| Unity 2021 `entry-method-graph` | 9.25 / 17.23 / 22.81 ms | 0.29 / 2.44 / 2.63 ms | 9.22 / 17.18 / 20.41 ms | 1.84 MiB |

预热只改变首次使用的时间位置，不减少总 CPU 工作；上表中 Entry median 分别降低约
83% 和 87%，而 reflection 的 P95/P99 没有稳定下降。exhaustive 反射会枚举当前压力
程序集的 1024 个类型，当前 lazy runtime 下其 touch 约 73--75 ms、峰值 private bytes
约 32 MiB；这属于“全量反射”契约，不能用入口 token 预热替代。

历史最终 runtime（团结 `4C297824...`、Unity 2021 `E9F3280D...`）的同身份
`entry-method-graph/selective(100)` 30 进程结果为：

| 引擎 | 无预热 Entry median/P95/P99 | 方法图预热 Entry median/P95/P99 | 预热 median/P95/P99 | 预热 private bytes 增量 median |
|---|---:|---:|---:|---:|
| Tuanjie 2022 | `2.74 / 5.12 / 5.34 ms` | `0.59 / 0.88 / 1.12 ms` | `9.08 / 14.42 / 16.04 ms` | `0.55 MiB` |
| Unity 2021 | `2.34 / 3.88 / 4.08 ms` | `0.26 / 0.38 / 0.51 ms` | `8.63 / 12.91 / 14.52 ms` | `1.91 MiB` |

这对应 Entry median 约降低 79% 和 89%；预热成本应在 Loading/Lobby 按帧摊平，不能
直接加到首帧同步路径中。

类级批量 A/B（同一 runtime，`entry-first/selective(100)`，30 进程）显示：团结 2022
预热 median/P95/P99 从逐项 `12.21/22.72/27.84 ms` 降至批量
`10.08/17.63/19.87 ms`；Unity 2021 median 在噪声范围内略高
(`8.55 -> 9.13 ms`)，但 P95/P99 从 `17.54/22.72 ms` 降至 `11.90/14.67 ms`。
批量化没有改变预热后的内存中位数，也没有引入 correctness 差异。

队列随后加入了按最近批次耗时估算的自适应调度：首个预算窗口只提交一个条目，
后续按 `1.25x` 保守单条目耗时估计批量，并同时受时间预算与 `maxTypes/maxMethods`
限制。最新同身份方法图结果（30 进程）中，团结预热 median/P95/P99 为
`9.08/14.42/16.04 ms`，Unity 2021 为 `8.63/12.91/14.52 ms`；相较此前类批量
结果，主要收益在 P95/P99，Unity median 基本持平。该策略只把突发工作拆散到后续帧，
不减少总准备量，也不保证每一帧严格不超预算，生产门禁仍应记录每批耗时和峰值 PSS。

曾尝试让整个类批次共享一把 native metadata lock，但 100 进程复测中两端 P99
均出现不稳定上升，因此该方案已撤回；当前保留的批量接口只减少调用边界，不扩大
锁持有时间，优先保护小游戏的尾延迟。

方法级结果说明：它减少的是预热阶段的“整类扫描/转换”范围，不会自动递归预热
`Touch` 间接调用的所有类型；在本清单上相较 `entry-graph` 只小幅降低入口 median，
却增加了预热时间，且团结 P99 略差。生产默认应优先使用类级接口；只有当真实首场景
调用图较小、构造函数边确实重要且预算允许时才使用 `PrewarmMethodBase`。
新增的 `metadata-warmed` 快照会记录预热后的 private bytes、working set、Unity allocation
和 Android PSS，便于判断预热是否超过小游戏内存预算。

若只比较真正首次入口阶段（不含预热），当前 20 进程 `entry-first/selective(1)` 样本中，
`entry-graph` 使 Tuanjie 的 Entry execute median/P95/P99 分别降低约 90%/83%/82%，
Unity 2021 分别降低约 96%/90%/89%。若把
预热排在同一条同步链路中，端到端 CPU 总量并不会消失；预热收益应按用户可见的入口帧
和 Loading 空闲预算评估。因此预热是把首用成本搬到可控时段，不是消除总工作量。生产侧
应限制每帧预算、分批执行，并记录预热耗时和峰值内存；
不应在首帧或用户点击回调中临时调用整程序集预热。

对应汇总：[Tuanjie final method graph 30 进程](/C:/hybridclr_optimize/lab/reports/manifestcache-entry-method-graph-tuanjie-selective100-30-summary.json)、[Unity 2021 final method graph 30 进程](/C:/hybridclr_optimize/lab/reports/manifestcache-entry-method-graph-unity2021-selective100-30-summary.json)、[Tuanjie final class graph 30 进程](/C:/hybridclr_optimize/lab/reports/classbatch-final-entrygraph-tuanjie-selective100-30-summary.json)、[Unity 2021 final class graph 30 进程](/C:/hybridclr_optimize/lab/reports/classbatch-final-entrygraph-unity2021-selective100-30-summary.json)、[Tuanjie entry-first 20 进程](/C:/hybridclr_optimize/lab/reports/latest-tuanjie-entryfirst-entry-method-graph-20-fixed.json)、[Unity 2021 entry-first 20 进程](/C:/hybridclr_optimize/lab/reports/latest-unity2021-entryfirst-entry-method-graph-20-fixed.json)。

该接口不依赖 FGS，且 native 实现同时放在团结 2022 与 Unity 2021 使用的 Metadata
runtime 工作区；两端仍需用各自冻结的 Player 完成 correctness、尾延迟和 Android PSS
验证后才能作为小游戏发布依据。

验证 runner 还提供 `-labMetadataWarmup entry-graph`，它加载与热更 DLL SHA-256
绑定的静态调用图清单，并用默认 `1 ms + 8 类型/批次` 的双重预算把清单逐批预热。
这只是在实验中模拟业务调度，生产侧应在 Loading/Lobby 的协程或 `Update` 中保存同一个
queue，在每帧调用一次 `Process`，而不是把整个循环同步放在首帧。

本轮新 Player 的跨帧 smoke 已实际走过延迟 manifest resolver：Tuanjie 2022 三进程的
预热为 33 帧、最大单帧约 `11.6 ms`；Unity 2021 同为 33 帧、最大单帧约 `11.1 ms`。
这些数字包含一次不可中断的单类型 native materialization，不能解读为严格的 1 ms 硬
上限；若业务要求更低的单帧下限，应继续拆分 native 类型物化本身。

## 正确性与发布状态

- 当前团结 2022 Player（build `F4697D75...`，runtime `CC622E9F...`）为 `220/220`，
  differential `0`，跨程序集 VTable 和并发首触达 probe 通过。
- 当前 Unity 2021 Player（build `D473A523...`，runtime `8383F347...`）为 `220/220`，
  differential `0`，跨程序集 VTable 和并发首触达 probe 通过。
- 两端 native CTest 均为 `1/1` 通过；这只证明 native/runtime 测试契约，不替代 Android
  真机 PSS 和尾延迟门禁。
  Android 旧候选曾通过 `203/203` Player 和 `7` 组 native smoke，但与当前身份不一致，
  不能替代当前门禁。
- FGS Windows 工作流记录为团结 `215/215`、Unity 2022 诊断 `220/220`、differential
  `0`；Unity 2021 生产工作流关闭 FGS。Interpreter steady 约 Core `2.2x`、All 15
  `1.88x`；两者均不计入 metadata 收益。DHE 仍是单程序集 demo，未覆盖项目全量热更集。

## Review Findings

1. **P0：源码尚未冻结。** 当前 Windows 证据已绑定到 Tuanjie `F4697D75.../CC622E9F...` 和
   Unity 2021 `D473A523.../8383F347...` 的 build/runtime hash，但 Metadata、两端 il2cpp hook
   和 managed package worktree 仍 dirty；性能数字只能按报告中的 hash 使用，不能按 profile
   名拼接，也不能把两端结果合并成一个候选。
2. **P0：小游戏发布证据缺失。** 尚无当前 runtime 同身份的 Android ARM64 Player
   correctness、PSS、P50/P95/P99、温度和弱核数据。
3. **P1：首次入口执行是当前主要卡点。** `atomicfast` 100 对中 `entryExecute`
   median/P95 分别回退 `+32.56%/+33.29%`；最新 `atomicfast3` 30 对中独立
   median/P95 为 `+13.17%/+38.26%`，配对 median 为 `+28.92%`。加载收益没有消失，
   但首触达成本可能推迟用户可见的冷启动收益；应优先定位批量方法物化和入口调用路径。
4. **P1：双引擎实现不对称。** 团结有 per-method API，Unity 2021 只有批量方法初始化；
   团结优化不能直接外推到 Unity 2021。
5. **P2：并发覆盖不足。** 固定 8 线程 checksum probe 已过，但还缺随机交错、重复首触达、
   回收重建和长时间压力测试。
6. **P2：内存余量需真机确认。** Unity 2021 Windows load-only 约 `-31.73%`，虽超过
   `30%` 门槛但余量很小，不能外推到小游戏容器。

## 当前工作区

- Metadata runtime：`worktrees/hybridclr-metadata-v8.13.0`
- 团结 2022 hook：`worktrees/il2cpp-plus-metadata-tuanjie-v8.13.0`
- Unity 2021 hook：`worktrees/il2cpp-plus-metadata-unity2021-v8.1.0`
- Interpreter：`worktrees/hybridclr-interpreter-next-v8.13.0`
- FGS：`worktrees/hybridclr-fgs-compatibility-v8.13.0`、`worktrees/il2cpp-plus-fgs-compatibility-v8.13.0`
- DHE：`worktrees/dhe-experiment-*`

## 最短收尾路径

1. 对当前 dirty worktree 做代码审查和提交前冻结，保留 build/runtime/manifest hash 绑定。
2. 在同一身份补齐 native CTest 与 Windows 100 对，重点验收 `loadAndEntry`、Entry P95/P99、
   Reflection P95/P99。
3. 用同一身份制作 Android ARM64，完成 Player correctness、PSS、尾延迟、温度/弱核门禁。
4. 通过后再提交并冻结三棵 Metadata 源码树；FGS、Interpreter、DHE 保持独立合并和回滚边界。

## 证据索引

- Windows 稳定批次：[vtable-share-comparison-tuanjie-100.json](/C:/hybridclr_optimize/lab/reports/vtable-share-comparison-tuanjie-100.json)、[latest-unity2021-comparison-20260826T130017Z.json](/C:/hybridclr_optimize/lab/reports/latest-unity2021-comparison-20260826T130017Z.json)
- 当前入口诊断：[latest-singlemethod-comparison-entryfirst-10.json](/C:/hybridclr_optimize/lab/reports/latest-singlemethod-comparison-entryfirst-10.json)、[metadatautilfast-comparison-entryfirst-10.json](/C:/hybridclr_optimize/lab/reports/metadatautilfast-comparison-entryfirst-10.json)
- 最新入口批次：[atomicfast3-comparison-entryfirst-30.json](/C:/hybridclr_optimize/lab/reports/atomicfast3-comparison-entryfirst-30.json)、[atomicfast3-paired-entryfirst-30.json](/C:/hybridclr_optimize/lab/reports/atomicfast3-paired-entryfirst-30.json)
- Player/manifest：[metadata-tuanjie2022-player-result.json](/C:/hybridclr_optimize/lab/reports/metadata-tuanjie2022-player-result.json)、[metadata-tuanjie2022-build-manifest.json](/C:/hybridclr_optimize/lab/reports/metadata-tuanjie2022-build-manifest.json)、[metadata-unity2021-player-result.json](/C:/hybridclr_optimize/lab/reports/metadata-unity2021-player-result.json)、[metadata-unity2021-build-manifest.json](/C:/hybridclr_optimize/lab/reports/metadata-unity2021-build-manifest.json)
- 设计与独立工作线：[HybridCLR-Full-Generic-Sharing-Merge-Design.md](/C:/hybridclr_optimize/lab/docs/HybridCLR-Full-Generic-Sharing-Merge-Design.md)、[HybridCLR-Optimization-Progress.md](/C:/hybridclr_optimize/lab/docs/HybridCLR-Optimization-Progress.md)、[HybridCLR-DHE-Formal-Project-Validation.md](/C:/hybridclr_optimize/worktrees/dhe-experiment-lab/docs/HybridCLR-DHE-Formal-Project-Validation.md)
