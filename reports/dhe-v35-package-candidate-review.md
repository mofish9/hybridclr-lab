# Unity 2022 DHE v35 package candidate

本轮结论：**有条件通过，作为 Unity 2022 项目验证候选；不是性能/真机生产发布结论。**
上游仍锁定 package / HybridCLR 8.13.0、IL2CPP v2022-8.11.0。
Unity 2021 和团结按用户要求延期。本轮没有推送、创建/移动 tag 或迁移 worker3。

## 冻结身份

| 仓库 | 候选分支 | 当前提交 | 正式维护线 / 已发布身份 |
|---|---|---|---|
| HybridCLR | optimize/dhe-release-runtime-v1 | 9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071 | optimize/v8.13.0；a4807e563c0cb245519a44c6ea7cc633246dd337；v8.13.0-opt7 |
| il2cpp_plus | optimize/dhe-performance-unity2022-v1 | a1ec0324a8a58cb8e175c7b86665d70b5afae57e，本轮未修改 | optimize/unity2022-v8.11.0；同提交；v2022-8.11.0-opt7 |
| package | optimize/dhe-release-package-v1 | 7a0ad49cf2efa60988f87a9195b0a97cfc806393 | optimize/v8.13.0；bf62316bafa6e2c84dbb922faace3af06ee3f6ca；不创建 package opt tag |
| Lab 验证源码 | optimize/dhe-release-lab-v1 | 23e4ba54b9c4e16155a96fa357f58a9a053ce509 | 本报告是后续文档提交，不重标验证源码身份 |

三个仓库均核实包含所批准上游 tag 的祖先；IL2CPP 未包含禁止的
11251b938d2ce7fa865165130bf257ca239db69f 祖先。
当前 Lab repo/runtime/package locks 已绑定以上组合。历史 opt6 trial lock 和历史报告保留原身份。

package Git tree：b714dfce8ba64f5179e78789fb18c4a4cd18efad。
canonical package tree SHA-256：3FCEDAD97094D2AB7BD9D0E8E40AA8B2C293893ADD96C7C81C6EE881F64DAA4C。
canonical HybridCLR source SHA-256：ED58C2795B8EE3FBCAB72CEBD10F820BBE8A68A3D3B4886EE9F7CF21548E12D9。

分发工具来自 clean source commit 127034ef70312168be60e2d3d5f39f519f2d60a4，
source tree 2b92c086908685d1efbe92814c65903341b39b1b，83 文件，
packageId 4cd360225b657b7ace949b239b5f1346c6fbc53b593d149fdb753f570700ebc6，
DLL SHA-256 78833BB9F1C0A9A6D90BA508A3589785857DA103CE523C8240C9A818A35D65BC。
保持 Mode=Exploratory / releaseReady=false，没有伪造完整 Release certification。

## 修复与整体审查

1. 空 MethodInfo 的直接 AOT guard：超过四个发布程序集时使用 immutable 名称索引和
   64-entry TLS cache，缓存 changed/unchanged/unknown 的正负结果。名称按内容比较且由缓存持有，
   不依赖字符串地址。publication identity 参与 key，后续发布使正负结果一起失效。
   <=4 保留有界短扫描，无变化发布仍提前退出。
2. resource-release-qualify 向底层 gate 透传现有 CLI ValidationSourceRoot，并归一化绝对路径。
   无参数/空白不添加该 key；matrix、historical roots、Player coverage 和身份参数保持原语义。
   没有增加配置 schema 字段或安装验证。
3. 构建器、工具准入和托管 runtime 的 contract 均为 dhe-runtime-v35，必须重建 Base。
   本轮 managed acceptance 测试曾发现 runtime 仍为 v34；已修复并在最终身份重新构建。
4. 整体复核保留泛型 closed context / physical ABI / 实际 receiver 判断。
   immutable state 与 guard index 在 release store 前完成，读方 acquire；索引值指向本快照，
   未读取写入中的共享 map。Metadata 字段 registry 仍在 mutex 下写完后 release 增加版本。
   dispatch backing 保持 process lifetime，TLS 逐出不会使返回的 InvokeData 引用失效。
