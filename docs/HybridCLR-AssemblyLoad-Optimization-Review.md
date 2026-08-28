# HybridCLR Assembly.Load / Metadata 优化 Review 简报

更新时间：2026-08-26

> 当前统一状态与 review 见 `HybridCLR-Optimization-Current-Review.md`。本文件保留为
> field metadata lazy 之前的 60 对阶段报告，不能直接代表当前 field-lazy 候选。

## 结论

**正式候选在 Windows x64 上已达到阶段目标，但尚不能作为 Android/小游戏平台发布版本。**

- Tuanjie `2022.3.62t12` 与 Unity `2021.3.45f2` 的 `Assembly.Load` median 分别改善
  `66.92%`、`73.83%`，均超过 `60%` 目标。
- Load private bytes 分别改善 `40.38%`、`30.17%`，达到 `30%` 目标；Unity 2021
  仅多出 `0.17` 个百分点，余量不足。
- 两端 Windows Player 均为 `220/220`、differential `0`，正式 comparison 的
  `hardGatePassed=true`、regression `0`。
- FGS diagnostics 在两端均关闭，本报告中的收益不依赖完全泛型共享，也不包含 FGS 改动。
- Android ARM64 APK 构建、manifest 校验和真机 native tests 已完成；但设备当前处于锁屏，
  本轮 APK 尚未完成 Player correctness、成对 metadata 性能和 PSS 门禁，因此发布结论仍为
  **有条件通过**。

本次源码 review 未发现新的候选实现正确性阻断项。剩余风险主要在移动端证据、并发专项覆盖、
构建产物身份管理，而不是已知的功能错误。

## 范围与隔离

本报告只评价 Assembly.Load/metadata runtime Candidate。FGS、解释器指令合并和其他独立
实验不计入本轮收益；正式 Windows metadata comparison 的 FGS diagnostics 均为关闭，
因此表中收益不依赖 FGS，也不会把“移除补充 AOT metadata”误算成 Assembly.Load 优化。

## 正式结果

测试场景为 `reflection-first/exhaustive`，每端 60 对 Baseline/Candidate 交替独立进程；
工作负载包含 2051 个类型、45065 个成员和 27653 个特性。

| 指标 | Tuanjie 2022 | Unity 2021 | 目标 |
|---|---:|---:|---:|
| Assembly.Load median | 11.752 -> 3.887 ms（**-66.92%**） | 10.503 -> 2.749 ms（**-73.83%**） | 改善 >= 60% |
| Assembly.Load P95 | 18.552 -> 5.442 ms（**-70.67%**） | 15.840 -> 4.284 ms（**-72.96%**） | 不回退 |
| Load private bytes | 8.58 -> 5.12 MiB（**-40.38%**） | 13.47 -> 9.41 MiB（**-30.17%**） | 改善 >= 30% |
| Through Reflection | **-9.55%** | **-7.06%** | 不回退 |
| Reflection median / P95 | -0.65% / +2.82% | +2.62% / -0.21% | 回退 <= 10% |
| Entry execute median / P95 | +13.82% / +10.63% | -3.94% / -14.54% | 回退 <= 25% |
| Reflection 后 private bytes | -1.97% | -1.04% | 回退 <= 5% |
| Entry 后 private bytes | -1.94% | -2.49% | 回退 <= 5% |

指标含义：`Reflection touch` 是加载后首次穷举类型、成员和 CustomAttribute 的高压触达；
`Through Reflection` 是 Load 与 Reflection 的累计链路，用于识别工作是否仅被 lazy 后移；
`Entry resolve/execute` 分别是入口类型/方法首次查找和入口方法首次执行。Reflection 与 Entry
都是首次触发成本，不是每帧重复成本。

## 当前保留优化

| 模块 | 实现与收益来源 |
|---|---|
| 方法与参数 | Load 只建立 MethodDef 头；签名、返回值、参数、默认值和参数特性按需解析；按 TypeDef 批量初始化；参数采用 1024 项地址稳定分块并保留连续指针快路径 |
| CustomAttribute | ThreadStatic 判定、token 索引、data blob 和 ctor 解析分层延迟；ThreadStatic ctor 直接读取 raw TypeRef；token 到 handle 使用负载不超过 0.5 的开放寻址表 |
| Property / Event | Load 仅建立范围，Property、Event 与 MethodSemantics 在首次访问时整体构建 |
| ClassLayout | 类型首次物化时计算；无静态字段类型跳过静态布局；布局过程移除临时字段指针 vector |
| VTable | 类型首次物化时计算；简单类型复用父树；跨 image 使用已发布快照；全量触达后释放中间树 |
| 类型与约束 | 泛型约束和接口类型按需解析；Il2CppType cache 使用地址稳定分段存储 |
| 并发发布 | lazy 构建统一在 `g_MetadataLock` 下完成，以 acquire/release 发布完成状态，避免观察到半初始化 metadata |
| 双引擎接入 | Tuanjie 2022 与 Unity 2021 的 `Class::SetupMethods` 各保留同一 4 行 lazy-method hook |

正式 HybridCLR 改动为 10 个文件、`+1256/-312`；两个 `il2cpp_plus` 工作树各改 1 个文件、
新增 4 行。主要收益来自把不属于 `Assembly.Load` 必需路径的工作延迟到真实首触达，而不是
减少 metadata 文件本身的读取字节数。

