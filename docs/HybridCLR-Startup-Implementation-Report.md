# 库级 Startup 首版实现与门禁

2026-10-06。结论：Windows Unity 2022 的纯托管热更场景**有条件通过**。
这是一份库级源码候选，未完成全平台方案，不是正式 opt 或生产发行。

## 已实现的实际路径

一个原生 `StartupPlayer.exe`、一个 `UnityPlayer.dll`、独立的启动控制层以及
两个独立 native 后端。启动器在 UnityMain2 之前读取请求、验证所选后端和
metadata 身份、只加载所选 DLL、绑定全部 386 个实际导出。传统端是 opt3
的非 DHE 实现，使用普通 Assembly.Load；不是把 DHE 方法全部改为解释。
选定后的指令、生成方法、字段和反射路径没有新增模式条件检查。引擎入口
有一次额外间接跳转，未测性能，不宣称零开销。

公共 API 在 package 的 `Runtime/Startup/HybridStartup.cs`：GetSelection /
EffectiveMode、RequestNextStartupModeAsync、ClearNextStartupModeAsync。
没有托管 Bind/Reset。Windows 测试将同一份 API 源码作为普通 AOT 输入编入
两个独立 fixture（Assembly-CSharp），其 hash 与 package 源码一致；不是把
整个新 package/DHE RuntimeApi 迁入传统后端。正式项目 package 迁移、桥接和
完整资源集成仍需项目门禁。

本次固定的 mode 和启动代数只读。写接口只提交下次请求，成功回执包含提交
代数及 RestartRequired。恢复默认提交 mode=0 的 tombstone，不删除文件。
记录有固定版本、little endian、稳定应用 scope、单调代数和 SHA-256；损坏、
scope 不匹配、IO 和 Busy 不会被吞掉或默认改选 DHE。

Windows 默认位置是 LocalAppData 下的应用 scope hash 目录；无需 Unity
PlayerPrefs，也不依赖安装路径。原生文件锁覆盖进程和 logon session，进程
死亡释放锁；写入先 flush 临时文件，再原子 write-through 替换。
Browser 使用 IndexedDB 事务完成回执及原子 compare/put，跨页面不丢代数；
Android Java adapter 使用 app no-backup 目录、FileChannel/AtomicFile、文件及
目录 fsync 和提交后读回。各端均使用同一 88-byte v2 协议。浏览器的存储
清理/驱逐仍会丢失请求，不宣称与原生 fsync 相同的持久性。

## 当前源码身份

| 仓库/边界 | 分支或身份 | 当前提交 / tag |
|---|---|---|
| package Startup 候选 | research/dhe-startup-selection-v1 | 36a7a38c0a947b926f82522a7441ca4ae23d80a0；无 package tag |
| Windows fixture 冻结 | lab research/dhe-startup-fallback-v1 | cec4864d76b3f7393ceb474496e3c713c8bba68c |
| cross-host gate 冻结 | 同一 lab 候选分支 | 63eb5c8，精确完整 SHA 在 protocol summary 中 |
| 正式 hybridclr（未改动） | optimize/v8.13.0 | 9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071 / v8.13.0-opt8 |
| 正式 Unity2022 il2cpp_plus（未改动） | optimize/unity2022-v8.11.0 | e426adc57c283865126423b169051558b339388c / v2022-8.11.0-opt8 |
| 正式 package（未改动） | optimize/v8.13.0 | f2946d5ba35a879724b76afd857176feb1a4adca；无新 tag |
| 正式 lab（未改动） | optimize/assembly-load-metadata-lab-v8.13.0 | 09436e96df0be3ed8f6b26ec935bfe549ec5a590 |

DHE 后端来自正式 opt8；传统后端 HybridCLR=
f40c6f08ccd0391ad9285276b4cc21ada3a180ab，Unity2022 IL2CPP=
bf15337e189ae7da5876aa51c9b896a36c52a155，package=
ac0fdc5c6363a1b6323d017e068c536dd22127dc。完整配对在报告内。
三个 Unity2022 upstream 版本未升级。没有合入正式线、推送或创建任何 tag。

## 新产物身份和验证

原始 Windows 报告：`reports/startup-router-36a7a38/summary.json`；cross-host：
`reports/startup-protocols-frozen/summary.json`。这两组是本实现的新证据。
此前 `reports/startup-windows-115bc81` 属于两个 Player 的历史实验，不替代本报告。

| 产物 | SHA-256 |
|---|---|
| 两模式共同 Current | 630ec03ceb57399ec34708aea405f781aa27bfc1f313f2b6ad8a5132a784884d |
| 原生 host EXE | 793903ed2a0d55b53590f73910e7cd38dbae713d74f7cab6e82bca5b19992f90 |
| GameAssembly router | 2be21e09b350b222232cb5bc7580926e6fa8e9e3eee6a4feecb6e7f872ad1b5f |
| DHE 后端 | e61a245a23f66ca218eea92cd0c1c9554e063d5b07a840fa7b6b6459404236da |
| 传统后端 | 09118b6104de36ede7c1319916cb92f4aae49567dc928fbebdcf429c67821ea3 |

