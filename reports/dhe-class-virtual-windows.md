# DHE 类虚方法演进：Windows 候选验证

## 结论与边界

Unity 2022 与团结 2022 各有一个包含类虚方法测试类型的新 AOT Base；两个 Base 的
构建、no-op 与 schema 均通过，changedMethodCount=0、interpreterEntryCount=0。
加上此前未修改的六个 v26 Base，同一份 first/latest DLL/MV 完成 24 个独立进程的
连续更新与跳版本回放。每次 71 组演化、220 个 differential cases，差异为 0。

本轮覆盖已有普通类新增 virtual/abstract、增加与删除 override、继承 override、
new virtual 隐藏、闭合泛型类型与泛型虚方法、含引用值类型返回、委托、反射、
GetBaseDefinition、空接收者异常与并发首次调用。新 Base 上的 8 个未变化调用入口
全部记录 nativeChanged=false、正数 AOT 入口计数。旧六 Base 中这些测试类不存在，
按新增解释类型执行，不能将其计为保留了这些类的 AOT。

结论是这些场景的 Windows correctness **有条件通过**。整套 DHE 目标仍未完成，
不构成生产发布或性能收益声明。只有原本 hotfix 的四个测试程序集参与 DHE，普通
AOT 依赖不热更。Unity 2021 不再安排新门禁；Android/iOS 尚无设备证据。连续更新
仍指替换资源后的新进程，不是在同一进程卸载并替换 Current。

## 精确源码和工具身份

本轮在 research/dhe-class-virtual-v8.13.0 及两个 engine 对应研究工作树实施，
候选提交以 fast-forward 收拢回 research/dhe-evolution 系列工作树，运行时提交身份不变。
没有修改正式 optimize 分支、runtime tag、Installer 默认选择或 CAT 项目。

| 组件 | 精确 commit |
|---|---|
| HybridCLR | 87c6f63b2d190d4bcb9f19e0d3ad63781f58b65b |
| Unity 2022 IL2CPP | 3482c81998b5e382b59a3d6f4e5fec2d0988d22b |
| 团结 2022 IL2CPP | df8d0123d9f5a9fce79283fe5261dba21e802aa0 |
| Unity package | bc319e5e376d97ed89fdd0f127d4256fa466f1ef |
| 冻结工具快照 | bdba246bf4f4e5e20c7616a51da7274b2ddf6a65 |
| 最终回放源码与 runner | 8634f3c8fd9cd0156c8aa87a5d139bc427be1525 |

合同为 dhe-runtime-v27，新增 existing-class-virtual-methods-v1 能力；MV 仍是
DHEMETA1/schema 1。package runtime 与 Editor 构建器的能力声明同步更新。
没有为 package 创建 tag。

工具快照为 toolchain-class-virtual-reflection-bound，执行文件在同名前缀的
host-class-virtual-reflection-bound。Package ID：
b33095f3cf076137aff4f6aadadb177946bca693d304c861bfe9e247099d5a6b。
它保持 Exploratory、releaseReady=false。后续仅文档提交不能替代上述执行身份。

下文产物路径均相对于 C:/hybridclr_optimize/artifacts/dhe-evolution-20260908。

## 运行时修复

1. 原始 AOT 调用继续携带 Base 槽位。新查找从 Base 根声明映射到 Current 声明，
   再选择 Current 接收者实现，避免删除旧 override 后仍调用旧 tombstone。
2. 解释器、Object/Class 的虚方法查找保留 MethodInfo 身份，区分 Current 新增方法
   与 Base 方法碰巧相同的槽位编号。物理 Base vtable 不扩容、不改编号。
3. 两个引擎真实 Player 发现 AOT 抽象类可能没有对应 vtable MethodInfo。现从其
   已初始化方法表补查声明，并增加原生空槽、错误槽、非虚方法等回归。
4. 真实热更回放发现 Type.GetMethod 返回新增 override 和旧父方法两项，导致歧义。
   现以逻辑根声明对反射方法去重，并让 GetBaseDefinition 遍历 Current 父表。
5. 回放工具保留已结束 Player 的进程句柄直到报告写完，避免 Windows 在大量编译
   进程活动时复用 PID。唯一 PID 检查仍然是硬门禁。

两套虚调用缓存按原始槽与 MethodInfo 分开。注册状态使用 acquire 读取后才访问
Current 视图，缓存构造与读取均在 g_MetadataLock 下；entry 发布后不变，unordered_map
节点地址在 rehash 后保持有效。缓存只持有进程生命周期的 metadata，不持有接收者
对象。以上源码审查不替代 ARM64 并发与实际设备验证。

## 当前身份的证据

| 证据 | 路径与结果 |
|---|---|
| 两引擎真实头文件 native compile/CTest | native-class-virtual-reflection-bound；均 mergeReady=true、surrogateExternalHeadersUsed=false |
| 新 Unity Base | base-class-virtual-reflection-u22；构建/no-op/schema 全通过 |
| 新团结 Base | base-class-virtual-reflection-tj；构建/no-op/schema 全通过 |
| 多 Base registry | registry-class-virtual-reflection-eight.json；旧六 Base 加本轮两个新 Base |
| first/latest 资源 | resource-class-virtual-reflection-eight-first、resource-class-virtual-reflection-eight-latest |
| 完整回放 | replay-class-virtual-reflection-eight/report.json；24/24 通过 |
| 独立复核 | 同目录 independent-audit.json；378 个文件重新计算哈希，无差异 |

新 Unity Base ID：f6cbc22502d32399101eb26e27510907bdf0864c45bf55050ddd67277062cba9。
新团结 Base ID：0186cf3e76b7b31a2c866570500f96ad32890c97fd667475470b50b81bb3ad7f。
旧六 Base ID 见 dhe-six-base-and-evidence-windows.md，原始 Player 未重建。

