# DHE 工作流审查记录

> Migration note: the supported production entry point is the cross-platform
> C# host in `tool/HybridCLR.DheTool.csproj`. The former `scripts/*.ps1`
> commands in this historical review are no longer distributed or supported.

The formal source boundary now contains only the C# host, Unity C# adapter,
package/runtime patches, schemas, and managed/native fixtures. The remaining
`scripts/*.ps1` files in this lab are historical engine or test helpers; they
are outside the DHE boundary and are never copied by `publish`.

审查对象是 `dhe-experiment-lab` 以及它依赖的三个 runtime/package/il2cpp
worktree。目标是确认哪些内容属于可复现工作流，哪些只是一次性验证材料，
并保证从干净 checkout 仍能判断工作流是否通过。

## 结论

当前工作流已经具备可复现的主链路：

```text
managed baseline/current
  -> strict MV JSON + binary
  -> Unity stripped baseline/current artifacts
  -> generated C++ native guard
  -> custom runtime installation
  -> Tuanjie 2022 Player gate
```

当前 Demo 已经跑通“全部已配置 hotfix 程序集进入 DHE”的完整能力闭环：四程序集
workflow 聚合结果为 27/27 supported managed tokens、0 unsupported，并解析出 34 个
真实 native entries。这里的 34 是 concrete/gshared 等原生入口数量，不是 34 个 changed
managed methods。该结论证明当前 Windows Demo 覆盖的 ABI 和工作流可以完整发布；它
不能外推为任意 Unity 版本、Android/小游戏平台或尚未纳入 fixture 的 ABI 自动可用。

脚本迁移后，C# host 已重新验证四程序集离线 preflight、MV/batch、包发布/校验和
安装/doctor；历史 Player/native 数字仍是旧脚本身份的参考，不能当作本轮 C# host
的 Release 证据。要恢复 Release 结论，必须用 `workflow -RunPlayer` 在目标 Unity、
Android 或 macOS+iOS 环境重新取得 native manifest、Player smoke、归档和签名证据。

当前工作区的未跟踪项并不等于临时产物：Tuanjie 2022 demo 源码和嵌入式
HybridCLR package 占据绝大多数；DHE C# host、adapter、fixture、schema 和审查文档是
正式工作流输入。Unity 生成缓存、Player、StreamingAssets 和历史 probe 已通过
`.gitignore` 隔离；`unity2021-probe` 与旧的 body-patch/player-watcher
脚本仍保留在本机供追溯，但不应进入正式提交。

## 本轮正式化复查

本轮发现并修正了几处会影响重复执行或独立复核的临时性实现：

- artifact validator 曾保留旧的 basename-only C++ hash；现已统一使用带
  `generatedCppRoot` 和 relative path 的公共 file-set hash。多文件顺序、重复路径、
  越界路径和内容变更均有 fixture 覆盖。
- fixture、capability、compatibility-negative gate 现在都明确处理已有输出目录；
  默认拒绝非空目录，只有显式 `-ForceOutput` 才会替换，demo 总入口会传入该开关。
- source preflight 的 package lock 改为显式 `-PackageLockPath`；只有 demo 或显式要求
  embedded package 时才校验 package tree。使用 registry/package manager 的项目不会再
  错误套用 demo 的 package lock。embedded lock 固定使用 `pathBase=project-root-v1`，
  lock 文件位置不会改变 `packagePath` 的解析基准。
- strict MV 现在把字段常量、`MethodImpl` 覆盖关系、属性/事件和程序集自定义属性纳入
  元数据边界，并加入字段常量变化负例。
- MV 的 managed ABI 参数列表现在在 PowerShell 5.1 和 pwsh 7 中都保持数组形状，字符串
  元数据集合使用 ordinal 排序；同一输入的 JSON（忽略生成时间）和 binary 可重复得到相同结果。
- demo 工作流按原始字节恢复 settings/identity 源文件；确定性 Player 构建的默认 native
  manifest 每轮首阶段都会重新解析，避免旧输出污染新生成 C++。
- archive gate 使用 hashtable 传递脚本参数，并把 coverage-only 失败与 artifact 损坏分开；
  对探索模式报告追加 `-RequireCompleteCoverage` 时仍会准确保留原始 artifact 结果。
- runtime 组装和 patch 应用增加了输出目录安全检查，并兼容 Git worktree 的 `.git` 文件；
  deterministic build 的子 PowerShell 调用统一使用 `ExecutionPolicy Bypass`。
- value-type receiver 仅放开已由 Player 验证的 `void()` state-machine ABI，使用独立的
  pointer receiver helper；其它 struct receiver 形状仍 fail-closed。
- no-op deterministic build 的 `generatedCppPaths=[]` 已修复并实测通过；archive schema
  允许空 generated-C++ 集合，空变更集不会再触发 StrictMode 数组越界。
- release gate 现在按 assembly name 配对多程序集物料，并重新调用独立 artifact validator；
  workflow report 的 `passed`/`releaseReady` 等自报字段不能单独绕过门禁。
- project preflight 增加 `-RequireDheEqualsHotUpdate`，Release 入口可强制完整 hot-update
  集合进入 DHE；通用 orchestrator 接受安全的 opaque target，仓库内 Demo adapter 仍只
  支持 `StandaloneWindows64`，Android/小游戏需提供各自 adapter 和 Player 证据。
- batch 在 DHE/AOT 集合不一致时仍会写出 `dhe-batch-summary.json`，其中的
  `configurationPassed=false` 和 `configurationErrors[]` 是机器可读失败原因；project
  preflight 会把该配置失败继续带入最终报告，而不是在子进程异常处丢失上下文。
- artifact validator 对 baseline/current/MV binary 的程序集 basename 映射现在拒绝重复和
  未命名匹配，也不再用数组位置回退，避免多程序集顺序变化时静默绑定错误物料。
- native manifest 现在声明 `resolverVersion=2` 和
  `il2cpp-generated-cpp-signature-v2` ABI adapter；一个 managed token 可绑定多个
  concrete/gshared native entry，未知生成器/ABI 版本会直接
  fail-closed，不会被当成零 unsupported 方法继续发布。
