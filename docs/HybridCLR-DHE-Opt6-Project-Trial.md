# opt6：项目打包试用步骤

这轮可开始 Unity 2022 项目接入和打包试用。Windows demo 已验证安装、Base 构建、
跨 Base 结构热更、Prefab/Scene 资源交付及回退；Android/iOS 尚未实测。工具模式
保持 Exploratory，不能通过修改 manifest 将它标为 Release。本文不要求复制 Lab
或单独的 tools/hybridclr-dhe，也不涉及 cat 项目的具体打包脚本修改。

## 1. 迁移完整 package

从 `repos/hybridclr_unity` 的 `optimize/v8.13.0` 取得
`dff191350c250c60d9a9fb910b1e612e049fd445`。两个 runtime 引用为：

| 仓库 | tag | commit |
|---|---|---|
| hybridclr | v8.13.0-opt6 | 04bd9b5cd5153a31e76815d65bc1a9b5655a5a05 |
| il2cpp_plus | v2022-8.11.0-opt6 | 57f3a065e84e3b1e7e13c1012752e27f23e4c041 |

也可使用已按 tracked 文件导出的
`F:/hybridclr_artifacts/dhe-opt6/hybridclr-unity-opt6-project-trial.zip`。
ZIP SHA-256：`7B9F11A2F987F2F5B82DD9D4DBB3F482E494D99C53A104F9008901C5A2574538`。
顶层目录为 `com.code-philosophy.hybridclr@8.13.0`，保留这个名字即可。应替换项目
内旧 package 的受控文件集，先保存项目已有定制；不要向旧目录随意叠加，留下旧工具。

迁移结果应保留完整 `Tools~/DHE`（85 文件），并在 Git/SVN 中确认这些文件被纳入
版本控制。`ToolsSource~` 是框架维护源码，Unity 不会把它编译进 Player；不需要
将其 bin/obj、Lab、验证产物迁入项目。项目不需要 SDK，随包 DLL 使用 Editor
自带的 .NET host。记录实际 package commit 与 Git tree：
`d3ca470e07563c604777fa199bcf25c01db5df42`。package 不另打 opt tag。

## 2. 配置并重新 Install

在项目的 `HybridCLRSettings` 中设置两个仓库 URL 为包含上述 tag 的 fork 或镜像。
当前正式来源是 `https://github.com/mofish9/hybridclr.git` 和
`https://github.com/mofish9/il2cpp_plus.git`。URL 属于项目配置，不写入
`Data~/hybridclr_version.json`；该 JSON 仅选择 tag。

保留实际 hotfix 集合，并令 `dheAotAssemblies` 与该集合一致。原本普通 AOT 的
程序集保持普通 AOT。关闭全局 IL2CPP，执行普通 HybridCLR Installer，再运行
`HybridCLR > DHE > Verify Bundled Tool`。Installer 会生成项目本机的
`HybridCLRData/DHE/runtime-manifest.json` 和 `package-lock.json`，后续阶段校验它们。
不要复制其他机器的安装记录，也不要修改记录以绕过源码或版本不匹配。

opt5 已废弃。必须重新构建 opt6 Base；旧 Player 内的 native 错误不能通过托管
热更包修复。本轮新 Base 契约是 `dhe-runtime-v34`，MV 文件格式仍未改变。

## 3. 接入项目自己的 C# 打包入口

项目提供 `DheProjectWorkflowAdapter`：场景、Player 输出、identity 脚本路径与命名
空间，以及已有 Player 构建回调。identity 与启动入口属于普通 AOT 程序集，不能
放进 hotfix/DHE 程序集。先导入 package 提供的空 identity 模板并完成编译。

新 Base 使用完整入口，而不是只调用 Unity 的 BuildPlayer：

```csharp
using HybridCLR.Editor.Commands;

DheProjectWorkflowRunner.BuildBase(adapter, new DheProjectWorkflowOptions
{
    Target = target,
    OutputRoot = outputRoot,
    BaselineRoot = System.IO.Path.Combine(outputRoot, "baseline"),
    Bootstrap = true,
    EngineWorkflow = "Unity2022Fgs",
    Il2CppCodeGeneration = "OptimizeSize",
});
```

