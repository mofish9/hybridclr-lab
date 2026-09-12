# DHE 三仓库交付与项目接入边界 review

审查身份：package `4fb36af996871ff1e7ab8585f096395dad5d2bd3`，HybridCLR
`b0fe826f071332d109d2bde87c0aa2cc18b9f3c7`，IL2CPP
`ecad8a09d1eb9b91a57c59fcdc69b268377bad59`，Lab
`b80e90cd7e6c3ea98c3d85793b5a31a9ff400fe9`。

结论：runtime 与 package 的主要功能已经提交到三个正式仓库，普通 Installer
的版本选择链正确；但“只交付 package、项目仅编写业务适配”的完整工作流仍未闭合。
本次发现 2 个 P1 和 4 个 P2。下面的缺口属于框架维护工作，不应通过向 cat
复制 Lab 或继续增加通用构建代码来绕开。没有修改实现、tag 或 cat 的文件。

本轮是交付架构、安装、构建 API、工具依赖和项目现状审查，不是对全部 native
语义逐行重做证明。移动端、结构演进、多 Base 和性能验收仍需各自的测试证据。

## 应有职责与当前分布

| 位置 | 应负责的内容 | 当前情况 |
|---|---|---|
| repos/hybridclr | MV 比较、AOT/解释执行选择、Current metadata、批量注册和异常状态 | 正式分支 optimize/v8.13.0，tag v8.13.0-opt5；实现已入库 |
| repos/il2cpp_plus | Unity 2022 对象、反射、泛型、数组与 native hook 接入 | 正式分支 optimize/unity2022-v8.11.0，tag v2022-8.11.0-opt5；实现已入库 |
| repos/hybridclr_unity | Installer、托管加载 API、完整通用构建流程、工具源码及 DLL 分发 | 正式分支 optimize/v8.13.0，无 package tag；主体与 DLL 已入库，工具源码仍在 Lab |
| Lab | demo、测试、性能与 correctness 证据、维护版本组合 | 合理保留；但目前还承担生产工具源码和部分必需输入的生产职责 |
| cat / 其他 Unity 项目 | 引用 package，配置仓库 URL/程序集，业务打包、资源、签名与加载适配 | 属于单独项目任务；不应实现 MV、guard、native finalize、Base 匹配或差分算法 |

已通过远端 refs 确认三个正式提交和两个 runtime tag。三个 repos 工作树 clean。
上游基线继续是 package/HybridCLR 8.13.0、IL2CPP 8.11.0。

## P1-1：Installer 到正式构建的身份链仍依赖 Lab 工作目录

位置：`repos/hybridclr_unity/Editor/Installer/InstallerController.cs:210`、
`:217`；`Lab/tool/ProductionGates.cs:77`、`:194`、`:249`、`:260`、`:281`；
`Lab/tool/LabCommands.cs:653`；`Lab/tool/UnityToolBundle.cs:10`。

默认 Install 正确读取项目 HybridCLRSettings 的两个 URL，按 package JSON 的 tag
clone 并组装源码。但是安装回执只写 package 版本号，未生成供 DHE 构建消费的
runtime manifest/锁。当前 `runtime-manifest.json` 的生产入口是 Lab 的
`assemble-runtime`；它不在随包 CLI 的允许命令中。

完整 Release workflow 要求 RuntimeManifestPath，随后要求原 staged libil2cpp、
外部 headers、dhe-runtime-lock 以及三个原始 Git 源目录存在且身份一致。
这不是项目 Install 后自然具备的输入；把实验机的 manifest 复制给项目仍会引用
实验机绝对路径。Exploratory 可省略部分输入，但这不等于正式交付闭合。

应由 package 提供安装身份记录与构建身份导出，绑定实际项目内 runtime、Editor、
已发布 refs、package/工具内容。维护方的源码验收证据与项目日常构建验收分开，
后者不应要求原维护工作目录一直存在。不能以关闭身份校验作为修复。

验收：在不包含 Lab/worktrees 的环境，仅安装审核过的 package、配置项目仓库、
执行普通 Install，再由通用 API 生成新 Base 所需的全部框架输入。

## P1-2：默认 Base 策略没有覆盖结构演进所需的普通 AOT guard

位置：`repos/hybridclr_unity/Editor/Commands/DheProjectWorkflowRunner.cs:227`、
`:378`；`Tools~/DHE/templates/DheWorkflowBuild.cs:36`；
`Lab/tool/FrozenAotAdmission.cs:13`、`:38`、`:98`。

