# DHE 多 Base 结构热更与初始化验证

2026-09-09。Unity 2022.3.62f3、Windows x64，源码候选有条件通过。
两个不同的不可变 Base 加载同一份真实 Unity 编译后的 Current DLL/MV：
每个 Player 的 31 条 Current 记录与 CLR 完全一致，普通 AOT 跨程序集调用结果一致。
本报告替代此前“新增方法默认值 Missing、静态初始化两次”的失败状态，
不代表整个 DHE 目标完成，不是 Android、iOS、团结或生产性能结论。

## Review 与剩余门禁

- 本轮两个已复现错误均修复：默认值查找以 Current 方法定义及其所属 image 为准；
  Base 与隐藏 Current 静态访问先选择同一初始化 owner，再使用 IL2CPP 原有的锁、
  递归/线程状态、原子完成发布和异常缓存。未复制完成位，也未改动静态字段存储地址。
- 参数元数据与方法执行目标分开：ParameterInfo.Member 保留逻辑身份，默认值读取
  Current。类型/方法映射在原有 DHE acquire 发布之后读取，准备失败无需撤销 Base
  cctor 状态。未验证已运行初始化器的在线替换。
- 资源编译器只放行参数常量及 Optional/HasDefault 的变化；仍拒绝方法权限、实现
  标志、参数名称、In/Out、marshalling、参数属性、约束和安全声明等邻接变化。
  添加能力声明，旧 Base 缺少能力时拒绝出包；未修改旧 Base 的身份文件。
- 尚未解决的主要能力边界仍是静态值类型字段扩容、普通 AOT 的值布局/原生 ABI。
  `current-storage-static-value-field`、`current-storage-ordinary-aot-layout`、
  `current-storage-native-abi` 拒绝条件均保留。
- 新增/删除 cctor、Base 原本无 cctor、泛型参数本身发生值布局演化等组合仍需专门
  Player 对照。本轮泛型初始化证据只覆盖 string/int 参数；默认值移除只有编译器
  policy 测试，不能当作真实 Player 的通过证据。
- 按用户指定顺序，先完成 Unity 2022，再移植团结。不再安排 Unity 2021。
  未改 CAT、正式维护分支、runtime tag、远端或 Installer 默认版本。

## 锁定源码

以下均是隔离候选分支，不是项目组应直接引用的正式发布分支。

| 仓库 | 分支 | 验证提交 |
|---|---|---|
| hybridclr | research/dhe-value-layout-v8.13.0 | 8cbff343f44ca9c677fb4e8d4f9e33747bc7f5d8 |
| il2cpp_plus | research/dhe-evolution-unity2022-v8.13.0 | 9d4e4f6a22f7059cd7046e71ef9639ee7de49bbb |
| hybridclr_unity | research/dhe-evolution-v8.13.0 | 083dc32a1fd44e06a2b239dedf78bc1935b0d9e7 |
| lab | research/dhe-value-layout-v8.13.0 | 9496abe658dac294c474b9058beed7ee258bd77d |

报告后续提交只增加文档，不将报告提交冒充上述构建身份。保留前序实现并在其上修复，
未重写或移动旧提交。package 没有创建 opt tag。

- HybridCLR canonical tree：`ED49FEDC823A9881E64D595E136B122D52B6D8F84D0214BA444047DF99F8AC16`
- IL2CPP canonical tree：`38E5DDBC34ECF680C00D4EDEA9AECB98F6CD34A75561D58B32D0B7F8D333523F`
- package canonical tree：`8A11315BD0D7535CC0CE8D00910BF0025E71DDA3852E36A1088AAFE887728D9B`
- assembled native tree：`7FCDB3AD324D2C05A9167B30584B34D12EF332C2B794431E905C3E63ACD99235`
- runtime manifest：`0FBB90F188468AA7FFD07DB8FC9A707317CE6C8C0B061555FC1EDAC2A6D3B9D4`

## 当前证据

证据根目录：`C:/hybridclr_optimize/artifacts/dhe-resource-evolution-20260909`。

