# DHE Unity 2022 当前状态（2026-09-11）

当前结论：Unity 2022 Windows 上已证明 DHE 核心架构与多种结构演进场景可行；
尚未达到“原 hotfix 程序集以后所有修改都能只发热更包”的完整目标，不能称为正式交付版。
不以零散用例通过率推算项目完成百分比。

目标保持不变：原 hotfix 集合在 Base 中编译为 AOT，后续 Current 资源热更；可安全保留的
未变方法继续 AOT，变化方法及受结构变化影响的方法走解释器。同一 Current 资源支持多个
已归档 Base。原来配置为普通 AOT 的程序集不成为可自由热更的程序集。

## 已有证据

| 范围 | 当前证据及其限制 |
|---|---|
| Base / MV / Current / 加载工作流 | Unity 2022.3.62f3 Windows Player 已跑通；新旧 Base 可以选择同一 Current 资源 |
| AOT 保留 | no-op 用例有 AOT 入口且 DHE 解释器入口为 0；这证明路由，不代表已测得性能收益 |
| 代码与结构变化 | 已覆盖方法体、类型和成员增删、部分字段布局/签名替换、接口/虚调用、泛型上下文、反射、异常、静态初始化及 GC；每项仍受对应报告的边界约束 |
| 跨程序集父类增删 | Base101 与 Base102 共用 Current 的真实回放通过，分别有 15 项跨父类检查及 18 项框架检查；父类删除只验收冷启动路径 |
| 泛型父类 | 新增解释器派生类继承已有闭合泛型父类已有验证；不等于已有物理类型可以任意更换泛型父类 |
| 失败处理 | 兼容性分析异常拒绝资源并保留原因；原生加载拒绝、部分准备失败和提交后初始化失败区分重试/重启 |
| 本轮补齐 | 公开程序集状态改为原子发布数组快照；当前 package 的 .NET 宿主 126/126，旧 package 对照检出 4 项失败；未新建 Unity Player |

主要证据入口：

- [跨程序集父类与同资源多 Base](dhe-cross-assembly-parents-windows.md)
- [最近两代 Base 的真实回放](dhe-current-host-cross-parent-replay-windows.md)
- [泛型父类准入策略](dhe-current-host-generic-parent-policy-windows.md)
- [分析异常拒绝门禁](dhe-analysis-failclosed-windows.md)
- [本轮公开状态并发修复与精确身份](dhe-public-status-snapshots-windows.md)
- [新 package 绑定及新一轮 Unity 2022 native 门禁](dhe-snapshot-package-native-windows.md)

## 剩余难点与推进顺序

1. 先把最新 package 放入 Unity 2022 demo 的新 Base，统一回归同身份下的已有能力与多 Base
   资源流程。已有旧 Player 不能因为宿主或 package 源码变更就改记为新版本证据。
2. 验证 Unity 场景/Prefab 序列化、Component 生命周期、加载前已存在对象，以及父类删除后
   缓存旧对象的行为。C# 类型/字段变化必须同时满足 Unity 原生对象与序列化的要求。
3. 继续处理已有物理类型的泛型父类与 TypeSpec 演进。这涉及布局、泛型实例化、虚表及跨程序集
   类型身份；单纯识别 IL 差异不能解决这些问题。
4. 完成原生发布并发压力、Windows 正确性总回归及性能/内存对照，再整理可供真实项目试验的
   package/runtime/工具组合。当前还没有 P50/P95/P99 或内存收益结论。

按用户约定先完成 Unity 2022 Windows，不推进 CAT 接入、Unity 2021 或团结移植。
Android 由用户后续在真实项目验证；Windows 通过不代表 ARM64 或 iOS 已通过。

## 候选源码状态

| 仓库 | 当前候选分支 | 实现提交 |
|---|---|---|
| HybridCLR | `research/dhe-parent-transitions-v8.13.0` | `6180597d2c0e455ab09fe0920d34d6dea5ad00fc` |
| IL2CPP Unity 2022 | `research/dhe-parent-evolution-v8.13.0` | `819f74c08e466a0d2a8fe5b1afaad5b1d784e482` |
| package | `research/dhe-parent-transitions-v8.13.0` | `ed4b7b52a49373069d1a1336e3f8784278a03b39` |
| lab 测试/工具 | `research/dhe-cross-assembly-parents-v8.13.0` | `46372939d728e13eb9f7c104d3b1c3c89a0d5522` |

本表为候选实现组合，不是已通过全部门禁的发布锁。后续文档提交不会替换测试报告中的实现身份。
旧 Player 的 package 仍为 `841abfd46e122343717fe4115186b97a215b58df`。
本轮改动已提交在候选工作树；没有推进正式维护分支、runtime tag 或 Installer 默认版本。
准确回滚步骤见各项报告。当前无需用户执行 CAT 或移动端测试来完成本轮宿主检查。
