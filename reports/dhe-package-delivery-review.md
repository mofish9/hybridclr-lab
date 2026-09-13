# DHE 三仓库与 package 交付闭环复审

历史检查点：当前交付状态以 [opt6 项目试用报告](dhe-opt6-project-trial.md) 为准。
下文保留当时提交与失败证据，不表示 opt6 的当前状态。

日期：2026-09-13。结论：职责划分正确，候选版已补齐主要 package 交付缺口；
**结构演进的 Windows Player 门禁失败，尚不能合入并宣称整套 DHE 可交付。**
本轮不迁移 cat，不移动 opt5 tag，不推进正式 package 分支。此前方法热更的通过
结论仍有效，但不能扩大为任意结构演进、移动端或完整正式发布流程已通过。

## 1. 当前身份及源码归属

| 仓库/位置 | 分支 | commit | 发布引用 |
|---|---|---|---|
| repos/hybridclr | optimize/v8.13.0 | b0fe826f071332d109d2bde87c0aa2cc18b9f3c7 | v8.13.0-opt5 |
| repos/il2cpp_plus | optimize/unity2022-v8.11.0 | ecad8a09d1eb9b91a57c59fcdc69b268377bad59 | v2022-8.11.0-opt5 |
| repos/hybridclr_unity | optimize/v8.13.0 | 4fb36af996871ff1e7ab8585f096395dad5d2bd3 | 无 package tag |
| package 候选 worktree | optimize/dhe-package-delivery-v8.13.0 | dbc02036c0eac6db5fce17b4a5facf3520888114 | 尚未合入正式分支 |

候选实现仍在同一个 hybridclr_unity Git 仓库的隔离 worktree，不是第四个生产
仓库。正式 repos checkout 暂时没有这些新改动，项目不能把候选 worktree 当正式来源。

本轮核查两个远端 annotated runtime tag 及正式分支，均解析到表内提交。基线固定为
package/HybridCLR 8.13.0、IL2CPP v2022-8.11.0。Unity 2021 使用官方原版；
团结与 Android、macOS/iOS 不纳入本轮实际执行结论。没有合入更高版本上游。

当前随包 DLL 的源提交是 `7265f36a28e54902ce572c96e643f45f83424627`，分发提交
是 `82170b04a91e220b694a6b4498fe30563038a703`，之后只添加未通过门禁的说明。
Bundle ID 是 `3ca8f50e1b99fd7624c8788aa672bfd03ebef86a7c89e7ecb512561abfa93659`，
84 个分发文件，模式仍为 Exploratory。来源提交、分发提交、项目迁移提交是不同概念。

## 2. 正确的职责划分

| 位置 | 必须承担的职责 | 不应转嫁给项目的内容 |
|---|---|---|
| hybridclr | MV/Current metadata、AOT/解释分发、注册状态、异常与并发语义 | 执行选择、元数据/对象身份算法 |
| il2cpp_plus | 对应 Unity 版本的类、泛型、反射、数组和 native ABI 接入 | 引擎 hook 与内部布局修补 |
| hybridclr_unity package | 托管 API、Installer、生成器、工具源码和 DLL、安装校验、Base/Current 生命周期、差分/多 Base/资源计划与验证 | MV 解析、native guard、编译器事务、Base 匹配与 frozen AOT 适配 |
| Unity 项目 | 配置仓库 URL/程序集，接入现有打包入口，场景、签名、资源系统、下载与启动 provider、真机 smoke | 不应复制通用算法或依赖维护机 Lab 目录 |
| Lab | demo/测试、能力矩阵、证据、版本锁和维护报告 | 不再作为项目日常打包的生产源码依赖 |

项目需要改打包脚本是正常的：原本只编译/下发 hotfix DLL 的流程，要接入 Base
归档、按 Base 生成和检验资源更新、运行时加载入口。但它应调用封装好的 package
函数，而不是自己重建框架状态机。

## 3. 尚未通过的关键问题

### P1：适配计划通过，结构热更却在业务入口失败

新建两个真实 Windows Base，版本分别为 59 和 61，均使用普通 Installer 从远端
opt5 tag 安装，4 个 hotfix/DHE 程序集、40 个普通 AOT 程序集。对 hotfix 的
Payload 值类型插入引用字段，实际改变布局，随后向同一份 Current 接回归档中的
46 个泛型、集合、反射、静态初始化、线程和普通 AOT 边界用例。

