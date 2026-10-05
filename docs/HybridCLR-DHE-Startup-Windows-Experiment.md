# Windows IL2CPP 启动双路径流程实验

日期：2026-10-06（Asia/Shanghai）

结论：限定 Windows prototype 流程通过。不是全平台实现或正式 runtime 发布验收。

## 1. 实际实现边界

一个发行目录内保存两套独立的 IL2CPP Player profile。公共启动器在创建 Player 进程之前读取本地选择并验证所选 exe、GameAssembly、metadata 与共享 Current 的 SHA-256，再只启动其中一套。两份 Unity bootstrap exe 的 SHA-256 相同，GameAssembly 和 metadata 不同；实际启动路径仍为两个独立 Player 目录。

此阶段未实现“单 Unity 可执行文件内绑定两套 GameAssembly”。选择是进程创建前的 profile 选择；只有所选后端的类型系统和 heap 被初始化。Windows pre-Unity launcher 需要已安装的 .NET 6，这是实验工具依赖，不是已决定的正式产品分发方式。

Player 中的 `HybridStartup` prototype 提供 EffectiveMode、RequestNextStartupModeAsync 和 ClearNextStartupModeAsync。EffectiveMode 来自编译时 profile，不接受运行中切换。下一次选择使用共同 StartupStore，由 launcher 与 AOT Player 共同读写；HTTP、下载和重启 UI 没有纳入实验。

StartupStore 不依赖 Unity、DHE 或 Current；Windows 原生命名 Mutex 串行化存储操作，临时文件 flush 后原子替换。试验记录为固定 88 bytes，含 magic、version、mode、generation、scope digest 和 checksum，未将其固化为跨平台发布协议。Windows async 外观目前同步完成，不等同于最终 busy/status DTO 和 WebGL 异步 adapter。

## 2. 冻结源码和身份

Fixture branch：`research/dhe-startup-fallback-v1`。冻结并重跑最终流程的提交：`115bc81c9663d64c234e486b1924ea2986c3fc30`。

| 输入 | DHE | 传统解释 |
|---|---|---|
| HybridCLR source commit | 9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071 | f40c6f08ccd0391ad9285276b4cc21ada3a180ab |
| IL2CPP source commit | e426adc57c283865126423b169051558b339388c | bf15337e189ae7da5876aa51c9b896a36c52a155 |
| Package source commit | f2946d5ba35a879724b76afd857176feb1a4adca | ac0fdc5c6363a1b6323d017e068c536dd22127dc |
| 引用 runtime tag | v8.13.0-opt8 / v2022-8.11.0-opt8 | v8.13.0-opt3 / v2022-8.11.0-opt3 |

以上都是既有身份，未新建或移动 tag。DHE 来源为当前正式维护线；传统端从既有非 DHE opt3 checkpoint 归档源码独立构建，已确认其锁定 upstream 祖先。Package 只引用 commit，没有为本实验创建 package tag。

原正式分支保持不变：HybridCLR 为 `optimize/v8.13.0`，IL2CPP 为 `optimize/unity2022-v8.11.0`，package 为 `optimize/v8.13.0`，HEAD 分别为上表 DHE 列的 commit。Package 项目迁移来源为相应列的归档 commit，不使用 package opt tag。原 lab 正式分支为 `optimize/assembly-load-metadata-lab-v8.13.0`，HEAD `09436e96df0be3ed8f6b26ec935bfe549ec5a590`，本实验未更新其发布锁。

Editor 为 Unity 2022.3.62f3，target StandaloneWindows64，IL2CPP OptimizeSize，两 profile 均使用对应项目本地安装的 runtime，未修改全局 Editor runtime 或用户游戏工程。

Current SHA-256：`bbc6f6204fcb8659edefc058358a83bf50198e7fa985357e5433f64ae02cab51`。

DHE GameAssembly SHA-256：`0c50c45cb0de555ea6ba87545d68f1b6df0c879d06f69e3d9253741547483800`。

传统 GameAssembly SHA-256：`c5d78c19aaacbcb37bc20cd595a85c46945bf65e0fe0e2c16f7521d6217a8c27`。

PairManifest SHA-256：`5c532efba3ecb5e523d3e9d31659f24f93e1e27224b05d58ea504b0f559740b3`。原始 final summary 包含两 profile 的 exe/metadata/native SHA、fixture 文件 SHA 和源码身份；CLR reference 对实际加载的 Current bytes 同样计算 hash。

## 3. 最终验证结果