- schema 文件现在被列入正式输入边界；`run-dhe-schema-gate.ps1` 使用严格 JSON
  解析和 draft 2020-12 validation 校验已登记格式，PowerShell 5.1 会转交 `pwsh`。
  `schema-gate-report.json` 才是 validation evidence，不能只检查 schema 文件存在。
- deterministic Player runner 现在对 settings/identity 源文件做字节级 finally 恢复；即使
   standalone no-op 或失败构建也不会把机器路径和构建 identity 留在项目源码中。
- source preflight 和 batch 现在共享 settings 解析器，支持 inline YAML list、asmdef GUID
  和显式 `dheAotAssemblies`；缺失/空数组保持 legacy HybridCLR 行为，不会隐式开启 DHE。
- native CTest 的 DHE profile 显式要求 `DheRuntime.cpp/.h`，C++ transformer 的临时
  manifest/report 只写入事务目录并在 finally 清理；归档 gate 同时拒绝 locale 格式的
  `generatedAtUtc`。
- `HybridCLR.Lab.sln` 现在包含 BoundaryContracts、ManagedCasesAot、MetadataStress 和
  CrossAssemblyDerived 四个 DHE 实际编译项目；CI 的 managed build 不再只编译主程序集和
  reference runner。`build-clean-baseline.ps1` 的 Profile 集合也与 DHE runtime/native
  test 入口保持一致。
- deterministic Player build 允许标准输出位于项目 `Builds/`，但显式保护 `Assets`、
  `Packages`、`ProjectSettings`、`Library` 和 `HybridCLRData`；误把输出路径指向这些目录
  会在 Unity 启动前失败，避免 `-ForceOutput` 破坏源文件或生成缓存。
- demo workflow 为 `ManagedCases`、`MetadataStress`、`CrossAssemblyDerived` 生成独立的
  baseline/current 变体，并在 Player 中执行 direct changed/unchanged 调用；本轮实际结果
  为 `secondaryAssemblyChangedValidated=true`、`secondaryAssemblyDirectValidated=true`。

此前的 pre-v2 Tuanjie Player 结果缺少当前 generic invoke-args、path semantics 和 scope
契约，只保留为历史诊断材料，不能再作为发布输入。当前正式证据必须由 resolver/ABI v2
重新生成，并通过 Player、archive、schema 和 release 身份复核。

## 2026-08-27 工作区全面盘点

本次盘点以 `git status`、clean-checkout gate 和独立 artifact validator 为准，
没有把 ignored 文件误判为源码变更：

| 类别 | 当前内容 | 处理结论 |
| --- | --- | --- |
| DHE 正式脚本 | 32 个 DHE/校验/归档入口和公共 helper，另有 1 个 adapter fixture | 保留；这些是工作流实现，不是一次性命令记录 |
| 共享/历史实验脚本 | 19 个旧的基线、性能、generic-sharing 和环境脚本 | 保留在实验室，但与 DHE 发布入口分开；其中旧 body patch/probe 已被 ignore |
| DHE schema、lock、patch | 31 个 DHE schema、runtime/package lock、3 个版本化 patch | 保留；它们构成输入和输出契约 |
| Unity demo/package | demo 源码、ProjectSettings、场景和 289 个 embedded package 文件（约 4.1 MB） | 保留；package 是锁定依赖，不是 Unity 生成物 |
| 生成/运行时材料 | `artifacts/`、`staging/`、Unity `Library`/`HybridCLRData`/`Builds`、StreamingAssets、日志 | 已通过 `.gitignore` 隔离；不能作为 clean checkout 成功依据 |
| 历史 probe | `unity2021-probe/`、旧 `patch-dhe-method-body.ps1`、旧 demo Player 构建脚本 | 只作追溯，不进入正式提交 |

盘点过程中重新验证了 PowerShell 7 和 Windows PowerShell 5.1 的 33 个正式脚本解析、
DHE fixture（含 value-type helper 和 no-op）、4 个兼容性负例、完整 hot-update scope
负例、source preflight、project plan validator、clean-checkout gate、可移植归档，以及
DHE native CTest；门禁运行本身没有产生额外的非 ignored 源码改动。

## 2026-08-28 修复与最终证据

本轮继续执行时发现并修复了两个会阻断正式复现的缺陷：DHE runtime 使用
`recursive_mutex` 却在两个路径中以 `lock_guard<mutex>` 编译，以及 Windows
`Start-Process` 未正确传递包含路径的 JSON 参数列表。前者已由 DHE native CTest
覆盖，后者已统一改为显式 argv quoting 和 Unity 兼容的分隔列表。

当前证据为：

- `run-dhe-native-gate.ps1`：native CMake/CTest 通过，新增的
  `PrepareAndRegisterChangedMethods` 成功提交、重复提交拒绝、解析失败回滚均通过。
- `run-dhe-project-workflow.ps1` + checked-in Demo adapter：4/4 程序集加载成功，Player
  `validationPassed=true`、`artifactValidationPassed=true`，changed=27、supported=27、
  unsupported=0、native entries=34；secondary direct/changed、direct/reflection/null generic、
  generic virtual、snapshot/hash 和同进程事务重试均通过。
- `run-dhe-archive-gate.ps1`：归档复制和独立复核通过；native gate、source preflight
  与 Player 报告均绑定到当前 runtime tree `35F110133B02C5BF5A23C71E0F5F000AA076654281447C1DB9C19B6C3B8F2E3F`，DheRuntime.cpp
  hash 为 `6AA3633CB836CA79C20C2BBFADEE8A55CA16E6EB9924801EB45ED754B06C4553`。
- `run-dhe-release-gate.ps1`：完整覆盖的 Exploratory 报告仍会被拒绝；Release 必须同时
  证明 `mode=Release`、project/tool 双 Git 身份均 clean 且 boundary tracked、clean runtime
  source 和 non-surrogate headers。

正式 Release source preflight 现在同时要求 `DheRuntime.cpp/.h`、匹配引擎的
non-surrogate external headers，以及完整 hot-update/DHE 集合。没有匹配 Tuanjie
安装时只能运行显式 exploratory native lane；该约束也通过手动
`.github/workflows/dhe-native.yml` 固化到 self-hosted runner 入口。