最新 package 工具生成的更新报告为 `passed=true`、`compatibleBaseCount=2`，
两个原 Player 都已加载 4 个 DHE 程序集，却在 `list-growth-shift-copy-and-enumeration`
用例失败，revision 保持 0，异常栈含 `List<T>.Enumerator.Dispose()`：

```text
System.ExecutionEngineException:
DHE Current value layout requires a Current call frame; the old AOT ABI cannot be used.
```

触发保护的位置：`repos/hybridclr/hybridclr/DheRuntime.cpp` 的
`CanEnterWithBaseAbi` / `ShouldDispatchToInterpreter`（约 580–610 行）。保护不能关闭。
调用方选择涉及 package `ToolsSource~/DHE/FrozenAotAdaptation.cs`；托管计划与 native
泛型/值类型调用约定的衔接仍需继续定位，不能仅凭异常位置就认定单一根因已完全确定。

本轮已修复其中一个明确缺口：原计划选择了枚举器构造函数，却漏掉
`List<T>.GetEnumerator()`。新增泛型调用方闭包及测试后，构造边界推进，仍在 Dispose
失败。这是**局部修复，不是完整解决**。不能把托管测试通过或计划 admission
通过当作实际结构热更安全的证明。

证据：`F:/hybridclr_artifacts/dhe-package-delivery/structural-05`，以及
同根 `structural-player-base-03.json`。见旁附 evidence lock 中的文件 SHA。

### P1 能力边界：已有线程静态值类型存储不能随布局一起增长

保持原有 `[ThreadStatic] Payload Value`，直接改变 Payload 布局，工具在生成资源
时拒绝：`current-storage-thread-static-value-field`。这是有效的 fail-closed
负例，不是测试环境问题。普通 AOT guard 完整也不能迁移旧线程静态槽。

为了继续检查其余边界，另一个明确命名的 fixture 引入新的 TLS owner 类型；这不等于
修复或支持原有 TLS 槽迁移，原负例继续保留。新的 owner 方案又复现上面的 Dispose
故障，不能作为完整正例。本轮没有通过修改生产检查策略来让测试放行。

证据：`structural-03/resource/dhe-resource-update-validation.json`。新增类型与已有
类型演进必须区别验收；不能宣称配置为 hotfix 的程序集内所有修改都已支持。

### P2：完整 Release 生命周期仍没有闭合的当前证据

已验证安装生成的回执、严格 source preflight 和随包 schema 不要求维护机 Lab/
原始 Git checkout；但完整 Release 工具资格、归档后证据重放、真实资源系统与
目标设备链路未完成。发布证据拒绝测试通过只证明无效证据不能冒充 Release，
不证明存在一个已通过全部门禁的 Release 分发。本轮继续保持 Exploratory。

源码迁移后，旧 Lab source-distribution/regression 命令还存在 `tool/*.cs` 等旧路径
假设（例如 ProductionGates.cs 的历史回归夹具）。这些命令不在随包 CLI 允许列表中；
正式维护重建走 package 的 `publish-unity-tool -PackageSourceRoot`。在重新启用旧
维护门禁前需要迁移其路径，不能把它们记为已通过。

## 4. 前轮六项交付问题的候选修复状态

| 前轮问题 | 候选改动 | 本轮判断 |
|---|---|---|
| Install 到构建依赖 Lab manifest | package release lock、capture/verify-installation、普通 Installer 自动生成本地记录 | 安装与 source contract 通过；完整 Release 资格待验收 |
| 默认 Base 缺普通 AOT guard | 默认 true，自动从归档 AOT DLL 收集，finalize 严格核对 | 两个 Base 各 49,650 个请求均无缺项；结构正确性整体仍失败 |
| 生命周期只能解析命令行 | Options/Context、C# BuildBase、四阶段显式重载、前后回调 | 真实 Editor 无 -dhe* 参数构建通过；项目仍需适配自己的输入/资源/签名 |
| SVN @8.13.0 路径失败 | SVN local path 统一追加空 peg | 真实本地 SVN 仓库的目录和文件 @ 路径通过 |
| CLI 生产源码归属 Lab | 源码与重建入口移入 package ToolsSource~/DHE，项目使用 Tools~/DHE DLL | package 可独立重建；仍为候选提交，尚未进入 repos 正式分支 |
| 文档、模板和配置旧路径/旧版本 | 8.11 opt5 refs、工具自动定位/真实 ID、本地回执路径与项目 API 说明 | 候选中完成，未更新 cat |

