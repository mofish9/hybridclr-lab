# DHE 固定 Base Player 与单资源包设计

本方案的线上目标不是“为每个 Base 生成一份差分包”，而是：每个 Base Player 只构建
一次，后续每次发布只有一份 current hotfix DLL/MetaVersion 资源。不同 Base Player 使用自身内置
的 Base MetaVersion 与同一份 current MetaVersion 在本地求差。

```text
Base Player（每个平台、每个商店版本各构建一次）
  hotUpdateAssemblies 全量进入 IL2CPP AOT
  Base MetaVersion 内置到 Player，不上传到热更包
  universal guard 覆盖既有可变 AOT 方法入口
  BuildIdentity 1 固化 Base DLL/MetaVersion/native guard/runtime 身份

同一份资源更新（每次代码发布只生成一次）
  payload/<assembly>.dll.bytes
  payload/<assembly>.mv.bytes
  dhe-runtime-plan.json
  dhe-resource-update.json
  dhe-resource-update-validation.json

客户端
  内置 Base MetaVersion + 远端 current MetaVersion -> 本地 changed/new 集合
  unchanged 既有方法 -> Player AOT
  changed/new 元数据 -> HybridCLR 解释器
```

MetaVersion 是 current 程序集的完整稳定身份表，不是针对某个 Base 预先计算的 patch。服务器保存
各线上 Base 的 DLL、BuildIdentity 和 native manifest，仅用于发布前证明同一 current 包对
每个受支持 Base 都安全；这些 Base 文件不会复制进远端 payload。

## Base Player

首次主包通过项目 `workflow -Bootstrap -RunPlayer` 构建。bootstrap 必须满足：

- `dheAotAssemblies` 与 `hotUpdateAssemblies` 完全相等，所有热更程序集进入 AOT；
- 为全部既有、可支持的方法生成 `universal` native guard；
- 每个 DHE 程序集的 Base MetaVersion 被写入 Player 的只读资源目录；
- `build-identity.json` 把 Base 程序集集合、Base MetaVersion 集合和 native manifest 绑定到最终
  Player；
- scripts-only 到 final 之间的临时 identity 源码在成功或失败后都恢复成零模板。

每个已上线的 Base 必须归档以下发布材料，供后续资源发布离线验证：完整 Base DLL 根目录、
`build-identity.json`、`dhe-native-manifest.json`、目标平台及 runtime/package 身份。删除这些
材料会失去为该 Base 证明后续更新兼容性的能力。

`baseId` 不是版本号或 Base DLL 集合 hash。identity 1 对 target、managed assembly set、
AOT snapshot、Base MetaVersion set、native guard/manifest、runtime protocol/contract/capabilities 和
两个资源根做规范化 SHA-256。两个 Player 即使 managed DLL 相同，只要 runtime 或 native
guard 不同，`baseId` 就不同。`managedAssemblySetSha256` 作为独立字段保留。
`runtimeContract` 是不可复用的 runtime 实现发布号；managed/native runtime 源码发生变化就
必须分配新值。不同 contract 能否接收同一更新由稳定 protocol 和 capability 子集决定，而不是
把两个实现版本伪装成同一个 Base。

## 单 current 包生成

先由项目现有 C# build pipeline 编译一套 current hotfix DLL，然后运行：

```text
dotnet HybridCLR.DheTool.dll resource-update \
  -CurrentRoot <current-dll-root> \
  -BaseRoots <base-v1-dll-root>,<base-v2-dll-root>,<base-v3-dll-root> \
  -BaseNativeManifests <base-v1-native.json>,<base-v2-native.json>,<base-v3-native.json> \
  -BaseBuildIdentities <base-v1-identity.json>,<base-v2-identity.json>,<base-v3-identity.json> \
  -AotMetadataRoot <stripped-aot-dll-root> \
  -SettingsFile <ProjectSettings/HybridCLRSettings.asset> \
  -OutputRoot <resource-release-output>
```

三个 Base 参数必须一一对应。需要补充 AOT metadata 时传入 `-AotMetadataRoot`，工具会按
`patchAOTAssemblies` 完整收集；已经通过无补充 metadata 门禁的项目可省略该参数。命令先
删除旧 manifest/runtime plan，随后：

