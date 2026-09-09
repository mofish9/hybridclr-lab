# DHE Base AOT 快照与 Windows 资源闭环

2026-09-09。结论为**有条件通过**：Unity 2022 Windows 已跑通 package Base 构建、
最终 AOT 快照冻结、无变化 Current 资源生成、staging，以及同一个 Player 的资源加载。
这不是任意结构变化的完整 DHE 验收，也不是正式发布或移动端性能结论。

按用户最新范围，仅优先验证 Unity 2022.3.62f3 / Windows x64 / OptimizeSize。
团结 2022 等这条工作线完整通过后再移植；不再为本目标增加 Unity 2021 工作。
热更对象仍是原 hotfix 集合；普通 AOT DLL 被保存用于依赖分析。

## 本轮实现

- 捕获完整 stripped AOT DLL 清单，区分 DHE 与普通 AOT，保存原始 DLL 和各自哈希。
  快照位于 Base 输出的 `aot-analysis/<manifest-sha256>/manifest.json`。
- 将快照摘要绑定到生成的 C# 身份、复合 Base ID、资源 supportedBases 和 staging 校验。
  资源编译器使用该 Base 的普通 AOT DLL 分析布局依赖；缺失快照的历史 Base 仍不能
  发布需要 Current storage 的资源，静态值存储和普通 AOT ABI 拒绝条件继续保留。
- dnlib 归一化只清除模块构建 ID、时间戳和指定生成身份类型的常量/初始化体，保留
  类型、成员、签名及其他代码。归档保持快照 JSON 原始字节；移动后仍能验证。
- 最终构建若改变 native identity 或 stripped AOT 输入，验证原快照完整性后重建
  身份并额外构建一次。最终 native identity 和 AOT 输入必须同时匹配，不能无限重试。
- 修正 package runner 固定使用热更输出目录的问题：预检和依赖回调使用冻结的 Current
  集合，因此外部预编译 hotfix DLL 也能走同一流程。补齐外层 host 必需的 Player 构建报告。
- 对 Windows 原子替换的 sharing/lock/unable-to-remove-replaced 错误最多重试六次，
  总等待 1050 ms；不使用删除后重写的降级路径，持续失败仍保留旧完整记录并报错。
- Unity JsonUtility 会把缺省或 null 内联对象变成空对象。可选计划改为
  `executionPlans` 数组，长度只能为 0 或 1；计划含明确的 token 数量校验。
  能力标记为 `current-storage-execution-plan-array-v1`，旧加载器不能误接收新计划。
  DLL/MV 及 MV schema 没有因此改名或改变格式。

## 源码身份

本轮没有修改 native 两个仓库；保留前面已经验证的实现。以下均为本地候选工作线，
未推送正式分支、创建 tag、修改 Installer 默认引用或迁移 CAT。

| 仓库 | 候选分支 | 验证使用的提交 |
|---|---|---|
| hybridclr | research/dhe-value-layout-v8.13.0 | 0b5252cde1192194b17705d037ef5c9dab762701 |
| il2cpp_plus | research/dhe-evolution-unity2022-v8.13.0 | 80ee2ce13208b0f8b1984e1d78b3a29105472160 |
| hybridclr_unity | research/dhe-evolution-v8.13.0 | b575d5805027547c719e9d5c505007246b04de6d |
| lab | research/dhe-value-layout-v8.13.0 | 540c03f18f0830bcf6ac84235d103531be712bda |

package canonical tree SHA-256：
`B86595231A0FF080D879A3FE8EE9212E2904523CFF56E33FF44887A836AA2C27`。
repo/package/runtime locks 已绑定上述组合。本报告后的文档提交不改变被测实现身份。
package 不使用 opt tag。

## 证据与适用范围

证据根目录：`C:/hybridclr_optimize/artifacts/dhe-aot-analysis-20260909`。

| 证据 | 结果与身份 |
|---|---|
| unity-arrays/result.json | 通过；lab 540c03f、package b575d58。真实 package Prepare / StageRuntimePlan / BuildScriptsOnly / BuildFinalPlayer，最终 native 证据经实际 host 校验 |
| unity-arrays/player-result.json | 内置资源启动通过，加载 3 个 DHE 程序集，revision=41、sentinel=5 |
| unity-arrays/resource-player-result.json | 同一个 Player 使用 InitializeFromResourceUpdate 加载生成资源通过；Base MV 仍从内置目录读取 |
| unity-arrays/stage-noop.json | 通过；EXE、GameAssembly.dll 前后 SHA 一致，内置 MV tree 前后 SHA 一致 |
| schema-base-arrays.json / schema-resource-arrays.json | 实际 Base 和生成资源的完整 schema gate 通过，识别新快照格式 |
| unity-json-arrays.json | 9/9；真实 Unity Editor 调用 package 实际计划校验，覆盖 null/缺省、有效、空对象、重复、缺失数组、计数不匹配 |
| execution-host-arrays.json | 50/50；lab 540c03f、package b575d58。两 Base 计划选择、MV 绑定、编译器与 staging/schema；native 调用在宿主中记录，未执行 |
| snapshot-final/result.json | 73/73；lab 6bc1a32、package dbbc5ce。完整真实 Unity DLL 清单、归一化、篡改拒绝和实际归档重写器。此后快照归一化/读取代码未改变；这是组件证据，不冒充最新 Player 整体验收 |
| compiler-sharing/report.json | 36/36；lab f151b15、package 0dbf00b 的 compiler session，使用真实 Unity 2022 compiler DLL；包含短暂/持续占用及旧记录保留。该组件之后未改 |