## Review 风险

1. **Android 发布门禁未闭环。** Huawei ADA-AL00 / Kirin 8000 / Android 12 / ARM64 已在线，
   Baseline 和 Candidate native tests 均为 `7 groups passed`。本轮 APK manifest 和
   `-ValidateOnly` 输入检查已通过，但设备仍在锁屏，安装确认未完成；当前设备上的
   `F34D4098...D86` 是旧 APK，不能代表本轮 Baseline `108D4781...A0115` 或 Candidate
   `673B9BC6...86CFD`，旧 Player 结果必须作废。
2. **Android PSS/Player 证据缺失。** 当前两个 APK 均包含 4 个 AOT metadata 文件、共
   `2,286,080` bytes，manifest 已记录该值；真正的 Player correctness、Assembly.Load
   成对 benchmark、P50/P95/P99 和 PSS 仍待设备解锁后执行。
3. **Unity 2021 内存余量过小。** `30.17%` 只略高于门槛，应在 Android PSS、弱核和不同温度
   状态下复核，不能把 Windows private bytes 直接外推到小游戏容器。
4. **lazy 并发专项仍需扩大。** 新增的跨程序集并发首触达 probe 已在 Tuanjie Baseline、
   Tuanjie Candidate 和 Unity 2021 Candidate 通过，固定 checksum 为 `60048`；但这仍是
   单组确定性 probe，尚未覆盖随机交错、重复回收和长时间并发压力。
5. **正式源码尚未提交。** 三个源码工作树均为未提交状态；虽然 tree SHA-256 可唯一识别，
   但冻结、review、回滚与 CI 复现仍应以 commit 为单位。Windows 通用 `Candidate` 等 profile
   也曾被实验 runtime 覆盖，后续必须以 tree/runtime hash 而不是 profile 名判断产物身份。

## 已否决实验

以下实验均未进入正式 tree：

- VTable slab/direct-tree、方法名复用、泛型父树直达、InlineVector、接口索引 inline、临时树和
  单接口 sealed 快路径：双端收益不稳定或跨引擎反转。最近的 transient tree 在 Tuanjie
  Through Reflection `+1.74%`，而 Unity 2021 为 `-1.43%`；单接口快路径使两端
  Assembly.Load 分别回退 `6.43%`、`5.51%`，已回退。
- ClassLayout shape cache：Tuanjie 30 对中 Assembly.Load、Reflection、Through Reflection
  分别回退 `9.55%`、`1.68%`、`3.29%`，未继续进入 Unity 性能门禁，已回退。
- CustomAttribute data arena、ctor open-map/direct-index、旧 flat map、整程序集 ThreadStatic
  预扫描，以及方法签名模板/小型 type cache：正确性可通过，但收益不足、内存收益太小或出现
  Entry/P95 和跨引擎回退，均未保留。正式 ctor info 仍使用 `unordered_map`。

最新定位显示 VTable 已不再是主要优化面：穷举 Reflection 中 LazyVTable 约
`6.34 ms/process`，其中 BuildByType 约 `5.15 ms`；而 Reflection 本身约 `87.94 ms`，主要成本
集中在成员发现约 `30.82 ms` 和成员特性实例化约 `32.45 ms`。继续做 VTable 微优化的预期收益
已接近噪声，优先级应低于真机门禁和成员 Reflection 路径研究。

## 工作区与证据

| 用途 | 路径 / 标识 |
|---|---|
| HybridCLR 正式源码 | `worktrees/hybridclr-metadata-v8.13.0` |
| Tuanjie il2cpp_plus | `worktrees/il2cpp-plus-metadata-tuanjie-v8.13.0` |
| Unity 2021 il2cpp_plus | `worktrees/il2cpp-plus-metadata-unity2021-v8.1.0` |
| HybridCLR tree SHA-256 | `46E35A39B40ECB6128C77543C3E608288CEB6B9F59E0E5B3B47F3F342870CF73` |
| Tuanjie 正式 runtime | `6227E82AD666ACE3B491B3463D9F32D613BA457826746FAB80C2628FD6939002` |
| Unity 2021 正式 runtime | `9B9270492F706E53893C119E0DC1927652EB84DDE77572CF180C0286C6076EC3` |

核心证据为 `ca-token-openmap-final-comparison-*-rf-exhaustive-60.json`、对应 paired 报告、
两个 Windows build manifest、两个 Android ARM64 build manifest/native test 报告、
`LazyMetadataConcurrencyTarget.cs` 的三端 probe 结果，以及 `vtable-breakdown-buffered-*`
插桩结果。本次 review 校验了报告、manifest、runtime hash 与当前 source tree 的一致性，
没有重新执行耗时较长的 Windows 60 对基准。

## 发布前最短路径

1. 解锁设备并确认 Huawei USB 安装，校验设备 `base.apk` 为本轮 Baseline/Candidate hash，
   再分别执行 Android Player correctness。
2. 运行已完成的 Baseline/Candidate 交替 metadata runner，每端至少 10 个独立进程，验收
   Assembly.Load、Reflection、Entry、P50/P95/P99、Android PSS 和温度状态。
3. 扩展 lazy metadata 并发 probe 为重复/随机交错压力测试；全部门禁通过后提交并冻结三个
   正式源码工作树。
