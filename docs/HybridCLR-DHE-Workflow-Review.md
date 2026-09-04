# DHE 生产工作流审查

本文定义 opt4 DHE 工作流进入项目试用和正式发布时必须满足的契约。实现由跨平台
.NET host、HybridCLR Unity package API、项目 C# adapter 和 opt4 runtime 组成；项目只
拥有资源系统、签名和设备 smoke，MV、native guard、身份校验、归档和发布门禁由公共工具
统一执行。

## 适用边界

本工作流有两个明确模式：`bootstrap` 生成一次带 universal guards 和内置 Base MetaVersion 的
Base Player；`resource-update` 只生成一份 current DLL/MetaVersion 资源。普通 changed-only
finalize 仍是诊断路径，不能被误认为线上资源更新能力。

DHE 将全部已配置 hot-update 程序集同时编入 Player AOT。资源更新不是为每个 Base 计算并
下发 changed-token 文件；MetaVersion 描述完整 current 元数据，客户端拿自身内置 Base MetaVersion 本地
求差。发生变化或新增的受支持元数据注册到解释器，未变化方法继续执行 Player 中的 AOT
代码。当前 proven-safe subset 除方法体变化外，还覆盖新增顶层/nested 类型、既有普通
类型的非虚方法、受约束的实例/静态字段演进、删除类型/方法、方法签名替换、逻辑
property/event 和既有成员 custom attribute。以下变化仍会在发布资源前被拒绝：

- 既有值类型的实例布局变化，以及继承、接口集合、class layout 或 vtable 变化；
- 既有方法的泛型约束、override、P/Invoke 或非 custom-attribute 声明元数据原地变化；
- 给既有接口新增方法，给既有类型新增 virtual/abstract/PInvoke 方法；
- 既有泛型类型实例字段、ThreadStatic/RVA/pointer/byref 字段和已取地址字段演进；
- native resolver 无法覆盖的 changed AOT 入口或 ABI 形状。

被拒绝的更新必须重新发布基础包，不能降级为未验证的普通热更。项目可通过关闭
`dheAotAssemblies` 回到原 HybridCLR 路径；已发布的 baseline、MV 和 runtime payload 不得
跨 package/runtime 身份复用。

## 锁定身份

历史条件发布 `0.1.18` 使用以下 integrated 输入；这些身份和报告只作为审计基线，
不代表当前候选：

- HybridCLR runtime commit `f777ed7` / annotated tag `v8.13.0-opt4.1`；
- Unity 2021、Unity 2022、团结 2022 的 il2cpp_plus commit 分别为 `b3fdf1e`、
  `6032274`、`52968ad`，tag 分别为 `v2021-8.1.0-opt4.1`、
  `v2022-8.11.0-opt4.1`、`v2022-tuanjie-8.13.0-opt4.1`；
- HybridCLR Unity package commit `749eaee`，位于 `optimize/v8.13.0`，package 本身不打 tag；
- 团结引擎 `1.10.0` / Unity compatibility `2022.3.62t12`；
- `manifests/dhe-runtime-lock.json`、`dhe-package-lock.json` 和工具包 manifest 中的
  commit、tree、文件集合及 SHA-256。

Release preflight 会实时重算上述身份。dirty、mixed SVN revision、surrogate headers、错误
Editor 版本、integrated commit/tree、审计 patch hash、目标引擎不匹配、未登记 package 文件或
runtime tree 漂移都会失败。integrated 模式只校验源码，不再应用 overlay。

当前 `0.1.20` 候选的锁已经更新为：HybridCLR `fe3b1edb222511a1d3227f7e76e8b83b618c4d27`、
HybridCLR Unity package `b288f2f33ad9dc25e05a3fdb10f5f405cd8ada64`（tree
`6D15EDF77405E51300E3A115FBA93B359E26366AEA985D09816A5A45E3110E64`），以及同一三引擎
`il2cpp_plus` opt4.1 维护线。候选尚未创建新的 runtime tag、尚未推送，也不能作为
Release package 使用；以 `manifests/dhe-runtime-lock.json`、`dhe-package-lock.json` 和
`repo-lock.json` 中的完整 commit/tree/hash 为准。

## 标准工作流