1. 每个 current DLL 和 current MetaVersion 只写入 `payload/` 一次；
2. 可选的补充 AOT metadata 也只写入 `payload/` 一次并记录逐文件 SHA-256；
3. 逐 Base 重建 Base MetaVersion，检查 BuildIdentity、native guard 和结构兼容性；
4. 只把兼容性记录写入 `supportedBases`，不生成 Base 专属 DLL、MetaVersion 或目录；
5. 根据每个 Base 到 current 的真实变化推导 `requiredRuntimeCapabilities`；
6. 任一声明支持的 Base 不兼容时整体失败，并且不留下可发布 manifest；
7. 全部通过后才写 validation、runtime plan 和最终 manifest，manifest 通过
   `runtimePlanSha256` 绑定 plan 原始字节。

因此 CDN/资源系统发布的是一个目录（或由该目录制作的一个 bundle 集），不是按客户端版本
选择的多个差分变体。加入新的线上 Base 只会扩大发布前审计集合；current payload 字节仍然
相同。

线上必须维护“仍可领取当前资源版本”的 Base registry。每次发布都把 registry 中所有 Base
传给 `resource-update`，不能为了让门禁通过而漏掉老 Base。某个 Base 不兼容时只有三种选择：
收窄本次代码变化、停止向该 Base 发布并要求玩家升级主包、或者发布带新 runtime 的 Base；
不存在未经验证仍强行下发的第四条路径。

每个 Base 对应的资源/catalog 构建必须显式携带该 Player 归档的完整身份，而不是仅凭内置
MetaVersion 集合猜测 Base：

```text
dotnet HybridCLR.DheTool.dll stage-resource-update \
  -UpdateRoot <resource-release-output> \
  -AssetRoot <player-runtime-asset-staging-root> \
  -BaseBuildIdentity <that-player/build-identity.json> \
  -ImmutableFiles <player-binary>,<GameAssembly-binary> \
  -Output <resource-stage-report.json>
```

host 校验 identity schema/version、复合 `baseId` 和 identity 文件 SHA，按 `baseId` 精确选择
一个 `supportedBases` 记录，再逐程序集校验内置 Base MetaVersion。两个 Player 即使 Base
MetaVersion 集合相同，只要 native/runtime 身份不同，也会各自选择正确记录而不会产生歧义。
staging 同时校验 `runtimePlanSha256`、current DLL/MetaVersion 和每个补充 AOT metadata 的
SHA-256；任一文件缺失或篡改都拒绝整包，不留下可接受的部分 staging 报告。

真实 Player/device smoke 完成后，运行 `resource-player-evidence`，把该 Player result、上述
stage report、资源发布目录和归档的 Base `player-workflow-report.json` 绑定为
`resource-player-workflow-report.json`。工具链发布时的 `demo-changed` 角色只接受这份资源更新
证据，并额外要求第二个不同 Base 的 `demo-changed-base2` 证据；两者必须绑定同一个 current
manifest、validation 和 assembly set。不再要求、也不允许通过重建非 universal-guard 的
changed Player 来伪造线上流程。

## 客户端选择与本地求差

客户端不下载 Base MetaVersion，也不按版本号选择远端差分文件：

1. 从编译进 Player 的 `DheRuntimeIdentity` 读取 Base 程序集集合、Base MetaVersion 集合和 native
   manifest 身份；
2. 校验 manifest 与 validation 的 hash、current 程序集集合和 runtime protocol；
3. 在 `supportedBases` 中要求当前 Base 身份唯一匹配且 `unsupportedChangeCount == 0`；
4. 要求该记录的 `requiredRuntimeCapabilities` 是自身内置 capability 集合的子集；
5. 从 Player 内置目录读取每个程序集的 Base MetaVersion，从远端 payload 读取 current DLL/MetaVersion；
6. native runtime 以稳定 type/method ID 比较两份 MetaVersion，changed/new 进入解释器，unchanged
   继续使用 AOT；
7. 任一程序集注册失败时不发布部分状态；同一 current DLL 可在进程内重试，替换成另一 DLL
   则要求重启进程。

没有唯一匹配、validation 被篡改、Base MetaVersion 被替换、guard 不完整或存在不支持的结构变化时，
客户端拒绝新资源并继续使用上一份已验证资源。客户端版本号只能用于观测和灰度，不能代替
加密身份匹配。

