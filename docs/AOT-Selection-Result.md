# 普通 AOT 入口选择 DHE／解释模式：实测结果

2026-10-06，Unity 2022.3.62f3 Windows x64 IL2CPP。

**结论：普通 AOT 入口成立，可以继续采用；仅延迟程序集公开的最小实现不满足完整的传统兜底要求。**
这是可复现的边界验证，`meetsFullFallbackRequirement=false`、`mergeReady=false`。
未否定更完整的“可选热更模块”设计，也没有把当前诊断代码作为正式 API。

## 已证实的路径

同一个候选 EXE / GameAssembly / metadata，直接由 Unity 正常启动，不经过之前的
原生 StartupPlayer host、双 DLL router、持久化服务或页面桥接。项目普通 AOT
`RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` 读取测试参数，在加载 Current 前
调用 internal call 选择一次。不同进程分别选择 DHE 和普通 Assembly.Load。

测试参数仅用于由同一 AOT 回调指定模式；没有在 native 启动前传入模式。
后续实际项目可以在这个 AOT 阶段用自己的 C# 配置逻辑，不需要为“取得选择值”
分别编写 Android/iOS/Windows 原生入口。但这不是所有平台运行时都已验收的声明。

| 场景 | 结果 |
|---|---|
| 独立非 DHE opt3 Player | 11 cases，与 CLR reference 一致 |
| AOT 选择 DHE | 11 cases，一致；changed 解释、unchanged AOT，观察到两类执行 |
| 同一个候选 Player，AOT 选择普通解释 | 11 cases，一致；名称查找和第二热更程序集使用同一个 Current 类型 |
| 选择前强制 AOT 直接取 Base 类型 | 取得了隐藏 Base 类型；选择解释后，Base 与 Current 类型不同 |
| 解释模式的现有 DHE hook 故障注入 | 反射查询失败，确认仍依赖该 DHE hook 入口 |
| 12 个并发选择请求 | 仅一个成功，余下 11 个返回已选择；后续 workload 一致 |
| 损坏 DHE MV 后选择普通解释 | 11 cases，一致；这条路径没有读取 DHE MV |

共 7 个 PID 唯一的 Player 进程。其中 1 个为传统 reference，6 个使用完全相同的
候选 Player。6 个进程完成全部 11 cases，differential=0；故障注入进程按预期
中断 workload。`diagnosticTestsPassed=true` 表示诊断结果符合预期，不表示兜底验收通过。

原始结果：`reports/aot-selection-9aa514c/summary.json` 和同目录分场景 JSON。
Current SHA-256：`90e9cdf87a2397e56c50b40ac5f9c4241a10fd3612eac4f397bec29f24a8792e`。
第二热更 DLL SHA-256：`cf2035d568e5f49aea895642693bb6b9638baf275ffefc8fd39e5f591d2975ad`。
两种模式、独立传统 Player、CLR reference 使用同一份这两个 DLL。

## 两个实际缺口

第一，隐藏程序集没有消除生成代码的类型索引和直接方法调用。
反例中，普通 AOT 的 `typeof(StartupHotfix.Worker)` 在模式未选时仍取得 Base 类型。
选择解释并加载 Current 后，名字查询和跨程序集引用正确指向 Current，但旧引用仍在：
直接 AOT 方法返回 **105**，Current 的对应方法返回 **205**。

生成的 C++ 也说明原因：类型通过 `il2cpp_codegen_initialize_runtime_metadata`
解析静态类型句柄；方法直接调用 `Entry_Changed...`，没有经过程序集名称查找。
本次代码只延迟 `Assembly::Register` 和名字查找，没有拆分全局生成 metadata。
因此不把它称为“已完整延迟热更模块初始化”。

这个反例是刻意加入的 AOT→热更具体类型依赖。普通 HybridCLR 项目本来就应通过
稳定 AOT interface/DTO 等边界接入；若严格禁止这类依赖，反例可以在构建时拒绝。
它没有证明符合该边界的业务无法采用 AOT 入口。还必须检查场景、预加载对象、
RuntimeInitialize 回调、泛型和序列化是否在选择前接触 Base 热更类型，不能只扫描
一处 bootstrap 方法。

