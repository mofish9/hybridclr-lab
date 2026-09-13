# DHE 物理接收者分派修复：Unity 2022 Windows

日期：2026-09-13。结论：此前失败的装箱枚举器及其原始 Current 已在两个新 Base
上通过全部 47 项 CLR/Player 顺序比对，差异为 0。本轮修复有条件通过，尚不是
正式 package 交付或新版本发布。用户已废弃 opt5 作为交付目标；旧 tag 不再用于
新项目试用，不移动、不复用，也未在本轮删除远端历史引用。

## 修复内容与边界

旧失败发生于 `List<T>` 的非泛型 `IEnumerable.GetEnumerator`，并非因为更新计划
遗漏了该方法。Current 已被选择，但方法的声明 owner 来自 fallback image，实际
对象则按选定的物理定义分配。直接比较对象类型与声明 owner 会错误地退回 Base
native 入口。该入口对变化后的值类型布局拒绝执行是正确的保护行为。

修复将方法 owner 通过分配时使用的同一套 metadata 映射解析为物理执行类型：

- 引用接收者验证实际物理父链；不以逻辑类型等价代替内存布局兼容。
- 装箱值接收者要求精确 box 类型相同，然后继续验证调用者参数、返回值和 byref。
- constrained 值类型调用在生成具体调用帧前，将实现方法映射到 Current execution。
- 保留 `CanEnterWithBaseAbi` 和 `ShouldDispatchToInterpreter` 的拒绝路径。

不合入旧实验 `a7c4cbe` 的泛型引用 owner 全面放行逻辑。引用类型的 this 指针大小
相同不能证明值参数、返回值、字段或泛型上下文兼容。

元数据映射沿用 `g_MetadataLock` 保护 intern/type context；发布状态通过 acquire
读取。没有增加新的无锁缓存或改变提交顺序。解释器虚调用缓存的首次查询和命中
路径均调用接收者校验。真实 helper 在 Player 中执行；native 单测的映射 stub
只能证明分派决策，不能单独证明 metadata 实现。Windows 结果不推断 ARM64 并发。

本轮没有测量新映射在频繁虚调用中的锁开销，不作吞吐、内存或 P99 收益声明。
只覆盖启动时加载后使用 Current 对象；没有增加运行中旧对象迁移能力。

## 精确源码身份

| 组件 | 分支或角色 | commit |
|---|---|---|
| HybridCLR 修复 | optimize/dhe-physical-dispatch-v8.13.0 | 1b7d5a9fe7100290fb3ef54c30e142896bcef561 |
| IL2CPP | optimize/unity2022-v8.11.0，无新增改动 | ecad8a09d1eb9b91a57c59fcdc69b268377bad59 |
| Base 实际 package | optimize/v8.13.0 | 4fb36af996871ff1e7ab8585f096395dad5d2bd3 |
| 资源生成使用的随包工具 | package 候选分发提交 | 5476c95b9692834c83bf8646a6d029138aaf4cfa |
| 工具源提交 | 上述 bundle 的来源 | ccc01f9b533a9782b380dc68f358a5b9085fd729 |
| Lab / Base 构建与 native | 已冻结源码 | f7fd2440dcf3711de410ed41cad7a5f31a9985e3 |
| Lab / 两 Base 资源与 public probes | 已冻结源码 | 56e51b651dd9e56f3ffc76585d0678c9b5598e50 |
| Lab / 独立审计修正 | 已冻结源码 | 94db3b1f4085d20a7595ae36d513d54708c31a09 |

Runtime 相对正式 `b0fe826` 仅包含 `38e08d0`、`d9da1ee`、`1b7d5a9` 三个修复
提交，四个文件，33 行增加、5 行删除。上游仍为 HybridCLR/package 8.13.0、
IL2CPP v2022-8.11.0；三者均确认相应官方 tag 是当前提交祖先，未执行上游同步。

源码 canonical SHA-256：
`80F9096AF4C9AB73E081617E91E1543A0E1019BAF6C079F4838DE2751AB3F983`。
资源生成使用 `Tools~/DHE/HybridCLR.DheTool.dll`，SHA-256：
`9812CAF9F66EB30844698025D78BF33EDB0076567EC603BE32A99ED404A314AD`。

必须区分 Base package 与资源工具的提交：这次为了隔离 runtime 问题，Base 使用
正式 package 4fb36af，资源使用候选5476的 DLL；不能写成“新 package 的完整
Installer/BuildBase 流程已经通过”。Base 内的 v33 contract 字符串也不是新发布
身份。发布前还要同步 managed/runtime 身份和 receipt，并用完整新组合构建验证。

运行时拼装与 native gate 使用了现存 unbundled C# host；它不作为新 bundle 的
源构建证明。拼装产物另有已验证的源码锁和真实 runtime/headers 哈希，新 Player
经过完整 C# UnityWorkflow 构建，未手工修改 staging 或生成项目。正式交付前需
从锁定工具源码重新构建并验证 host/bundle 来源。

## 新证据

产物根：`F:/hybridclr_artifacts/dhe-physical-dispatch`。
机器可读锁：`manifests/dhe-physical-dispatch-windows.json`，包含下列报告的 SHA-256。