最终 report.json SHA-256：
BFB1E6B00E5538E3FE4B8597128C9CF95D67A0DE9601D9A61A099D84F30A3361。
配置为 manifests/dhe-class-virtual-eight-bases-windows.json；其 SHA-256：
160D2C70130B3990F42219684EB9CF21D5B04C3A1D24A528AFDAA9786384C72A。

24 个 PID 均不相同。每次执行 71 组实际演化 receipt 与 220 cases；所有 latest/skipped
运行另有 220 个解释器 case 入口 receipt。新 Base 的六次运行各验证 8 个保留 AOT
caller。原有未适用的删除成员断言仍明确跳过，不计作通过。

独立复核重新计算 Player 报告、日志、演化 receipt、差异结果、caller receipt、
回放不可变文件及其原始 Base 对应文件的哈希。修复反射前后，两份资源的 16 个
DLL/MV 文件逐字节一致；运行时修复没有通过改写预期结果或 payload 绕过问题。

Current 输入通过 Unity 正常准备流程生成：prepare-class-virtual-current/current。
latest 在 lab 工作树 artifacts/class-virtual-observable-latest，使用现有 C# 工具
推进可观察版本，并带 220 case 入口插桩。插桩只用于 correctness，不用于性能结论。
first/latest 的完整 CLR 参考和 differential golden 比较均通过。

## 保留的失败

- class-virtual-rejected-before：原分析器拒绝已有类新增虚方法，8 个 caller MV 不变。
- class-virtual-analysis-candidate：编译器增加泛型参数 NullableAttribute，被现有
  元数据门禁拒绝。后续 fixture 在两侧统一关闭 nullable annotations 来隔离虚方法
  场景；没有放宽泛型参数元数据门禁，该能力仍需另行实现。
- base-class-virtual-u22/tj：抽象 Base 槽位缺少 MethodInfo，两引擎 no-op 失败。
- base-class-virtual-abstract-u22/tj：修复抽象槽后 no-op 通过，但热更反射尚有问题。
- native-class-virtual-reflection：Unity 编译通过，团结因缺少显式 hook include 失败。
  最新两个引擎均显式包含依赖，并在 reflection-bound 身份重跑通过。
- resource-class-virtual-old-six：把未经 Unity 准备的 .NET DLL 交给旧 Base，被引用
  身份检查拒绝。正式回放使用准备后的 DLL；没有忽略 netstandard/mscorlib 差异。
- replay-class-virtual-old-six：前置检查发现两份资源的可观察结果没有推进。
- replay-class-virtual-old-six-observable：17 成功、1 次 PID 重用被拒绝。
- replay-class-virtual-eight：旧六 Base 的 18 次运行通过；新两个 Base 的 6 次运行
  因反射歧义失败。此轮使用了 24 个唯一 PID。

上述失败产物及原始报告全部保留。中间失败的实验 v27 Base 不属于最终支持 registry；
它们缺少已修复的原生能力，不能通过 DLL/MV 安装修复。尚未对外发布这些实验 Base。

## Review 后仍需推进

- 当前通过的是列出的类虚方法场景。需要继续补跨程序集的既有类继承、普通 AOT
  派生类边界、可能被编译器直接调用/内联的调用点，以及把已演进的虚方法再编入
  新 Base 后继续更新的世代。
- 普通 AOT 派生类没有自己的 Current shadow；其 GetBaseDefinition 路径仍存在以
  原始方法槽访问 Current 父表的风险。需要专门修正和真实 fixture，当前不宣称该
  边界完成。该风险与其他未完成能力由 releaseReady=false、禁止生产发布约束。
- 已有值类型字段布局、基类替换、泛型参数元数据、Unity Component/serialization
  和外部 AOT API 可用性仍未完成；不能将此次通过解释为任意业务改动都可热更。
- 虚调用缓存目前使用全局 metadata lock。需要测量并优化稳态查找成本、反射去重
  成本、AOT 保留率、尾延迟及内存。没有当前身份的性能/内存收益数据。
- 完整 Release ledger/authority/source 门禁、Android/iOS、ARM64 仍待完成。

源码回滚可选择六 Base v26 报告中冻结的运行时/package/tool 组合及其兼容资源。
当前实现的原生修复需要新 Player；不能移动 runtime tag 或重新标注历史证据。

## Demo 输入与工作区

两个 scratch Demo 的已验证输入 DLL 已保存到 stash，工作树恢复 clean。最终两个
stash 为 Unity 5544f4af638e90e7016b226baa0620118fab5fce、团结
f0ced639eecf633bf4a7ff33bf2b0ba99140fd43。当前 scratch package 提交分别为
337284c、9a21132，均迁移了 bc319e5 的两个合同声明文件；安装的 runtime 为
runtime-class-virtual-reflection-bound 中各自的精确来源。

本轮早期输入/设置的恢复点也保留：Unity b9bdb6f、a59ac6e；团结 1b99e72、8bae46e。
没有删除以前的 stash 或失败 Player。后续构建输入仍以显式归档和 manifest 为准，
不要直接恢复不明 stash 后把它当作当前已验证构建。

本轮已结束的 replay-class-virtual-old-six-observable 与 replay-class-virtual-eight
副本启用了透明 NTFS 压缩。前后复核全部 10,206 个文件的内容哈希汇总完全一致，
文件与路径均保留；观察到 C: 可用空间增加约 6.8 GiB 至约 17.4 GiB。详细结果在
class-virtual-replay-compression-audit.json。这是本地存储维护，不属于跨平台构建
依赖，也不改变任何历史执行结果。
