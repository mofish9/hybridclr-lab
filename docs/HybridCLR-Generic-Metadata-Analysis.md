# HybridCLR 完全泛型共享与元数据优化分析

更新时间：2026-08-11

> 本文保留为研究阶段快照。完全泛型共享现已集成到优化主干，并完成团结 2022
> FGS/no-metadata 与 Unity 2021 standard/supplemental 的双工作流门禁。正式方案、
> 最新的 216-case 证据和回滚边界见
> `docs/HybridCLR-Full-Generic-Sharing-Merge-Design.md`。

## 1. 结论

基线固定为 Tuanjie `1.10.0 / 2022.3.62t12`、HybridCLR `8.13.0`、
`il2cpp_plus v2022-tuanjie-8.13.0`。Windows 使用 x64 Release Player，Android
使用 ARM64 Release APK。

- 完全泛型共享可以在社区版上补通。IL2CPP 的 exact/shared/fully-shared 查找、
  invoker redirect、rgctx 和 `has_full_generic_sharing_signature` 链路已经存在，
  缺口位于 HybridCLR 的 managed-to-native 调用桥。
- FGS Candidate 只修改 HybridCLR，不修改 `il2cpp_plus`。Windows 四象限均为
  `185/185`、differential 0；`OptimizeSize + none` 不再依赖补充 AOT 元数据。
- 该 Candidate 仍是 research 状态。Android ARM64 `OptimizeSize` 已成功编译和
  打包，但当前没有连接设备，尚未完成真机 correctness、native test 和 benchmark；
  测试 Player 也仍打包 2,263,040 bytes 元数据以支持同一构建的双模式测试。
- 元数据 lazy-vtable Candidate 也成立：修复 `<Module>` 计数后，20 对 20 配对
  测试中 `Assembly.Load` 改善 `17.25%`，reflection 改善 `0.94%`，无时间回退。
- Windows private bytes 没有达到有效收益阈值。延迟构建与缓存释放的逻辑收益
  尚未转化成可宣称的进程内存收益，仍需 Android RSS/PSS 验证。

官方资料只作为方向和量级参考。当前结果证明两条路线都能独立研究实现，但不
等同于复刻商业版完整能力，也不能把 Windows 单机数据外推到移动端。

官方当前说明：Unity 2021+ 的 IL2CPP 支持 full generic sharing，Unity 2022
默认开启；HybridCLR 商业版利用它省去补充元数据，同时警告
`faster (smaller build)` 可能使泛型函数性能下降 15% 甚至更多。元数据页面公开
的商业版 `Assembly.Load` 约为社区版 30%，并给出补充元数据和热更新程序集的
内存倍率。它们是项目级目标，不能替代同机 Clean/Candidate 验收：

