# DHE 生产工作流审查

本文定义 opt4 DHE 工作流进入项目试用和正式发布时必须满足的契约。实现由跨平台
.NET host、HybridCLR Unity package API、项目 C# adapter 和 opt4 runtime 组成；项目只
拥有资源系统、签名和设备 smoke，MV、native guard、身份校验、归档和发布门禁由公共工具
统一执行。

## 适用边界

DHE 将全部已配置 hot-update 程序集同时编入 Player AOT。更新时只允许 method-body-only
差分：发生变化的方法注册到解释器，未变化方法继续执行 Player 中的 AOT 代码。以下变化会
在生成 Player 前被拒绝：

- 程序集、模块、类型、字段、属性、事件或资源布局变化；
- 方法、参数、泛型约束、override、P/Invoke 或安全元数据变化；
- token 集、异常处理边界、locals、switch target 或自定义属性契约漂移；
- native resolver 无法覆盖的 changed token 或 ABI 形状。

被拒绝的更新必须重新发布基础包，不能降级为未验证的普通热更。项目可通过关闭
`dheAotAssemblies` 回到原 HybridCLR 路径；已发布的 baseline、MV 和 runtime payload 不得
跨 package/runtime 身份复用。

## 锁定身份

当前组合由以下不可变输入组成：

- HybridCLR runtime `v8.13.0-opt4`；
- HybridCLR Unity package 分支 `optimize/v8.13.0`，精确 commit 记录在
  `manifests/repo-lock.json`；
- 团结引擎 `1.10.0` / Unity compatibility `2022.3.62t12`；
- `manifests/dhe-runtime-lock.json`、`dhe-package-lock.json` 和工具包 manifest 中的
  commit、tree、文件集合及 SHA-256。

Release preflight 会实时重算上述身份。dirty、mixed SVN revision、surrogate headers、错误
Editor 版本、未登记 package 文件或 runtime tree 漂移都会失败。

## 标准工作流

1. `Prepare` 在 current-generation phase 生成当前 stripped AOT，并从上一版本复制完整
   baseline 集合。
2. host 对全部 `dheAotAssemblies` 生成 MV JSON/binary，要求其集合严格等于
   `hotUpdateAssemblies`。
3. `StageRuntimePlan` 向运行时资源写入 current、MV、snapshot 和逐文件 SHA-256，同时绑定
   AOT metadata manifest；完整 baseline 只保留在 workflow handoff 中供独立审计，不进入资源包。
4. scripts-only Player 生成干净 C++；package 解析所有 changed token、注入 native guard，
   并生成完整 build identity。
5. final Player 编译该 identity；第二次 native finalize 必须证明 guard 和 immutable native
   manifest 没有漂移。`guard-block-set-v1` 只认证 manifest 声明的完整 begin/end guard 块，
   不把同一 C++ 文件中由 build identity 引起的无关变化计入 guard 身份；缺失、重复或内容不符
   的块必须失败。项目 adapter 在 Player 和 smoke 完成后必须在 `finally` 中恢复临时 build
   identity 源码模板，成功和异常路径都不得污染工作区。
6. Player smoke 验证程序集集合、payload hash、changed/interpreter 路径、unchanged/AOT
   路径以及失败事务回滚重试。no-op 更新必须证明解释器和 native changed 计数均为零，并
   实际校验四程序集基线结果、direct/reflection 能力和无解释器调度，不能只检查计数。
7. release gate 从原始 DLL、MV、runtime plan、native manifest、Player 和资源报告实时重算
   结果，不能只信任报告中的 `passed`。
8. archive gate 生成无绝对路径的可移植证据，保留 immutable native manifest 原始字节并在
   归档目录离线重跑 release 校验。

## JSON 契约

工具包内每种已登记 DHE `format` 都必须唯一映射到一个 schema。`schema-gate` 会拒绝未知
DHE format、未支持的 schema 断言关键字、额外属性、错误类型、越界数值和报告契约漂移；
它生成的 gate 报告也必须通过自身 schema。CI 在 Windows 和 macOS 上构建分发包后，使用
包内 host 重新执行 package、batch 和 regression schema 验证。

## 发布证据

工具包只能从 clean、tracked 的精确提交发布。`-Mode Release` 需要一份绑定同一 HEAD/tree
的 release evidence，并且以下六个角色各出现一次、文件 hash 正确且报告格式匹配：

- `regression`：全部生产负例和 package/schema 认证通过；
- `demo-changed`：真实 changed-method Player 路径通过；
- `demo-noop`：零变化 Player 路径通过；
- `native-tuanjie2022`：团结 2022 锁定 runtime、真实外部 headers、CMake/CTest 通过；
- `native-unity2022`：Unity 2022 锁定 runtime、真实外部 headers、CMake/CTest 通过；
- `native-unity2021`：Unity 2021 锁定 runtime、真实外部 headers、CMake/CTest 通过。

六角色证据必须由 `release-evidence` 命令从 clean HEAD 自动生成，不能手工拼装后直接发布。

发布后的 `dhe-toolchain-manifest.json` 必须同时满足 `mode=Release`、
`releaseReady=true`、`sourceIdentity.clean=true`，并通过包外和包内两次
`verify-package -RequireRelease`。项目配置必须固定 `expectedToolchainPackageId`，禁止只按
目录名或版本号选择工具包。

## 项目试用

首次接入先运行 `Exploratory + StopAfterPreflight`，验证 adapter、全程序集 scope、package
lock 和 MV 兼容性。随后在目标平台运行完整 Player；只有 Player、resource、release、archive
和 schema gate 全部通过，才能把本轮 baseline 作为下一次更新输入。

Android 和桌面证据不能替代 iOS。iOS 使用同一套 C# host/package/adapter，但仍必须在 macOS
安装对应 Editor module，并完成 Xcode、签名和设备 smoke。缺少该环境时只能声明源码与
Windows/Android lane 已通过，不能声明 iOS 已验证。

## 回滚

工具链回滚通过固定上一份 package ID 和 package commit 完成；runtime 回滚通过上一组
annotated tag 和 runtime manifest 完成。项目若暂时退出 DHE，应清除 DHE runtime plan
assets、恢复原 hot-update 加载路径并重新构建基础包，不能让旧 DHE payload 混入普通构建。
