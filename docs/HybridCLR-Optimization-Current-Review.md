# HybridCLR 当前优化 Review

更新时间：2026-08-28

> 状态提示：本文保留历史审计数字，同时在下文明确标注已经修复的代码问题和仍需外部
> 设备验证的发布门禁。性能数字必须以报告中的完整 build/runtime hash 为准，不能按
> profile 名拼接不同源码树或不同 Player 产物。

本轮加入了 `CreateIncrementalMethodTokenQueue` 及其 manifest queue，并按 FGS 的发布流程
将运行时合入三条正式维护线。package 已在 commit
`ac0fdc5c6363a1b6323d017e068c536dd22127dc` 冻结，`hybridclr_version.json` 只引用 opt3 tag。
旧性能报告仍不能作为 opt3 同身份的最终性能证据，必须重新构建 Player 并重跑性能门禁。

## Findings

### P0：Android ARM64 不能发布

当前没有把正式 Windows 候选对应的最终 runtime、build manifest、程序集和 workload 在同一
ARM64 真机上完成 Player correctness、PSS、P50/P95/P99、温度和弱核门禁。现有 Android
产物属于更早的 runtime 身份（candidate 例如 `6227...`），与正式 Windows 性能候选不一致；
历史 baseline/candidate 的 case 数也不一致，不能外推小游戏平台结论。

### P1：Unity 2021 性能证据需要按最终身份重跑

较早的 `vtable-share` 100 对比较中 `Assembly.Load` 改善 `77.45%`，但
`aotMetadataLoadP99` 回退 `11.08%`。随后基于更新源码的 100 对样本改善为
`Assembly.Load -77.08%`、AOT metadata P99 `-2.97%`，comparison hard gate 通过。
该数字来自历史候选身份，不能直接作为最终发布证据。当前 Unity 2021 Player correctness
已在最新 runtime/build identity 上通过 `220/220`，但仍应在源码冻结后重跑同身份的 100
对性能比较，避免把历史 workload 与新产物混用。

### P1：产物需要按 opt3 身份重采样

Metadata、三套 engine hook 和 package 已提交并进入各自正式维护分支。当前 opt3
冻结为 package `ac0fdc5c6363a1b6323d017e068c536dd22127dc`、HybridCLR
`f40c6f08ccd0391ad9285276b4cc21ada3a180ab` 和三套 il2cpp hook commit；历史性能结果绑定
团结 runtime `B2F5...`（build identity `CF75...`）、Unity 2021 runtime `F694...`
（build identity `1452...`）；历史重建 manifest 已变为团结 `7748DB...`/`A78E13...`、Unity 2021
`502558...`/`3F87D3...`。`_classMap` 单次 lookup 变体已回退。因此 comparison 中的
manifest 路径是可变引用，不能单独用于复现；必须以 summary 内的 build/runtime hash 为准，
并为每个产物保存不可变 manifest。opt3 的新性能 Player 仍需重新生成，不能复用这些历史结果。

另一次 Unity 2021 clean baseline 构建使用了与 `il2cpp_plus` hook 不匹配的 HybridCLR clean
工作树，缺少 `CopyMethodInfo` 等接口而失败；这是基线构建组合问题，不能作为候选 correctness
失败，但必须修正后才能生成同身份的最终对照产物。

### P2：ClassLayout fast path 需要同身份尾延迟门禁

30 对探索结果的 `Assembly.Load` 改善 `75.63%`，但 Reflection P95/P99 回退 `13.01%/56.06%`，
hard gate 失败；baseline/candidate AOT metadata payload 相差 `2048` bytes，paired 统计也不
具备严格同 payload 条件。当前实现已修复 `actualSize` 丢失等正确性问题，且 Player correctness
已通过；仍需用同一 metadata payload、至少 100 对独立进程重跑，若 Reflection P95/P99 超过
policy 阈值则回滚 fast path，而不是把它当作已验证收益。

