# DHE Current storage：Unity 2022 Windows 验证状态

当前研究候选已通过带原生入口 guard 的 method-only、no-op 和两种 Base 布局验证。
正式 package 的资源加载入口尚未接通 Current storage plan；不能将本报告称为完整
DHE 工作流已交付，也不能用于 Android、iOS、团结或性能结论。

## 已定位并修复的问题

此前 method-only 的 FieldInfo 异常来自 Unity 2022 的两处无条件映射：
Type.GetTypeFromHandle 与 RuntimeType.GetFields_native 使用 GetDheCurrentType，
把没有改变物理布局的 Base 类型也替换为隐藏的 Current metadata 类型。

两处现在改用 GetDheExecutionType，只有选中的物理存储类型才切换。
未变化类型继续使用 Base 句柄，变化方法仍独立进入解释器。本次未采用把整个 hotfix
调用图转为解释执行的做法。此前关于必须整体解释、必须扩散所有调用者的推断不成立。

同时删除了研究加载入口中的“同哈希 no-op 不加载镜像”短路，让 no-op 也实际经过
正常的 metadata 加载与注册，检验其反射语义。

## 源码与产物身份

| 仓库 | 分支 | 精确 commit |
|---|---|---|
| HybridCLR | research/dhe-value-layout-v8.13.0 | b2ce881b306176ec5b963707df1f2761d2075496 |
| Unity 2022 il2cpp_plus | research/dhe-evolution-unity2022-v8.13.0 | b9821d6dd1f085999c3e2ebd6e6caf3504c29ad8 |
| package（本轮未修改） | research/dhe-evolution-v8.13.0 | bc319e5e376d97ed89fdd0f127d4256fa466f1ef |

Player 的 lab 构建身份为 2c7cc8e46bedef1140351f1efd8050df44e5786a；
独立审计工具源码为 99d64d0，其 DLL SHA-256 记录在 audit.json。
后续报告提交不改变以上构建身份，也不替代重测。runtime 源码、lab fixture 和锁
均在各自提交之后构建；没有修改 CAT、正式维护分支、Installer 默认版本或 runtime tag。

HybridCLR canonical source SHA-256：
31A29CA1B3A5FA7A5FDDAB87BD0BFEC7E8DDDFC330738F891183E9B31BEBCC44。
Unity 2022 il2cpp_plus canonical source SHA-256：
AEFB53639DACD764E5433E67FE48BDB7BE4219406EC43BA8DCB0E9789CE4168C。

## 当前验证

本节路径相对 C:/hybridclr_optimize/artifacts/dhe-reflection-storage-20260909。

| Base | payload | 结果 |
|---|---|---|
| base-old-fixed（旧值布局） | payload-method-old | 方法返回值 41 → 73；14/14、Consumer、反射通过 |
| base-old-fixed | payload-noop-old | 原值 41；14/14、Consumer、反射通过 |
| base-old-fixed | payload-latest-old | 布局演化 + 方法返回 73；14/14、Consumer、反射通过 |
| base-new（新值布局） | payload-latest-new | 方法返回 73；14/14、Consumer、反射通过 |
| base-new | payload-noop-new | 原值 41；14/14、Consumer、反射通过 |

每个成功运行都检查了直接调用与反射调用返回值。更新时 revisionInterpreterEntries=1，
no-op 时为 0；未变化哨兵方法 UnchangedRevision 的 unchangedAotEntries=1。
两个 Player 的原生清单均为 universal，50 个方法受到 guard 覆盖，unsupported=0。
这证明选定方法的执行路径；结果末尾的 interpreterEntries 会受计数器重置影响，
不能用它声称整个调用图完全没有解释执行。

两种 Base 使用字节相同的最新 Current DLL 与 Current MV。各自的 Base MV/选择计划
由其真实构建 baseline 生成并核验；不会把一个 Base 的 MV 冒充另一个 Base。
当前验证是独立启动进程的版本选择，不是同一进程内多次替换已加载程序集。

