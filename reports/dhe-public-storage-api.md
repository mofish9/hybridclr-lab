# DHE 公共加载 API 与普通反射验证

后续资源选择计划的接入状态见 [dhe-resource-execution-plans.md](dhe-resource-execution-plans.md)。
本报告仍只代表下文列出的原生 API/Player 组合。

Unity 2022 Windows 的公共 RuntimeApi 加载、普通反射和多 Base 样例有条件通过。
本轮解决了研究探针掩盖的反射调用约定缺口，并保留未变化方法的 AOT 路径。
这不是完整资源工作流发布：DheRuntime、resource-update 和 staging 尚未接通
Current storage 选择计划，现有资源兼容检查仍保留布局演化拒绝。

## 正确性修复与边界

新增 RuntimeApi.LoadDifferentialHybridAssembliesWithMetaVersionAndExecutionPlan，
复用现有多镜像准备和原子注册。先解析全部 DLL/MV/类型及方法 token，再进入加载事务。
它没有改变 MV 格式，也没有把整个 hotfix 程序集改为解释执行。

最初将 fixture 改为公共 API 和普通 MethodInfo.Invoke 后，Factory.Create 的结构体
返回触发了旧 AOT ABI 保护异常。原研究入口 Resolve 会主动选取 Current MethodInfo，
因此原探针成功不能证明普通反射已经可用。本次修复位于 Unity 2022：

- RuntimeMethodInfo.InternalInvoke 在接收者检查和参数转换之前选择 Current 执行签名。
- ReturnType 和 ParameterType 使用对应的物理类型。
- ParameterInfo.Member 及方法所属类型保留逻辑身份，避免暴露隐藏 Current 方法对象。

原始 native Invoke 仍受既有 ABI 约束；不能把一块旧布局的原生参数内存直接解释成
新布局。当前测试在进程启动阶段加载，尚未验收加载前已缓存的反射结果、存活对象迁移、
实例/虚方法的全部反射边界和进程内重复更新。调用方仍须提供经过完整影响分析的计划；
原生 token 校验不能替代编译器对遗漏依赖、静态存储和普通 AOT 边界的分析。

## 精确身份

以下均为 research 候选，未修改正式维护线、runtime tag、Installer 默认版本或 CAT。

| 仓库 | 分支 | commit |
|---|---|---|
| HybridCLR | research/dhe-value-layout-v8.13.0 | 0b5252cde1192194b17705d037ef5c9dab762701 |
| il2cpp_plus | research/dhe-evolution-unity2022-v8.13.0 | 80ee2ce13208b0f8b1984e1d78b3a29105472160 |
| hybridclr_unity | research/dhe-evolution-v8.13.0 | 710f9698da0d64fb80fcc73834e78a0680670619 |
| lab 构建/审计工具 | research/dhe-value-layout-v8.13.0 | 0bfbe5c06e6f4105a2776366e63fd21427d0b830 |

两个最终 Player 都在该 lab 提交上构建；build-binding.json 记录开始构建时的 commit、
工具 DLL 和实际 fixture 文件哈希。报告提交不改变构建身份。

证据根目录：C:/hybridclr_optimize/artifacts/dhe-public-reflection-20260909。
Editor：2022.3.62f3，StandaloneWindows64，OptimizeSize/FGS，真实 Editor headers。

- runtime manifest SHA-256：91BC0837C7D5FAE41D345859EFF2304E6122EC83368B0D26E58A7B992B7D1C1A。
- native gate SHA-256：C289D51F4B2113536541520F869808E853F701A683188E50762990D9CCB36C58。
- C# 构建/审计工具 SHA-256：93854B703364972174A43204980481BAA4CD33205DE647CF9854F9749AFBCB91。

## 验证结果

| Base | 更新内容 | 结果文件 | 结果 |
|---|---|---|---|
| base-old-reflection | 只修改方法，41 → 73 | method-old-fixed-result.json | 通过 |
| base-old-reflection | 同字节 no-op | noop-old-result.json | 通过 |
| base-old-reflection | 值布局演化 + 方法修改 | latest-old-reflection-result.json | 通过 |
| base-new | 同一份最新 Current | latest-new-result.json | 通过 |
| base-new | 同字节 no-op | noop-new-result.json | 通过 |