- [完全泛型共享](https://www.hybridclr.cn/docs/business/fullgenericsharing)
- [元数据优化](https://www.hybridclr.cn/docs/business/metadataoptimization)

## 2. 测试标准

协议见 `docs/HybridCLR-Generic-Metadata-Test-Standard.md`。当前关键约束为：

- 正确性矩阵覆盖 `OptimizeSpeed/OptimizeSize` 与 `supplemental/none` 四象限。
- managed differential suite 为 185 项，其中 30 项是真实 Player 边界用例。
- 每次 Player 运行都是新进程；`none` 必须真实跳过
  `LoadMetadataForAOTAssembly`，原始结果中的加载耗时必须精确为 0。
- FGS 稳态 workload 每组至少 10 个独立进程，并审计 metadata mode、唯一 PID、
  checksum、managed assembly hash 和 build manifest hash。
- 元数据输入为确定性生成的 1024 类型程序集，DLL 为 1,598,464 bytes；完整
  触达 2051 types、45,065 members、27,653 attributes。
- 元数据 Clean/Candidate 奇偶轮交换顺序，每边至少 10 个独立进程；时间变化
  必须同时达到 10% 和 1 ms，内存必须同时达到 5% 和 1 MiB 才可宣称有效。

元数据策略 SHA-256 为
`7CC273935910B34492CC7811B5E2D9C7F2A0ADD0EE06F7BB35EA23C0313281E9`；FGS
benchmark 策略 SHA-256 为
`40ECC2FD99B8DE6B4EA9352F38DF79D0385AA2A2FBEFECC011A909583ABC4D69`。

## 3. 完全泛型共享

### 3.1 根因

生成 C++ 与 IL2CPP runtime 已经包含 fully-shared 方法。`GenericMethod` 在
fully-shared 命中后保留 raw method/virtual/invoker 指针，并设置
`has_full_generic_sharing_signature`。原社区版 HybridCLR 有两个缺口：

1. managed-to-native 准备与 reflection fallback 仍要求
   `methodPointerCallByInterp`，并把它传给 `invoker_method`。fully-shared 方法
   使用不同签名，正确入口是 IL2CPP 保存的 `methodPointer` 配合 redirect invoker；
   `methodPointerCallByInterp` 可以为 null。
2. `callvirt` 在 transform 阶段按声明方法缓存普通签名桥，但运行时解析出的实际
   方法可能带 FGS signature。基线在
   `System.Array+InternalEnumerator<int>.get_Current()` 处经生成桥 `__M2N_u4u`
   执行 null pointer，Windows 异常为 `0xC0000005`。

因此修复分为两层：

- `PrepareInterpreterManaged2NativeCall` 与
  `GetInterpreterInvokerMethodPointer` 对 FGS signature 使用
  `invoker_method + methodPointer`，普通签名保持原路径。
- `ResolveRuntimeManaged2NativeMethodPointer` 在运行期实际方法带 FGS signature
  时覆盖 transform 缓存桥，统一走 `Managed2NativeCallByReflectionInvoke`。
  覆盖三个动态 `callvirt`、三个携带 `MethodInfo` 的 `calli` 和 delegate invoke。

候选 runtime tree SHA-256 为
`AC58971FA0ED74A86275B8800B700A885AAEF6D7CA3B1AEF5EFE2136C6E9561D`。

### 3.2 正确性

| IL2CPP 模式 | 元数据模式 | 结果 | differential |
|---|---|---:|---:|
| `OptimizeSpeed` | supplemental | 185/185 | 0 |
| `OptimizeSpeed` | none | 185/185 | 0 |
| `OptimizeSize` | supplemental | 185/185 | 0 |
| `OptimizeSize` | none | 185/185 | 0 |

候选原生 CTest 通过。最新 Windows Player 在修正 benchmark 的 `none` 计时语义后
重建，并重新执行了四象限门禁。`il2cpp_plus` worktree 保持干净，说明当前修复
不需要修改 IL2CPP 集成层。

### 3.3 稳态与包体

`interp_generic` 每组使用 10 个独立进程、2,000,000 iterations，checksum 均为
`2000001000000`：

| Codegen | 元数据 | median ns/op | P95 ns/op | relative MAD | 建议噪声阈值 |
|---|---|---:|---:|---:|---:|
| `OptimizeSpeed` | supplemental | 18.3621 | 18.4408 | 0.2930% | 5% |
| `OptimizeSpeed` | none | 18.4312 | 18.6531 | 0.1358% | 5% |
| `OptimizeSize` | supplemental | 14.1979 | 14.3613 | 0.2982% | 5% |
| `OptimizeSize` | none | 14.2292 | 14.5065 | 0.4498% | 5% |

同一 codegen 内 `none` 相对 supplemental 仅变化 `+0.38%` 和 `+0.22%`，均低于
5% 噪声阈值，没有可宣称的性能回退。`OptimizeSize` 在这个单一 workload 上约
快 `22.7%`，只能作为本机观察值，不能推断所有泛型代码都会更快。

最新 Candidate 构建中，`OptimizeSize` 相对 `OptimizeSpeed`：

- Player 总文件：244,608,567 -> 207,049,516 bytes，减少 37,559,051 bytes
  (`15.35%`)。
- `GameAssembly.dll`：10,042,368 -> 8,505,344 bytes，减少 1,537,024 bytes
  (`15.31%`)。
- Candidate 相对对应 Clean 的 `GameAssembly.dll` 只增加 1,024 / 1,536 bytes。

当前双模式测试构建仍包含 2,263,040 bytes 补充元数据。`none` 四象限证明运行时
不需要加载它们；生产包体收益还需增加“不打包元数据”的构建变体并再次运行门禁。

Android ARM64 `OptimizeSize` APK 已生成，大小为 13,617,546 bytes，包含
`lib/arm64-v8a/libil2cpp.so`。这只是编译/打包证据，不是运行验收。

## 4. 元数据优化

Candidate 把 `InterpreterImage::InitRuntimeMetadatas` 中的全量 `InitVTables` 改为：

1. 首次创建对应 `Il2CppClass` 时，在元数据锁内构建该类型的 vtable 和 interface
   offsets。
2. 每个可初始化类型完成后递增计数，全部完成时析构并释放 `_cacheTrees` 中的
   `VTableSetUp`。
3. 预先标记第一行 `<Module>`。它不会由 `Assembly.GetTypes()` 返回；若把它计入
   lazy 初始化目标，完成计数永远少 1，缓存树就永远无法释放。

正确性结果：supplemental 为 185/185、differential 0；none 为 170/185，失败
集合与 Clean 完全一致，因此没有引入新差异。

`<Module>` 修复后的 20 对 20 配对结果：

| 指标 | Clean median | Candidate median | 变化 | 判定 |
|---|---:|---:|---:|---|
| AOT metadata load | 12.928 ms | 13.003 ms | +0.58% | 阈值内 |
| `Assembly.Load` | 10.141 ms | 8.392 ms | -17.25% | 改善 |
| first entry | 2.783 ms | 3.162 ms | +13.61%, +0.379 ms | 阈值内 |
| reflection touch | 74.230 ms | 73.530 ms | -0.94% | 阈值内 |
| load-only private | 8.535 MiB | 8.309 MiB | -2.65% | 阈值内 |
| entry private | 8.727 MiB | 8.695 MiB | -0.36% | 阈值内 |
| reflection private | 33.334 MiB | 34.018 MiB | +2.05% | 阈值内 |

硬门禁通过，唯一达到有效改善阈值的指标是 `Assembly.Load`。Windows allocator
没有把逻辑上的延迟创建和缓存释放转化为同时超过 `5%`、`1 MiB` 的 private-bytes
改善，所以当前不能宣称元数据内存优化已经达成。

## 5. 决策与下一步

1. FGS Candidate 保留在独立 research worktree，优先补充无元数据 payload 的
   实际构建门禁，以及 Android ARM64 真机的四象限 correctness、native test 和
   配对 benchmark。
2. 将真实项目的泛型清单加入门禁，特别是更多参数/返回值布局、泛型构造函数、
   多播 delegate、接口/虚调用和 function pointer 组合；185 项不是完备证明。
3. 元数据 Candidate 保持单变量实验，不与 FGS 或解释器性能 Candidate 混合。
   下一轮在 Android 记录 PSS/RSS，并增加 vtable 已初始化类型数、缓存树节点数和
   原生分配统计，验证内存未改善究竟来自 allocator 还是仍有存活引用。
4. 在移动端结果完成前，两项都维持 research Candidate，不进入生产合并结论。

主要证据：

- `reports/fgs-candidate-*-player-result.json`
- `reports/fgs-candidate-*-differential-result.json`
- `reports/fgs-candidate-*-interp-generic-*-10.json`
- `reports/fgs-candidate-interp-generic-comparison.json`
- `reports/fgs-candidate-build-manifest.json`
- `reports/fgs-candidate-optimizesize-build-manifest.json`
- `reports/fgs-candidate-optimizesize-android-arm64-build-manifest.json`
- `reports/exploration-metadata-candidate-module-fix-comparison-20.json`