所有资源更新都要求 `resource-update-plan-integrity-v1`；携带补充 AOT metadata 时还要求
`resource-update-aot-metadata-path-v1`。这两个能力参与 BuildIdentity/baseId 计算，因此升级
managed runtime 后旧 Player 不会被误认为可安全消费新 plan。缺少能力的 Base 必须停止领取
该资源版本或升级主包。

## 当前兼容能力

当前 `dhe-proven-safe-subset-v1` 已放行并通过真实 Player 验证：

- 修改既有方法体、常量和分支逻辑；
- 新增顶层普通类型或泛型类型，以及这些新类型的字段、构造器、属性和方法；
- 给既有非接口类型新增非虚 static/instance 方法；
- 给既有普通引用类型增加受约束的实例字段，通过 sidecar 保存并参与 GC；
- 给既有普通类型增加/删除受支持的静态字段，删除引用类型实例字段；
- 删除既有方法或类型、用旧签名 tombstone + 新方法实现签名替换；
- 新增 nested type，并正确保留 declaring type；
- 变更既有类型的逻辑 property/event，以及既有类型、字段和方法的 custom attribute；
- 编译器生成的 `<PrivateImplementationDetails>` 静态数据变化；
- 多程序集 current 更新，以及多个不同 Base Player 共用同一 current payload。

当前仍会 fail-closed：

- 修改既有值类型的实例布局，或删除其实例字段；
- 修改既有类型的继承关系、接口集合、class layout 或 vtable；
- 原地修改既有方法的泛型约束、override、P/Invoke 或非 custom-attribute 声明元数据；
- 给既有接口新增方法，给既有类型新增 virtual/abstract/PInvoke 方法；
- 给既有类型新增带 custom attribute 的方法；
- 给既有泛型类型增加实例字段，增加 ThreadStatic/RVA/pointer/byref 字段，或增加地址已被
  `ldflda` 取得的实例字段。

这些限制不是打包工作流问题，而是 AOT 对象布局、GC、vtable、反射和序列化的一致性问题。
在 sidecar/object-extension 及对应 GC root、反射、序列化语义完成以前，不能仅靠放宽离线
门禁宣称支持。

## 已有证据与剩余门禁

identity 1 的同一 structural current payload 已在两个不同 Base Player 上完成本地 MetaVersion
求差：current set 为
`808f854c3e2171fe2dd932aa7dd8fff4999faccc98c6698aa1cb26143e46f318`，两个 Base 分别识别
71 和 72 个 changed 方法，并同时通过解释器/AOT、结构演进、custom attribute、事务回滚
和重试。启用补充 metadata 时，两次 staging 都写入相同 8 个 current DLL/MetaVersion 和
4 个 AOT metadata payload；删除测试 Player 副本中的旧根目录 metadata 后仍通过，证明
runtime 按受哈希保护的 plan 从 `payload/` 加载。Base MetaVersion、Player executable 和
`GameAssembly` 未变化。plan/payload 篡改、漏文件、错误 Base、废弃 sidecar 残留和缺
capability 均会 fail closed。新能力 Player 的 Base ID 为
`fd72fadeff6b8bed9f03e7156866b21a54d68b72b32f6690111cbf0db8746eb8` 和
`1c468ef89d4ccb3f85b1ca66ff7e5ca494cb6d40836cdb3988c55226f27e3e8a`；旧身份缺少资源 plan
能力，资源生成阶段即被拒绝。

这套兼容只从首个采用 identity 1/runtime protocol v1 的正式 Base 开始。已经发布的旧 runtime
不会因下载资源而获得新的 DHE runtime 能力；runtime 自身、IL2CPP ABI、native plugin 或
平台层缺陷仍必须通过新 Base Player 修复。

这证明“Base 只构建一次、后续一份资源包服务多个 Base”的架构链路成立，但不等于官方旗舰版
DHE 的全部元数据能力。正式源码身份已收口到 `v8.13.0-opt4.1`、三条 il2cpp_plus
`opt4.1` runtime tag 和 package commit `c19e235`。Android Player、iOS/Xcode/device、性能、
内存和现有结构限制仍是正式发布前的独立门禁；当前结论只能是 Windows Player 与三引擎
native 有条件通过，不能声明全平台生产发布完成。