需要明确的是：当前 formal worktree 已 clean，正式 DHE 文件已经形成可审查提交，
并已生成本地 Release package/transport record。该记录绑定当前 source HEAD，尚未
替代远端 branch 的正式发布；推送远端并从远端 clean checkout 重跑门禁后，才能作为
团队共享的发布身份。不能用 `git add .` 把 Unity cache、Player 或历史实验目录
一并纳入提交。

下面几项仍属于正式化缺口，而不是应该通过删除文件解决的“临时内容”：

1. 可移植归档缺口已闭合：`run-dhe-archive-gate.ps1` 会复制 generated C++、
   managed payload 和报告，重写相对路径，并在临时复制目录中通过独立 validator。
   归档应包含 generated C++、managed payload、scope plan 和报告；原始 `Library/Bee`
   路径不再是复核前提，且在 scope schema 收紧后必须重新生成；后续应继续保持该 gate
   作为所有项目归档的必经步骤。
2. 项目无关编排缺口已闭合：`run-dhe-project-workflow.ps1` 通过 Prepare/Player adapter
   contract 管理公共 preflight、archive 和 release；checked-in Demo adapter 已完整跑通。
   正式项目仍需提供自己的 Unity 构建与 Player 断言，但不再复制公共 MV/门禁逻辑。
3. generated-C++ guard 依赖函数签名和 `RuntimeMethod* method` 的文本格式。升级 Unity、
   Tuanjie 或 IL2CPP generator 时，必须先让 fixture 失败并更新版本契约，不能把匹配失败
   当成“没有 changed method”。长期应迁移到稳定的 generator 集成点。
4. `README.md` 仍保留早期优化实验的命令，其中若干脚本在本 DHE-focused checkout
   不存在。该段现在有独立的 `Historical lab results` 标题和不可执行 scope note，
   且不再引用 checkout 中不存在的历史文档；新使用者仍应只复制上方 DHE 入口，避免把旧
   Candidate/Android 命令当成当前工作流。
5. native CTest 的 DHE 部分是 compile/link 和状态机单元测试，真正的 interpreter/AOT
   dispatch 证据仍只来自 Player；Windows x64 之外的 ABI（尤其 generic、struct 和
   adjustor thunk）仍必须用目标平台真实构建验证。

## 2026-08-28 正式化修复

本次工作区复核又补上了三项可交接性问题：

- source-boundary 不再放行整个 demo `Assets/` 目录，改为只接纳
  `Assets/Editor`、`Assets/Runtime`、`Assets/Scenes` 及其根级 `.meta` 文件；
  生成的 `Plugins`、`StreamingAssets` 和 `HybridCLRGenerate` 仍由 ignore 规则隔离。
- `build-managed-cases.ps1` 的两个可递归清理输出目录现在经过统一的路径/链接保护，
  防止误传 `LabRoot` 或 junction 时删除工作区外内容；总入口中未使用的历史变量和
  重复 C++ hash 计算已移除。
- script fixture gate 的故意失败 validator 调用在 Windows PowerShell 5.1 下改为
  局部 `ErrorActionPreference=Continue` 捕获，PS5.1 与 pwsh 7 均能留下完整负例报告，
  不再因 `Write-Error` 重定向导致 fixture gate 中途退出。
- 跨子进程的多路径参数改用 JSON 数组传输，同时保留旧分号格式的读取兼容；合法的 Windows
  路径包含分号时不再被静默拆成多个物料。demo 总入口也只在成功准备输出目录后写失败报告，
  拒绝 stale output 时不会修改上一轮证据。
- release gate、project-plan validator 和 archive gate 现在都拒绝把自定义输出路径指向
  输入报告、计划或归档内容；这三类覆盖输入的负例由 script fixture 在 PS7/PS5.1
  中回归，避免门禁报告本身破坏后续独立复核所需的证据。

本轮针对上述边界继续收紧了正式入口：

- `assemble-runtime.ps1 -Profile DHE-Tuanjie2022` 现在必须接收
  `-PackageRoot`，并在同一组锁定 patch 上处理 native/runtime 与 embedded package；
  缺少 package 时直接失败，不再生成带 `dheEnabled=true` 但无法证明 package patch
  的半成品 manifest。`build-clean-baseline.ps1` 也明确拒绝把 DHE profile 当作非 DHE
  control lane 使用。
- `run-dhe-release-gate.ps1` 会重新执行 project-plan validator，并按 assembly name
  对比 plan、workflow MV JSON/binary、batch report 和 runtime-plan 的路径、集合和
  hash；旧的“通过报告 + 另一份物料”组合会被拒绝。
- `run-dhe-archive-gate.ps1` 的所有早期失败现在都会落盘一个
  `hybridclr.dhe-archive-gate.json` 失败报告，`archiveFileCount=0` 是可识别的输入
  缺失状态，不再只有控制台异常。
- settings 读取仍明确是 HybridCLR settings 的受限 YAML 子集，但现在能正确处理
  引号中的逗号/井号以及行尾注释，并由无 Unity fixture 固定回归；复杂 YAML
  （anchor、multiline scalar 等）仍应在接入项目时先拒绝而非静默解析。
- `HybridCLR.Lab.sln` 已统一为 CRLF，并通过 `.gitattributes` 固定该约定，避免
  Windows checkout 反复产生整文件换行 diff。
- 新增 `manifests/dhe-source-boundary.json` 与
  `scripts/run-dhe-source-boundary-gate.ps1`；static gate 会检查所有非 ignored
  的未跟踪路径都在 DHE allowlist 内，并确认 artifacts、Unity cache、历史 probe
  等生成目录确实被 Git ignore。
- source-boundary 对 tracked 与 untracked 修改统一使用 `exactPaths`/`prefixes`；
  不再保留迁移阶段的 `trackedChangePaths` 宽口径例外，下一轮修改正式脚本或
  package 时也必须落在同一份声明边界内。
- archive 的 runtime manifest、workflow report 和 build identity 现在显式声明
  `archive-relative-v1`；本机 editor、source checkout 和 staged runtime 路径不会
  被复制成看似可用的跨机器输入，runtime lock/hash 仍作为 provenance 保留。
- `run-dhe-archive-gate.ps1` 现在转发 workflow、identity、native manifest、runtime
  plan、project plan 和 batch report 路径，生产 adapter 不必复用 demo 的固定目录布局。
