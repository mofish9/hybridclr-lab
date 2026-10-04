# DHE 性能候选审查与验证（Unity 2022）

2026-10-04。结论：opt7 的生产计数、普通路径锁/临时分配和诊断 DLL
保留确实有成本；本轮候选已修复这些问题，并通过下面列出的 Windows 验证。
候选有条件通过，尚未发布、推送或迁入 worker3；不代表 Android 真机生产验收。
Unity 2021、团结暂按用户要求延期，上游仍为 8.13.0 / v2022-8.11.0。

## 改动与剩余成本

| opt7 问题 | 候选处理 | 保留的边界/成本 |
|---|---|---|
| AOT、解释器、桥接入口始终做原子计数，另有计数专用分类查询 | native `HYBRIDCLR_DHE_DIAGNOSTICS` 默认 0；生产编译消除计数及专用查询；旧 getter 返回 0，新增能力查询 | 开启诊断的 Player 才计数，不能用于生产性能声明 |
| smoke 工具与产品 runtime 混在一起 | `DheRuntimeSmoke.cs` 仅在 Editor/显式诊断符号下编译；smoke 检查 native 诊断能力 | opt7 smoke 本来没有自动启动挂钩；原来主要是可能被引用的诊断代码，并非自动每帧运行 |
| 普通泛型分配进入 metadata 锁和临时 vector | 无 DHE 发布时直接返回；有发布时缓存每线程的映射，包括未映射结果 | 冷 miss 仍锁住 metadata 并完成类型映射 |
| virtual/interface 缓存命中仍拿全局锁 | 每线程固定容量缓存指向不可变 backing 节点；发布身份改变时失效 | shared map 仅在锁内访问；旧 dispatch backing 保留以保证已返回引用的生命周期 |
| 普通反射 Field API 查询 sidecar 也拿 mutex | 无 supplemental field 时 acquire 版本查询直接退出；注册后缓存字段分类和 cell layout | 管理对象/GC handle 的 sidecar 查询和写入仍拿原锁，GC barrier 保留 |
| invoke-args 每次堆 vector | 常见 <=32 个 StackObject 使用独立栈缓冲；大帧保留堆溢出路径 | 每调用约 256 字节内联元素空间；重入不共享 scratch buffer |
| Current DLL 重复 Clone 并永久为诊断保留；冻结源字节不释放 | 生产不保留诊断 Current；显式 probes 只 Clone 一次；确认 phase >=3 后释放冻结源字节 | defensive ownership、加载身份检查和提交失败后的 restart 状态保留 |

guard 仍有运行时成本。旧 AOT ABI、具体 Current receiver、字段物理布局、
模块初始化顺序的检查不能删除；它们防止旧帧/旧箱体进入不兼容的 Current 实现。
本轮未缩减普通 AOT 与 inline guard 覆盖。缓存约占 16.5 KiB/线程（64 位布局，
六个容量 64 的缓存），需要在 Android PSS 门禁里核查，不把缓存描述为免费。

发布依然是：先完整写入 state，再 release 发布；读方 acquire 获取身份。
TLS 不访问未加锁的 shared unordered_map，不保留 managed object 裸指针。
字段注册在 mutex 内完成后 release 增加版本，分类读方 acquire；cell 对象仍按原
GC handle、owner reference 和 barrier 流程管理。泛型 inflated 方法的 ABI 判断保持
实时检查；初版错误缓存该判断导致 9 个边界断言失败，修复后全部通过。

## 精确身份

| 仓库 | 候选分支/提交 | 发布状态 |
|---|---|---|
| hybridclr | optimize/dhe-performance-v1 / 63c1c729e765ae7a6189d0305538523aa31151c4 | 本地候选；未打新 tag |
| il2cpp_plus | optimize/dhe-performance-unity2022-v1 / a1ec0324a8a58cb8e175c7b86665d70b5afae57e | 本轮源码未改；原 v2022-8.11.0-opt7 |
| hybridclr_unity | optimize/dhe-performance-package-v1 / f78997f512b08858b5b2519d33e0aaf21bb0b430 | 本地候选；无 package tag；工具标记 Exploratory |
| lab 测试源码 | optimize/dhe-performance-lab-v1 / 5bd526ba1031df9af55633f9b202f70df7940947 | 本报告后续提交不替换历史测试身份 |

候选 native canonical source：
`056ADFD66D486AB5CCFAC0FB28D1A9CEA9FDB0D6A770C11F393CA55792D83175`，929 文件。
完整 staging native tree：
`3B2D2858FB57132EBC4785F1FEF4683FFBFF489B273CD736EE17DDF27C4853F9`。
两次 staging 的 native tree 相同；后续 package 修改只修正诊断 build scope。
候选清单用精确本地 runtime commit 做 Installer 身份绑定，只用于 lab 的
InstallFromLocal，不能把这个未发布候选当成工程可安装的新正式版本。

## 验证结果与证据身份

证据根目录：`F:/hybridclr_artifacts/dhe-performance-v1`。