该 fast-path/cache 已包含在冻结的 HybridCLR opt3 提交中；是否可用于生产仍由同 payload、
至少 100 对进程的 Reflection/Entry 尾延迟门禁决定。

随后 `_classMap` 单次 lookup 30 对实验的 Reflection P95/P99 为 `+13.43%/+22.25%`，
Entry resolve P95/P99 为 `+75.15%/+352.09%`，AOT metadata P99 为 `+14.63%`，同样不应合并。

### P2：并发压力仍需设备级长时覆盖

`EnsureType*MetadataInitializedLocked` 和双引擎 `Class::SetupFields/SetupMethods` 要求调用者
已持有 `g_MetadataLock`。本轮已在 Unity 2021/Tuanjie 的 `SetupFieldsLocked`、
`SetupMethodsLocked` 入口加入 `IL2CPP_DEBUG` 锁对象断言，native CTest 均通过；仍缺少随机
交错、同类型重复首触达和长时压力，这些需要在目标设备继续执行。

### P2：插桩耗时不能当生产耗时

CSV 截断已由 `setvbuf(..., _IONBF, 0)` 修复；最新 instrumented Player 为 `220/220`、
differential `0`，5 个进程文件完整。但逐行写盘会争用 metadata lock，stage 绝对耗时只用于
排序和定位，不用于发布收益声明，且样本数仍低于 10 进程画像门槛。

## 本轮修复

- 大型类型、方法和 token manifest 均可通过增量队列推进；新增
  `CreateIncrementalMethodTokenQueue` 消除了 token 路径在队列构造阶段同步解析声明类型的缺口。
- 增量队列仍保留同步 API 兼容，解析异常不会被吞掉，失败位图按条目记账；这解决的是首帧峰值，
  不改变单个 native 类型物化的不可中断耗时。

## 实现审计

### Metadata / Assembly.Load

- Load 阶段只建立必要头部；方法签名/参数、字段类型、泛型约束、CustomAttribute、
  Property/Event、`Il2CppType`、ClassLayout、VTable 延迟到真实首触达。
- MethodInfo 详情压缩、参数使用地址稳定分块；token/type cache、无字段布局 fast path、
  父类和跨 image VTable/slab 复用减少分配与重复构建。
- `SetupFields/SetupMethods` 在持锁阶段按类型批量兑现 metadata；完成状态用 acquire/release
  发布，避免其他线程观察到半初始化对象。
- 团结正式 100 对历史批次 hard gate 通过：`Assembly.Load -80.77%`、P95/P99
  `-80.07%/-82.46%`、Load-only private bytes `-42.80%`，回退数为 0。
- Unity 2021 更新后的 100 对历史样本为 `Assembly.Load -77.08%`、P95/P99
  `-79.57%/-79.75%`，Load-only private bytes `-31.73%`、AOT metadata P99 `-2.97%`。
  最新 Player 已重建并通过 correctness，但性能数字仍需在冻结身份上重新采样。

### 双引擎 hook

团结 `2022.3.62t12`、Unity `2022.3.62f3` 和 Unity `2021.3.45f2` 均接入同一 lazy hook；RuntimeType 的 fields/methods
reserve 和按 `vtable_count` 的槽表已加入，团结另有连续 MethodInfo 分配。连续分配的独立净
收益尚未证明，应视为内存/分配形态优化。

### Interpreter

`TransformContext::AddInst` 统一承载窥孔、load/copy propagation、常量算术/转换/return 合并、
值类型构造 inline 和临时量消除；同时修复 strict-aliasing、未对齐读取和整数环绕 UB。独立
steady 结果为 Core 11 `2.23x`、Core 12 `2.15x`、All 15 `1.88x`，不计入 `Assembly.Load`。

### FGS