- project adapter fixture 不再读取上一轮 `artifacts/review-*` 目录；`Prepare` 每次从
  managed 源码重新生成四程序集 baseline/current，并把输入放在本次 OutputRoot 下。
  static gate 只解析正式 DHE 脚本和 fixture，不会因本机存在被 ignore 的历史 probe
  而改变结果。
- project workflow 只有在调用者显式传入 `-ForceOutput` 时才会把覆盖权限传给 sibling
  archive；旧归档默认保持 fail-closed，避免一次新的 Player 验证静默替换仍在交接中的
  证据。
- Unity package 的 `DheAotAssemblyNames` 现在是显式 opt-in：缺失或空的
  `dheAotAssemblies` 保持 legacy hot-update 过滤行为，且 DHE preflight 会拒绝这种配置；
  DHE workflow 必须显式提供目标集合。
  `DheRuntimePlanOptions.HotfixAssemblyNames` 允许 package 在 DHE 子集与 legacy hotfix 共存时
  写出完整加载列表，并只清理有 MV/snapshot 证据的 DHE sidecar；对应 patch/tree lock 已更新。
- project workflow、release gate、project-plan validator 和 artifact validator 对关键
  `passed`/`coverage` 字段执行 JSON boolean 类型检查，避免 PowerShell 将字符串
  `"false"` 按非空值误转为 `$true`。
- runtime patch 的 Git apply 检查使用精确 patch root 和 `GIT_CEILING_DIRECTORIES`；即使
  外部 runtime 目录嵌在另一个 Git 仓库内，也不会借用祖先 index 产生 forward/reverse
  同时成功的假状态，fixture 已覆盖 applied 与 clean 两种方向。
- clean-checkout gate 分别输出 `projectGit` 与 `toolGit`，每份身份包含 HEAD、HEAD tree、
  clean/tracked 状态和 source-boundary SHA-256；正式项目仓库不必与工具仓库合并，但
  Release 要求两边都可复现。
- project adapter 的 `Prepare` 与 `Player` 失败都会透传版本化
  `workflow-failure.json` 根因；通用 orchestrator 的 target 已从 Windows 常量改为安全的
  opaque identifier，平台能力由 adapter 决定。
- archive 会重写 source preflight、clean checkout、MV 和 plan-validation 中的路径，
  并递归拒绝所有 JSON 中的 Windows drive/UNC 绝对路径；manifest 保留双 Git 身份 hash，
  Release archive 不允许缺失 project/tool provenance。
- project preflight 现在声明并转发 `-RequireCleanRuntimeSources`；Release orchestrator 的
  runtime clean 要求可以真正到达 source preflight，缺失 runtime fixture 同时覆盖该
  Release-only 参数链，避免 Exploratory 通过而 Release 在参数绑定阶段失败。
- Release orchestrator 也会把实际 `RuntimeSource` 传给 clean-checkout gate，从而强制执行
  stale-manifest 负例。artifact validator 遍历 project/tool Git 身份时使用独立变量，
  不再覆盖 build identity 后产生虚假的 identity mismatch。

当前磁盘上的运行产物仍然很多，但都不属于提交边界：本 DHE worktree 的
`artifacts/` 约 5.57 GB、`staging/` 约 5.37 GB、Unity demo 的缓存/Player 约
9.69 GB，`unity2021-probe/` 约 0.25 GB；旧 `lab` worktree 的 artifacts 和 Unity
缓存另约 21.37 GB。它们已由 ignore 规则隔离，不能用 `git add .` 清理或提交；需要
释放空间时应先确认路径，再按实验批次删除，不能把这些本地产物当作工作流输入。

`run-dhe-clean-checkout-gate.ps1 -RequireTrackedSources` 会额外确认 boundary
manifest 中的必需 exact paths 和每个 source prefix 已经被 Git 跟踪；Release
demo adapter 同时要求 Git clean。该检查只有在正式文件形成提交后才会通过，
因此当前迁移 worktree 的 untracked 文件不能被误当成 clean checkout 证据。

本轮正式化修复还包括：

- `run-dhe-source-preflight.ps1 -RequireCleanRuntimeSources` 会读取当前
  `runtime-workflows.json` 和 `repo-lock.json`，再对 runtime manifest 中三份
  源码执行实际 commit、dirty 状态、engine metadata/ProductVersion 和独立
  external-header tree hash 校验；
  Release 项目 workflow 默认开启该检查。`assemble-runtime.ps1` 也拒绝对
  publishable DHE profile 使用 `-AllowDirty`。
- 通用 `run-dhe-project-workflow.ps1` 新增 clean-checkout stage，Release 默认
  使用 `GitRoot=ProjectPath` 并要求 clean worktree；也支持显式的
  `-RequireGitClean`、`-RequireTrackedSources` 和 `-GitRoot`。
- 输出目录公共保护同时检查 Git tracked 内容和正式 source-boundary，避免
  `-ForceOutput` 指向 `scripts`、`schemas`、`manifests`、`patches` 或 demo 源码
  目录。正常 `artifacts/`、`staging/` 子目录仍可用。
- MV runtime parser 与独立 artifact validator 现在都拒绝未知 flags；native
  CTest 和无 Unity script fixture 各有对应负例。归档 gate 还会比较 manifest
  的 `files[]` 与实际归档文件集合，拒绝未声明的额外文件。
- demo Player 的结果路径采用显式 Windows argv quoting；Unity/Player 超时后
  会等待被终止进程退出，避免 finally 恢复工程设置时发生竞态。

## 保留内容

以下内容是工作流的正式输入或实现，应保留并纳入后续提交：

- `scripts/generate-dhe-mv.ps1`：严格 method-body-only MV 生成和二进制输出。
- `scripts/generate-dhe-batch.ps1`：从项目 HybridCLR settings 解析 DHE AOT 程序集列表；若项目要覆盖全部
  hot-update 程序集，`dheAotAssemblies` 必须与 `hotUpdateAssemblies` 完整一致。
- `schemas/dhe-project-plan.schema.json` 和预检生成的 `dhe-project-plan.json`：把每个程序集的
  MV、hash、路径和 changed-method 数量作为 Player 适配器的稳定输入。
- `scripts/analyze-dhe-capabilities.ps1`：对项目程序集做离线能力盘点，辅助选择 Player
  gate 用例；它不替代 native ABI 或运行时验证。