第二，普通解释加载不等于代码级绕开 DHE。未加载 DHE MV/差异计划时，本 fixture
的 workload 仍进入 `GetDheCurrentMethodMetadata` 117 次、
`GetDheReferenceAllocationClass` 33 次。它们正常情况下可以直接返回普通类型/方法；
**这些计数本身不表示发生了 DHE 类型映射，也不是发现了现有线上 bug。**
但在已存在的 DHE hook 入口注入明确的实验异常后，普通解释模式也失败了，证明
目前没有独立的旧行为入口足以隔离这类回归。

完整方案需要确定 DHE 专属代码和共同传统路径的边界，让解释模式进入被验证的
传统实现。不能仅凭“不注册 DHE 差异资源”声称已恢复原版逻辑。选择入口、故障
隔离和无持续模式检查是三项独立验收。

## 本轮到底改了什么

运行时代码只有 4 个文件、两个研究提交，共 66 行新增、1 行删除：

| 仓库 | 文件／用途 | 候选提交 |
|---|---|---|
| il2cpp_plus | MetadataCache.cpp/.h：只对 fixture 的 StartupHotfix 暂缓公开，提供一次性选择与查询 | ee3918e2dcd733b9043155059e5b9de88e4e1102 |
| hybridclr | RuntimeApi.cpp：注册实验 internal calls；MetadataModule.cpp：计数和故障注入 | 7237fa2a01cd4887ce7167b123835c103263ac26 |
| lab | fixture、构建／差分脚本、预先声明的验证契约 | 9aa514c7948e2f440a090ac2f867275faf091f34（证据对应的源码冻结） |

三者均为 `research/aot-mode-selection-v1`。lab 的后续文档/证据提交不是另一个
runtime 身份。package 没有新增改动；之前的 Windows router 候选也未改动。
这里的 `SelectLabAotMode` 和 hardcoded fixture 名仅用于验证，不可作为通用实现合并。

正式维护线仍为：

| 仓库 | 正式分支 | commit / runtime tag |
|---|---|---|
| hybridclr | optimize/v8.13.0 | 9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071 / v8.13.0-opt8 |
| il2cpp_plus Unity2022 | optimize/unity2022-v8.11.0 | e426adc57c283865126423b169051558b339388c / v2022-8.11.0-opt8 |
| hybridclr_unity | optimize/v8.13.0 | f2946d5ba35a879724b76afd857176feb1a4adca；无新 package tag |

独立传统 reference：HybridCLR f40c6f08ccd0391ad9285276b4cc21ada3a180ab、IL2CPP
bf15337e189ae7da5876aa51c9b896a36c52a155、package ac0fdc5c6363a1b6323d017e068c536dd22127dc。
上游基线没有升级，没有正式合入、推送或新 tag。源码和正式/研究工作树交付时 clean；
没有创建或改动用户 stash。回滚只需不使用这两个诊断 runtime 提交，回到表中的正式
opt8 配对重新构建；诊断模式不构成可交付的运营回滚能力。

## 复现与适用范围

在本 lab 工作树依次执行：

```powershell
./scripts/build-aot-selection-probe.ps1 -Stage Prepare
./scripts/build-aot-selection-probe.ps1 -Stage DHE
./scripts/build-aot-selection-probe.ps1 -Stage LegacyInterpreter
./scripts/build-aot-selection-probe.ps1 -Stage Package
./scripts/test-aot-selection-probe.ps1 -ReportRoot reports/aot-selection-new
```

重跑构建需新 OutputRoot；默认已保留本轮 `artifacts/as`。脚本从冻结提交导出
runtime 输入，未手工修改 Unity 生成 C++/Library 源码。最终 GameAssembly SHA：
候选 `3811b8125bf597e34d860ba8c418de876c7aa4a265e554420ac2480fdf002a54`，
独立传统 `02d9c5fc7a0a79d5e0e75e33ecd4cff39929e8f8efad6e9b748e1b2e38606704`。

已完成真实 Windows native Player 编译与托管 differential。未执行三引擎 native
CTest 矩阵，未验证 Android/WebGL/iOS。空场景、纯 managed workload，无补充 AOT
metadata；DHE ordinaryAotGuards=false、诊断插桩开启。选择的 store-release/query-acquire
和锁只覆盖本实验状态，不构成完整 metadata 事务发布证明。没有性能/P50/P95/P99/
内存结论，也不能推断 ARM64 正确性。旧双 Player、Windows router 的历史证据不计入。

后续正式设计应保留普通 AOT 入口，集中解决：可选模块的类型/静态引用隔离，以及
传统路径对 DHE 专属 hook 的隔离。平台原生配置接入已不再是这个方向的主要问题。