| 门禁 | 结果 |
|---|---|
| Unity 2022.3.62f3 真实 headers native compile / CTest | 通过；mergeReady=true；无 surrogate；FGS enabled |
| Base revision 59 与 61 | 两份完整构建通过，原始运行及 no-op 资源均通过 |
| ordinary AOT guard coverage | 每份 Base 40 个程序集、49,650 项请求，missing=[] |
| boxed-02 共用资源 | 一套原始 Current DLL，两个独立 Base，47/47，顺序差异 0 |
| 快照篡改及恢复 | 每个 Base 均在业务入口前拒绝；恢复后完整通过 |
| audit-02 | 两个 Base，4 次成功、2 次拒绝；125 文件重新校验 |
| public-probes-01 | 16 个汇总条件全部通过；6 个真实 Player 进程 |
| 故意损坏 metadata | 每个 Base 11 项失败语义检查；无业务/初始化/程序集发布副作用 |
| 新进程恢复 | 两个 Base 各 47 项业务用例及 17 项 Unity 生命周期检查通过 |

两个 Base ID 分别为：

- `ab9af5efbc39ab7378797bfda01d20d5da2c37d150cd1d80ff797bbfe246b262`
- `29b360ac6bf02d1a6893c3ff50b96e0d4d03346d7f2cc7779f3b5ec7e02ff636`

二者 GameAssembly 哈希不同。共享 Current assembly-set SHA-256：
`a4cffad3bd4901c4daae24cce65265bca30d8936af0aa5dbd3c50c029350724e`。
47 项包含装箱枚举、泛型值/引用 owner、容器、反射/delegate、异常/filter/finally、
TLS、并发首次 static/cctor、新增程序集和冻结 ordinary static 的适配。计数代表
这些明确命名的断言，不代表任意 C# 改动都已覆盖。

## 测试设施修正及失败记录

`boxed-01` Player 本身通过47项，旧汇总器仅接受4/23/31/46导致 overall=false。
`56e51b6` 改为使用独立 CLR reference 的通过标记，reference 仍根据实际已知
probe 验证期望数量，Player 必须与它的完整顺序相同。修正后重新运行 boxed-02，
没有覆盖旧报告或将旧失败改写成通过。

独立审计第一次失败于 module-entry-verification。原始 Current 保留 ModuleState
及初始化器，但 GetRevision 没有调用 Verify；FrozenStaticWorkflow 的 setEntry
会重建业务入口，故不能仅凭类型存在要求该专项日志。`94db3b1` 根据 fixture 的
直接调用契约确定是否要求日志；调用时必须恰好一次、值正确，未调用时必须没有
该日志。常量反射、初始化器恰好一次、文件哈希和47项顺序校验均保留。
policy 自测在内存中补入真实 Verify 调用，覆盖两种入口及缺失、重复、错误值、
非预期日志；不修改 Current DLL。audit-02 重新只读审计通过。

native-01 曾因测试 stub 缺少 MetadataModule 声明编译失败，修复后 native-02
完整编译/CTest 通过。恢复会话时一次 host 构建误用正式 package 作为工具源码根，
因其尚无 ToolsSource 而失败；改用锁定候选5476后编译通过。没有因此修改生产代码。
旧 constrained staging、trace 及 ABI 放行实验均未复用为新证据。

## 复现和后续交付

使用本 Lab 的 `tool/fixtures/aot-snapshot/AotSnapshotTests.csproj`，Release 构建
时传 `DhePackageRoot` 为 package5476的源码目录。以下均为 C# host 子命令；输出
必须指定新的目录/文件，完整参数契约位于对应 workflow 类开头：

1. 从冻结源码/锁执行 `assemble-runtime`、`native-tests`，只使用 Unity 2022 headers。
2. 用 `unity-workflow` 分别构建 revision59、61，明确传入 shipped 工具 DLL。
3. `frozen-resource-workflow` 输入上述两个 Base、新输出和原始 boxed-current-01/current。
4. `unity-public-probes` 输入同样有序 Base 集、已通过共用资源、`:current:` 和新输出。
5. `frozen-resource-audit-policy` 检查原始 Model DLL；`frozen-resource-audit` 审计共用
   输出及原始 Current，新写 audit 文件。不得在运行时重建 host 或改动输入。

本轮正式 repos 的三个 HEAD 保持 b0fe826 / ecad8a0 / 4fb36af，均 clean；修复
runtime、package候选及本 Lab 均在隔离分支中。未创建 stash、未修改 cat、未推送
或创建/移动 tag。正式分支合入、完整 package 安装/构建闭环及新的版本证据锁
仍待完成；用户已取消继续把这些修改塞回 opt5 的要求。

该修复编译进 native runtime，旧 opt5 Player 不能通过只发托管热更包获得修复。
必须用修复组合构建新 Base。回滚候选源码边界是三项 runtime 提交及其匹配的
Base；不能把旧 Base 替换源码文件后冒充新构建，也不建议继续试用已废弃的opt5。

Unity 2021 保持官方未优化版；团结按用户要求延期。Android、iOS、ARM64、macOS、
性能、内存、P99和跨平台 package 最终发布资格均不在本轮通过结论内。
