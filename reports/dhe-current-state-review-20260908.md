# DHE 当前实施状态与复核（2026-09-08）

## 最新范围与验证结果

用户已明确只支持 Unity 2022 App 和团结 2022 小游戏；Unity 2021 不再安排新构建或门禁。
当前两引擎各有 original/evolved/generic-interface 三类 Base，全部 Base/no-op 通过。
六 Base 的 18 个独立进程完成连续/跳版本回放，每次 61 组演化、220 cases，差异为 0。
未变化泛型调用入口保持 AOT。混合调用与修改/新增/删除计数校验也已修复并验证；
已有普通类新增虚方法、值类型布局等仍有明确实现限制，完整 DHE 目标尚未完成。

最新身份、保留的失败记录、工作区与回滚见
[六 Base 与证据工具验证报告](dhe-six-base-and-evidence-windows.md)。
下文保留的是此次范围调整和 FGS 修复之前的复核记录，不能作为最新提交的验收结论。

## 结论与目标

现有 DHE 路线已经有真实的 Windows Player 执行证据，早先提交的运行时和
C# 工作流可以继续复用。它还没有达到“所有 hotfix 程序集 AOT 化之后，
一般业务演进都只需要发资源”的完整目标，也不能据此取消 CAT 的 BattleAOT 特殊路径。

目标是：每个主包固化自身的 Base DLL/MV 和运行时；每次热更发布最新 DLL/MV，
客户端以自身 Base 对最新资源求差，未变化且具有可用原生实现的方法保留 AOT，
变化、新增或缺少可用 AOT 实现的方法使用解释器。发布端逐一验证所有仍支持的 Base。
跨平台条件编译需要不同 DLL 时，一次发布可以包含多个 payload variant。

这不仅涉及打包流程。新增字段、泛型方法、接口槽位、类型身份、反射和 GC 都需要
运行时支持。把方法切换到解释器本身，不能消除旧 AOT 对象布局、调用约定和原生 API 的限制。
同一进程只注册一份 Current；当前连续/跳版本证据均为替换资源后重新启动 Player，
不是在运行中的进程里卸载、替换已加载的程序集。

## 已发布线与研究线

本次通过 git ancestry 确认研究线继承了以前的提交；没有重新开始或丢弃旧实现。
通过 git ls-remote 复核以下正式分支及 annotated runtime tag 的远端解析：

| 仓库 | 已发布分支 | commit | runtime tag |
|---|---|---|---|
| HybridCLR | optimize/v8.13.0 | fe3b1ed | v8.13.0-opt4.2 |
| IL2CPP Unity 2021 | optimize/unity2021-v8.1.0 | b3fdf1e | v2021-8.1.0-opt4.1 |
| IL2CPP Unity 2022 | optimize/unity2022-v8.11.0 | 6032274 | v2022-8.11.0-opt4.1 |
| IL2CPP 团结 | optimize/tuanjie-1.10-v8.13.0 | 52968ad | v2022-tuanjie-8.13.0-opt4.1 |
| Unity package | optimize/v8.13.0 | 18abd01 | 不创建 package tag |

正式工具历史发布是 0.1.35，限定于当时已经证明的能力。最新研究产物虽然沿用
工具版本号，实际 Package ID 是
6e99a264907d2d07131eeb90ef39da3ca4746e714e9d62f4a650c5e4e86a9530，
明确为 Exploratory / releaseReady=false，不能把历史 Release 资格转移给它。

本次继续使用的候选实现：

| 组成 | 精确 commit |
|---|---|
| HybridCLR | 0787aad2e3c62d071113d028d050836b0bc68b8d |
| IL2CPP Unity 2021 | e654a956cbdc2730e445e11b4771b6d45b30884b |
| IL2CPP Unity 2022 | c482af68eee4dfa83d8cbe775fecfbb00bbce550 |
| IL2CPP 团结 | a585c35c2cc0d465e772e0e081f573132e7fd041 |
| Unity package | d01788df74dbb7230dbce257b6e188025c1e46f4 |
| 已冻结 C# host / 三引擎原生门禁配置 | 19b0ef3dbdf258357982d2cbf7e64e8cbf8f28e2 |
| 本次 Demo 检查修复 | de885e1 |
| 本次完整 focused replay | e14e2be073441b6eac8897b0a3351e7a3a509024 |
| U21 scratch Demo | 52fd456 |

