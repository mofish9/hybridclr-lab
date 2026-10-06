# 启动模式选择：发布收敛计划

2026-10-06，承接 b2edce0 / 9d3e781 / cefc8fd 源码候选及 aot-review-20261006 review。

目标保持不变：同一 Base 与兼容 Current 资源，在普通 AOT 启动代码中选择 DHE 或普通
HybridCLR 解释模式；选择返回后才启动热更加载/查询/业务线程；本进程不能重选。
库仅提供 Select/Get，配置持久化、HTTP 和重启流程属于项目。禁止逐指令检查及生产诊断计数。

本阶段先修复构建资产检查的循环引用、场景 bundle/Editor Resources 误拒绝，以及错误 loader
拒绝时的 image/index 泄漏。改动边界是 package 构建检查、runtime Assembly 加载入口和 lab 回归。
不修改正常执行的解释器循环、不增加运行期计数，不更换锁定的 upstream 基线。
本轮明确仅实现 Unity 2022，在准确提交上执行该维护线既有阶段回归与新能力验收，再合入维护线。
Unity 2021 和团结不属于本轮范围；用户对本轮范围的明确限定优先于工作区通用三引擎发布模板。

正确性必须包括：Base 中热更对象仍被拒绝、合法自环/互环/共享 SerializeReference 可遍历、
重复引用后的热更节点仍被拒绝；独立热更场景 bundle 可构建，选择及 Current 加载后双模式可反序列化；
Editor 专用资产不误拦截，真实 Player Resources 仍拦截；重复错误加载不消耗 image index，随后正常加载成功。
所有这些用例位于 lab，不能把测试开关/故障注入放回生产库。

性能主指标：正常 DHE steady-state、启动选择+加载+首次入口；次指标：解释模式对照、
首次反射、内存和二进制体积。记录 P50/P95/P99、MAD、唯一 PID、源码和程序集身份，
尚无批准的数值退化阈值，不能自动把 measurements 记为性能通过。P99 门禁各组至少 100 进程。
选择/注册工作纳入端到端计时。性能壳应与 correctness 专用并发/错误重试代码分离，基线与候选保留根一致。

Unity 2022 Windows 是本轮 Player 验证点，同时用真实 headers 完成开启/关闭编译、CTest。
不修改 Unity 2021/团结维护线，也不将其接入作为本轮发布前置条件。
Android/WebGL/iOS 无现成 Player 环境，保留明确门禁，不能用 Windows 结论代替。

回滚：业务在具备能力的 Base 保存下次 Interpreter 配置并重启；源码可整体恢复 opt8 的
Unity 2022 runtime/package 组合重建 Base。新代码按 package/runtime/Unity 2022 接入/lab 独立提交。
修复后的新身份不继承 v1 的性能通过结论，正式合入或 merge 后需重跑并更新发布锁。