adapter 的 `GuardOrdinaryAotMethods` 默认 false，模板没有设置该值，默认构建
不生成完整普通 AOT guard。若后续 hotfix 类型布局变化需要 frozen AOT 适配，
`FrozenAotAdmission` 却要求 snapshot 中普通 AOT 可执行方法均被 Base guard
覆盖；缺失时产生 `frozen-aot-missing-base-guard`。已发布 Base 无法靠资源包补 guard。

最新 59 → 73 demo 是方法变更测试：44 个 AOT 程序集中 4 个为 DHE，native
manifest 的 guard 表只涉及这 4 个及 fixture 额外添加的 ValueLayoutNative。
该通过结果不能证明默认项目 adapter 已具备完整结构演进能力。

普通 AOT 依然不能发布新程序集代码。这里需要的是对原有 AOT 调用方作安全适配，
防止它继续按旧布局访问 hotfix 对象。应由 package 的明确能力策略决定覆盖，
在 Base 构建时校验并输出能力清单；项目组不应手工收集 MV 或复刻 Lab 的补 guard
操作。若保留受限 profile，必须在发 Base 前明确其不可补救的边界。

验收：用正式模板生成 Base，热更一个被普通 AOT 引用的 hotfix 值类型布局与
相关泛型/调用边界，验证安全通过或在 Base 构建阶段明确拒绝该能力策略。

## P2-1：完整通用生命周期不是可直接调用的参数化 C# API

位置：`repos/hybridclr_unity/Editor/Commands/DheProjectWorkflowRunner.cs:18`、
`:67`、`:131`、`:390`；`Editor/Commands/DheToolCommand.cs:18`；
`Editor/Commands/DheBuildPipeline.cs:3543`。

四个生命周期入口都直接解析 `Environment.GetCommandLineArgs()`，context 属性
仅有 private setter，没有接受显式 options/context 的 Runner 重载。已有项目
Editor 菜单/打包函数直接调用这些入口，会因缺少 `-dheTarget` 等启动参数失败。
`DheToolCommand.Run("workflow")` 又正确禁止启动嵌套 Editor。

底层 GenerateCurrentArtifacts、PrepareProjectArtifacts、StageRuntimePlan、
BuildPlayer、BuildSupport 等 API 已经存在，因此不是项目无法实现；问题是若要
在已有 C# 打包脚本内组合完整流程，仍需自己重新串接框架状态机。Prepare 底层的
BeforeCurrentGeneration/AfterCurrentGeneration 扩展点也未由高层 adapter 透传。

应提供参数化生命周期入口，将命令行入口变为薄封装。项目只传入目标、产物路径、
输入编译与恢复回调、场景和资源策略。Unity 编译/domain reload 的限制仍需由
package 明确管理；参数化不等于可以忽略 Unity 的生命周期。

## P2-2：SVN 校验不支持约定保留的 @8.13.0 目录名

位置：`Lab/tool/ProductionGates.cs:510`，尤其 `:518`。

`IsTrackedPath` 的 SVN 分支把本地 path 原样传给 `svn info`。对合法路径
`com.code-philosophy.hybridclr@8.13.0`，SVN 将后缀作为 peg revision 解析，
返回 `E205000: Syntax error parsing peg revision '8.13.0'`；同一路径末尾加
空 peg `@` 后成功返回 revision 24495。

这会使包含该 package 路径的 source-boundary 校验将已受控文件误判为未受控，
阻断 Release clean-checkout。工具已有 SVN 支持，缺的是路径参数转义，不能让
项目通过重命名 package 绕过。应统一处理 SVN 本地路径并补目录/文件级验证。

## P2-3：生产 CLI 的源码尚未归入三个正式仓库

位置：`repos/hybridclr_unity/Tools~/DHE/build-provenance.json:2`；
`Lab/tool/UnityToolBundle.cs:47`、`:69`、`:100`。

随包 DLL 本身已提交、身份可追溯，项目消费它无需下载工具源码，这是正确的。
但其 MetaVersion、resource-update、Base registry 与发布编排等生产实现仍在
第四个 hybridclr-lab 仓库，重建也硬要求 clean Lab。仅 checkout 三个 repos
无法维护和复现完整工具实现。

按本轮明确的三仓库职责，应将生产工具源码及重建入口归入 hybridclr_unity 的
维护边界，向项目继续分发 DLL/数据；Lab 仅引用它做测试、benchmark 和证据归档。
不需要为此把全部测试/实验源码随 package 拷进 cat。

## P2-4：package 内仍有旧版本和旧工具布局说明

位置：`repos/hybridclr_unity/Documentation~/dhe-maintenance-integration.md:8`、
`:10`、`:15`；`Tools~/DHE/templates/dhe-workflow-config.json:9`、`:13`、`:14`；
`Lab/tool/Program.cs:4246`。