1. Base bootstrap 的 `Prepare` 生成当前 stripped AOT，并将其冻结为 Base 集合；host 对全部
   `dheAotAssemblies` 生成 Base MetaVersion，要求其集合严格等于 `hotUpdateAssemblies`。
2. scripts-only Player 生成干净 C++；package 解析全部可支持的既有方法、注入 universal
   native guard，并生成完整 build identity。
3. final Player 编译该 identity；第二次 native finalize 必须证明 guard 和 immutable native
   manifest 没有漂移。`guard-block-set-v1` 只认证 manifest 声明的完整 begin/end guard 块，
   不把同一 C++ 文件中由 build identity 引起的无关变化计入 guard 身份；缺失、重复或内容不符
   的块必须失败。项目 adapter 在 Player 和 smoke 完成后必须在 `finally` 中恢复临时 build
   identity 源码模板，成功和异常路径都不得污染工作区。
4. 后续资源发布只编译一套 current hotfix DLL。`resource-update` 为每个程序集生成一次
   current MetaVersion，并使用所有仍受支持 Base 的 DLL、BuildIdentity 和 native manifest 做离线
   兼容审计；identity 1 的复合 `baseId` 唯一绑定完整 Player 身份，按每个 Base 的真实差异
   推导 `requiredRuntimeCapabilities`。Base 专属二进制不进入 payload，任一 Base 不兼容时不
   生成可发布 manifest。
5. `stage-resource-update` 只替换 current DLL/MetaVersion、可选补充 AOT metadata、manifest、
   validation 和 runtime plan；manifest 使用 `runtimePlanSha256` 绑定 plan，并逐文件校验所有
   payload hash 后才复制，
   强制接收当前 Player 归档的 `build-identity.json`，校验 identity schema、复合 `baseId` 和
   文件 SHA 后精确命中一个 `supportedBases` 记录，再证明 Player、GameAssembly 及 Player
   内置 Base MetaVersion 的 hash 未变化。MetaVersion 集合相同但 runtime/native 身份不同的
   Player 不会再被误判为歧义。
   `resource-update-plan-integrity-v1`、`stable-method-identity-v1`、`resource-update-aot-metadata-set-selection-v1` 和可选的
   `resource-update-aot-metadata-path-v1` 必须参与 capability/baseId；旧 Player 缺少任一必要
   能力时整个 Base 记录不兼容。
6. Player smoke 验证程序集集合、payload hash、changed/interpreter 路径、仍保留的 AOT
   路径以及失败事务回滚重试。no-op 更新必须证明解释器和 native changed 计数均为零，并
   实际校验四程序集基线结果、direct/reflection 能力和无解释器调度，不能只检查计数。
   changed smoke 完成后必须运行 `resource-player-evidence`，将资源 manifest、stage、Base
   identity/native manifest 和 Player result 绑定为发布证据；不得为了生成证据重新构建一个
   非 universal-guard 的 changed Player。
7. release gate 从原始 DLL、MV、runtime plan、native manifest、Player 和资源报告实时重算
   结果，不能只信任报告中的 `passed`。
8. archive gate 生成无绝对路径的可移植证据，保留 immutable native manifest 原始字节并在
   归档目录离线重跑 release 校验。

## 跨 target 与 metadata set

registry 中每个 Base 独立声明 `aotMetadataRoot`。`null` 只表示该 Player 的
`aotMetadataSetId` 是空集合 hash；非空 root 必须包含项目 `patchAOTAssemblies` 的完整
文件集合。这个选择会写入 `baseSelections`，不会把上一份 current 或其他 Base 的 metadata
写回 Player。相同 payload 可以因此服务不同 metadata set，但不能掩盖 managed DLL 的平台差异。

单 target/legacy 模式下 current DLL 按字节共享。若 Android、Windows 或 iOS 因条件编译
增加了不同方法/PInvoke/类型布局，不能把一个 target 的 DLL 伪装成另一个 target；可以在
同一资源发布中声明多个 `payloadVariants`，由 registry 的 `payloadVariantId` 把每个 Base
绑定到对应 DLL/MV 子目录。每个 variant 仍须分别通过 target 的 native/Player 门禁；
当前已有的三引擎共享 payload 证据均为 `StandaloneWindows64`，不能外推到 Android/iOS。

## JSON 契约