这些 SHA 必须以本报告的最终 raw summary/manifest 为准；SDK 构建参数和身份
的任何变化都必须重跑门禁，不重标已有数据。

- 8 个唯一 PID 的同一 EXE：默认 DHE → 保存传统 → 重启传统 → 持续传统 →
  保存 DHE → DHE 注入失败并保存传统 → 坏 DHE MV 下重启传统 → 清除为
  tombstone → 重启默认 DHE。7 个完整成功 workload 均为 10 cases、CLR
  differential=0；另一个是预期 DHE 加载拒绝。每次 native backend mask
  都只包含所选端。实际编译 profile、本次 native mode 和拒绝第二次 prepare
  均经过核对。
- 所选 native metadata 损坏和选择记录损坏，均在 Unity 初始化前退出；没有
  同进程重选另一个后端。DHE 后端仍观察到 changed-interpreter 与 unchanged-AOT。
- 冻结提交上的 standalone native compile/CTest 通过：8 线程、4 写进程、Busy、
  持锁写进程被终止后的恢复、损坏/非法值/scope/IO、tombstone 和代数溢出。
- 真实浏览器 12 个协议/host cases 通过，包含 16 个并发事务及 4 个 iframe
  独立 store 上下文。Unity loader 使用 test double，**不是实际 WASM Player**。
- Windows 原生、浏览器和纯 Java 编解码记录逐 byte 一致；native 也读取了
  browser/Java 写出的记录。Android adapter 用 Android 35 真实 SDK headers
  和 JDK8 源级别、warnings-as-errors 编译通过；没有在设备调用 AtomicFile/fsync。
- C# WebGL 条件编译分支通过编译，但使用 Windows 的 Unity engine managed APIs
  和 Unity NetStandard reference；这不是目标 WebGL module/Player/callback 验证。
- host/router 只依赖系统 DLL；没有 .NET 或额外动态 CRT 依赖。

复现 Windows：先用 `scripts/build-dhe-startup-router.ps1` 依次执行 Prepare、
DHE、LegacyInterpreter、Package；再跑 `scripts/test-dhe-startup-router.ps1`。
本机最终产物为 `artifacts/startup-router-frozen`，native CTest build 也在该目录。
cross-host CLI gate 是 `scripts/test-dhe-startup-protocols.ps1`。浏览器测试需先
运行 fixture browser-server.mjs，打开 localhost 并点击运行按钮；最终记录与
summary 会由测试页保存。所有构建输出均为忽略的本机产物，不作为源码提交。

## 审查、限制与发布门禁

核心正确性/ABI：本轮证明了真实双 DLL 隔离和启动前选择；导出名字及序号
一致，并拒绝数据导出。链接器对 DllCanUnloadNow 保留序号给出 LNK4222
建议警告；这是显式保持后端 ordinal ABI 的选择，不改变路由行为。
没有发现本次纯托管门禁的阻断问题。

并发：本次选择内容和完整指针表在 release store 前完成；查询使用 acquire。
prepare 使用 try-lock；成功/失败都消耗本次选择机会。记录锁覆盖 read/
validate/increment/commit。Windows x64 测试不能推出 ARM64 正确性。

仍以以下门禁约束候选，mergeReady=false、productionReady=false：

1. 共享场景、MonoBehaviour/ScriptableObject、AssetBundle、序列化、完整泛型/
   reflection/callback/GC/cctor 和多热更程序集图。packager 当前只允许显式
   ManagedOnly，公共 Unity data 来自传统 profile；不能泛化为任意资源兼容。
2. 生产等价 ordinary AOT guards，以及三引擎真实 headers/native matrix 与
   各自 Player。当前 DHE fixture diagnostics=true、ordinaryAotGuards=false。
3. Android 同 APK 的 libmain/JNI/双 ABI 后端加载链、真机持久化/重启/ARM64；
   Java 文件协议不代表 APK 功能已完成。
4. 真正的 Unity2022 WebGL 两 WASM/profile 及 managed jslib callback。主线程
   模式、缓存、配额/禁用存储、不同浏览器、worker/threaded WebGL 需要各自
   验证；页面测试的写失败为明确标注的注入测试。
5. iOS 完整静态符号、GC/OS/constructor 隔离和 UnityFramework 启动接入：
   目前只有设计，没有实现或 Xcode/device 证据。
6. 引擎 API 间接跳转、包体、内存、尾延迟和稳态性能。未测 P50/P95/P99，
   没有 100 独立进程性能门禁，不能给出生产性能/移动内存结论。

Windows operator/default 记录损坏的恢复由明确的 host 修复流程负责；库不会
把 corruption 当 missing。控制层本身、共同解释器/Unity 或资源错误不在
DHE 专属隔离承诺中。每平台仍只编译一份 Current，Base 双后端构建和每轮
双模式 QA 是维护增量，不需要发布两份业务 Current。

回滚：运营侧保存 LegacyInterpreter 并重启；源码侧撤销 package 候选 feature
提交并重新构建 Base。不能混用不同身份的 Backend.dll 和 metadata。相关
正式仓库及两候选工作树在交付前 clean；未创建、应用或丢弃用户 stash。
