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
  audit/dhe-base-registry.json (registry-backed releases)

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
  -AotMetadataRoots <base-v1-aot-root>,<base-v2-aot-root>,<base-v3-aot-root> \
  -SettingsFile <ProjectSettings/HybridCLRSettings.asset> \
  -OutputRoot <resource-release-output>
```

四组 Base 参数必须一一对应。需要补充 AOT metadata 时传入 `-AotMetadataRoots`，工具会按
`patchAOTAssemblies` 从每个 root 完整收集，并按内容寻址跨 set 去重；单数
`-AotMetadataRoot` 仅是所有 Base 共用同一 root 时的 shorthand。只有
`patchAOTAssemblies` 为空时才允许省略 metadata roots。命令先
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

### 连续资源更新

Base registry 中保存的是每个主包首次发布时的 immutable Base 身份和 Base MetaVersion，
不是上一次热更的 current。玩家领取第一份资源后，下一份资源仍然用同一个 registry
重新从所有历史 Base 求差；不需要、也不能把上一份 current 写回 Player 的
`BaseMetaVersion`。因此同一个 Player 可以依次 staging `resource-update-N`、
`resource-update-N+1`，每次只替换 current DLL/MetaVersion、AOT metadata 和 manifest，
而内置 Base MetaVersion、Player executable、`GameAssembly` 和 native identity 保持不变。

每个 current payload 都必须独立通过 registry 中全部仍在线 Base 的兼容性审计。若新版本
只改变了某个已经在上一版本中变化的方法，MetaVersion 仍按 stable method identity 对原始
Base 求差；这保证老 Base 不会因为漏领中间资源或跨版本安装而失去可更新能力。只有重新
构建并发布新的 Base Player 时，才新增 registry 条目并从该 Player 归档新的 Base
MetaVersion、native manifest 和 AOT metadata set。

回归门禁可以在同一份 staging 根上验证连续资源更新。除了首个更新根外，传入
`-ResourceUpdateRoot2 <resource-update-N+1>`；host 会先 staging `resource-update-N`，再
用同一份 Base identity staging 第二份 payload，并要求两次选择的 Base/AOT identity、
原始 Base MetaVersion 集合和 tree hash 完全一致，同时要求两份 current assembly set
确实不同。该检查只验证资源替换，不会把第一份 current 写入 Base 归档。

线上必须维护“仍可领取当前资源版本”的 Base registry。每次发布都把 registry 中所有 Base
传给 `resource-update`，不能为了让门禁通过而漏掉老 Base。某个 Base 不兼容时只有三种选择：
收窄本次代码变化、停止向该 Base 发布并要求玩家升级主包、或者发布带新 runtime 的 Base；
不存在未经验证仍强行下发的第四条路径。

registry 使用 `hybridclr.dhe-base-registry.json` schema v1。每个条目绑定一个
`baseId`、`engineWorkflow`、baseline DLL 根目录、universal native manifest、完整
`build-identity.json`，以及可选的 `patchAOTAssemblies` 根目录；路径可以采用
`registry-relative-v1`，便于把 registry 与 Base 归档一起迁移。`resource-update`
会按 registry 条目顺序读取并重新计算所有身份，拒绝重复 Base、错误引擎、路径缺失、
identity/baseId 不一致或 registry 与旧式并行参数混用，并将 registry SHA-256 和 entry
count、原始 registry 的精确副本 `audit/dhe-base-registry.json` 及其 SHA-256
写入资源输出。消费侧会在 staging 前校验该副本的 hash、格式、Base 数量和唯一身份；
缺失或篡改会整体拒绝。新增线上 Base 只需先归档并加入 registry，下一次资源构建会将它
与所有旧 Base 一起审计。

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
`resource-player-workflow-report.json`。0.1.20 起以可扩展的 `player-changed` 列表记录所有
代表性 Base，至少覆盖 Unity 2021、Unity 2022、团结 2022 三个不同 Base；所有记录必须绑定
同一个 current manifest、validation 和 assembly set。不再要求、也不允许通过重建非
universal-guard 的
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

所有资源更新都要求 `resource-update-plan-integrity-v1`、`stable-method-identity-v1` 和
`resource-update-aot-metadata-set-selection-v1`；携带非空补充 AOT metadata 时还要求
`resource-update-aot-metadata-path-v1`。AOT metadata 按内容 SHA-256 去重；文件名使用 SHA-256
前 128 位作为短查找键，以避免长工作树路径触发 Windows MAX_PATH，完整 SHA-256 仍写入并校验，
跨 Base set 复用相同 blob，
runtime plan 用 `baseSelections` 将每个 `baseId` 映射到唯一 set。metadata set 身份参与
BuildIdentity/baseId 计算，因此升级 managed runtime 后旧 Player 不会被误认为可安全消费新
plan。缺少能力的 Base 必须停止领取该资源版本或升级主包。

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

identity 1 的同一 structural current payload 已在 Unity 2021、Unity 2022 和团结 2022 的
三个不同 Base Player 上完成本地 MetaVersion 求差。current set 为
`4ca7b5a1c90cfccb4fb9e6d1eb0eb2fee133e4bb0e22c84ec2f36e0dc788229f`，对应 Base ID 为：

- Unity 2021：`56f1b4cb3081e7af05518241635b7fc57274c7b6a472f303fa992542cde03db8`；
- Unity 2022：`c047d8309159e3ec4474dda51d378739b6ca63848200adee76d570a3ab13519a`；
- 团结 2022：`0e751fbfa0ebaecd8cf5d1ae05a1100ba4506a261acc18a3d9baf177e208a9b5`。

三个 Player 都识别 43 个 changed/new 方法，并记录 12 次解释器入口和 35 次 AOT 入口；
多程序集、结构演进、dispatch probe、事务回滚和同进程重试均通过。统一资源 manifest
`0e16b4d67d9b8906245b74dc399d4b0197f82e9c4292eb3a55de7d7e6b6abba5` 包含 3 个按 Base
选择的 AOT metadata set，跨 set 内容寻址后去重为 10 个 blob。三个 staging 均保持 Base
MetaVersion、Player executable、`GameAssembly` 和引擎 Player DLL 不变。plan/payload、
metadata set、错误 Base、废弃 sidecar 残留和缺 capability 等负例均 fail closed。

这些 Player 和资源报告仍是 exploratory evidence，三个
`resource-player-workflow-report.json` 的 `releaseReady` 均为 `false`，不能输入正式
`release-evidence`。它们只证明当前 Windows Player 下的三 Base 共享 payload 链路。

这套兼容只从首个采用 identity 1/runtime protocol v1 的正式 Base 开始。已经发布的旧 runtime
不会因下载资源而获得新的 DHE runtime 能力；runtime 自身、IL2CPP ABI、native plugin 或
平台层缺陷仍必须通过新 Base Player 修复。

这证明“Base 只构建一次、后续一份资源包服务多个 Base”的架构链路成立，但不等于官方旗舰版
DHE 的全部元数据能力。当前候选源码使用 HybridCLR commit `fe3b1ed`、package commit
`6b2444c` 和三条已锁定的 il2cpp_plus `opt4.1` 维护线；本轮尚未创建新的 runtime tag，也未
合入正式 package 维护分支。Android Player、iOS/Xcode/device、性能、内存和现有结构限制仍是
正式发布前的独立门禁；当前结论只能是 Windows Player 与三引擎 native 有条件通过，不能声明
全平台生产发布完成。