5. InvokeBuffer 的栈缓冲属于每次调用，溢出使用原 vector 语义；不共用可重入 scratch。
   managed Current 仅在显式 validation probes 下 Clone，native 已拥有数据后释放冻结源字节；
   phase 3 初始化失败仍需要重启，未提前丢掉可重试输入。
6. 生产默认关闭 HYBRIDCLR_DHE_DIAGNOSTICS，guard / interpreter 热路径不执行原子计数；
   smoke 受编译宏控制。diagnostic scope 覆盖 native finalization 并恢复 Editor flags。
   安装由使用者负责；SourceLock、安装 receipts、dhe-runtime-release.json 未恢复。
   ordinary tool commands 不扫描整个 bundle，显式 Verify 才进行检查。

guard 是进入旧 AOT 方法时决定是否改走 Current 解释器的入口逻辑。
移除它会绕过方法和布局依赖的更新；其运行时成本仍存在，缓存只能降低查找成本。
新增 TLS 固定存储在 Windows x64 约 3.5 KiB/thread，长名称另有首次分配并复用的缓冲。
guard 名称索引还增加每个 immutable publication 的 O(程序集数) 存储；没有新增共享锁或生产计数器。
不能把这些成本写成零，也不能从 x64 验证外推 ARM64。

## 当前身份验证

证据根目录：F:/hybridclr_artifacts/dhe-release-candidate-v1。

| 验证 | 结果 / 范围 | 证据 |
|---|---|---|
| 真实 Unity 2022.3.62f3 headers、FGS、生产 native compile/CTest | passed；mergeReady=true；surrogateExternalHeadersUsed=false | native-production-04/DHE-Unity2022/native-gate.json |
| 同源诊断 native compile/CTest | passed；与生产各自独立编译 | native-diagnostic-03-retest.log / native-diagnostic-03/Testing/Temporary/LastTest.log |
| guard regression | 内容相同地址不同、caller buffer 改名、负缓存、碰撞、六程序集跨发布正/负结果、4 threads x 10,000 查询；无 metadata 枚举 | native-unit-tests/hybridclr_native_tests.cpp |
| managed runtime | 134/134，concurrent snapshots、加载 phase/retry/restart、字节 ownership 等；native calls 为 host stub | final/managed-result.json |
| qualification forwarding | 8 项 compiled helper + 真实 runtime contract resolver 正/反例 | final/qualification-checks/result.json |
| 来源、跨仓库和授权边界 | 19 项通过 | final/provenance-checks/result.json |
| 默认流程简化 | 10 项通过 | final/default-flow-checks/result.json |
| bundle / 路径 / 显式检查 | 27 项通过，含 SDK-free Unity host、中文空格路径、参数 roundtrip、拒绝覆写工具目录 | final/bundle-checks/result.json |
| Release evidence 拒绝 | 6 项通过；没有将候选提升为 Release | final/publication-checks/result.json |
| 实际 Unity Base / no-op | passed；35 vs 35 records，differential=0；40 ordinary AOT assemblies，49,456 guard requests，missing=0 | player-03/result.json / final/player-differential.json |
| 同一 Player 的真实布局变化 | passed；新增 Int64/Object 字段；revision=41；前/后 checksum 通过，diagnostics=false、smokeIncluded=false；原生 SHA 不变 | final/current-player-review-checks.json |

真实 Base 绑定 v35，package=7a0ad49、runtime=9c607a3、IL2CPP=a1ec032、Lab=23e4ba5。
Snapshot.exe SHA-256：143DD3C6A93A45819A98E3CCA8FD1535B461CDF860C927D955CA54815FAF5D7B。
GameAssembly.dll SHA-256：A0A4A560BDAB0442142CC969A49E20E7767E0B31489F7B2F6144893E2A180864。
真实更新另用两个独立 PID（116136 / 115736）分别验证 no-op / layout，
资源准入使用新 Base 的 AOT analysis archive；没有替换 Player 的 exe/DLL。
此 fixture 一次进程测试不能代替实际项目 correctness 或性能门禁。