最终 Player Base ID：
`e4729f6b86affcf69667f05734ac65c8c7b1c802a17dc52551a704c880ceb019`。
快照含 40 个普通 AOT DLL 和 3 个 DHE DLL；最终 manifest SHA-256：
`624c65dd646fe57b3462be7cfc1b50bf402366f10f4e66aa735809588e930f14`。

关键最终 SHA-256：

| 产物 | SHA-256 |
|---|---|
| runtime-arrays/DHE-Unity2022/runtime-manifest.json | 5BBE8F5B161719D3B09EBDF95C8F1270A94577AF492F855BAC03E434F5BCDA60 |
| HybridCLR.DheTool.dll | 3C931BFD501E1C2E0DD8DACA16DED48A03A309B359238B3A76274FE79123C8CE |
| AotSnapshotTests.dll | B8E2406B56666AA5075E7661EF3A2D4FF41D9865D81B994CB00691F266BBB4AA |
| GameAssembly.dll | 13C93EF791354BA56147B9AD58710D818D9557222857A35AD37B590A327ABA0E |
| resource-noop/dhe-resource-update.json | ECA74B6CA6E21899A76A76DECE94F26F5AEFF351A948E944481BF68666C593D5 |

最终报告记录实际输入和所有进一步产物哈希。之前 public-reflection 的五个 Player 场景、
14 个 CLR reference records、27 项审计以及 real-header native CTest 仍属于其原身份，
见 dhe-public-storage-api.md。本轮未改 native 实现，未重新宣称这些为新 Player 的结果。
本轮没有性能、尾延迟、内存或 ARM64 结论。

## 复现与失败记录

在对应 lab 提交构建 `tool/HybridCLR.DheTool.csproj`、
`tool/fixtures/value-layout/ValueLayoutTests.csproj` 和
`tool/fixtures/aot-snapshot/AotSnapshotTests.csproj`；最后一个传入
`-p:DhePackageRoot=<候选package绝对路径>`。使用锁定源执行 assemble-runtime 后运行：

```text
dotnet AotSnapshotTests.dll unity-workflow <lab> <package> <Unity.exe> <runtime-manifest.json> <初始四个fixture DLL目录> <全新输出目录>
```

初始 DLL 本次取自旧 storage probe 的 project-old/Assets/Plugins/ValueLayout。
测试会通过真实 Unity Prepare 生成用于比较/资源生成的 stripped Current；没有把 raw SDK
Current 直接作为公共资源兼容性成功输入。该 fixture 只验证无变化资源，未验证完整 release
渠道/资源 catalog 流程。

保留所有失败记录：unity-workflow 暴露外部 DLL 输出目录问题；unity-frozen-inputs
与 engine-diff 显示 UnityEngine.TextAsset 类型转发在最终裁剪中消失；unity-resource
暴露 host 不支持的 schema keyword；unity-complete 记录 generation.json 原子替换失败；
unity-atomic 和 unity-json.log 记录 JsonUtility 的空计划问题。这些不是通过证据。

## 剩余目标与回滚边界

1. 继续用正式 Unity Current 生成路径验证实际代码/结构变化，以及多 Base 同一 Current
   的完整资源加载；当前最终闭环只有一个新 Base 和无变化资源。
2. 完成静态值存储与普通 AOT 值类型/native ABI 边界。不能仅凭 identity-return 测试
   宣称任意 native 值拷贝桥接成立。现有出包拒绝条件仍生效。
3. 扩展反射/虚调用/泛型/异常及并发启动的综合验证；旧反射对象或已经运行实例的在线
   替换尚未验收。之后才移植团结，并由用户进行 Android 真机测试。

本轮代码回滚边界是 package 快照/身份/计划 JSON/构建可靠性，以及 lab 编译器、
staging、schema、locks 和 fixtures；native 两仓库无需回滚。恢复研究起点可整组使用
package c439f2733ce23a33ef1fe6ecbc873c962f124d8c 与
lab 3c2405e6c4b87e7c8073eda2bae7fc2173a59e54；那是旧研究方案，不是生产降级版本。
旧 Base 和快照不可覆盖，不兼容资源继续拒绝出包，保留该 Base 原有可用资源。

四个相关候选工作树在交付文档提交后保持 clean，无新增 stash；C 盘剩余约 174.5 GiB，
本轮未进行清理。