补充修复：Base 现在记录实际 supplemental metadata 程序集集合，资源工具按每个
Base 的不可变集合选取并校验字节，而不是受项目当前 patchAOTAssemblies 设置影响。
之前无 metadata 的 Base 却被要求附带项目当前 3 个 metadata DLL 的失败保留为历史
失败；两个新 Base 的无变化更新已通过修复后的流程。

项目模板的 BuildIdentity 必须属于普通 AOT。若 Assembly-CSharp 是 hotfix，项目应
把 identity 及调用其 internal Create 的 bootstrap 放进普通 AOT assembly，配置
adapter 路径/namespace 并提前 import。package 文档明确此约束；不擅自改变项目
asmdef 所有权，默认模板也不应被描述成对任意程序集布局零配置可用。

## 5. 证据范围

产物根：`F:/hybridclr_artifacts/dhe-package-delivery`。

| 检查 | 结果 | 证据身份/范围 |
|---|---|---|
| 最终功能分发安装、metadata、SVN、泛型调用方 | 29/29 | package 82170b0，unit-06；之后候选只增加失败说明 |
| 随包 DLL、Unity 自带 .NET host、参数/路径/篡改 | 26/26 | bundle-03，tool-tests-03 |
| 托管加载/执行计划/错误状态 | 126/126 | 当前 package 源与 bundle-03；模拟 native，不代替 Player |
| 无效发布证据拒绝 | 6/6 | package 82170b0；publication-tests-03，不签发 Release |
| 普通 Install + 参数化 Base + 无变化资源更新 | 2/2 | package 22cdbbb，Lab f58deef；真实 Unity 2022.3.62f3 |
| 完整普通 AOT guard | 2/2 | 每个 Base 40 个普通程序集、49,650 个方法请求，missing=[] |
| 同一 Current 方法更新覆盖不同 Base | 2/2 | bundle-03，Base 59/61 均返回 73；4 个 DHE 程序集，sentinel=5 |
| 实际结构变更的 46-case .NET reference | 通过 | 原归档用例由当前 entry 执行；不是新编译全部 workload 的声明 |
| 同一结构变更计划 + 原 Player | **2/2 失败** | admission=2/2，通过第 10 项后在第 11 项 List 用例失败 |
| Windows native compile/CTest | 既有记录通过 | 相同 runtime 提交，dhe-il2cpp-811-restore/native；非本轮新 native 构建 |
| Android、macOS/iOS、团结、性能/内存/P99 | 未验收 | 不作生产/性能结论 |

22cdbbb 之后的功能改动是资源规划器的泛型调用方闭包，82170b0 之后只有文档。
两个 Base 的构建报告保留原提交，不能改写成最终 candidate 上重建过；当前工具对这些
不可变 Base 的方法/结构更新另有独立记录。

`base-01` 的旧 metadata 失败、`structural-01/02` 的 fixture 准备失败都不计为通过。
尝试逐项选择 case 的 sweep 发现归档 DLL 不含该选择器，实际重复执行完整序列；
`sweep-01` 不构成 23 个独立能力结论，对应无效诊断入口已移除。

## 6. 后续实施顺序及发布边界

1. 保留这两个不可变 Base 和失败 Current，继续修复值类型泛型/接口调用的完整
   Current frame 路径，并为 TLS 存储能力制定明确边界。不得删除/放松 ABI 检查。
2. 在精确候选身份上重新完成 full-sequence Player differential、多轮资源发布、
   多 Base、调用失败恢复和完整 Release 归档资格门禁。当前通过检查不能替代这些。
3. runtime 无改动且门禁通过时，package 候选可 fast-forward 到
   repos/hybridclr_unity 的 optimize/v8.13.0，再更新 Lab 发布锁。若修复确实需要
   runtime 变更，必须处理新的不可变运行时身份，不能让既有 opt5 tag 静默指向新代码。
4. 最后另开项目接入工作：迁移审核 package、记录迁移 commit/tree、配置项目 URL、
   普通 Install、项目 C# 打包适配、归档 Base，再测 Android。现在不更新 cat。

本轮三个 repos checkout 与两个活动候选 worktree 均保留 clean 提交状态；没有创建
stash、清理用户改动或迁移游戏源码。正式 refs/发布锁没有推进到这个失败候选。
恢复本轮候选只需继续使用正式 package 4fb36af 及其匹配 Base；这并不消除相同 runtime
上的结构边界问题。切换 package 后应重新 Install，不能重标或拼接已有 Base 身份。