FGS 按真实 signature 选择 fully-shared ABI，执行一次性 preparation；有效 AOT 不降级，缺失
AOT 才回退解释器，覆盖 callvirt/calli/delegate/reflection。团结生产 Player 为 `215/215`、
Unity 2022 诊断 Player 为 `220/220`，对应 differential 均为 `0`；Unity 2021 工作流明确
关闭 FGS，其 `220/220` 属于 Metadata correctness，不是 FGS 证据。FGS 与 Metadata lazy
独立验收，不把无 metadata workflow 收益计入 `Assembly.Load`。

### DHE

运行时代码生成和缓存仍是 research worktree，尚无可替代当前 Candidate 的完整兼容性、内存
和移动端证据，不进入发布候选。

## 验证矩阵

| 范围 | 正确性 | 性能/证据 | 判断 |
|---|---|---|---|
| opt3 三端 native compatibility | Metadata/Workflow 各 `3/3`，真实 Editor headers | `mergeReady=true`，无 surrogate headers | 源码合并门禁通过 |
| 团结 2022 Windows x64 Metadata | `220/220`，differential `0` | 历史 100 对 hard gate 通过 | opt3 性能待同身份复采 |
| Unity 2022 Windows x64 Metadata | 冻结身份 `220/220`，differential `0` | native compile/CTest 通过；100 对性能待补 | correctness 通过，性能待门禁 |
| Unity 2021 Windows x64 Metadata | opt3 merge commit `220/220`，differential `0` | 性能为历史身份 | opt3 性能待同身份复采 |
| ClassLayout fast path | `220/220`（修复后试验构建） | 需同 payload、同身份重跑 100 对尾延迟 | 待门禁 |
| FGS Tuanjie 2022 / Unity 2022 | `215/215`、`220/220`，differential `0` | clean compatibility matrix；Unity 2022 为诊断 Player | 独立工作线，可单独验收 |
| Android ARM64 Metadata | 当前正式身份未闭环 | 无合格 PSS/尾延迟/同 workload 证据 | 阻止小游戏发布 |
| Interpreter | 参考与候选 checksum 一致 | All 15 `1.88x` steady | 独立验收 |
| DHE | capability 级实验 | 无发布级门禁 | research only |

## 建议顺序

1. 以锁定的 opt3 组合完成 Workflow/Metadata 三端 native compatibility matrix。
2. 分别重建团结、Unity 2022 和 Unity 2021，重跑 correctness、differential 和 100 对性能比较。
3. 以 opt3 runtime 分别制作 Android ARM64，完成 correctness、native tests、PSS、P50/P95/P99、温度和弱核门禁。
4. 在目标设备补随机并发、重复首触达、嵌套 struct 和 generic valuetype workload，并记录锁断言开启的 debug 构建结果。
5. 产物同时记录 tag、commit、tree SHA、runtime SHA、程序集 SHA 和 workload/policy SHA；FGS、Interpreter、DHE 继续独立验收。

## 证据索引

- 正式 Metadata：`reports/vtable-share-comparison-tuanjie-100.json`、`reports/latest-unity2021-comparison-20260826T130017Z.json`
- opt3 native compatibility：`reports/runtime-compatibility-matrix-metadata-opt3.json`、`reports/runtime-compatibility-matrix-workflow-opt3.json`
- 正确性：`reports/metadata-tuanjie2022-player-result.json`、`reports/metadata-unity2021-player-result.json` 及对应 differential
- ClassLayout：`reports/classlayout-fastpath-comparison-30.json`
- 插桩：`reports/vtable-share-instrumented-summary-flush.json`
- Interpreter：`reports/review-fixed-candidate-steady-benchmark.json`、`reports/review-fixed-candidate-steady-repeat-benchmark.json`
- FGS：`reports/fgs-merge-ready-runtime-compatibility-workflow.json`、`reports/fgs-merge-ready-runtime-compatibility-metadata.json`、`reports/fgs-candidate-optimizesize-nometadata-none-player-result.json`、`reports/unity2022-fgs-diagnostic-optimizesize-nometadata-none-player-result.json` 及对应 differential
