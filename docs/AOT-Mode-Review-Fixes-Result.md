# Unity 2022 启动模式选择：review 修复结果

> 后续 review 新确认一项 opt8 已存在的原生类型枚举 P2；本报告中的旧两项修复仍有效，
> 定版结论以 [后续 review](AOT-Mode-Followup-Review-20261006.md) 和最新 candidate lock 为准。

本轮准备范围为 Unity 2022、Windows IL2CPP。用户明确要求先不发布；无打标、推送或安装清单切换。
此前全面 review 的 P1/P2 均已修复并在新身份产物上验证。当前在 Unity 2022 / Windows IL2CPP
范围内有条件通过，已达到本地新版本源码提交与准备完成状态；不代表其他平台生产资格或已经发布。
精确提交/冻结状态以 `manifests/aot-mode-opt9-candidate-lock.json` 为准。

## 实现边界

生产代码仅改动 HybridCLR 的 GetDheSupplementalImage：先保留 canonical Base 的直接返回，
再识别 Unity 的确切公开 image 别名。隐藏 Current interpreter image 被明确排除，不将其当成 Base overlay。
改动为 11 行新增、1 行删除，提交 `da9403a8c0f6421cd9afa45fdb234c867e0dac9f`。
没有新增模式开关检查、缓存、锁、计数或平台启动实现；IL2CPP 和 package 功能源码未再修改。

性能检查器现在要求完整的热点、启动、内存指标，并校验各组指标样本数和有限的 P50/P95/P99。
缺失报告数据直接失败，解释兜底的成本豁免不再有机会绕过这类完整性检查。
冻结时同时校验检查器 SHA、policy SHA 和 12 项正反测试，防止误沿用旧检查器的通过结果。

## 修复后的实际验证

新产物根目录为 E:/hclr/ar6，旧 ar5 失败复现和旧测量保持原样。

| 验证 | 结果 |
|---|---|
| Unity 2022 实际 headers，选择开/关 native compile + CTest | 均通过，没有 surrogate headers |
| 启动 correctness，6 个 profile | 全通过，包含非法/重复/12 并发选择、512 次错误 loader、image/native 查询和资源加载 |
| 220 项完整 managed 套件 | 候选两路、功能关闭、独立 opt3，各自在两种补充 metadata 配置下 220/220，差异 0 |
| opt8 负对照 | 仍准确复现原 2 个 P/Invoke 失败，不计入正对照通过 |
| 原生类型查询 | 所有 correctness profile 得到 mask=63，覆盖已有/新增普通类型、已有/新增 Unity 类型和 image 地址稳定性 |
| 同一份 Current bundles | 已有与新增 MonoBehaviour 的 prefab、additive scene，以及已有/新增 ScriptableObject 都通过，合计结果 1298 |
| Base 真实 Player 场景拒绝 | 新 runtime/IL2CPP/package 身份下仍正确拒绝 Base 的 deferred 对象 |
| package 资产 9 项与依赖 7 项 | 沿用相同 package da9eac3、相同测试/validator 文件的既有通过证据；未冒充重新运行 |
| hook 再生成 | 83 项（57 metadata、26 runtime），7 个相关文件再生成后无差异 |
| 性能检查器正反用例 | 12/12，涵盖空/缺失指标、缺百分位、样本不足、无效值、正常路径退化及兜底成本豁免 |

原生查询通过测试专用 Windows DLL 调用导出 API，不向生产库增加探针。
Player 在选择之前缓存 public image 地址，在加载之后查询 Current 新增类型并验证地址未变。
Current-only 资源在独立 Editor 项目中使用被冻结的 Current DLL 构建，避免错误地拿 Base DLL 生成测试 bundle。
失败复现中的 StartupHotfix DLL SHA 与 ar6 一致，修复后能解析同一个 AddedType。

完整套件的“无补充”仍只表示没有核心库/BoundaryContracts 的补充 metadata；启动 smoke 会补充 StartupAotSupport。

## 当前身份的性能

维持原门限：正常 DHE/功能关闭严格验收；解释兜底允许额外成本并披露。
数据来自新产物，不继承 ar5 的数字。所有样本保留，包括 Windows 重用数值 PID 的一次启动；
不同 PID 下界为 DHE 200/200、legacy 100/100、fixed 200/199，均满足各组至少 100。
新采样器记录父子 PID 和启动/退出时间。

