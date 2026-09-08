# DHE 普通 AOT 派生类：Windows 候选验证

## 结论与适用范围

普通 AOT 程序集继承 DHE hotfix 类型时，Current 新增虚方法导致 GetBaseDefinition
错误返回另一个方法的问题已复现并修复。普通 AOT 程序集始终不参与热更；
本轮只修正它与已热更父类型之间的元数据查找。

Unity 2022、团结 2022 两个新 Base 的构建、no-op 和 schema 均通过；
changedMethodCount=0、interpreterEntryCount=0，9 组普通 AOT 检查实际执行。
加入原有八个不重建的 Base 后，同一份 first/latest DLL/MV 完成 30 个唯一 PID
的连续和跳版本回放，每次 71 组演化、220 differential cases，差异为 0。
新两个 Base 的六次回放各有 9 组普通 AOT 检查全部成功。

这些边界的 Windows correctness 有条件通过。完整 DHE 目标仍未完成，
没有当前身份的性能或内存收益结论，也未发布到正式维护分支。
只有原本 hotfix 的四个测试程序集进入 DHE；Unity 2021 已退出新验收范围。
连续更新是替换资源后启动新进程，不是在同一进程内卸载 Current。
Android、iOS、ARM64 和小游戏真机仍待用户具备环境后验证。

## 精确身份

| 组件 | commit |
|---|---|
| HybridCLR | 760634e503c8ad5f9099f10b8546d1a018ca8e80 |
| Unity 2022 IL2CPP（复用） | 3482c81998b5e382b59a3d6f4e5fec2d0988d22b |
| 团结 2022 IL2CPP（复用） | df8d0123d9f5a9fce79283fe5261dba21e802aa0 |
| Unity package（复用） | bc319e5e376d97ed89fdd0f127d4256fa466f1ef |
| 冻结 C# host/toolchain 来源 | a56ac6780aafa45f28f629d4da99685370498a86 |
| 最终 runner 与十 Base 回放 | 5efbb2cbdb0a3b6e23b51dc6f9d6bd41bbd7c199 |
| 普通 AOT fixture 源码 | 019ceeb0c883eb31b20e68427b1b3f7e8ba9ebdf |
| 强制 fixture 链接和执行的 Demo | 96d0928c5b2767eaf2b4f13e734735b47ff5fc2f |
| Unity 2022 scratch Demo | 54e0d6eb732ff8d08ed810235b5791fd605cd315 |
| 团结 2022 scratch Demo | 160182808603946c2aea983c9dcfd8def3df248d |

HybridCLR canonical source SHA-256：
9947303526A03889A70B0DE0A982FF160716423EFAEE055A5E874E482FC4D1E9。

工具快照 toolchain-native-descendants-bound 的 Package ID：
19cbbbcc19c9c516b9fcdc8737bc473905fbc67be6ed2ad7ce0b35f9eb5d50d8。
保持 Exploratory / releaseReady=false。合同仍为 dhe-runtime-v27，
MV 仍为 DHEMETA1/schema 1；本轮是未发布能力的修正，没有新增协议版本。
报告提交不替代上述执行身份。

## 修复与 review

旧实现用普通 AOT override 的 Base slot 索引 Current 父类表。
当 Current 在 Stable 前新增 Inserted 时，GetBaseDefinition 错误返回 Inserted。
两引擎的 first/latest/跳 latest 六个独立进程全部复现该错误，其他八组普通 AOT
检查通过。冻结普通 DLL 分别搭配 Base、raw Current、准备后的 first/latest
在 CLR 下均为 9/9，通过参考执行确认预期语义。

修复在 MetadataModule.cpp 的 GetDheCurrentVirtualBaseMethod：
跨越普通 AOT 类和 Current 父类边界时，先找 Base 根声明，再映射 Current 声明的槽。
随后沿每个实际方法声明继续查找；每一级遇到 new-slot 均停止。
definition=false 仍返回最近的继承实现，让属性继承保留中间原生 override。
这同时验证了 NativeGrandchild 的中间父类属性，以及 native new-slot 的独立根。

两个调用者仍持有 g_MetadataLock；本轮没有增加缓存、修改发布顺序或对象布局。
现有 Current 注册的 acquire 与 metadata 初始化约束继续适用。
这些检查不代替 ARM64 并发验证。全局 metadata lock 的稳态成本仍需测量。

九组普通 AOT 检查覆盖基方法定义、原生 override、继承 Current override、new-slot、
反射、委托、闭合泛型与泛型虚方法、含引用值类型返回、显式 base 调用、
具体构造的 sealed hotfix 接收者，以及继承特性。返回值类型本身布局未变化；
不能据此声称支持已有 struct 字段布局热更。

## 产物与独立复核

以下路径相对 C:/hybridclr_optimize/artifacts/dhe-evolution-20260908。

| 证据 | 路径与结果 |
|---|---|
| 两引擎真实头文件 compile/CTest | native-native-descendants-bound；mergeReady=true、surrogateExternalHeadersUsed=false |
| 修复后的 Unity Base | base-native-descendants-fixed-u22；构建/no-op/schema 通过 |
| 修复后的团结 Base | base-native-descendants-fixed-tj；构建/no-op/schema 通过 |
| 多 Base registry | registry-native-descendants-ten.json；原八 Base 加两个新 Base |
| first/latest 资源 | resource-native-descendants-ten-first、resource-native-descendants-ten-latest |
| 完整回放 | replay-native-descendants-ten/report.json；30/30 通过 |
| 独立哈希和覆盖复核 | replay-native-descendants-ten/independent-audit.json；551 个文件 |
| 普通 AOT 的 CLR 参考 | native-descendants-prepared-first-reference.json、native-descendants-prepared-latest-reference.json；均 9/9 |