微基准采用同一 Lab native test source、真实 Unity headers、相同编译配置，baseline=63c1c72，
candidate=9c607a3，双方 diagnostics=false。10 对 / 20 个唯一 PID，每 case 1,000,000 次查询。
保存原始每进程样本、exe/source SHA、配对与非配对 median 和 MAD：
benchmark-03/guard-benchmark-summary.json，重跑脚本 benchmark-03/run.ps1。

| 发布程序集 | 查询 | baseline median ns | candidate median ns | paired median ratio |
|---|---|---:|---:|---:|
| 0 | unknown | 1.69 | 1.68 | 0.99 |
| 1 | changed / unchanged / unknown | 7.53 / 5.02 / 2.77 | 7.85 / 5.64 / 2.93 | 1.04 / 1.11 / 1.06 |
| 4 | changed / unchanged | 10.94 / 8.98 | 13.70 / 12.36 | 1.00 / 1.04 |
| 4 | alternating | 17.15 | 16.82 | 0.99 |
| 40 | changed / unchanged / unknown | 7.62 / 5.24 / 63.24 | 4.60 / 4.41 / 4.66 | 0.59 / 0.87 / 0.07 |
| 40 | alternating | 134.52 | 32.88 | 0.24 |

小集合有额外分支，1-assembly 的绝对差异约 0.2-0.6 ns；不声称各 case 都提升。
4-assembly changed/unchanged 的 baseline MAD 为 3.13/4.09 ns，非配对差异不稳定，
不能以配对结果掩盖非配对噪声。40-assembly 结果仅说明这个 native helper 查找优化有效。
metadata 为 Lab stub；它不是原生无 DHE 对照，也不是整 Player、首触达/P99或真机性能验收。

第一次纯 TLS 方案 041c051 的 1/4-assembly 回退已根据真实测量修正，旧样本保留于根目录
guard-benchmark-summary.json。该实现的 player-02 只作中间验证，不冒充最终 9c607a3 Player。
混用 worktree 与 staged header 的试编译失败、新 native fixture 未清 resolver 的 CTest 失败、
旧 bundle fixture 仍期望自动扫描的失败均保留；已修复测试配置/fixture 并在最终源码重跑通过。
旧 opt6/opt7、bfea3c7、63c1c72 的 Player/性能/内存报告只作历史参考。

## 分发与发布边界

候选导出：final/hybridclr-unity-8.13.0-dhe-v35-candidate.zip。
SHA-256：9D5C888BC3A69B2D7B0F547689E62DD7014FF76FE2C9C199F075DF8721EEC5A9。
git archive 仅导出 570 个 tracked files；83 个工具文件，无 bin/obj、SourceLock 或重复 runtime release 清单。
final/export-audit.json 锁定 package commit/tree、bundle identity 和导出 SHA。

本轮所有候选 worktrees 已提交；最终文档提交后保持 clean。正式 repos 亦 clean。
worker3 package 仍为正式 opt7 / SVN r24964，本轮未修改；旧 SourceLock 的本地 scheduled deletion 保留。
没有新增 stash；保留的历史 stash 是 FGS prototype 和团结 lazy metadata 研究归档，未清理。

后续正式发布须先将审核源码合入正式维护线，发布并验证不可变 runtime tag 和远端身份，
再更新 package 的唯一 runtime 清单、重新生成对应分发、更新 Lab lock，并在最终身份复核。
当前 runtime commit 引用是本地候选，不能直接当作远端普通 Installer 已可获取的新版本。
package 不打 opt tag，opt7 tag 不移动。

剩余生产门禁：同配置未插桩的无 DHE 对照、实际项目端到端/steady-state/首触达，
Android APK ARM64 correctness、PSS/RSS、帧耗时、温度与弱核，以及 >=100 独立 PID 的 P99。
工具若要声明 Release，还需真实完整 certification；不手工改 releaseReady。
Unity 2021/团结本轮延期，不把本轮 Unity 2022 数据外推过去。

精确回滚：恢复正式 package bf62316、HybridCLR a4807e5 / v8.13.0-opt7、
IL2CPP a1ec032 / v2022-8.11.0-opt7，正常 Install 并重建 v34 Base。
现有固定 Base 上的资源回滚只能使用兼容归档且重启进程；不要替换旧 Player 的原生 DLL，
不要给旧 build identity 改写 v35，也不要混合候选 runtime 与旧 package。