每次均通过 14/14 CLR 参考记录、未重新编译的跨程序集 Consumer、普通 AOT Echo
样例、字段反射、公共反射值参数/返回值、ref、nullable、闭合泛型和同名异程序集拒绝。
ParameterInfo.Member/DeclaringType 的身份检查也包含在 reflectionSignaturePassed 中。

每个进程先拒绝五种非法计划：空选择集、错误数组数量、空行、非法类型 token、重复
方法 token；随后成功加载有效计划。更新的直接调用和反射调用均返回 73，
revisionInterpreterEntries=1；no-op 返回 41，该计数为 0。
未变化的 UnchangedRevision 哨兵在五次运行中均 unchangedAotEntries=1。
末尾的 interpreterEntries 会受计数器重置影响，不代表整个调用图的解释执行次数。

两个 Base 的 native manifest 均覆盖 50 个方法，unsupported=0，guardMode=universal。
latest 的 Current DLL/MV 在两个 Base 间字节相同，各 Base 使用自身真实 baseline
生成的 MV 和选择计划。这些结果来自独立启动进程，不代表同一进程内连续替换。

native-reflection/DHE-Unity2022/native-gate.json：compile/CTest 通过，FGS 开启，
mergeReady=true、surrogateExternalHeadersUsed=false。此 mergeReady 只属于原生门禁。
audit.json：27/27 检查通过，包含构建/guard/运行时绑定、各结果与 CLR 记录一致、
Base MV 身份及同一 Current 内容核验。

负向门禁：错误 Base 计划在 Player 启动前拒绝；故意对更新资源期望 41 的运行实际
返回 73 并按预期失败（wrong-expected-result.json），不是待修复回归。

## 历史对照及复现

旧目录 C:/hybridclr_optimize/artifacts/dhe-public-storage-20260909 中保留了普通反射
首次失败的 latest-old-result.json，以及较早 runtime 的结果。它们不属于上述最终身份。
该目录 base-new 的 build.log 记录过生成证据 generation.json 的 File.Replace 失败；
该次构建没有完整 build-binding，未纳入成功证据。最终新目录重新构建成功，未绕过检查。
工具在一个较早 Player 构建进程持有 DLL 时曾遇到 MSBuild 文件锁；进程结束后重建成功。
最终构建和审计使用同一已提交工具，所有相关进程均已退出。

复现命令沿用 docs/DHE-Public-Storage-Workflow.md 及 ValueLayoutTests 的
probe-project、probe-build、probe-payload、probe-run。普通反射测试源码为
tool/fixtures/value-layout/Unity/CurrentStorageRuntime.cs，没有调用 lab Load/Resolve。
最终证据布局的审计命令是 api-audit <新证据根目录>，要求 audit.json 及负向结果尚不存在。
不要覆盖本轮产物来重跑，也不要在 Player 构建持有工具 DLL 时编译覆盖该 DLL。

## 剩余工作与回滚

下一步将 per-Base 选择计划接入 C# resource-update、staging 和 DheRuntime：
计划需绑定嵌入的 Base MV、Current MV、完整 hotfix 集及运行时能力；manifest、
validation、runtime plan 的选择表必须一致。新增方法已经解释执行，不应作为有 Base
入口的方法 token 传入。还需完整验证跨程序集影响、静态存储、泛型上下文及普通 AOT
参数边界，然后验证跨版本/跳版本资源和失败恢复。不能仅放开现有兼容性拒绝。

Unity 2022 的公共资源流程及回归稳定后再移植团结 2022，Unity 2021 不在当前用户范围。
性能、内存、ARM64 并发和移动端实机门禁未完成，没有 Android/iOS/小游戏可发布结论。

源码回滚使用上一组一致候选：HybridCLR b2ce881、il2cpp_plus b9821d6、package bc319e5、
lab d3e428b，连同其对应构建产物使用。不能将新增托管 API 与旧 runtime 混装。
本轮仅候选提交，没有运行时或资源在线发布，不需要项目侧线上回滚。
四个相关工作树在验收时 clean；本轮未创建 stash，也未删除任何产物。C 盘约余 198 GiB。