- `scripts/resolve-dhe-native-manifest.ps1`：把 MV 方法解析为 IL2CPP native ABI。
- `scripts/inject-dhe-guard.ps1`、`scripts/apply-dhe-generated-cpp.ps1`：生成后 guard 注入。
- `scripts/run-dhe-capability-gate.ps1`：受控方法/ABI 能力报告。
- `scripts/run-dhe-compatibility-negative-gate.ps1`：token、增删方法、类型布局负例门禁。
- `scripts/run-dhe-script-fixture-gate.ps1`：无 Unity 的 guard 幂等性和 ABI 分类回归门禁。
- `scripts/run-dhe-clean-checkout-gate.ps1`：验证干净源码、stale output、缺失 runtime
  和 stale runtime manifest 的失败边界。
- `scripts/run-dhe-project-workflow.ps1`：项目无关的 Prepare/Player、preflight、archive、release 总入口。
- `scripts/adapters/dhe-demo-project-adapter.ps1` 与 `scripts/run-dhe-demo-workflow.ps1`：
  checked-in Demo 的项目专属 adapter 和执行核心。
- `scripts/run-dhe-deterministic-player-build.ps1`：清理生成缓存、注入并复用 C++ 的确定性构建。
- `scripts/validate-dhe-artifacts.ps1`：独立校验 MV、binary、native manifest、clean-checkout gate
  和 workflow report 的一致性。
  多程序集输入会按 build identity 的 `aotSnapshotAssembly` 绑定 AOT 快照，不依赖参数数组顺序；旧 identity
  没有该字段时才回退到唯一的 baseline hash 匹配。
- `scripts/validate-dhe-project-plan.ps1`：独立复核多程序集 project plan，并重新回算每个程序集的
  DLL/MV/binary 物料，同时校验 plan 中的 hot-update/DHE 集合元数据、settings/baseline/current/batch
  来源引用和 batch 内部程序集绑定。相对路径按所属报告文件解析。
- `scripts/run-dhe-release-gate.ps1`：把 project plan、Player workflow、native coverage 和程序集集合
  做最终一致性门禁，并拒绝缺少完整 hot-update/DHE 集合证明的旧 plan。
- `scripts/dhe-workflow-common.ps1`：统一子脚本解释器选择和输出目录安全检查。
- `scripts/run-dhe-source-preflight.ps1`：在 Unity 启动前校验正式源码边界、package/runtime
 lock、embedded package tree hash、settings 覆盖范围和 runtime manifest；输出受 schema 保护的
 `source-preflight-report.json`。
- `scripts/run-dhe-native-gate.ps1`：封装 runtime manifest 校验、native CMake/CTest
  编译和测试，并输出带 runtime tree hash 的 `native-gate-report.json`。它默认拒绝
  surrogate external headers；匹配引擎安装的 self-hosted runner 可通过手动
  `.github/workflows/dhe-native.yml` 执行该正式 native lane。
- `scripts/archive-dhe-artifacts.ps1`、`scripts/run-dhe-archive-gate.ps1`：复制生成 C++、
  managed payload、settings 和报告为 self-contained archive，重写 plan/batch 的相对路径，并在
  临时复制目录中独立验证 project plan、file inventory、source-set hash 和 artifact 交叉引用。
- `scripts/apply-dhe-runtime-patches.ps1`、`manifests/dhe-runtime-lock.json`：从锁定的干净
  checkout 组装 DHE runtime，并校验每个 patch 的 base commit、strip 层级和 SHA-256。
- `manifests/dhe-package-lock.json`：锁定 demo 使用的 embedded package 完整 tree hash、
  基础提交和已应用的 package patch；workflow 会以 `-VerifyOnly -RequireApplied` 检查，
  不允许“只可应用但尚未应用”的 package 进入 Unity 构建。
- `scripts/resolve-repos-root.ps1`：在多个 sibling `repos` 目录存在时，按 `repo-lock.json` 的 HEAD
  选择唯一匹配的仓库根，避免路径正确但提交错误。
- `scripts/fixtures/`：guard/resolver/ABI 的最小回归夹具。
- `schemas/dhe-*.schema.json`：DHE 输出契约，供 CI 或其他工具消费；其中
  `dhe-runtime-plan.schema.json` 约束项目内 Player 逐程序集加载所需的 current/MV/snapshot 映射，
  `dhe-runtime-handoff-plan.schema.json` 约束归档交接所需的 current/baseline/MV/snapshot/hash 映射；
  `dhe-runtime-manifest.schema.json` 约束 workspace 与 archive 两种 provenance 路径语义；
  `dhe-clean-checkout-gate.schema.json` 约束源码边界回归报告，
  `dhe-archive-manifest.schema.json`/`dhe-archive-gate.schema.json` 约束可移植归档。
- `unity2021-dhe-demo/Assets/Editor`、`Assets/Runtime`、场景、`Packages/manifest.json` 和
  `ProjectSettings`：demo 的可复现源文件。
- `Assets/Plugins/HybridCLRLab` is workflow-owned staging. Its four planned
  DHE assemblies are the project scope; `HybridCLR.BoundaryContracts` is a
  refreshed compile-only AOT reference and is intentionally outside that
  scope. Stale DLLs from an earlier plan are removed before Unity imports it.
- `unity2021-dhe-demo/Packages/com.code-philosophy.hybridclr`：demo 使用的嵌入式 package，
  不是构建生成物。
- `patches/dhe-lite/` 中的 native/runtime/package patch：运行时实现的版本化来源；依赖 worktree
  只用于生成和审计 patch，不是 clean-checkout 的必要输入。

## 隔离内容

以下内容不会作为源码提交，已加入 `.gitignore`：

- Unity 的 `Library`、`Temp`、`Logs`、`UserSettings`、`HybridCLRData`、`Builds`。
- demo 的 `Assets/StreamingAssets`、`Assets/Plugins`、`Assets/HybridCLRGenerate`。
- `unity2021-dhe-demo/reports`、根目录 `reports` 中的每轮 JSON/log，以及 `dhe-trace.log`。
- `unity2021-probe/` 整个目录（一次性 probe 的源码、Unity cache、build 和日志）。

