# opt7 Unity2022 基线修复与验证

日期：2026-10-04。状态：Unity2022 opt7 正式维护线已锁定；Windows Player 回归通过，Android 和正式 Release 门禁仍未完成。

本轮验收范围以 Unity2022 为基准。Unity2021 不属于当前 DHE 方案范围；团结版本待 Unity2022
基线确认后，按同一实现和验证流程单独迁移、编译并做 Player/设备验证。

## 实现与边界

装箱反射与解释器虚调用共用 Current receiver 选择逻辑。值接收者要求精确物理 box
类型；保留 raw native 未装箱入口的限制以及解释器的具体签名/byref 检查。反射入口
的早期检查在声明 owner 被映射时使用物理 owner，随后才执行虚查询、unbox 和参数
转换。旧物理 box 不得通过一个已经描述 Current 的 MethodInfo 进入 Current 方法。

删除 ResolveInterpreterMethod / ResolveCurrentExecutionMethod 的执行期标志位写入。
注册事务在发布前准备 selected generic definition 的 interpreter 标志，泛型实例
继承这个状态；失败时由 snapshot 恢复。guard 使用 acquire 读取 interpData，并交给
已有一次性 transform 逻辑处理首次触达。泛型定义的 flag snapshot 不提交 vtable，
因为此时还没有具体调用帧的桥接指针。

没有增加缓存、改变 raw ABI 放行策略或升级上游。仅覆盖启动加载后执行 Current，
不提供业务运行中的对象/TLS 迁移。不宣称性能、内存或 P99 收益。之前审查中的
全局 metadata 锁成本保留为独立测量事项。

## 锁定组合

| 组件 | 候选分支 | commit |
|---|---|---|
| HybridCLR | optimize/v8.13.0 / `v8.13.0-opt7` | a4807e563c0cb245519a44c6ea7cc633246dd337 |
| Unity2022 IL2CPP | optimize/unity2022-v8.11.0 / `v2022-8.11.0-opt7` | a1ec0324a8a58cb8e175c7b86665d70b5afae57e |
| package | optimize/v8.13.0 | bf62316c5dd7b6f83c16ef5f8d0fa1cda5f52fbf |
| Lab | optimize/dhe-release-lab-v8.13.0 | 本次锁定提交 |

Package 使用 Unity2022 两个 opt7 runtime tag，bundled tool 已在 package commit
bf62316 上以 Exploratory 模式重建并通过 `verify-package`。Package 本身不创建 tag。
现有 Windows Player 证据的 Package 输入仍是 85eaa24；opt7 Package 只重锁 runtime
tag 并重建工具分发，Android/设备门禁需要使用 bf62316 重新验证。

最终 runtime tree SHA-256：
`5A65E4BB29566BE5F10491D98355EC3382F0A00910730E62D07B1AF2D14F3639`。
排除声明的 generator inputs 后，927个 native 文件 canonical SHA-256：
`21A8D9B6707FADFDD3802238AE587DDC86059C507B65CD2A4632D8FD275F1243`。
完整最终 package 重拼装与正式 native gate 的运行时 tree 相同；两个不同 manifest
保留自己的 package identity 和哈希，没有互相重标。

## 已完成的验证

- 旧 opt6 上新增 native 回归失败3项：泛型定义发布前状态、装箱反射选择、旧箱拒绝。
- 中间候选新增 generic vtable 负例失败1项，证明 flag-only preparation 不得清空桥接
  尚未产生的 vtable；最终版本修复。
- 最终 Unity2022 真 headers compile/CTest 通过，FGS=true，mergeReady=true，
  surrogateExternalHeadersUsed=false。
- 团结真实 headers compile/CTest 的探索性结果保留在产物中，但不纳入本轮 Unity2022
  基线结论；团结版本仍需在 Unity2022 基线确认后单独迁移和验证，没有 Player 资格声明。
- Unity2021 按本轮范围明确排除，不纳入当前方案结论，也不使用 Unity2022 结果推断 Unity2021。
- 最终候选 package 安装身份/拒绝/schema suite 30项通过。
- 最终两个独立 Windows Base 完整四阶段构建、原始运行和 no-op 资源运行通过。
  每份 ordinary guard coverage 无遗漏，具体数量锁定在对应报告。

两个最终 Base ID：

- eea608bded43364b781592e3779fb51fc1765f7381da2a3d02110d84d9787769
- 8810b0547e60932d850f6e0054245e2a136d189dca9cc380d6d5ccfd97e179d6

## Player 回归

最终两个新 Base 各自通过：

- 原始失败 Current 的精确字节重放：两个 Player 完整序列通过，旧资源快照拒绝、
  恢复和共享资源运行均通过。
- 扩展 Current：在原47项顺序前增加 interface MethodInfo、具体 MethodInfo、
  `Current` 异常、旧 box 拒绝和 8 线程重复泛型触达；两个 Player 均通过。
- 新增日志明确记录 `boxed-interface-reflection`、`boxed-concrete-reflection`、
  `old-box-interface-and-concrete-rejection` 和
  `boxed-reflection-and-concurrent-generic-touch`。

最终工作流均返回 `passed=true`、`stage=complete`，每个 Base 的 GameAssembly 保持
自己的哈希，未修改归档 Player。对应产物为 `exact-repro-final-01` 和
`reflection-final-01`。

## 证据与剩余门禁

产物根：`F:/hybridclr_artifacts/dhe-opt6-review-fixes`。native-final-02、
runtime-final-02、base-final-01/02、package-tests-final-01 属于最终组合。native-red、
generic-vtable-red、base-01/02/03、reflection-01 属于定位和中间候选，不写成最终
提交通过数据。第一份 Base 因 fixture 中 Payload 重名编译失败；修复在 Lab 源码，
重新建立新项目。初始矩阵目录缺真实 external headers 的相对布局，重新完整拷贝
各自 Editor external 后执行。所有失败日志保留。

还缺团结版本的同源迁移、Player/接入和设备验证，以及 Android/ARM64、iOS/macOS、
race detector/ARM64 并发、性能/PSS/尾延迟和正式 Release 资格。

回滚候选使用父提交和匹配的重新构建 Base，不混装源码/版本身份。修复需要新的
native Base，旧 opt6 Player 无法通过仅发 Current 资源获得修复。业务资源回退需
选择与不可变 Base 匹配的归档资源并启动新进程。`v8.13.0-opt6` 和
`v2022-8.11.0-opt6` 保持原身份，可按对应父提交回滚。

本轮 runtime、package 和 lab 工作树以提交冻结，无新增 stash；opt7 runtime tag 已建立，
正式 repos 的工作树保持 clean。
