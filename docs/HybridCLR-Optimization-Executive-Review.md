# HybridCLR 优化执行摘要

更新时间：2026-08-26

> 当前统一状态与 review 见 `HybridCLR-Optimization-Current-Review.md`。本文件保留为
> field metadata lazy 之前的阶段摘要，表中的 runtime hash 和改动统计不代表当前候选。

## 结论

当前结果应判定为：**Windows 阶段目标通过，小游戏发布仍需条件通过**。

- Assembly.Load/metadata 候选在 Tuanjie 2022 与 Unity 2021 的 Windows x64 Release
  独立进程测试中，Assembly.Load 中位数分别改善 `66.92%` 和 `73.83%`，超过
  `60%` 目标。
- Load-only private bytes 分别改善 `40.38%` 和 `30.17%`。Unity 2021 只比 `30%`
  门槛高 `0.17` 个百分点，移动端仍需重点复核。
- 两端正式 Player 均为 `220/220`，differential 为 `0`，FGS diagnostics 关闭。
- Android ARM64 已完成 APK 构建、manifest 校验和 native tests，但当前设备锁屏；
  Player correctness、PSS、成对性能和合格 P99 尚未完成，因此不能作为小游戏发布证据。
- FGS 与本轮 metadata 收益隔离；解释器指令优化也不计入 Assembly.Load 数字，避免重复计算。

## 三条工作线

| 工作线 | 当前保留内容 | 工作区 | 状态 |
|---|---|---|---|
| Assembly.Load / metadata | 方法/参数、CustomAttribute、Property/Event、ClassLayout、VTable、泛型约束与 `Il2CppType` 的分层 lazy 初始化；锁内构建和 acquire/release 发布；双引擎 `SetupMethods` hook | `worktrees/hybridclr-metadata-v8.13.0`；`worktrees/il2cpp-plus-metadata-tuanjie-v8.13.0`；`worktrees/il2cpp-plus-metadata-unity2021-v8.1.0` | Windows 通过；Android 未闭环；三棵树 dirty |
| FGS 兼容 | 按真实 `MethodInfo` 选择 fully-shared ABI；一次性 preparation 状态；有效 AOT 不降级；支持 no-metadata 生产组合 | `worktrees/hybridclr-fgs-compatibility-v8.13.0`；`worktrees/il2cpp-plus-fgs-compatibility-v8.13.0` | Windows 工作流通过且源码 clean；Unity 2022 真实 Editor、Android 仍待补证 |
| 解释器执行 | `AddInst` 窥孔入口、load/copy propagation、常量算术/转换/return 合并、临时量消除及 UB safety fix | `worktrees/hybridclr-interpreter-next-v8.13.0` | Core workload 约 `2.2x`，All 15 约 `1.88x`；独立于 metadata |

## Assembly.Load / metadata 结果

场景为 `reflection-first/exhaustive`，每端 60 对独立进程，触达 2051 个类型、45065 个成员、
27653 个特性。负数表示 Candidate 更快或占用更少。

| 指标 | Tuanjie 2022 | Unity 2021 |
|---|---:|---:|
| Assembly.Load median | `11.752 -> 3.887 ms`（`-66.92%`） | `10.503 -> 2.749 ms`（`-73.83%`） |
| Assembly.Load P95 | `-70.67%` | `-72.96%` |
| Load private bytes | `8.58 -> 5.12 MiB`（`-40.38%`） | `13.47 -> 9.41 MiB`（`-30.17%`） |
| Reflection touch median | `-0.65%` | `+2.62%` |
| Entry execute median | `+13.82%` | `-3.94%` |
| Through Reflection | `-9.55%` | `-7.06%` |

Reflection touch 是加载后首次穷举类型、成员和 CustomAttribute 的兑现成本；Entry 是入口类型/方法
首次解析与执行成本，均为一次性首触达，不代表每帧重复开销。当前主要收益来自把非 Load 必需的
metadata 构建延迟到真实使用，而不是减少 metadata 文件读取字节数。

## 正确性与 FGS 证据

- Tuanjie 2022 Candidate：`220/220`，differential `0`。
- Unity 2021 Candidate：`220/220`，differential `0`。
- metadata lazy 并发 probe：三端通过，固定 checksum `60048`；目前仍是确定性专项，不是长时
  随机压力测试。
- Android Baseline/Candidate native tests：均为 `7 groups passed`。
- FGS Tuanjie Windows 生产组合（supplemental、none、exclude/none）均 `220/220`、differential
  `0`；diagnostic exclude/none 为 `220/220`，FGS dispatch `201`、interpreter invoker `5`。
- Unity 2021 standard 保持 supplemental metadata 路径，FGS diagnostics `0/0`；因此 FGS 不会被
  误算为 Assembly.Load/metadata 优化收益。

## Review 发现与发布判断

1. **发布阻断：Android 证据未闭环。** 当前 APK manifest 已固定：Baseline
   `108D4781...A0115`、Candidate `673B9BC6...86CFD`，两端 supplemental metadata 均为
   `2,286,080` bytes；但设备上仍是旧 APK，且设备锁屏。不能用旧 Player 结果推断当前候选。
2. **可复现性风险：主候选未提交。** metadata 三棵树和解释器树均为 dirty worktree。当前
   tree SHA 可用于识别产物，但冻结、回滚和 CI 复现应改为 commit + tree SHA 双重绑定。
3. **内存风险：Unity 2021 余量很小。** Windows Load-only 只超过门槛 `0.17` 个百分点，不能
   外推到 ARM64 小游戏容器；需要 PSS、温度和弱核状态分层数据。
4. **尾延迟风险：暂无合格 P99 证据。** 当前 Windows 结论以 60 对和 P95 为主，Android runner
   的 P99 门槛要求至少 100 个独立进程，尚未满足。
5. **后续收益重点不在继续微调 VTable。** 插桩显示 Reflection 总成本约 `88 ms/process`，成员
   发现和特性实例化约占主要部分；下一轮优先检查 Field metadata lazy、CustomAttribute 重复扫描
   和可安全复用的 Reflection 成员缓存。

## 最短收尾路径

1. 解锁设备，确认安装包 hash 后分别运行 Android Player correctness。
2. 运行 Baseline/Candidate 交替 benchmark，至少 100 个独立进程，记录 Assembly.Load、Reflection、
   Entry、P50/P95/P99、PSS 和温度分层。
3. 扩展 lazy metadata 并发测试到随机交错、重复首触达和长时压力。
4. 所有门禁通过后提交并冻结 metadata、双引擎 il2cpp_plus 和解释器工作区；FGS 继续保持独立
   合并与回滚边界。

详细过程、被否决实验和证据索引见
`lab/docs/HybridCLR-AssemblyLoad-Optimization-Review.md`、
`lab/docs/HybridCLR-Optimization-Progress.md` 和
`lab/docs/HybridCLR-Full-Generic-Sharing-Merge-Design.md`。