这些目录仍可在本机保留作证据或加速下一次运行，但不能作为 clean-checkout
成功的依据。正式输出应放在 `artifacts/<workflow-name>/`，该目录整体被忽略。

## 历史材料

`unity2021-probe/`、`scripts/patch-dhe-method-body.ps1` 和
`unity2021-dhe-demo/run-dhe-player-build.ps1` 仍有诊断价值，但不属于正式发布入口。
`scripts/inject-dhe-aot-probe.ps1` 是验证计数器的辅助脚本，会被正式 guard
transformer 在显式开启诊断的验证构建中间接调用；它只写入生成 C++ 的诊断代码，
不会进入发布 runtime。升级 Unity 后如果生成代码格式变化，只需用这些诊断材料定位问题，
不应把其输出混入发布报告。

## 正式验证命令

先验证 runtime/native 层，再通过公共 project workflow 执行 Demo adapter：

```powershell
./scripts/assemble-runtime.ps1 -Profile DHE-Tuanjie2022 -EngineWorkflow Tuanjie2022Fgs -ReposRoot ./../dhe-locked-repos -PackageRoot ./unity2021-dhe-demo/Packages/com.code-philosophy.hybridclr
./scripts/run-dhe-native-gate.ps1 -Profile DHE-Tuanjie2022 -OutputRoot ./artifacts/dhe-native-gate -ForceOutput

./scripts/run-dhe-project-workflow.ps1 `
  -AdapterScript ./scripts/adapters/dhe-demo-project-adapter.ps1 `
  -ProjectPath ./unity2021-dhe-demo `
  -SettingsFile ./unity2021-dhe-demo/ProjectSettings/HybridCLRSettings.asset `
  -RuntimeSource ./staging/runtime/DHE-Tuanjie2022/libil2cpp `
  -OutputRoot ./artifacts/dhe-project-workflow `
  -ArchiveRoot ./artifacts/dhe-project-workflow-archive `
  -BaselineAotRoot ./releases/previous/stripped-aot `
  -DnlibPath ./unity2021-dhe-demo/Packages/com.code-philosophy.hybridclr/Plugins/dnlib.dll `
  -PackageLockPath ./manifests/dhe-package-lock.json `
  -IdentityTemplatePath ./unity2021-dhe-demo/Assets/Runtime/HybridCLRDheBuildIdentity.cs `
  -GitRoot . -SourceBoundaryPath ./manifests/dhe-source-boundary.json `
  -Mode Release -RequireEmbeddedPackage -RequireIdentityTemplate -ForceOutput
```

工作流完成后可单独复核归档物料（不依赖 Unity 进程状态）：

```powershell
$preflight = Get-Content -Raw ./artifacts/dhe-project-workflow/project-preflight/dhe-project-plan.json | ConvertFrom-Json
$planned = @($preflight.assemblies | Sort-Object assemblyName)
./scripts/validate-dhe-artifacts.ps1 `
  -MvJson @($planned | ForEach-Object { $_.mvJson }) `
  -MvBytes @($planned | ForEach-Object { $_.mvBytes }) `
  -BaselineAssembly @($planned | ForEach-Object { $_.baseline }) `
  -CurrentAssembly @($planned | ForEach-Object { $_.current }) `
  -NativeManifest ./artifacts/dhe-project-workflow/dhe-native-manifest.json `
  -BuildIdentity ./artifacts/dhe-project-workflow/build-identity.json `
  -WorkflowReport ./artifacts/dhe-project-workflow/workflow-report.json `
  -RuntimePlan ./artifacts/dhe-project-workflow/runtime-plan/dhe-runtime-plan.json `
  -BatchReport ./artifacts/dhe-project-workflow/project-preflight/batch/dhe-batch-summary.json `
  -Output ./artifacts/dhe-project-workflow/artifact-validation.json
```

这里的 `baseline`/`current`/`mvJson`/`mvBytes` 都来自同一份 project plan，
因此不会把旧的单程序集路径误当成当前四程序集 DHE 物料。若需要复核复制后的
归档，使用复制目录中的 `runtime-plan/` payload，并把上述各参数改为复制目录内的
实际路径；报告中的绝对路径只用于本机追踪，不能作为跨机器输入。

多程序集离线产物使用同一个 validator：将 `-BatchReport` 指向
`dhe-batch-summary.json`，并在发布门禁中加上 `-RequireCompleteCoverage`，
可防止 stale、missing 或 incompatible 程序集混入当前版本。

项目无关的正式入口是 `scripts/run-dhe-project-preflight.ps1`。它从
`HybridCLRSettings.asset` 解析程序集，严格生成所有 MV JSON/bytes，并逐个
调用 artifact validator，输出 `project-preflight-report.json`。该入口只证明
程序集兼容性和产物完整性；native ABI 与 Player dispatch 仍必须在 Unity/IL2CPP
阶段验证。

若只需要盘点程序集兼容性，可去掉最后一个开关；此时报告中的
`generationPassed`/`validationPassed` 可以为 `true`，但 `coverageComplete`
和 `artifactReady` 仍会明确反映 missing/incompatible 程序集。离线预检始终
保持 `releaseReady=false`，因为它不评估 native ABI 和 Player，不能把探索
结果误读为可发布结果。embedded package 从项目根解析 dnlib；registry/external package
应显式传入项目自己的 `-DnlibPath`，不会回退到 Demo 或 sibling checkout。
workflow 会同时归档 `runtime-manifest.json`、`build-identity.json`、
`dhe-native-manifest.json`、独立的 `runtime-plan/` payload 和
`artifact-validation.json`；validator 会重新计算归档 payload 的 hash，报告不再依赖
被 `.gitignore` 隔离的 `Assets/StreamingAssets`。缺少 native manifest 会直接使
coverage gate 失败，不能被当成零个 unsupported 方法。
若 workflow report 提供 `packageLock`，归档还会复制对应的
`provenance/dhe-package-lock.json`；使用 registry-managed package 的正式项目可以省略该
可选 provenance，不会被绑定到 demo 的 package lock。