| 证据 | 结果与范围 |
|---|---|
| native-capabilities/DHE-Unity2022/native-gate.json | 真实 Editor headers 编译及 CTest 通过；mergeReady=true、surrogateExternalHeadersUsed=false；包含 Parameter.cpp、Runtime.cpp 编译门禁 |
| default-policy/result.json | 8/8；允许常量改变/移除默认值，拒绝邻接元数据变化与旧能力列表 |
| old-capabilities-rejected/dhe-resource-update-validation.json | 预期拒绝；缺少 current-parameter-default-metadata-v1 与 shared-type-initialization-v1 |
| capability-matrix/result.json | 8/8 总体检查；两个 Base 的初始/无变化/结构热更对照及共享载荷检查全部通过 |
| capability-matrix/player-current-old.json | 31/31 Current 记录一致，ordinaryAotReferenceResult=11000000032 |
| capability-matrix/player-current-new.json | 31/31 Current 记录一致，ordinaryAotReferenceResult=11000000032 |
| capability-matrix/stage-old.json、stage-new.json | 同一载荷，Base MV 与 Player/GameAssembly 不变 |

验证使用全部三个 fixture hotfix 程序集，普通 AOT Native DLL 保持独立。
两个 Base 初始 revision 分别为 41/51，Current 为 73。Current 使用独立 Unity 项目
生成 stripped DLL；CLR reference 使用同一批实际 DLL，不以 SDK 控制构建替代。
覆盖新增字段、方法和类型、修改虚方法及继承调用、普通 AOT 引用对象调用，
以及 int/string/null/enum/long/bool/泛型默认值、已存在方法默认值 2→17、
反射 Member 身份、静态字段初值、递归初始化、两次异常访问、8 线程混合直接/反射
首次访问和两个泛型实例的初始化。静态初始化次数全部为 1。

共享 Current assembly set：`6987b41ae43e4dfb68f55175907e267e9a1aa5648ebcfa5c53b7ad2f098d8e05`。
资源 manifest SHA：`B323A11B717F8B33A01D8576FF6BC2557A242CD9C006600604634C84652FBF5F`。
测试 host SHA：`581EAAFBA187A3282A7CF6F651CA713567968205DF4720E7FD85D45FAEE45B87`；
tool SHA：`99D92C99379A2B9EB51BDEDF15F57A433088F8C5343C0C627A2ABFB9EA7522EC`。

## 历史失败与复现

`reference-matrix` 是旧 runtime 下默认值 Missing、初始化次数 2 的失败证据。
`initialization-matrix` 使用本轮 native 修复、旧 package，初始 Base 成功，但被已有方法
默认值变化的工具门禁拒绝，不能标为结构热更通过。`first-current-*` 是最终第一个 Base
加载上一轮真实 Unity Current 的提前验证，31/31 一致；最终双 Base 结论以
`capability-matrix` 为准。没有覆盖这些旧目录。

在锁定提交构建 tool、value-layout fixture、aot-snapshot fixture（最后一项传入
`-p:DhePackageRoot=<package绝对路径>`），用 C# host `assemble-runtime` 锁定本轮
三仓源码，随后 `native-tests -Profile DHE-Unity2022`。完整工作流命令为：

```text
dotnet AotSnapshotTests.dll evolution-workflow <lab> <package> <Unity.exe> <runtime-manifest.json> <全新输出目录>
dotnet ValueLayoutTests.dll parameter-default-policy <Base Model DLL> <全新输出目录>
```

流程全部由 C# 工具/fixture 执行，未新增 ps1。没有性能、P95/P99、内存或 ARM64
测量结论；八线程单次正确性测试不能代替长期并发压力测试。

## 后续与回滚

后续静态值存储及真实资源验证已推进，见
[dhe-static-value-storage.md](dhe-static-value-storage.md)。下述内容保留本报告原始身份。

下一步先验证静态值存储的 Current 地址、未变化字段的一致性，以及包含演化值类型
的泛型初始化状态；再解决普通 AOT 值布局/ABI 边界。出包门禁只有在实现与 Player
对照都通过后才可放开。Unity 2022 完整验收后再移植团结，由用户补 Android 真机。

本轮 native 两提交必须配套回滚；package 能力列表与 lab policy/locks 一并恢复。
回到本轮开始前的组合：HybridCLR `0b5252cde1192194b17705d037ef5c9dab762701`、
IL2CPP `80ee2ce13208b0f8b1984e1d78b3a29105472160`、package
`b575d5805027547c719e9d5c505007246b04de6d`、lab
`f7df334669105c8a8ff7845138075e98e5b38aa5`。该组合有本报告记录的缺陷，只能作为
研究回滚点；已构建母包不能靠资源包更新其 native runtime。

四个候选工作树在报告提交后保持 clean，无新增 stash。C 盘剩余约 151 GiB，
本轮无需清理；没有删除旧源码、母包或失败证据。
