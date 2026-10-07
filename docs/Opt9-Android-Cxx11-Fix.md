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

## 修复与本工作区验证结果

HybridCLR `252100f9decff54f0f0f1a58ca3127f3d6aa291b` 已快进合入
`optimize/v8.13.0`，并于 2026-10-08 推送、核验远端提交。生产净改动为一个文件 7 行新增、3 行删除；无新增构造函数或状态字段。
邻近 PendingInterpreterImage 已使用默认构造加赋值；未发现 RuntimeApi.cpp 内另一处同类初始化。

使用 Unity 2022.3.62f3 随附 Android NDK Clang 12.0.8、真实引擎 headers、
`--target=aarch64-linux-android22 -std=c++11 -fPIC -O2 -c`，编译完整 RuntimeApi.cpp。
启动选择关闭和开启分别验证：opt9 均只报告 PendingDheImage 无匹配构造函数；修复均成功生成 ARM64 对象。
测试的编译参数由脚本显式记录，未声称恢复了用户原始 APK 构建命令。

复现脚本：scripts/test-opt9-android-cxx11.mjs（lab 测试提交 1e985d0）。
证据：C:/hybridclr_optimize/tmp/opt9-cxx11-validation/verified/summary.json。
SHA-256：048671d9daf2d6ce2117d2a182018b87379adf5e7be9a85391dbbfcac8321550。
保留完整参数、源文件/编译器/日志/对象哈希，以及两个模式的基线错误和修复结果。

已发布的 v8.13.0-opt9 仍指向 552bc0b；修复已推送维护分支，尚未发布新 tag，package 仍引用原 opt9。
重新安装原 opt9 不会包含此补丁。后续发布需要新的不可变版本号，不能复用原 tag。
没有重跑 Windows Player/性能或完整 APK；此编译期修复不冒充获得新的运行时验收资格。