runtime manifest 现在包含完整 DHE patch lock 和 staged tree hash。Player 构建还会把
`aotSnapshotSha256` 写入编译进 Player 的 `HybridCLRDheBuildIdentity`，运行时必须同时通过
MV 基线 hash、热更包 snapshot hash 和 Player 内置 hash 三重校验。空模板只用于让 Unity 在
首次生成前可编译，工作流结束后会恢复。
identity v2 另外绑定 patched generated-C++ source hash 和 native manifest hash；它比单独
校验 stripped DLL 更强，但仍不是最终 native binary 的完整 hash，因此不能替代 Player gate。
`Baseline-Clean` profile 不含 DHE patch，只是控制组；DHE workflow 必须使用
`DHE-Tuanjie2022`。
deterministic Player build 会让 Unity 先生成最终 Bee DAG，在 Unity 退出前保存 staging
输入；随后对最终 C++ 输入注入 guard，并调用同一 DAG 的 `bee_backend Player` target
重编译 `GameAssembly.dll`，避免第二次 Unity frontend 覆盖注入结果。

本轮还收紧了几个容易产生假阳性的边界：最终 `workflow-report.json` 只在独立 artifact
validator 首轮通过后才发布，并且强制包含 `artifactValidationPassed`；发布后的报告会再
被 validator 复核。native changed-method 准备阶段采用 resolve/commit 两阶段，失败时恢复
已触碰的 `MethodInfo` 和 vtable 字段。deterministic Player build 会先清理本次输出目录中
旧的 assembly C++ backup，再用本次变换后的源 hash 对 backup 做匹配，历史 guard marker
不能满足当前构建验证。resolver 对每个生成 C++ 文件只读取一次，并把不支持的 ABI 形状
记录为 `unsupportedChangedMethods`。

当前 struct-by-value 和 generic value-return 的 ABI 判定证据主要来自
Windows x64；当前 DHE release contract 明确排除 Android ARM64，不能仅凭
生成函数名推断其它目标安全。runtime loader 现在采用 resolve/commit 两阶段：
changed-method 准备或发布失败时，会在 metadata lock 下从 active registry 移除
本次 homologous image，并保留 retired image 的分配，避免 method-body cache 悬空。
Demo Player 已验证同一程序集先以非法 method token 得到
DHE_MV_REGISTRATION_FAILED，随后以合法 MV 重试得到 OK；这项事务证据会写入
workflow report 的 transaction 节点。

## 本次修正

- 修正能力分析脚本的 opcode 聚合，首个程序集的 opcode 不再少计一次。
- 修正 C++ 注入器的中间 manifest 命名，避免不同目录下同名 `.cpp` 相互覆盖。
- 将 demo 输出、Unity 缓存和历史日志从版本边界中隔离；移除 Unity 2021
  无法解析的 `com.unity.modules.infinity` 与 `com.unity.ai.navigation` 临时依赖。
- 同步正式验证文档中的 runtime source、baseline AOT 和部分覆盖说明。
- 完整覆盖 validator 现在强制要求 MV binary/native manifest，并校验 native token 是 MV
  changed token 的合法子集；`unsupportedChangedMethods` 同样携带程序集和 method token，
  因此 supported+unsupported 的并集在探索和完整覆盖两种模式下都必须精确等于 MV
  changed token 集合，避免只靠数量产生伪通过。
- AOT probe 改为显式诊断开关；无变更或无可注入 guard 的分析结果可以正常产出 manifest，
  但仍会由 complete-coverage 门禁拒绝发布。
- 批量不兼容时先写出完整 summary 再返回失败码，避免失败证据只存在于控制台。
- demo 总入口统一使用公共输出目录安全检查，拒绝文件系统根目录、祖先目录和
  junction/symlink；多程序集 C++ 变换按稳定路径顺序处理，避免证据命名受字典遍历顺序影响。
- artifact validator 不再静默接受缺失的 `generatedCppPath`；project plan 拒绝重复程序集并校验
  MV 内部 `assemblyName`；release gate 同时确认计划程序集确实进入 AOT 集合和 DHE 加载集合。
- DHE 入口的 `LabRoot` 默认值和转发的 switch 参数兼容 Windows PowerShell 5.1；pwsh 与
  Windows PowerShell 的 fixture、四程序集预检均已实测通过。
- native loader 增加 homologous image 的失败回滚 API；Player 事务探针覆盖
  “注册后准备失败 -> 同进程合法重试”路径，并将失败码和程序集名写入报告。
- resolver/ABI v2 使用统一 invoke-args bridge，完整覆盖 Demo 的 concrete/gshared generic、
  null reference 和 generic virtual；managed coverage 与 native entry 数分开计数。
- release gate 不再接受 Exploratory 自报结果，且独立复核 source-preflight、Git clean/tracked
  boundary 和 runtime provenance；二次验证文件只写入 release 输出目录，不修改输入证据。
- Player result 有独立 `hybridclr.dhe-player-result.json` schema；完整 workspace/archive 的
  schema 聚合不会再把 Player 报告误识别为 build identity。
- checked-in Demo adapter 已通过 `run-dhe-project-workflow.ps1` 的 Prepare、公共 preflight、
  Player、artifact、archive 全链路；workflow 的 plan/batch 引用在归档时改写为相对路径。

## 剩余边界

1. 在目标平台继续验证 Demo 尚未覆盖的 unsafe pointer/byref return、任意
   struct/adjustor thunk 等 ABI；当前 Windows x64 完整覆盖不能外推到 Android/小游戏。
2. 将生成后文本注入收敛为稳定的 Unity/IL2CPP generator 集成点；当前转换器仍受
   Unity 版本和生成时序影响。
3. 为新的 Unity/Tuanjie 版本、Android、小游戏或实际项目实现 adapter 后，必须使用
   匹配 runtime/editor 的 clean Release Player 重新取得证据；不能沿用当前 Demo 的
   Windows 结果，也不能用 Unity 2021 Editor 替代 Tuanjie 2022 构建。

本次审查又修正了下游报告布尔字段的读取方式：clean-checkout、static gate 和 native test
现在要求 `passed`、`runtimeReady`、`dheEnabled` 等字段实际是 JSON boolean；字符串
`"false"` 不会再被 PowerShell 当作真值。该规则只影响门禁判定，运行时数据格式没有扩展。

