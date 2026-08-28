# HybridCLR 社区版优化阶段报告

更新时间：2026-08-25

> 本文件保留优化过程和解释器阶段数据。它不是当前发布结论；当前源码、身份和门禁
> 以 `HybridCLR-Optimization-Concise-Report.md` 与 `HybridCLR-Optimization-Current-Review.md`
> 为准，本文中的旧 runtime/hash 不应与最终 Metadata 候选混用。

## 1. 当前结论

当前最终版本为 Candidate 28 Safety Fix，基于 HybridCLR `8.13.0`，测试引擎为
团结 `1.10.0 / 2022.3.62t12`，目标平台为 Windows x64 Release Player。

- runtime SHA-256：`E8E68F8756F7BF87DEE5C9EB777C73BE7D0BDBEAD44A9571060FFD667628F053`
- native tests：通过
- .NET reference：`175/175`
- Player：`175/175`
- differential：`0`
- instrumented Player：`175/175`，differential `0`，并发用例和 JSON snapshot 均通过
- 两轮 15 workload checksum 差异：`0`
- Core 11 Candidate/Clean：`2.225900413x / 2.241449861x`
- Core 12 Candidate/Clean：`2.153217292x / 2.171691472x`
- All 15 Candidate/Clean：`1.877672653x / 1.892216431x`

因此，核心解释器与互调路径“均值 2x”目标已经稳定达到；若目标严格指全部
15 项 workload，则当前约为 `1.88x`，尚未达到 `2x`。boxing、字符串分配和异常
三项主要受分配、GC、异常运行时影响，当前指令合并路线对它们的提升有限。

Safety Fix 相对原 Candidate 28 两轮的 Core 11、Core 12 和 All 15 几何均值均约
慢 `2%`；逐 workload 最大差异为 `5.11%`，没有超过本轮 `6% / 9%` 的建议噪声
阈值。修复版两轮同项 P50 最大差异为 `3.07%`，性能表现判定为相近。

AOT 相对原 Candidate 28 的几何均值为 Core 11 `12.00x`、Core 12 `11.49x`、
All 15 `6.80x`。Safety Fix 没有重跑 AOT 对照；当前结果仍足以说明社区版有大幅
独立优化空间，但没有消除与 AOT 的数量级差距。

## 2. 最终性能结果

下表使用两轮 Candidate P50 的平均值；Speedup 为 Clean/Candidate。

| Workload | Clean ns | Candidate ns | Speedup |
|---|---:|---:|---:|
| `aot_to_interp_boundary` | 30.94 | 19.36 | 1.60x |
| `interp_arithmetic` | 20.73 | 5.87 | 3.53x |
| `interp_array` | 30.22 | 13.13 | 2.30x |
| `interp_boxing` | 56.37 | 51.17 | 1.10x |
| `interp_branch` | 24.08 | 12.16 | 1.98x |
| `interp_call` | 17.34 | 5.74 | 3.02x |
| `interp_delegate` | 29.73 | 19.51 | 1.52x |
| `interp_exception` | 302.47 | 296.83 | 1.02x |
| `interp_field` | 23.22 | 12.04 | 1.93x |
| `interp_float` | 16.67 | 5.18 | 3.22x |
| `interp_generic` | 14.16 | 5.03 | 2.82x |
| `interp_string_allocation` | 82.13 | 71.55 | 1.15x |
| `interp_struct` | 66.57 | 42.17 | 1.58x |
| `interp_to_aot_boundary` | 14.60 | 6.90 | 2.12x |
| `interp_virtual` | 32.21 | 21.28 | 1.51x |

两轮同项 P50 的最大差异为 `3.07%`，低于两轮报告建议的 `6% / 9%` 噪声阈值。

## 3. 已保留的优化

### 3.1 统一的 transform 窥孔入口

`TransformContext::AddInst` 在不生成 PDB offset map 时执行在线窥孔优化。`ldc.i4`、
`conv.i8`、整数 shift 和 4 字节 `ret` 等原先绕过该入口的路径已改为通过
`AddInst`，让同一套合并和复制传播规则覆盖真实 IL 生成流程。PDB 模式保持原始
IR 序列，避免破坏 IL offset 映射。

### 3.2 Load、copy propagation 和无用临时量消除

- 连续 `LdlocVarVar` 合并为 2、3、4 路 load 指令。
- `stloc` 尽可能重定向 producer 的目标槽，消除 producer 后的复制。
- 单路或双路 load 向整数算术、比较分支、switch、字段读写和 stfld consumer
  传播源槽。
