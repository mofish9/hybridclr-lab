# Unity 2022 DHE opt8 发布记录

2026-10-04：审核过的源码已进入正式维护线，以 opt8 发布。结论为 **Unity 2022 有条件通过，可以进入项目验证；尚未完成 Android ARM64 和完整性能验收**。
范围按用户要求限定 Unity 2022.3.62f3；Unity 2021、团结延期，package 对它们的选择保持原值。
上游保持 package / HybridCLR 8.13.0、IL2CPP v2022-8.11.0，没有升级基线。

## 正式身份

| 仓库 | 正式分支 | 提交 | runtime tag |
|---|---|---|---|
| HybridCLR | optimize/v8.13.0 | 9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071 | v8.13.0-opt8 |
| il2cpp_plus | optimize/unity2022-v8.11.0 | e426adc57c283865126423b169051558b339388c | v2022-8.11.0-opt8 |
| hybridclr_unity | optimize/v8.13.0 | f2946d5ba35a879724b76afd857176feb1a4adca | 不创建 package tag |
| Lab | optimize/dhe-release-lab-v8.13.0 | 验证源码 3cbb3c096832ad416991bd69f9d1d986894dd14d；本报告为后续文档提交 | 无 |

两个 runtime tag 均为不可变 annotated tag，远端 peel 已核对：

- HybridCLR tag object：b3243e0397278fe618865dac7b24e29cbdedda5a → 9c607a3。
- IL2CPP tag object：52c537309db0d671895fee5013a33fc25e199211 → e426adc。

发布顺序为 HybridCLR branch/tag → IL2CPP branch/tag → 远端 tag 核对 → package branch → Lab branch。
opt7 tag object/peel 保持原值；package 通过正式分支的上述精确 commit 迁移。
IL2CPP 的 opt8 新提交仅增加 README 配对说明，libil2cpp tree 与 opt7 a1ec032 完全相同。
三个正式仓库均保持批准上游 tag 的祖先；没有引入更高版本 upstream。

## 变化与正确性边界

HybridCLR 相对 opt7：删除生产热路径计数，增加 dispatch / field 的 TLS 缓存、每次调用的内联 InvokeBuffer，
以及直接 AOT guard 的 immutable 程序集名称索引与正负缓存。发布程序集 <=4 保留有界扫描，
较大集合按字符串内容缓存，publication identity 使旧缓存失效。
索引在 release 发布前构建完毕、读方 acquire；field registry 在锁内完成写入后 release 更新版本。
closed generic / receiver / physical ABI 判断保留，TLS 逐出不释放 process-lifetime backing。
InvokeBuffer 的溢出与重入仍各自拥有调用存储。

package 相对 opt7：生产不保留无用途的 Current 副本和冻结源字节，加载重试/重启状态保持原语义；
smoke 和计数由诊断编译开关控制。诊断编译配置覆盖 finalization 并恢复 Editor 状态。
删除安装 receipts、ProjectInstallation、重复 runtime release 清单和自动 bundle 扫描；安装由使用者保证。
修正独立 Lab / package 的证据来源与 qualification 参数转发，managed contract 对齐 dhe-runtime-v35。
本轮发布还把 package 清单、分发工具及 Lab 的 runtime-workflows 全部对齐 opt8。

guard 是旧 AOT 方法选择 Current 解释器的必要入口，仍有执行成本。
新增 guard TLS 固定存储在 Windows x64 约 3.5 KiB/thread；长名称首次缓存另有分配，
每次 immutable publication 的名称索引增加 O(程序集数) 存储。没有声称零成本或各场景都提升。
完整审查详见历史候选报告 dhe-v35-package-candidate-review.md；该报告的旧身份和旧数据未改写。

## 在最终 opt8 身份上重新验证

证据根：F:/hybridclr_artifacts/dhe-opt8。文件 SHA 和结果摘要见 dhe-opt8-release-artifacts.json。
测试使用已提交、clean 的 Lab 3cbb3c0 和 package f2946d5；报告提交不重标验证源码身份。

| 验证 | 本轮结果 | 证据（相对证据根） |
|---|---|---|
| Unity 2022 真实 headers、FGS、生产 native compile/CTest | passed；mergeReady=true；surrogateExternalHeadersUsed=false | native-production/DHE-Unity2022/native-gate.json |
| 同源诊断 native compile/CTest | passed；与生产配置独立编译 | native-diagnostic/Testing/Temporary/LastTest.log |
| managed runtime | 134/134；native calls 为 host stub | managed-result.json |
| qualification forwarding / runtime contract | 8 项通过 | qualification-checks/result.json |
| 来源与跨仓库授权 | 19 项通过 | provenance-checks/result.json |
| 简化后的默认流程 | 10 项通过 | default-flow-checks/result.json |
| 最终 bundle / 路径 / 显式检查 | 27 项通过 | bundle-checks-final/result.json |
| Release evidence 拒绝 | 6 项通过；没有提升为 certified Release | publication-checks/result.json |
| 实际 Unity Base/no-op | passed；35 vs 35 records，differential=0；40 ordinary AOT assemblies | player-01/result.json；player-differential.json |
| 同一 Player 的布局变化资源 | passed；新增 Int64/Object 字段；revision=41；加载前/后 checksum 正确 | current-player-review-checks.json |
| 生产配置 | no-op/layout 均 diagnostics=false、smokeIncluded=false | no-op-final.*.json；changed-layout-final.*.json |