| 检查 | 结果与证据 |
|---|---|
| opt7 red control | 默认关闭计数断言失败 3 项；生产 Current 保留/disabled probe 状态的 host control 失败；不是候选通过证据 |
| Unity 2022 native | production、diagnostic 两种模式真实 Editor headers 编译及 CTest 全通过；`native-final-*-build.log`、`native-final-*-ctest.log`；runtime 63c1c72、IL2CPP a1ec032；不是 ARM64 执行结果 |
| managed package | 134/134；`managed-final-03.json`；加载 phase 1/2/3/4、初始化失败、重入、retry/restart、public snapshots、生产不保留字节、诊断 ownership 和冻结源释放；native 调用为 host stub，不能代替 Player |
| 最新生产 package Base/no-op | `player-production-final/result.json` passed；package f78997f、Lab 437a21c；同一不可变 Player 加载 no-op 资源；40 ordinary assemblies、49,456 个普通方法请求，guard coverage missing=[] |
| 诊断 Player | package f78997f；`player-diagnostic-04` 的 final adapter、compiler generation、native evidence 绑定开启计数的构建；core 35、nullable 39、generics 37、collections 41、arrays/byref 38、old-values 45，共 235/235 断言；每个变体单独进程；failed batch 保留 Base、Current copy、旧值拒绝、GC、scalar generic 保留 AOT 均通过 |
| 生产变化布局/盒装反射 | `production-reflection-final-result.json` passed；同一个 f78997f 生产 Player，具体 Current 方法拒绝旧箱体，8 线程各 64 次 reflection/iterator 重复调用，引用/异常断言通过；`production-final-mode.*.json` diagnostics=false、smokeIncluded=false，5 项 checksum 通过 |

盒装反射负例修正了测试适用范围：未被切换的 Base interface 方法允许继续执行
旧空 iterator，并验证返回 false；具体 Current descriptor 的旧箱体仍必须拒绝。
原始无条件拒绝断言在此 Base 上不适用，保留失败与 selection 输出作为排查记录。
layout-only 合成资源没有改变 Factory 的返回常量，最终验证正确使用 41；之前
误用 73 的失败记录不能算 runtime 失败或通过证据。

原始 `player-production-02` 与 opt7 baseline 的工作负载返回 61，初次误传 41 的
workflow 失败保留。配对性能脚本已固定该 workload 的正确预期，并重新跑过全部
20 个进程。中间被中断、路径配置错误、编译身份不一致的构建全部保留，不混入
通过结果。诊断 scope 修复用 final Player 复验，而不是修改 generated runtime 源码。

## 性能采样

`paired-performance-01/result.json`：10 组交替顺序配对、20 个唯一 PID。
baseline 为正式 opt7 runtime a4807e5、IL2CPP a1ec032、package bf62316。
candidate 为 runtime 63c1c72、IL2CPP a1ec032、package 9264524。
最新 f78997f 的 runtime 与 managed 生产路径源码相同，仅修正 Editor 诊断 scope；
该旧候选性能结果保留其原 package 身份，不冒充 f78997f 的重新配对结果。
基准源码、workload DLL、Current payload DLL、循环数、checksum、Editor、配置均校验相同。
比较包括 opt7 出厂默认计数的成本；不能把全部收益独立归因于缓存。

加载后 steady-state：

| 工作负载 | opt7 P50 ms | candidate P50 ms | 配对耗时下降 |
|---|---:|---:|---:|
| ordinary entry，2,097,152 次 | 18.55 | 9.14 | 50.5% |
| virtual，2,097,152 次 | 65.52 | 29.04 | 56.4% |
| interface，2,097,152 次 | 54.99 | 31.72 | 42.3% |
| generic allocation，102,400 次 | 22.97 | 12.19 | 46.9% |
| reflection Field，102,400 次 | 63.91 | 35.78 | 44.0% |

所有 checksum 一致；before-load、P95、MAD、配对/非配对结果和 Player SHA 在原始
JSON 中。这里只能证明这些 Windows 微基准的改善，不是整游戏收益或 P99/Android
发布门禁。没有把 opt1/2/3 的收益相加，也没有用诊断 Player 计算生产收益。

## 剩余门禁与回滚

尚未完成：完整业务项目 APK、Android ARM64 correctness/PSS/温度/弱核、匹配
build identity 的端到端 Load+Entry/Reflection 内存与尾延迟，以及 >=100 独立进程
P99 门禁。Android SDK 已有，adb devices 当前无设备。2021/团结延期。

正式 repos、原 opt7 tag 和 worker3 SVN r24964 package 未更新。候选工作树各自 clean，
本轮未创建 stash。待上述门禁完成后再讨论正式 opt 发布与项目迁移。
精确回滚组合为 hybridclr a4807e563c0cb245519a44c6ea7cc633246dd337、IL2CPP
a1ec0324a8a58cb8e175c7b86665d70b5afae57e、package bf62316bafa6e2c84dbb922faace3af06ee3f6ca。
恢复整套 package/runtime、执行 Installer 并重新构建匹配 Base；不能只替换 DLL 或
混用新旧源码锁。现有 worker3 尚未迁移，无需执行回滚。