所有 runtime/IL2CPP/package 改动仍在 research 工作树。当前能力合同为
dhe-runtime-v25，MV 仍为 DHEMETA1 schema 1。本次没有改变 native/package 源码、
正式维护分支、tag、Installer 默认选择或 CAT 项目。

## 能力和证据分层

1. C# Base 构建、通用 AOT 入口保护、MV 生成、资源包、多 Base registry、
   跳版本更新与身份校验已经存在，项目只需保留构建/资源加载适配边界。
2. 新增类型与方法、部分方法/字段替换和删除、属性/事件/特性反射、已有泛型引用类型
   的字段扩展、字段地址、接口与跨程序集调用都有逐步建立的测试证据。
   这些是具体覆盖场景，不能解释为全部 C# 演进都已支持。
3. 历史 v22 六 Base 记录：三引擎 × original/evolved，18 个独立 PID，每次
   52 组演化 + 220 differential cases，差异 0，114 个受保护文件不变。
   精确 replay 源码是 5f27aa7；本次核对报告 SHA 与历史记录相同。
4. 当前 0787aad 对应的三引擎 real-header compile/CTest 均通过，报告中
   mergeReady=true、surrogateExternalHeadersUsed=false。它们是原生门禁的字段，
   不代表整体方案已具备发布资格。
5. 本次当前源码的 U21 focused replay 已通过；U22/团结和当前源码的多 Base
   演化回放尚未完成，不能套用第 3 项的历史结论。

## 本次修复与实际执行结果

继续使用此前 MemberRef 签名修复，用原样的 61 组资源构建并运行。最初的
replay-mr-u21 有两次成功、一次失败。最新/跳最新均为 61 组和 220 cases 全通过；
第一份资源执行了全部 61 组，但 Demo 要求几个固定入口之一必须变化，因此拒绝。

真实 MV 和执行记录表明 ExerciseCurrentMembers 保持 AOT，它的调用过程包含
39 次解释器入口。这是预期的混合执行。该错误前提在旧 v24 失败报告中也已存在。
新增 mixed-dispatch 回归用真实 DLL 和失败报告先复现 13/14，再于干净 de885e1
通过 14/14；零/负计数、未执行、错误路由、Base 身份和方法身份等反例继续被拒绝。

修复只调整 Demo 入口判定及 host 对这一执行证据的独立核验。
changedProbeChanged 仍如实保留 false，没有把未变化入口标成已变化。
业务断言、CLR golden、原始 DLL/MV 和旧 Player 没有改动。
正式 Release qualification 命令仍要求 changedProbeChanged=true；这部分
通用门禁的兼容处理仍须在晋级前补齐，当前不声明 Release 通过。

新 Demo 的 Base/no-op/schema 工作流通过，随后同一个固定 Player 回放：

| 场景 | PID | 演化组 | differential cases | 差异 |
|---|---:|---:|---:|---:|
| 第一份资源 | 13936 | 61 | 220 | 0 |
| 连续更新至最新 | 29080 | 61 | 220 | 0 |
| 跳过第一份直接更新至最新 | 42812 | 61 | 220 | 0 |

第一份资源的 220 个 case 方法均判定未变化；后两次各有 220 个解释器入口收据。
Integer/Reference/Value 三个泛型接口原有 caller 均保持未变化，并记录了 AOT 执行。
这些计数只证明测试路径，不是整体项目 AOT 保留率或性能收益。

BaseId 为 c2f2f198b3dcaaa42b28c40d4a579d0ec287e539330b4c2cbca4bd96360e0a71。
修正前后的 Demo 使用相同 DHE Base 输入和配置，因此 BaseId 相同，但测试程序
和 GameAssembly 字节不同，必须按下列产物 SHA 区分，不能只用 BaseId 代指证据。
两份归档均保留；本次通过的 Player 只位于 base-mixed-call-u21。

以下路径相对 C:/hybridclr_optimize/artifacts/dhe-evolution-20260908：