native guard 产物实际 requested/guardedMethodCount=49,535，nativeEntryCount=54,494，
unsupportedMethodCount=0，interpreterOnlyMethodCount=4；覆盖与身份由完整 workflow 校验。
Base/no-op 的 35 条记录覆盖静态初始化、递归/失败初始化、8 线程首次触达、泛型、
静态值复制/byref、反射读写、GC 后值保持和邻接字段；范围仍是 fixture，不代表实际项目全部代码。
布局更新由最终分发 DLL 编译，使用此新 Base 的 AOT analysis archive，只改变资源。
两个 correctness probe 的独立 PID 为 118648 / 118768；每种仅一个进程，采样仅作探索。

Player SHA-256：143DD3C6A93A45819A98E3CCA8FD1535B461CDF860C927D955CA54815FAF5D7B。
GameAssembly SHA-256：8528ED3234FC879DF0DCDE615714FE263B1C64AAB14F7F36B27953BDAC6BA19B。
Base id：5d4bd9aa2f68e01f1f05da63ae0d0a54d62ae102c76d129e0fffdb104a118127。
AOT analysis snapshot SHA-256：7ef810d89e4436e5f849d574de76fdc8468e8a210961818128d432fb3af02f3c。

历史 guard helper 微基准的 candidate runtime 源码也是 9c607a3，说明较大集合查找优化有效，
但其 baseline 为 63c1c72 且 metadata 为 Lab stub，不是 opt7 或无 DHE 的整 Player 对照。
本轮未重跑完整性能验收，不把历史 opt6/opt7、候选 Player、微基准结果重标为 opt8 性能数据。
首次 assemble-runtime 因 Lab workflow 仍引用 opt7 被拒绝，随后修正；
发现分发 workflow 仍引用 opt6 后也已修正并重建。最终证据只使用 runtime-final / bundle-final / f2946d5。

## 分发与回滚

package Git tree：c96190e1dc4b3e2f6bc6a6d44baed29be9a9b645。
canonical package tree SHA-256：27B75F680A25205402330E535E5A5EF039E2056F7FA83881886BD0F853813540。
canonical HybridCLR source SHA-256：ED58C2795B8EE3FBCAB72CEBD10F820BBE8A68A3D3B4886EE9F7CF21548E12D9。
canonical libil2cpp SHA-256：1398548B3707F0B87BFD01CC722739C0FAE8C8E49FB79252D760F70A8A263805。

分发工具来自 clean source 62d8d39401fac3cedbdcc4e6aa7aaaeb34d2474c，
source tree 106d0681a871f8eefb8d28c1bdecd24cf1b0563b，83 文件（含 manifest），
packageId 60b4e30f86f95c2d03f67bdf3590fe3af6b1794e159bde429bb2d2e26101100e。
DLL SHA-256：2AECCD09CABA9F45822E95C64AC122EA67C9B68C2842AB2CAE46EE2EBE29E00B。
继续保持 Mode=Exploratory / releaseReady=false，runtime tag 发布不等于完整 Release certification。

导出：F:/hybridclr_artifacts/dhe-opt8/hybridclr-unity-8.13.0-opt8.zip。
SHA-256：126EE0DE2BB8FB37EECCA3047456E00D24DD89F3306E9299C8C5173B8845067A。
git archive 与 570 个 tracked files 完全一致；无 bin/obj、HybridCLRSourceLock.json 或 dhe-runtime-release.json。

剩余门禁：实际项目 correctness；同配置未插桩的无 DHE 对照；端到端、steady-state、首触达；
Android APK ARM64 correctness、PSS/RSS、帧耗时、温度与弱核；>=100 独立 PID 的 P99。
Windows 结果不外推 ARM64、Unity 2021 或团结，不声明小游戏生产可发布。
接入使用 opt8 package 精确 commit，正常 Install，并重建 dhe-runtime-v35 Base。

回滚到 package bf62316bafa6e2c84dbb922faace3af06ee3f6ca、
HybridCLR a4807e563c0cb245519a44c6ea7cc633246dd337 / v8.13.0-opt7、
IL2CPP a1ec0324a8a58cb8e175c7b86665d70b5afae57e / v2022-8.11.0-opt7，正常 Install 并重建 v34 Base。
固定 Base 的资源回滚必须使用兼容归档并重启进程；不得改写旧 identity 或替换旧 Player 的 native DLL。

正式仓库和本轮相关候选 worktree 均 clean；候选保留原 commit，未被 package 引用。
没有新增或清理 stash；既有 FGS prototype / 团结 lazy metadata stash 保留作研究归档。
worker3 本轮未迁移，仍是 opt7 / SVN r24964；既有 SourceLock 的本地 scheduled deletion 保留。
