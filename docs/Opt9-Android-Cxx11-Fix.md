# opt9 Android ARM64 C++11 编译修复

范围：Unity 2022.3.62f3，Android ARM64，RuntimeApi.cpp 的 PendingDheImage 初始化。
用户报告原失败单元经项目本地修复后已通过相同 Android/C++11 参数编译，完整 APK 尚未验证。
当前工作区的 worker3 安装副本仍是原 opt9；该用户验证不冒充本工作区的测试证据。

根因：C++11 中带成员默认初始化的类型不满足聚合类型要求，PendingDheImage 的
metadataReady=false 导致六参数花括号初始化无法匹配构造函数。该写法在 opt8 之前已经存在。

最小修复：默认构造 entry，逐一赋值 image、currentAssemblyHash、executionPlan、
batchAssemblies、interpreterPeers，再 move 到 pending map；metadataReady 保留 false。
目标是原失败编译单元在 C++11 下通过，基线应复现相同诊断；锁、所有权、异常行为和 map
发布顺序不变，不增加执行热点、平台分支或诊断计数。无需重新做性能收益声明。

源码候选从已发布 HybridCLR 552bc0b 建立独立 worktree；验证后本地快进到 optimize/v8.13.0。
opt9 runtime tag 不移动，package 的 opt9 引用不改写为未发布提交；本修复不自动创建新版本。
回滚为撤销本次 RuntimeApi.cpp 初始化改动，会恢复已知 Android/C++11 编译失败。
Android 完整 APK、真机 correctness 和性能资格均需另行验证；本轮不扩展到其他引擎。