- 数组 i4 读取前收缩三路 load，只保留真正需要独立执行的 load。
- `load-pair + ldc + and` 收缩为单路剩余 load 和已有 const-and handler。
- Candidate 28 将 `load-pair + ldc + add` 收缩为单路剩余 load 和已有 const-add
  handler；dispatch 数不变，但消除了临时 load、常量写栈和读栈。`interp_float`
  相对 Candidate 24 从约 `6.00 ns` 降到约 `5.13 ns`。

### 3.3 常量算术和类型转换合并

- `ldc.i4 + add.i4`
- `ldc.i4 + and.i4`
- `ldc.i4 + mul.i4`
- `ldc.i4 + shr.i4`，shift amount 显式按 CLI 语义执行 `& 0x1f`
- `ldc.r8 + mul.f8`
- `conv.i8 + add.i8`
- 上述可达 producer 与相邻 load 的安全复合形式

Candidate 21 的 `ldloc + ldc.i4 + mul.i4` 复用已有 const-mul handler，画像中删除
`132,500,000` 次 dispatch。Candidate 24 接通 shift combiner，再删除
`50,000,000` 次 dispatch。

### 3.4 返回路径合并

Candidate 25 将 4 字节 `RET` 接入 `AddInst`，使已有
`LdcVarConst_4_Add_i4_Ret_4` 真正生效。在固定画像中删除 `75,000,000` 次
dispatch；AOT 到解释器边界的两轮定向结果相对 Candidate 24 平均提升约 `8.5%`。

### 3.5 总体 dispatch 变化

Clean instrumented profile 为 `7,887,613,025` 次 dispatch；最新实测的
Candidate 25 profile 为 `3,186,938,300` 次，减少 `4,700,674,725` 次，约
`59.596%`。Candidate 28 是 dispatch-neutral reduction，因此不靠进一步降低
次数获益，而是减少同一 dispatch 数下的临时栈内存流量。

### 3.6 诊断能力

仅在 `HYBRIDCLR_LAB_INSTRUMENTED` 下启用的 `InterpreterProfile` 支持：

- opcode 动态计数；
- opcode transition 计数；
- 解释器入口次数；
- transform 次数与耗时；
- JSON snapshot。

画像计数现在按线程保存；`Reset` 和 `Snapshot` 使用暂停屏障归并存活线程及已退出
线程的数据，不再并发写共享 `unordered_map`。该代码不进入 Candidate 性能构建，
不参与最终倍率。

### 3.7 Safety Fix 正确性加固

- double 常量位模式改用 `memcpy`，消除 strict-aliasing UB；
- packed IR 中的 32/64 位立即数通过 unaligned read helper 读取；
- unchecked i4/i8 add 使用无符号运算保留 CLI 环绕语义，消除 signed overflow UB；
- reducer 直接更新原 IR 的公共前缀字段，不再跨 struct 写入；
- 删除 3 个由当前 `AddInst` 合并顺序证明不可达的 opcode、handler 和生成路径；
- 保留并补测 `LdlocVarVar_4` 与
  `ConvertVarVar_i4_i8_Add_i8_LdlocVarVar_2` 两个可达但旧画像未命中的 opcode。

## 4. 已回退的实验

| 实验 | 结果 | 决策 |
|---|---|---|
| native-call 参数转发 | 目标 workload 无稳定收益 | 回退 |
| `ldloc -> null-check -> ldfld` 传播 | struct 无收益 | 回退 |
| call 前 pair-load 缩减 | call 无收益 | 回退 |
| 冗余 null check 消除 | 无稳定净收益 | 回退 |
| virtual return 转发 | 无稳定净收益 | 回退 |
| field partial load | 无稳定净收益 | 回退 |
| AND + BranchFalse 新 opcode | 巨型 switch 布局造成全局退化 | 回退 |
| `load-pair+ldc+add` 32 字节复合 handler | 虽减少约 1.03 亿 dispatch，但 float 从约 6.00 ns 退化到 12.23 ns，Core 11 降至 2.10x | 回退 |
| 复合 handler 源槽解析 | float 仍为 12.17 ns | 回退 |
| `pair-load -> ldfld` reduction | Candidate 28/29 均为 11.8007 ns | 回退 |

最重要的经验是：dispatch 数减少不是充分条件。新增或启用大型 switch case 可能
改变 MSVC 生成代码、指令缓存和分支布局；必须以完整 workload 的实际耗时决定
是否保留。当前优先级应是复用小型已有 handler、复制传播和临时量消除。

## 5. 关键里程碑