| 流程 | 结果 |
|---|---|
| 无记录默认 DHE，Player 请求下一次传统解释 | 当前仍 DHE，restartRequired=true |
| 全新进程启动 | 进入传统解释，10 cases 与 CLR 一致 |
| 再启动一次 | 传统选择持续保留，不自动清除 |
| 传统 Player 请求下一次 DHE | 当前仍传统解释 |
| 全新 DHE 进程注入坏 MV，失败后请求下一次传统解释 | DHE 按预期拒绝，nativeLoadCode=11；仍能保存下一次选择 |
| 在共享资源目录持续保留坏 DHE Base MV 后重新启动传统端 | 相同 Current 正常执行，10 cases differential=0 |
| 传统 Player 清除下一次选择 | 本次仍传统解释，下次返回默认 DHE |
| 清除后的全新进程 | DHE 正常执行，10 cases differential=0 |
| 本地选择记录损坏 | launcher 返回拒绝，未产生 Player report、没有隐式回到 DHE |

8 个实际 IL2CPP Player 进程 PID 唯一，其中 7 个执行完整 workload，1 个是预期 DHE 拒绝；坏记录测试在启动前结束。正常 workload 的 CLR reference caseCount=10，所有正常 Player caseCount=10，differential=0，Current hash 一致。

Workload 覆盖：修改方法、未修改方法、interface/virtual 调用、delegate、值类型参数、static field、异常捕获、新增方法、泛型方法和新增类型。DHE 诊断确认 Changed 被选为解释、Unchanged 未被选中，且存在实际 interpreter/AOT entries；传统端确认加载前不存在 StartupHotfix AOT assembly，随后通过普通 Assembly.Load 创建解释程序集。

首个探索运行暴露 `System.Threading.Mutex(bool,string)` 在 IL2CPP 中抛 NotSupportedException。改成 Windows 原生 CreateMutexW / WaitForSingleObject / ReleaseMutex / CloseHandle adapter，重新编译两 Player 后流程通过；探索报告不作为最终身份结果。最终门禁在冻结代码提交后重跑。

## 4. 复现入口

在本候选 lab worktree 根目录运行。Prepare 要求 OutputRoot 不存在，避免覆盖来源不明内容；需要同版本 Editor、Windows IL2CPP/VC 工具链和 .NET SDK。

```powershell
./scripts/build-dhe-startup-windows.ps1 -WorkspaceRoot C:/hybridclr_optimize -Stage Prepare
./scripts/build-dhe-startup-windows.ps1 -WorkspaceRoot C:/hybridclr_optimize -Stage DHE
./scripts/build-dhe-startup-windows.ps1 -WorkspaceRoot C:/hybridclr_optimize -Stage LegacyInterpreter
./scripts/build-dhe-startup-windows.ps1 -WorkspaceRoot C:/hybridclr_optimize -Stage Package
./scripts/test-dhe-startup-windows.ps1 -ReportRoot reports/startup-windows-new-run
```

若输出目录已存在，用新的 OutputRoot，并将同一路径传给后续阶段及测试的 PairRoot。测试要求 ReportRoot 为新目录；只在该目录维护实验选择记录。故障测试暂时修改该 PairRoot 中的 DHE Base MV，finally 恢复它，不修改 Current，不使用用户设置或启动用户 Player。

保留产物在 `artifacts/startup-windows-v1`。最终原始结果在 `reports/startup-windows-115bc81`；所有 run/report/identity 都是本次 Windows 实验产物，不借历史 Android/Windows evidence 充当结果。

## 5. 适用范围、剩余门禁及回滚

- 仅证明 Windows x64、双独立 Player、10-case fixture、当前选择不可变与下次选择生效；不证明单 executable 的 runtime API 绑定。
- DHE 是诊断构建，EnableDispatchDiagnostics=true、GuardOrdinaryAotMethods=false。这个限制仅用于不含 native hotfix 布局依赖的 managed fixture，不可宣称生产等价 DHE、全部 ABI 或布局演进正确。
- 没有运行三引擎 native compile/CTest 矩阵、ARM64 并发/内存门禁、Android/WebGL/iOS 接入、完整 Unity 场景/AssetBundle/序列化兼容或全部 DHE 特性测试。
- 没有性能或内存声明；8 次进程验证不能用于 P99，不能从这两个最小 Player DLL 的大小推断真实项目包体增量。
- 坏 MV 证明传统端不依赖该 DHE 输入，不代表已穷尽所有 native crash 或公共启动控制层故障。
- 最终 API 的存储错误 DTO、busy/cancellation、并发压力和各平台持久性仍须补齐；本实验不宣称 SDK 接口已正式交付。

后续 Windows 研究可继续在当前独立 worktree 扩大 workload，或验证相同 Unity stub 下的后端入口接入；跨平台仍分别验收。不以这次成功弱化传统端独立性要求。

回滚流程代码：撤销候选提交并重建实验产物；正式 runtime/package 未改动。运营式回滚实验：调用下一次选择接口保存 LegacyInterpreter，退出当前 Player，再由同一 launcher 启动新进程。源码恢复不改变某个仍运行进程的模式。

相关正式仓库和候选 worktree 在交付时 clean；没有 stash、没有 push、没有新 runtime/package tag。报告提交只添加文档与原始证据，源码冻结身份保持上述 fixtureCommit。
