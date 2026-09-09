# DHE 普通 AOT 固定源适配策略

2026-09-09，Unity 2022.3.62f3 / Windows x64，源码候选有条件通过。

本轮把普通 AOT 适配从“直接放宽资源拒绝”改为独立的 source-bound plan：

- Base 快照认证并保留每个普通 AOT DLL 的原始字节、SHA-256 和 MV 身份。
- 只有依赖变化值布局的普通 AOT 方法/持有者进入执行投影候选；未受影响的原生入口继续保留。
- 所有投影方法都产生 `base-native-guard-required` 义务；PInvoke、internal-call、RVA、ThreadStatic 和无法证明的原生 ABI 继续是 gate。
- hotfix 可变程序集、普通 AOT 冻结源和 native guard 集合保持独立，不能把普通程序集加入 `hotUpdateAssemblies` 绕过门禁。
- 生成的 SnapshotIdentity 类型被排除，避免把最终 Player 重新生成的 identity 常量误当成普通业务代码。
- 运行时 `CurrentImageSourceKind::FrozenBaseAot` 要求 Base/执行 MV 和源哈希一致；错误来源、MV 改写、重复角色或批量准备失败都不会发布状态。

## 当前证据

| 证据 | 结果 |
|---|---|
| `artifacts/dhe-frozen-aot-20260909/native-v5/DHE-Unity2022/native-gate.json` | 真实 Unity 2022 headers，CTest 1/1，FGS，通过，`mergeReady=true`，无 surrogate headers |
| `artifacts/dhe-frozen-aot-20260909/policy-final3/result.json` | 21/21 policy checks，通过真实完整 AOT 快照 |
| HybridCLR 候选 | `e22aed2a049c0f85d56a89a9b1bad272a57653e8`，冻结来源运行时校验 |
| package 候选 | `2d4c3845d8716087c49ec9d50176abd4e1253548`，统一原子加载入口与普通 AOT guard 输入 |
| lab 候选 | `8824a38`，策略编译器、资源 staging 和 fixture |

HybridCLR `e22aed2a049c0f85d56a89a9b1bad272a57653e8` 提供带 source role 的 native 原子加载；package `e965c34e1c247de6fc5f323c4de4874d0d525e9d` 已将 frozen source 与 mutable hotfix 合并到唯一的 `LoadAssemblyImages` 事务，移除可绕过原子发布的旧 frozen loader。重新组装的 `runtime-v5` 在 Unity 2022 真实 headers 下 CTest 1/1、FGS 通过。

随后 package `2d4c3845d8716087c49ec9d50176abd4e1253548` 增加了 `AdditionalMvJsonPaths` guard-only 输入，允许项目为普通冻结 AOT 程序集提供完整 MV 方法表；这些程序集仍保持 AOT 分类，不会被加入 `hotUpdateAssemblies`。基于该提交组装的 `runtime-v6` 在 Unity 2022 真实 headers 下 CTest 1/1、FGS 通过。

提交后的 managed execution-plan 门禁也已重跑，所有检查通过；该门禁覆盖统一 API 参数记录、双 Base 选择、schema、hash/MV 绑定和失败重试语义。

随后在 Unity 2022.3.62f3 Windows Player 上完成了同一 Base 的 resource-only no-op staging：修正 staging 物理目标路径后，`Snapshot.exe` 返回 `passed=true`、`resourceUpdate=true`，并验证 staged `payload/*.dll.bytes` 与 `payload/*.mv.bytes` 可被 Player 读取。该结果只证明现有 hotfix 路径，尚未证明普通 AOT frozen source 的 direct-entry 适配。

`materialize-frozen-aot` 已用真实 Base 快照和人工扩大的 `Payload` Current 生成 source assets/plan：2 个普通源被选中（`HybridCLR.ValueLayoutNative`、`mscorlib`），其中包含普通 Echo、内联 owner、泛型调用等 token。`mscorlib` 被选中说明完整程序集扫描会触及框架泛型代码，必须经过单独 Player 验证；该命令本身不等于可加载证明。

`merge-frozen-aot` 已将 source records 写入匹配的 `baseSelections[baseId].frozenAotSources`，避免多个 Base 共用错误的普通 AOT 源。示例合并输出验证了 2 条 source records 被绑定到 `9e7cc69b...` Base。

资源生成器现在接受 `-FrozenAotPlans`，要求每个 Base 传入一个已绑定自身 `baseId`、AOT 快照 SHA、源 DLL/MV SHA 和 token 选择的计划，并将记录写入资源 validation/runtime plan 的对应 Base selection。旧 Base 若没有重新构建 native manifest 的 `frozen-aot-source-v1` 能力会明确失败，不会静默发布。

策略结果包括：完整快照读取、重复框架引用身份保留、普通 AOT 原始 Echo 与内联 owner 选中、StaticNeighbor 保持 AOT、静态值字段选择、generic-context 和 native-only 义务、错误 Base/Current/manifest/source 拒绝，以及引用重定向拒绝。

## 仍未通过的发布门禁

普通 AOT 投影尚未在真实 Unity Player 中完成资源 staging 和调用验证；加载 API 已具备，但尚未证明旧机器码 direct native entry、byref/数组/容器、回调、反射、对象已存在时的静态状态迁移和两个普通代码不同的 Base。因此资源编译器仍保留 ordinary-layout/native-ABI rejection，不能称为生产可发布。

下一步应在 Unity 2022 Windows 新建 Base，先加载 source-bound projection 并执行上述 Player correctness 用例；通过后再把相同接口和 hook 移植到团结 2022。Android/iOS 真机、PSS、尾延迟和温度仍需项目侧环境补测。