`adapter`、`target` 和路径由项目配置。Android/iOS 使用各自目标和输出形式，不能
复用 Windows 条件编译后的程序集。默认保持 `GuardOrdinaryAotMethods=true`。
项目拥有预编译 DLL 时，用 Before/AfterCurrentGeneration 回调管理目标平台输入，
After 在 finally 中恢复。不要在生成目录手工修改 IL2CPP 源码或绕开 guard 阶段。

完整入口负责 Prepare、preflight、StageRuntimePlan、BuildScriptsOnly、native
finalize、BuildFinalPlayer 和 identity 恢复。跨 Editor 进程的 CI 也可调用同一
runner 的阶段重载。APK/AAB、Xcode 导出后的签名/上传及业务资源编排仍由项目负责。

## 4. 归档每一份上线 Base

将该次完整 Base 构建输出作为不可变 CI 产物保存，至少保留 identity、baseline
DLL/MV、native manifest、identity 指向的完整 AOT analysis snapshot、所选补充
metadata，以及原 Player。安装 receipt 不能代替这些归档。

以后以 `baseId` 标识 Base，不能只用应用版本号或某个 DLL 的 SHA。多个在线 Base
各保留独立归档，不用新产物覆盖旧目录；按平台/架构分别组织。普通 AOT snapshot
包含兼容分析和必要原始 IL，不能只归档 hotfix DLL。

## 5. 后续只构建热更资源

使用 package 的 `GenerateCurrentArtifacts` 或 Prepare 入口生成目标平台 Current，
把实际 hotfix/DHE 集合导出为一套 Current DLL。不要重新 BuildBase。通过
`DheToolCommand.Run` / `RunWithTimeout` 调用随包 `resource-update`，传入：

- CurrentRoot 和项目 SettingsFile；
- 所有仍在线 Base 的 BaseRoots、BaseNativeManifests、BaseBuildIdentities；
- 对应 Base 归档的 AOT/metadata 资料；
- 新 OutputRoot 与 `Mode=Exploratory`（本轮打包试用）。

同一份 Current 可以服务多个 Base，内部仍有各 Base 的差异选择与兼容资料。任何
Base 不兼容都应阻止该更新发布；本轮已实测旧 opt5 缺少接收者能力时被拒绝。

使用 `stage-resource-update` 按精确 Base identity 验证 staging。项目资源构建应
消费完整生成目录：除了 Current DLL/MV，还可能有按 Base 存放的 frozen ordinary
AOT 原始 IL/MV 和去重 metadata。它们不是允许热更普通 AOT 程序集的入口，不能
自行删掉，也不能替换为当前普通 AOT DLL。

涉及 Prefab/Scene 等资源时，用 `DheAssetBuild.Build` 包裹项目现有资源构建回调，
检查 authoring Editor 与 Current 的序列化 schema 并记录 asset-build.json；然后
用 `DheDeliveryBuilder.Build` 绑定代码资源目录、资源 ID/文件映射、目标、workflow
及该来源记录，得到一个完整 Delivery 目录及预期 manifest SHA。本轮已验证真实
AssetBundle 路径；项目仍需实现自己的 YooAsset/Addressables/CDN 适配。

## 6. 替换启动加载与验证回退

先取得同一版本的不可变资源与可信预期 manifest SHA，在业务入口前调用
`DheRuntime.TryPrepareDelivery`，传入资源 provider、嵌入 Base 资源 provider、
`DheBuildIdentity.Create()` 与预期 SHA；成功后调用返回 handle 的
`LoadCurrentAssemblies`。通过后再进入业务，资源读取使用该已验证 handle。
不要再对同一批 DHE 程序集执行原来的普通 Assembly.Load 流程。

下载、认证发布、渠道选择属于项目。准备阶段拒绝可以修正资源后重试；native
preparation/commit 失败按 `RestartRequired` 状态处理，不能 Reset 后强行继续。
已经加载新 metadata 后，切换或回退到另一份资源需要新进程。

项目首次验收依次覆盖：空更新；方法逻辑变化；类型/字段与泛型容器变化；两份
不同 Base 加载同一热更资源；错误资源拒绝；A→B→A 重启回退；实际资源加载。
Android/iOS 完成实际打包及设备验证前，不把 Windows 通过写成移动端生产结论。

详细源码身份和本轮证据见 `reports/dhe-opt6-project-trial.md`。