| 版本 | Core 11 | Core 12 | All 15 |
|---|---:|---:|---:|
| Candidate 4 | 1.2345x | - | 1.1878x |
| Candidate 21 round 1/2 | 2.1253x / 2.1333x | 2.0609x / 2.0692x | 1.8140x / 1.8167x |
| Candidate 24 round 1/2 | 2.2078x / 2.2267x | 2.1319x / 2.1534x | 1.8670x / 1.8832x |
| Candidate 28 final round 1/2 | 2.2762x / 2.2828x | 2.1979x / 2.2093x | 1.9159x / 1.9213x |
| Candidate 28 Safety Fix round 1/2 | 2.2259x / 2.2414x | 2.1532x / 2.1717x | 1.8777x / 1.8922x |

## 6. 完全泛型共享集成与元数据研究

> 2026-08-25 更新：FGS 已从 `optimize/v8.13.0` 隔离到
> `feature/fgs-compatibility-v8.13.0`。团结 2022 真正开启 FGS 的三个
> `OptimizeSize` 组合与 Unity 2021 standard 工作流均达到 `220/220`、
> differential 0。正式 ABI、工作流、合并门禁和回滚边界见
> `docs/HybridCLR-Full-Generic-Sharing-Merge-Design.md`。以下内容保留为研究过程记录。

以下两项使用独立 worktree，不属于 Candidate 28 Safety Fix，也没有与解释器
性能分支混合：

- 完全泛型共享已定位到 HybridCLR managed-to-native bridge：普通路径错误依赖
  `methodPointerCallByInterp`，运行期动态目标还可能与 transform 阶段缓存桥的
  signature 不同。`Fgs-Candidate` 已接通 IL2CPP 的 full-signature invoker，并在
  `callvirt`、携带 `MethodInfo` 的 `calli` 和 delegate invoke 上按实际方法覆盖
  缓存桥。
- Windows 的 `OptimizeSpeed/OptimizeSize x supplemental/none` 四象限均为
  `185/185`、differential 0；原生测试通过。候选 runtime SHA-256 为
  `AC58971FA0ED74A86275B8800B700A885AAEF6D7CA3B1AEF5EFE2136C6E9561D`，
  `il2cpp_plus` 不需要修改。
- `OptimizeSize` 相对 `OptimizeSpeed` 的 Player 总文件和 `GameAssembly.dll` 均
  减少约 `15.3%`。10 进程 `interp_generic` 中，none 相对 supplemental 的差异
  为 `+0.22%` 到 `+0.38%`，低于 5% 噪声阈值。
- Android ARM64 `OptimizeSize` APK 已成功编译，但没有连接设备，尚未执行真机
  correctness、native test 和 benchmark，因此 FGS 仍是 research Candidate。
- 元数据 Candidate 将 vtable/interface offsets 延迟到首次创建 `Il2CppClass`，
  并修复 `<Module>` 不被 `Assembly.GetTypes()` 返回导致 `_cacheTrees` 永不释放
  的计数问题。20 对 20 配对中 `Assembly.Load` 改善 `17.25%`、reflection 改善
  `0.94%`，无有效 Windows private-bytes 收益，仍需 Android RSS/PSS 验证。

### 6.1 Assembly.Load metadata 触达曲线

最终 metadata Candidate 不再使用上面的早期 lazy-vtable 实验。它在团结
`2022.3.62t12` 与 Unity `2021.3.45f2` 上分别绑定已验证 runtime SHA-256
`043C34A48E7227264C2C379D1CEEBD158FD60C7DD80299102BA825225AE8CE0D` 和
`489DD0EE0323E347F862A7ECF0E15C17F0840D934A2ABBCD95D0D137884AEDC6`，两端均为
`220/220`、differential 0。

选择性 Reflection 不先调用 `Assembly.GetTypes()`，而是直接解析离散分布的外层
类型。30 对独立进程的重点复测如下；负数表示 Candidate 更快或占用更少：

| 触达档位 | Assembly.Load paired | Entry paired | Reflection paired | 到 Reflection 端到端 paired | Reflection P95 | Reflection private bytes paired |
|---|---:|---:|---:|---:|---:|---:|
| selective 500 | -62.55% | -0.11% | -1.25% | -11.47% | +3.55% | +1.26% |
| selective 1024 | -61.85% | +42.47% | +2.61% | -2.00% | +8.05% | -0.46% |
| exhaustive 2051 | -62.66% | +24.21% | +2.04% | -6.35% | +0.79% | +1.51% |

结论是全量 Reflection 会回收大部分 `Assembly.Load` 收益，但 30 对样本中没有把
端到端中位数反转成回归。单进程上下界仍很宽，因此 selective 曲线只作诊断，正式
发布门禁仍使用 exhaustive 独立中位数。此次确认轮的 exhaustive 独立中位数为
`Assembly.Load -61.68%`、到 Reflection 端到端 `-6.64%`；但 Entry 为 `+26.51%`，
比 25% 阈值高 1.51 个百分点，且 AOT metadata p95 出现无关波动，所以该轮整体
hard gate 未通过，不能替代已通过的 `final34` 正式报告。