新 Unity Base ID：
2fa91358c423a494e20aba5b90ce56fcaf8f150a2919f8145502998a2462c959。
新团结 Base ID：
773d4f9e280f9d16a53204faf4a178466be560f0ceb1124d9ebec9cf3fd90809。

最终 report.json SHA-256：
DD7DC5FA3AB705F3D2CC2D7548D50A023A511B048C6CFA028DBE3FEA45DD6119。
配置 manifests/dhe-native-descendants-ten-bases-windows.json SHA-256：
11A371E3C6813FED81AAFFA0B941AF692E138DA076D7D5FBB66D3598F79B7554。

12 次回放（此前两个 class-virtual Base 和本次两个 native Base）各验证 8 个
未变化且 AOT 入口为正的原有 caller。旧六 Base 中这些类型不存在，其新增解释类型
不计作保留 AOT。全部 20 次 latest/跳 latest 运行各有 220 个解释器 case 入口收据。
不适用的历史删除成员断言仍明确跳过，没有改写为通过。

独立复核包括 runner/tool/config、实际 Player 结果、日志、staging 记录、differential
二进制及参考、演化和 caller 记录，以及受保护文件与对应的原始 Base 文件。
两版资源的 16 个 DLL/MV 与失败回放、前一八 Base checkpoint 完全一致。
增加 Base 只更新 registry 和资源清单中的选择记录。

普通 AOT DLL 原始 SHA-256：
3A1677D7E9E5237BD46E234747DCAECBF7AC4450C5A9B3A18D7D92882A4D9600。
两个 scratch 的输入均与该冻结 DLL 相同。经 Unity 正常准备和链接的 DLL SHA-256：
43FBB79AEBEA4D1E6D60A3251FFE0175E1D9CA8932D8771AF3C6AA281EDCBA65。
两个引擎、修复前后四个 Base 的该文件完全一致；它不在热更资源中。

## 保留的失败和工作区

base-native-descendants-before-u22/tj 是最早的部署实验。当时保留文件误命名为
NativeDescendants.link.xml，导致普通程序集被裁剪，nativeDescendantPresent=false。
它们的普通 workflow 通过不能作为此边界的成功证据。已改为真正 link.xml，
并在包含 fixture 的构建中强制要求该程序集存在、执行九组检查。

base-native-descendants-linked-u22/tj 保留实际包含 fixture 的旧运行时 Player；
其 no-op 通过，但 replay-native-descendants-before 的六次热更失败。
修复后仅构建新的 fixed Base，未覆盖旧 Player、失败报告或资源。

实施在 research/dhe-native-descendants-v8.13.0 的 lab/runtime 隔离工作树，
验证后以 fast-forward 收拢到对应 research/dhe-evolution 主研究线。
引擎和 package 本轮无新增源码改动，正式 optimize 分支、runtime tag、
Installer 默认版本和 CAT 项目均未改动。

两个 scratch 保持已提交的 class-virtual Base 输入和 NativeDescendants DLL，
以便后续 Editor 导入时能解析普通 AOT 类的 hotfix 父类。它们及相关研究工作树
收尾时 clean。旧 stash 全部保留，本轮未增加 stash。
Installer 暂时重置的 OptimizeSize 配置已恢复，两个实际 Player 都以 FGS 配置构建。

已完成的历史 replay-class-virtual-reflection-eight 启用了 NTFS 透明压缩。
前后 5,843 个文件、9,164,422,673 个逻辑字节的内容摘要相同，记录在
native-descendants-compression-audit.json。期间有并行构建，不能把 C: 空闲差值
单独归因于压缩。此项本地存储维护不是跨平台工作流依赖。

本轮回放结束后，replay-native-descendants-ten 也启用了透明压缩。
前后 7,315 个文件、11,465,348,896 个逻辑字节的内容摘要完全一致，报告和 Player
全部保留。记录在 native-descendants-ten-compression-audit.json；完成时 C: 可用
空间约 10.15 GiB，之前约 5.03 GiB。没有留下本任务的 Editor、Player 或压缩进程。

## 下一步与回滚

继续推进已有值类型布局变化、基类替换、更多虚方法 Base 世代、
泛型参数元数据及 Unity Component/serialization 边界。发布前仍需完善
通用 Release 门禁、性能/AOT 保留率/尾延迟/内存及 Android 真机验证。
不能把上述具体测试通过解释为任意后续业务修改均已可热更。

源码回滚采用上一八 Base checkpoint：
HybridCLR 87c6f63b2d190d4bcb9f19e0d3ad63781f58b65b、
lab 5194cb72e19a9d083c3921bd81daf058bd033845，
配合原有 U/T/P 身份和八 Base 资源。
修复前的 native-descendants 实验 Base 缺少正确原生实现，
不能仅靠 DLL/MV 给它补上本轮 C++ 修复；它们不进入受支持 registry。