| 比较 | 样本对数 | native P50 | changed P50 | virtual P50 | 进程到入口 P50 |
|---|---:|---:|---:|---:|---:|
| 正常 DHE / opt8 | 200 | -18.15% | +0.05% | +4.09% | -1.06% |
| 功能关闭 / opt8 | 200 | -15.68% | +0.64% | +0.18% | -0.51% |
| 解释兜底 / 独立 opt3 | 100 | -2.19% | -0.90% | +6.14% | +12.77% |

正常 DHE 的 virtual P95/P99 为 +2.91%/+2.54%，启动 P99 为 -1.99%，private bytes P50 增加 8192 B。
解释兜底启动 P50 增加 14.305 ms，virtual P50 增加 0.3053 ms，private bytes P50 增加约 3.97 MiB。
正常 DHE 和功能关闭的全部原硬门限通过；解释模式成本按用户已确认取舍披露。

这些是固定 workload 的比较，不表示整个游戏的收益，也不将 native 微基准变化归因于这 11 行修复。
构建/生成代码变化可能影响微基准的地址布局；本轮目的是消除 correctness 回归并确认性能没有超门限。
完整分位数、配对/非配对结果与 MAD 均在 performance-acceptance.json 引用的报告中。

## 再次 review

- 精确别名匹配避免接受任意共享 assembly 的隐藏 image；canonical 路径继续直接返回。
- 新逻辑没有改变选择的 metadata 锁、release/acquire 发布或进程不可变约束。
- 实际资源测试与原生查询覆盖了先前漏掉的 Current-only 类型，已有 Base 类型和解释路径同时通过。
- 同一份 Current DLL/bundles 用于两路；正常 DHE 运行循环无新增判断或诊断计数。
- checker 修复不更改性能门限，也不删除或改写历史失败样本。
- 对本轮新增/修改内容再审查后，未发现未解决的 P0/P1。Android/WebGL/iOS 的 Player/平台 ABI 资格仍未验证。

## 提交、接入及回滚

| 仓库 | 本地目标维护分支 | 功能源码 commit |
|---|---|---|
| HybridCLR | optimize/v8.13.0 | da9403a8c0f6421cd9afa45fdb234c867e0dac9f |
| il2cpp_plus | optimize/unity2022-v8.11.0 | 124f90294beb45a3c4cba8a525ac2488f82170f8 |
| package | optimize/v8.13.0 | da9eac383cd953aab58ad0a8c7d851db5e0a36dc |

库仍是普通 AOT 启动时 SelectExecutionMode/GetExecutionMode，选择早于热更加载及补充 AOT metadata。
项目负责配置持久化、HTTP 与下一次启动/重启。每次热更只需一份兼容 Current 业务资源，双模式回归是主要新增维护成本。
单独撤销 da9403a 会重新引入本轮 P1，不作为可用发布回滚；整体回滚仍为原 opt8 runtime/IL2CPP/package 组合并重建 Base。
运行期降级则由项目保存下一次 Interpreter 并重启。详见 AOT-Mode-Implementation.md。

本轮只完成版本准备。拟用 opt9 的名称与清单保持为提案，不创建 runtime tag，不创建 package tag，不访问远端推进发布。

上述功能源码已合入本地正式维护线；合入后使用实际 Unity 2022 headers 重新通过 selection 开/关
native compile + CTest、6-profile 启动及 8 个正对照各 220/220 的完整回归（另有 2 个 opt8 负对照）。
合入采用 fast-forward，runtime/package/Player 身份没有变化，性能沿用本轮 ar6 的精确身份测量。
测试脚本基于 lab `5045c4420aae8ccc65bd2c84fd512f315e8ddb75`；之后只有准备锁与本文的记录更新。
相关源码工作树 clean；没有新 stash，历史 FGS/opt3 stash 保留。

精确源码回滚组合：HybridCLR `9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071`，
il2cpp_plus `e426adc57c283865126423b169051558b339388c`，package `f2946d5ba35a879724b76afd857176feb1a4adca`，
需要重新构建 Base。当前 Base 的运行降级不需要换业务热更包，但仍需按项目流程重启。