- native-fixed/DHE-Unity2022/native-gate.json：compile/CTest/FGS 通过，
  mergeReady=true，surrogateExternalHeadersUsed=false。仅原生门禁有此 mergeReady，
  不代表方案整体可发布。
- audit.json：22/22 检查通过，绑定 native gate、runtime manifest、两个 Player
  GameAssembly、guard manifest、各 Base MV、结果文件和审计工具的哈希。
- reference-base.json、reference-method.json、reference-layout.json：CLR 14 组参考语义。
- impact.json：35/35 影响分析检查通过；公共工作流仍拒绝未集成的值布局演化。
- wrong-base 检查：错误 Base 计划在启动 Player 前拒绝。
- wrong-expected-result.json：故意期望旧值 41，实际执行得到 73，测试按预期失败；
  此文件是负向门禁证据，不是待修复回归。

对应结果文件为 method-old-fixed-result.json、noop-old-result.json、
latest-old-result.json、latest-new-result.json、noop-new-result.json。
method-old-result.json 属于只修了 GetFields、尚未修 typeof 的失败对照：
新方法返回已生效，但 AOT typeof 的反射仍失败。不得将该对照写成成功结果。

## 如何复现

C# fixture 主程序：tool/fixtures/value-layout/ValueLayoutTests.csproj。
命令定义见该目录 Program.cs；全部以独立输出目录运行，拒绝覆盖既有结果。

1. 用 build <lab> <inputs> 构建旧/新布局 DLL；method 变体额外编译
   DHE_METHOD_UPDATE，GetRevision 从 41 改为 73，不改原有 14 组语义。
   latest 变体同时定义 DHE_VALUE_LAYOUT_CURRENT 与 DHE_METHOD_UPDATE。
2. 用 probe-project <lab> <package> Unity2022Fgs <input DLL root> <project>
   创建隔离 Unity 项目。分别使用旧、新布局 Base。
3. 用 probe-build <lab> <Editor exe> <project> <assembled libil2cpp> <output>
   进行准备、原生 guard 注入和重链接，生成 build-binding.json。
4. 用 probe-payload <output/baseline> <current DLL root> <payload>
   根据该 Player 的真实 stripped Base 构建 MV/选择计划；no-op 的两侧都用 baseline。
5. 用 probe-run <output/player-executable> <payload> <result.json> <41 or 73>
   校验 Base MV 与 Player 身份，再启动进程验证结果、反射和执行路径。
6. 本报告目录布局可直接运行 probe-audit <artifact root>，要求 audit.json 及负向结果
   尚不存在。该审计读取已构建的产物，不重建 Player。

## 历史证据修正与剩余工作

此前 p11/p48/p50 等研究探针没有原生入口 guard，也没有在运行前校验 payload 的
Base MV 与 Player 构建身份。因此“两个 Player 接受同一 plan”本身不能证明多 Base
工作流正确；本轮用相同 Current 内容、各 Base 正确计划和明确返回值补齐该证据。
历史 no-op 短路成功不能代替本轮正常加载的成功。历史提交和 artifacts 保留可追溯，
不将其数字换绑到新提交。

下一步仍需把 Current storage 选择、跨程序集影响闭包和身份验证接入公共 C# package
的资源构建/加载流程；当前只有 lab probe 传入这些选择。还需验证连续版本/跳版本、
更多泛型与布局边界、并发/回滚、静态存储、循环依赖和普通 AOT 边界。
不能把本 fixture 的通过外推为任意代码演化皆已支持。

Unity 2022 的公共流程与回归稳定后，按用户范围移植并验证团结 2022；不再投入 Unity 2021。
性能、内存与 ARM64 真机证据均未完成。方案尚未发布到正式维护线。
回滚边界为独立 research 工作树及精确提交/产物组合；失败实验 fff481d 已由
7a89392 回退，本轮保留的是反射存储选择修正，不是该失败的调用者改造。