runtime source preflight 现在还把项目 `ProjectSettings/ProjectVersion.txt` 的
`m_EditorVersion`（Tuanjie 项目另含 `m_TuanjieEditorVersion`）与 runtime manifest 的精确
引擎版本绑定；同一引擎家族但版本不同会在 Unity 启动前失败，避免把不匹配的 IL2CPP
生成结果误当作同一 ABI 证据。

runtime manifest 同时记录 staged external headers 的 tree hash；source preflight、native
gate 和 native CTest 会重新计算该目录，确保“非 surrogate”不仅是一个声明，而是绑定到
实际参与编译的头文件集合。

提交工作流文件时应按 `manifests/dhe-source-boundary.json` 的 `exactPaths` 与 `prefixes`
选择性加入。`artifacts/`、`staging/`、Unity `Library/Builds/HybridCLRData`、
`Assets/StreamingAssets`、`unity2021-probe/` 和旧的 body-patch/player 脚本属于生成或历史
材料，即使它们在本机存在，也不应通过 `git add .` 纳入正式提交。source-boundary gate
当前观察到的非 ignored 未跟踪文件均在声明边界内，主要是锁定的 embedded package
和 demo/adapter/脚本/schema 源码，不等同于临时产物；精确数量会随正式契约文件增加而变化。

本次全量 Schema 扫描还发现并修正了两个此前静态断言未覆盖的契约缺陷：单个
`MethodOverride` 和单个 managed parameter 曾被 PowerShell 序列化成 scalar/null，
违反正式数组字段。override identity 也不再使用恒定的 dnlib 类型名，而是绑定 method
body 与 declaration 的完整签名，MethodImpl 目标变化不会被静默漏报。fixture 中故意
损坏的 source-boundary 输入在完成负例断言后即删除，因此整批输出可以作为正式 Schema
gate 输入，而不需要跳过整个 fixture 目录。

`run-dhe-demo-workflow.ps1` 是 checked-in adapter 的项目专属执行核心，不是生产项目的通用总入口；
正式入口是 `run-dhe-project-workflow.ps1`，公共 MV、native guard、artifact/archive/release
门禁由 orchestrator 统一持有。

`run-dhe-clean-checkout-gate.ps1` 会在公共 orchestrator 的 Unity Player 阶段前运行；它只在隔离的输出/系统临时目录
构造负例，不会修改项目源码或正式 runtime。其报告中的负例错误文本是预期证据，最终
`clean-checkout-gate-report.json` 必须为 `passed=true`。

批处理严格模式在发现不兼容变更时会保留 `status=incompatible`，即使底层
MV 生成器以非零状态结束；只有生成器异常、损坏报告或缺失输入才会记录为
`error`。这使项目预检能够区分“预期拒绝”和“工具链故障”。预检的输出目录
也不能与项目、输入程序集目录重叠，且不能是实验室根目录或其祖先，避免 `-ForceOutput` 误删
输入材料。
可复用的正式层包括 project orchestrator、MV/batch 生成、native manifest 解析、guard 转换、
artifact validator 和 archive/release gates；生产项目只需通过 adapter 接入自己的 AOT 构建、
程序集加载顺序和逐程序集 Player 断言。

DHE runtime 组装在 `DHE-Tuanjie2022` profile 下要求显式 embedded package root；release
gate 会重新验证 project plan，并按程序集名称核对 MV、batch、runtime-plan 的路径和 hash。
archive gate 的输入缺失、manifest 损坏等早期异常现在也会写出失败 JSON；受限 settings
YAML 解析器增加了引号逗号/井号和行尾注释的回归用例。
- 低层归档原语和归档 gate 现在都拒绝把 `LabRoot` 本身或其祖先作为
  `ArchiveRoot`，即使输入物料来自工作区外，也不会因为 `-ForceOutput` 递归删除工作区。
  AOT probe 也纳入 generated-C++ 事务快照；故意失败的后续 probe 会恢复 probe 和 guard
  两类改动，PowerShell 5.1/7 的 fixture 均覆盖该回滚路径。

Player gate 默认有 120 秒超时，可通过 `-PlayerTimeoutSeconds` 调整；Unity 编辑器构建阶段默认有
600 秒超时，可通过 `-UnityTimeoutSeconds` 调整。任一超时都会主动终止进程并使工作流失败。

## 2026-08-30 流程复核

本轮复核补上了 baseline manifest 的正式生成和绑定链：生成器按
`dhe-runtime-manifest` 的实际 schema 校验，不再要求不存在的 `format` 字段；绑定时同时
核对目标、引擎版本、runtime tree、HybridCLR package tree、package lock、程序集集合和每个
baseline DLL 的 SHA-256。Release workflow 会在任何项目输入检查和 adapter 调用前验证外部
toolchain package ID，并保证早期拒绝也落盘 failure report。baseline manifest 输出也不能覆盖
runtime/settings/package lock 输入。

Cat adapter 的 YooAsset 平台选择改为按请求的 Android/iOS/Standalone target 显式映射，删除
Standalone 隐式复用 Android 平台的 fallback。PowerShell adapter 通过 `ProcessStartInfo`
和 PowerShell 7 在 Windows/macOS 共用；仓库 native CTest 仍依赖 Windows `cmd.exe`、MSVC
和 Visual Studio，不能作为 macOS native gate。

当前证据：formal static gate 通过（PowerShell parse、45 个 schema、source boundary、script
fixture、toolchain publish/install/upgrade/release fixture）；Cat Android
`Exploratory + StopAfterPreflight` 通过，16/16 hot-update 与 DHE AOT 程序集一致，16 个 MV
均生成且 project plan compatible，Unity 2021 Android `Prepare/GenerateAll` 和 YooAsset
收集（15739 assets、922 bundles）通过。MV 全量生成在 Cat 上约为分钟级，最大程序集明显更慢。

尚未完成的证据必须保持为条件状态：真实 changed-method 的 Android ARM64 Player dispatch、
Android 设备 smoke、iOS Unity/Xcode/device smoke、完整 archive/release gate 均未在当前机器
执行；当前 Unity 安装没有 iOS module。formal worktree 当前 clean，Cat SVN working copy
的生产状态仍未作为本轮 Release 身份验证。生产接入前必须为目标平台提供
`PlayerSmokeRunner`，使用对应 target 的 previous stripped-AOT baseline/manifest，并在
clean checkout 上重新取得 Player、archive 和 Release 身份证据。
