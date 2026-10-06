# AOT 启动选择与解释执行隔离 v2 验证结果

2026-10-06。Windows Unity 2022.3.62f3 的核心链路有条件通过：普通 AOT 选择一次模式，
同一个 Player 使用同一份 Current DLL，分别走 DHE 或标准 Assembly.Load 解释执行。
完整兜底需求尚未完成，`meetsFullFallbackRequirement=false`、`mergeReady=false`。
剩余共享代码边界见 [审计清单](AOT-Isolation-v2-Audit.md)，接入方式见
[接口说明](AOT-Mode-Integration.md)。

当前身份的实测结果：

| 场景 | 结果 |
|---|---|
| 独立 opt3 传统 Player、同包 DHE、同包传统解释 | 每组 19 项，均与 CLR reference 一致，differential=0 |
| 传统模式：加载前开启 83 个已绑定 DHE 实现的故障注入 | 19 项通过；DHE 实现调用数 0 |
| DHE 模式故障对照 | 实际进入实现后按预期抛出注入异常，未被误算为 workload 通过 |
| 同包 DHE 正常调度 | 37 次 AOT 入口、2 次解释器入口；Changed 被选中，Unchanged 未被选中 |
| 12 个并发选择请求 | 1 次成功、11 次 AlreadySelected |
| 无效模式、重复选择、选择前加载、错用加载入口 | 均拒绝；已选模式保持不变 |
| 损坏 DHE Base MV 后选择传统模式 | 19 项通过，传统加载不依赖 DHE MV |
| 程序集名称、类型身份、第二解释程序集引用 | 身份一致，没有暴露第二份 Base 热更程序集 |
| 构建依赖检查 | 7 项通过，包含上轮真实静态引用反例，以及 Unity 组件、ScriptableObject、启动回调拒绝 |
| Unity2022 真实 headers，选择能力开启与关闭 | 两组 native compile、CTest 均通过；没有替代 headers |

Player 门禁用了 7 个独立 PID，属于 correctness/fault-injection 验证，不是性能采样。
覆盖字段/属性/特性反射、泛型、interface、delegate、event、数组、装箱、cctor、异常、finally、
跨程序集调用及新增类型/方法。Native CTest 验证普通指令/FGS 单元，启动功能的证据来自
真实 Player；不能用其替代 Player correctness。

候选分支均为 `research/aot-mode-selection-v1`：

| 仓库 | 当前验证提交 | 改动责任 |
|---|---|---|
| HybridCLR | `4c42c36743a381d084c3d0cb59fe912a6f1d27e9` | 单次函数表绑定、83 个扩展入口、传统语义 fallback、加载入口检查 |
| il2cpp_plus Unity2022 | `9d3e7813bd6107e147709aca21e556b59d9560ea` | 配置程序集延迟公开、模式选择、生成代码 metadata 访问保护 |
| hybridclr_unity | `4b706f5ae3cc2fbb332b4739feae0a5b6a07fd67` | 两个托管 API、构建配置与依赖拒绝检查；候选迁移身份，无 package tag |
| lab 构建与测试源码 | `e2fb63b8e2309670513d90686678162dfe292107` | 构建、19 项 workload、故障/并发/依赖门禁与显式 fallback 清单 |

可复查证据固定在 `reports/aot-isolation-4c42c36/`，`evidence-lock.json` 记录源码 tree 和
报告 SHA-256。二进制及构建日志在本地 `artifacts/as4/`。

- Current DLL SHA-256：`451cd9f9604569fc692a37e1b7e613d73ca32e58c56aaf805626ea99b48aa39d`
- Consumer DLL SHA-256：`cf2035d568e5f49aea895642693bb6b9638baf275ffefc8fd39e5f591d2975ad`
- 双模式 GameAssembly SHA-256：`1599cbfc8964a492bb43a9bd808bb4349231f03442a8ed9b3ddbe4a55ec630b9`
- 独立传统 GameAssembly SHA-256：`c7a83af697f7974586f0653b25d76ba1323b7581c36fb442cd3d1abdee113ff4`

早先 `artifacts/as`、`as2`、`as3` 只作历史定位证据。`as2` 曾在进入 AOT 前的 native
泛型表初始化中被过早的类型构造检查拦住；最终候选改为保护生成代码的 metadata 发布，
允许引擎准备内部描述符。最终报告没有复用早先候选的通过数据。

本次只验证 Windows、Unity2022、OptimizeSize，diagnostics 开启，ordinary AOT guards
关闭，无补充 AOT metadata，空启动场景。三引擎矩阵尚未通过；Unity2021、团结 2022、
Android ARM64、WebGL、iOS 均未获得本次身份的 Player 证据。真实资源/序列化/组件、
补充 AOT metadata、普通 AOT guard、完整共享代码隔离、并发加载/GC、性能与内存仍需门禁。
尤其不能把函数表 acquire 和间接调用当作零成本；没有吞吐、尾延迟或内存收益声明。

正式维护线保持原身份，未合入、未推送、未创建新 tag：HybridCLR
`optimize/v8.13.0` / `9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071` / `v8.13.0-opt8`；
il2cpp_plus `optimize/unity2022-v8.11.0` / `e426adc57c283865126423b169051558b339388c` /
`v2022-8.11.0-opt8`；package `optimize/v8.13.0` / `f2946d5ba35a879724b76afd857176feb1a4adca`。
未升级上游基线。没有修改游戏项目 `C:/mofish_cat_worker3/cat`。

回滚：候选未进入正式线，可以继续使用上面的三个正式身份重建 Base。撤销候选时应把
三个仓库作为组合撤销，关闭新增构建选项并重建 native/metadata；不能把不同构建的
GameAssembly 与 global-metadata.dat 混用。已支持此能力的 Base 可由项目保存下次启动
选择并重启。此次没有创建 stash，四个候选工作树在证据与文档提交后保持 clean。

重跑顺序（先从上述提交导出到新的输出目录）：

```powershell
./scripts/build-aot-isolation-v2.ps1 -Stage Prepare -OutputRoot artifacts/as-next
./scripts/build-aot-isolation-v2.ps1 -Stage DHE -OutputRoot artifacts/as-next
./scripts/build-aot-isolation-v2.ps1 -Stage LegacyInterpreter -OutputRoot artifacts/as-next
./scripts/build-aot-isolation-v2.ps1 -Stage Package -OutputRoot artifacts/as-next
./scripts/test-aot-isolation-v2.ps1 -PairRoot artifacts/as-next -ReportRoot reports/as-next
./scripts/test-aot-isolation-native.ps1 -BuildRoot artifacts/as-next
./scripts/test-aot-isolation-validator.ps1
```