工具包内每种已登记 DHE `format` 都必须唯一映射到一个 schema。`schema-gate` 会拒绝未知
DHE format、未支持的 schema 断言关键字、额外属性、错误类型、越界数值和报告契约漂移；
它生成的 gate 报告也必须通过自身 schema。CI 在 Windows 和 macOS 上构建分发包后，使用
包内 host 重新执行 package、batch 和 regression schema 验证。

## 发布证据

工具包只能从 clean、tracked 的精确提交发布。`-Mode Release` 需要一份绑定同一 HEAD/tree
的 release evidence。证据由八个固定角色和可扩展的 changed Player 列表组成，所有文件 hash
必须正确且报告格式匹配：

- `regression`：全部生产负例和 package/schema 认证通过；
- `player-changed`：至少三份真实资源更新 Player 结果，Base ID 必须互不相同，覆盖 Unity 2021、
  Unity 2022、团结 2022，并由 `hybridclr.dhe-resource-player-workflow.json` 绑定回 immutable Base；
  可以继续增加其他在线 Base；同 target 可共用 default payload，metadata shape 不同的
  target 则绑定各自 variant，但所有记录必须来自同一份 manifest/validation。
- `demo-noop`：零变化 Player 路径通过；
- `native-tuanjie2022`：团结 2022 锁定 runtime、真实外部 headers、CMake/CTest 通过；
- `native-unity2022`：Unity 2022 锁定 runtime、真实外部 headers、CMake/CTest 通过；
- `native-unity2021`：Unity 2021 锁定 runtime、真实外部 headers、CMake/CTest 通过。

发布证据必须由 `release-evidence` 命令从 clean HEAD 自动生成，不能手工拼装后直接发布。
所有 managed Player 角色都必须来自 `Release` workflow，并绑定 integrated runtime、真实 headers
和 clean/tracked 项目源码；Exploratory 报告不能再提升工具包的 Release 位。Player 可以由上一份
已认证 Release 工具包执行，再由当前 clean host 独立重算证据，避免待发布工具包必须先认证自身
的循环依赖。

发布后的 `dhe-toolchain-manifest.json` 必须同时满足 `mode=Release`、
`releaseReady=true`、`sourceIdentity.clean=true`，并通过包外和包内两次
`verify-package -RequireRelease`。项目配置必须固定 `expectedToolchainPackageId`，禁止只按
目录名或版本号选择工具包。

## 项目试用

首次接入先运行 `Exploratory + StopAfterPreflight`，验证 adapter、全程序集 scope、package
lock 和 MV 兼容性。随后用 `Bootstrap + RunPlayer` 在目标平台构建并归档 Base。每次后续
更新把所有仍在线 Base 同时交给 `resource-update`，一次发布一个 manifest（可含多个
target variant）；不再执行
scripts-only/final Player。只有 Player、resource、release、archive 和 schema gate 全部通过，
才能把对应 Base 标记为该资源版本支持。

Android 和桌面证据不能替代 iOS。iOS 使用同一套 C# host/package/adapter，但仍必须在 macOS
安装对应 Editor module，并完成 Xcode、签名和设备 smoke。缺少该环境时只能声明源码与
Windows/Android lane 已通过，不能声明 iOS 已验证。

当前 `0.1.20` 候选已有 Unity `2021.3.45f2` Android ARM64 的两类探索性结果。旧归档
`dhe-u21-android-base-20260904-v4` 虽完成 4/4 preflight、26,287 个 universal guards
和 APK 构建，但其 identity 仍绑定非空 AOT metadata set，缺少对应 immutable root，不能
作为 no-metadata registry 条目。随后生成的
`dhe-u21-android-nometadata-base-20260905` 明确使用空 set
`e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`；以该 Base 的 current
做单 Base resource update 和 staging 已通过，payload 不含 AOT metadata，APK 保持不变。
由于没有 Android 设备，完整 workflow 在等待 `dhe-player-result.json` 处停止；Android
真机 correctness、PSS/RSS、温度和弱核门禁仍未完成，iOS/Xcode/device 也仍未验证。

## 回滚

工具链回滚通过固定上一份 package ID 和 package commit 完成；runtime 回滚通过上一组
annotated tag 和 runtime manifest 完成。项目若暂时退出 DHE，应清除 DHE runtime plan
assets、恢复原 hot-update 加载路径并重新构建基础包，不能让旧 DHE payload 混入普通构建。