| 产物 | SHA-256 |
|---|---|
| replay-mc-u21/report.json（通过） | 6E2E029DA064300F5BF61D463AACAD2E48F1032E1A61F979F9D29C8FAD44F1F8 |
| replay-mr-u21/report.json（失败保留） | C1E805D1DEB04E0EF6FF9CAF08F452A4DDB301D2A68FAC0A722B5AAEB216EB43 |
| base-mixed-call-u21/player/GameAssembly.dll | FF8778CA796C58D9BFCAB053F19DE4CFCA99BE4EBEF1CD4B9F9DA52941C69E08 |
| base-memberref-u21/player/GameAssembly.dll（旧测试程序） | 94B8489C167183E8CFB70A85F9EEA25EF765F610CD753AD2A2CAD700356AD739 |
| mixed-dispatch-clean/report.json | 3E5C4C12FAEE9097CA33BB4B32A10637F54EACCE73214F870B87BC47DF646743 |
| registry-mixed-call-u21.json | 51C479A72CC5EEB372531229724A4309B81624497A1D182B7D0FC70460314BE3 |
| resource-mixed-call-u21-first/dhe-resource-update.json | 12D2EE9600DDFA5B033B2D10AB7BE8B2A94950290A2DFA0310C4274A5606590B |
| resource-mixed-call-u21-latest/dhe-resource-update.json | 44D1A0480D144BD53D32A30778C77F4E9F181416A888AAB470AF2D5762892DA2 |

mixed-call-immutable-check.json 独立验证了 19 个受保护文件不变、
3 个 PID 全部不同，以及两份资源中的 16 个 DLL/MV 与修正测试程序前逐字节相同。
memberref-failed-immutable-check.json 另行保存了失败回放的 19 文件校验。
旧 six-Base 通过报告仍是历史参考，未被覆盖或重新标为当前结果。

## 剩余问题与推进顺序

1. 当前 native 实现下，完成三引擎 × original/evolved/generic-interface 的完整回放。
   U22/团结 scratch package 已是 d01788d，但安装的 Image.cpp 尚不匹配 0787aad；
   构建前必须经 package C# Installer 安装各自 runtime-memberref 并重新验证。
2. 补普通类虚方法/继承演进、已有值类型布局、Unity component/serialization、
   外部 AOT API 可用性。ResourceUpdateCompatibility.cs 当前仍明确拒绝多个此类变化；
   不能靠关闭检查把拒绝变成支持。
3. 补 supplemental method cache 并发首次触达、更多字段存储扩展和存活别名、
   GC/ABI 压力。对发布顺序的源码审查不能被 Windows x64 结果替代。
4. 对齐通用 Release 门禁的混合调用证据；完成 AOT 保留率、Load/首触达/稳态、
   尾延迟和内存测量。RVA 导致的保守失效仍需单独评估。
5. Windows 完整候选审查通过后再供用户实机 Android 测试。当前没有 Android/iOS
   设备证据，也没有当前源码的性能与内存收益结论。

不同 Base 对同一资源求差的机制已经存在。旧 Player 缺少原生能力时，资源更新不能
给它安装新的 C++ 实现。因此应在首个准备长期维护的 Base 发布前尽量完成上述能力；
已经发布的 Base 必须保持兼容检查，不能伪造能力或用重建 Player 冒充资源更新。

## 工作区与回滚

六个研究源码工作树及三个 scratch Demo 在本次收尾时保持 clean；
构建产生的四个输入 DLL 已放入可恢复 stash。U21 本次新增：

- 044cc34d0b8b39278113eb2377c45f0e28b7fa68：MemberRef Base 输入；
- fb19fcd6a458e972385ba11081d6a0a9816a3126：修正入口检查后的 Base 输入。

以前的 stash、失败报告和 Base 归档均保留。本次没有清理或删除历史产物；
收尾时 C: 可用空间约 64.7 GiB。没有留下本任务的构建进程。

撤销本次 Demo 检查修复可撤销 lab de885e1，并使用对应旧配置/旧 Demo 归档；
旧失败记录不会因此变成通过。运行时回滚必须选择匹配的 runtime/package/tool
及其已验证资源。当前研究代码不得直接替换已安装 Player 的原生运行时。