完整分析和测试边界见 `docs/HybridCLR-Generic-Metadata-Analysis.md` 与
`docs/HybridCLR-Generic-Metadata-Test-Standard.md`。

### 6.2 Reflection-first 方法元数据批处理

延迟方法签名初始化在 `Class::SetupMethodsLocked` 中曾按方法重复进入全局 metadata
锁。reflection-first/selective-500 的插桩样本包含 13,254 次方法初始化；构建体
本身只占约 1.21 ms，重复同步和调用框架成本没有包含在该计时中。

当前实现增加了一个仅在全局 metadata 锁已持有时使用的类型级批处理入口：

1. 从 `Il2CppTypeDefinition::methodStart/method_count` 得到连续方法区间。
2. 在 `SetupMethodsLocked` 全量创建 `MethodInfo` 前，一次性初始化该类型的方法签名。
3. 后续 `MetadataCache::GetMethodInfo` 只命中已发布的无锁快路径。
4. `GetOrSetupOneMethod`、方法体读取、method pointer 和 invoker 路径仍保持单方法
   初始化，不把直接执行扩大成整类型触达。

Tuanjie `2022.3.62t12` runtime SHA-256 为
`7D7F4CC928D0B7C8125C3DC5BBE9464BC56F48F2DDA6F00CB748F28E397F4805`；Unity
`2021.3.45f2` runtime SHA-256 为
`0254B2AE6AD16225289DAC2C0595BE823B16ABBE8B2193A0D2FCE60AA61DB34E`。两端原生
测试通过，Player 均为 `220/220`、differential 0。Unity 2021 使用独立的
metadata-only il2cpp 工作树，FGS diagnostics 关闭且计数为 0。

最敏感的 reflection-first/selective-500 使用 30 对交替顺序独立进程。以下为 paired
中位数，负数表示 Candidate 更快或占用更少：

| 指标 | 结果 | 绝对变化 | 判定 |
|---|---:|---:|---|
| `Assembly.Load` | -61.09% | -6.944 ms | 保留主要收益 |
| Reflection touch | +16.07% | +5.465 ms | 有效回退，延迟工作仍可见 |
| Load + Reflection | -3.83% | -1.995 ms | 未被 Reflection 抹平 |
| Through Reflection | -5.39% | -2.836 ms | 完整链路未反转 |
| Entry execute | +5.93% | +0.277 ms | 未达到 10% 且 1 ms 双阈值 |
| Load-only private bytes | -35.67% | -3.064 MiB | 有效改善 |
| Reflection private bytes | +1.00% | +0.277 MiB | 阈值内 |

因此，本轮消除了 selective-500 中“Reflection 将 Assembly.Load 收益整体反转”的
隐患，但没有消除 Reflection 单阶段的首次兑现成本。comparison hard gate 仍为
false，原因包括 Reflection median/P95 回退以及部分 60% 阶段目标未满足；该结果
应作为下一轮自定义特性索引、vtable 和 class layout 优化的基线，而不是最终发布
结论。证据为 `reports/method-batch-final-rf500-*-30.json` 与
`reports/method-batch-touch-curve-reflection-first.json`。

## 7. 最终证据

- `reports/candidate28-final-player-hybridclr-steady-benchmark.json`
- `reports/candidate28-final-player-hybridclr-steady-repeat-benchmark.json`
- `reports/candidate25-instrumentation-summary.json`
- `reports/candidate-player-result.json`
- `reports/candidate-differential-result.json`
- `reports/candidate-build-manifest.json`
- `reports/baseline-instrumented-player-result.json`
- `reports/review-fixed-instrumented-float-profile.json`
- `reports/review-fixed-candidate-steady-benchmark.json`
- `reports/review-fixed-candidate-steady-repeat-benchmark.json`
- `reports/fgs-candidate-optimizespeed-interp-generic-supplemental-10.json`
- `reports/fgs-candidate-optimizespeed-interp-generic-none-10.json`
- `reports/fgs-candidate-optimizesize-interp-generic-supplemental-10.json`
- `reports/fgs-candidate-optimizesize-interp-generic-none-10.json`
- `reports/fgs-candidate-interp-generic-comparison.json`
- `reports/fgs-candidate-optimizesize-android-arm64-build-manifest.json`
- `reports/exploration-metadata-candidate-module-fix-comparison-20.json`
- `native-unit-tests/hybridclr_native_tests.cpp`

最终 build manifest SHA-256：
`DB11F532057E488A04706D4F88F33FBBAA447C51E9B4FAEFC6A9B7BD5745038A`。

当前所有修改均保留在本地工作树，尚未 commit 或 push。