maintenance 文档仍推荐 v2022-8.14.0-opt5，描述“Unity 2022 pins the fork
repositories”，并让项目去 Lab 取工具。这与当前的 8.11.0 清单、项目负责 URL、
随包 DLL 三项约定冲突。

实跑 `new-config` 仍生成旧独立目录 `Tools/HybridCLRDhe`，全零 expected ID
及需要外部提供的 runtime-manifest 路径。模板可保留业务参数占位，但工具自身
身份和安装位置应自动解析；当前 config 优先级会让旧 toolchainRoot 覆盖随包
默认值。需要统一文档、模板和生成器；已认证的 bundle 必须重建，不能只改内部 JSON。

## 已符合职责边界的功能

- Installer 的 URL 从项目配置读取，JSON 只选择 tag；运行时正式 refs 已发布。
- Base 通用功能在 package：Current 生成、linker 保留、临时编译器事务、native
  guards、Bee/产物 finalize、AOT snapshot、BuildIdentity 和构建后恢复。
- 资源更新工具已随包：MV、Base registry、多 Base 选择、计划生成、payload
  staging 与哈希校验。结构演进需要的 frozen source 编译/物化已在生产工具中，
  不必须调用测试 fixture 的 FrozenAotMaterialize。
- 项目资源边界已有 DheAssetBuild、DheDeliveryBuilder；下载、签名、YooAsset
  或 Addressables 具体构建仍应在项目侧。
- 运行时已有 IDheRuntimeAssetProvider、TryPrepareDelivery 和 delivery handle
  加载入口；项目不应重写 MV 解析、Base 选择或 native 注册。
- C# 工具通过 Unity 自带 .NET host 调用，无需 PowerShell 工作流或项目 SDK。
  Windows 验证不外推为 macOS/iOS 已测试。

## cat 当前磁盘状态：与上一轮迁移完成时不同

本轮只读复查 `C:/mofish_cat_worker3/cat`，发现：

- 项目仓库 URL 指向 mofish9，useGlobalIl2cpp=0，仍有 16 个原 hotfix 配置，
  未配置 dheAotAssemblies。
- package 版本清单当前引用 opt3；DheProjectWorkflowRunner.cs 不存在。
- ProjectSettings/HybridCLRSourceLock.json 和 maintenance/dhe-opt5-worker3.md
  不存在。Assets/ProjectSettings/package 的普通 SVN status 无改动。
- Tools~/DHE 的 DLL 仍在；`svn status --no-ignore --depth empty` 将 Tools~
  标为 I（ignored）。因此普通 SVN clean 不等于磁盘没有遗留工具。
- 本地 libil2cpp 全树 SHA 为
  `46A2D276D1B370A6BB5D85D673C463CC1281CEB00FC6651179B89CACA7F9C90F`，
  仍匹配上一轮安装的 opt5 DHE runtime。

这说明当前磁盘是 opt3 package 主体、残留 DHE DLL 和 opt5 runtime 的混合状态。
无法从本轮证据判断由哪个外部操作造成；没有自动恢复、清理或覆盖。后续项目接入
必须先完整迁移审核后的 package、校验整个包，再普通 Install；现在不能直接
在这个混合状态上开始 DHE 构建。上一轮安装报告仅代表当时状态。

## 本轮检查与后续顺序

本轮实跑正式 package CLI 的 verify-package、new-config、help 与 resource-update。
resource-update 只使用归档 Base 与 Current，不传 FrozenAotPlans、不调用 Lab
host，生成的计划 SHA 为
`C1CB5E0F5008EEB8DBEE2D8CB87831A73C530287526689D863F1C605D9FC3B41`，
与上轮 73 版本计划一致。产物在
`F:/hybridclr_artifacts/dhe-package-boundary-review`。
SVN @ 路径问题已用真实只读命令复现。没有重建 Player；本轮不把上轮 59 → 73
证据扩大为全能力或项目打包验收。

建议顺序：

1. 先在框架侧补齐安装身份导出与 Base 能力/guard 策略，解决两个 P1。
2. 将生产工具维护归回 package 仓库，完善参数化 API、SVN 路径与模板文档。
3. 用只消费审核 package 的 demo 验证普通 Install → 新 Base → 同一 Base 的
   多轮 Current，覆盖方法与类型结构变化、资源、多个 Base；禁止依赖维护机路径。
4. 再按 runtime → package → Lab 证据顺序提交/发布，最后在项目任务里调整
   cat 打包脚本、资源与启动 provider，并独立进行 Android/iOS 验证。

项目适配量较大本身不是缺陷；把框架内部流程、源码目录依赖和能力决策留给项目
重新实现才是本轮需要收口的部分。
