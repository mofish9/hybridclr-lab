# AOT 启动选择：正式源码候选 v1

2026-10-06，Windows Unity 2022.3.62f3 IL2CPP 候选**有条件通过**。
源码、测试和产物身份已冻结；尚未合入正式维护线、创建新 runtime tag 或推送。
Android、WebGL、iOS Player，以及 Unity 2021/团结的启动选择接入仍未认证。

库只提供 `RuntimeApi.SelectExecutionMode` 与 `GetExecutionMode`。普通 AOT 启动壳
先选择，再加载补充 metadata 和 Current；选择在本进程不可变。配置持久化、HTTP、
下一次启动选择与重启提示属于游戏。同一份兼容 Current DLL 供两种模式使用，仍须
满足普通 HybridCLR 的桥接和必要的补充 AOT metadata 要求。

本轮把实验调用计数、故障注入和无条件 lab internal call 从 runtime 删除；生产
诊断计数关闭，ResetForTests 仅在测试/诊断构建存在。传统 SUPERSET 恢复为独立的
原 HybridCLR 实现。构建时检查实际 Base 序列化依赖，允许加载 Current 后动态创建
组件。四个高频入口内联选择表访问，DHE 内部直接调用，虚调用表直接绑定实现。
没有逐指令模式分支；仍保留不可变函数表的原子读取及间接调用成本。

## 冻结源码

| 仓库/用途 | 候选分支 | 精确提交 |
|---|---|---|
| HybridCLR runtime | optimize/aot-mode-selection-v1 | b2edce022a3eea0b9420f18fc5c448ded073b86c |
| Unity 2022 IL2CPP 接入 | optimize/aot-mode-selection-v1 | 9d3e7813bd6107e147709aca21e556b59d9560ea |
| 托管 package/生成器/构建检查 | optimize/aot-mode-selection-v1 | cefc8fd7544d455be001f97559a39b6845099bb2 |
| Unity 2021 普通路径兼容补丁 | optimize/aot-mode-compat-unity2021-v1 | be0493c9c98a19007d130ec4a6ef07c1496caab6 |
| lab 源码与测试脚本 | optimize/aot-mode-selection-v1 | 0fa922d3f216424b1ad7ac47a74ecaa56f896b05 |

以上源码候选没有发布 tag，package 也不创建 opt tag。证据提交只添加本报告与冻结
JSON，不改变表中测试源码。完整锁、源码 SHA、业务 DLL SHA、fixture hash 和 native
产物 SHA 见 [冻结目录](../reports/aot-mode-production-v1/lock.json)。

候选 GameAssembly SHA-256：
`7a68cf1ad87daaabf08e40a7fe35a8f244c4d7e2bc2e74c4929cca55241de6ac`。
性能基线是未插桩 opt8，传统参照是独立 opt3 Player，均使用相同的 Current/Consumer/
UnityHotfix/AotSupport DLL。候选两种模式使用同一个 Player、同一个 native 二进制。

## 当前身份的验证

- 5 个 correctness 进程全部通过：候选 DHE、候选解释、候选并发选择、固定 DHE 基线、
  独立传统 Player。每个进程 21 项托管用例，与 CLR reference 差异为 0。
- 动态 MonoBehaviour.Awake 与 ScriptableObject 结果均为 236，跨程序集类型身份一致。
- 补充 AOT metadata 加载成功，重复加载返回指定错误码 5；提前加载、非法模式、
  错误 loader、再次选择均被拒绝。12 个并发选择恰好 1 成功、11 AlreadySelected。
- 7 项程序集依赖检查通过；Base scene、preloaded assets、Resources 的 deferred 对象
  被拒绝，普通资产通过。测试逻辑留在 lab，不进入用户 package。
- Unity 2022：功能开启/关闭的真实头文件编译、CTest 通过。Unity 2021（FGS 关闭）
  和团结 2022：现有普通路径真实头文件编译、CTest 通过。全部没有 surrogate headers。
  后两者的结果不证明启动选择已接入，合并/发布标记保持 false。

## 性能与内存

最终 50 对、100 个唯一 PID，基线/候选交替顺序。每进程每项执行 200,000 次，
预热后采 9 次，以进程内 median 进入分布。所有 checksum 既与对照一致，也通过独立
算术预期校验。以下为 Windows 同一构建身份数据，时间单位毫秒。

| 指标 | 基线 P50 / P95 | 候选 P50 / P95 | P50 变化 | 配对变化 median |
|---|---:|---:|---:|---:|
| Load | 1.5971 / 1.9868 | 1.6254 / 1.8633 | +1.78% | +2.47% |
| Load + 首次入口（含反射与组件） | 3.3393 / 3.9234 | 3.4532 / 4.1037 | +3.41% | +2.84% |
| Native 调用微基准 | 1.7068 / 1.8000 | 1.3734 / 1.4628 | -19.53% | -19.16% |
| Changed 解释执行微基准 | 1.0616 / 1.0862 | 1.0607 / 1.0839 | -0.08% | -0.08% |
| Interface/virtual 微基准 | 1.7274 / 1.7672 | 1.7569 / 1.7953 | +1.71% | +1.76% |

接口测试增加约 0.0296 ms / 200,000 次，即约 0.15 ns/次；不是零开销。
Load + Entry 中位数增加约 0.114 ms。两者的配对差值 MAD 分别为 0.0211 ms 和
0.0964 ms；完整分布、MAD 与全部进程数据保存在 performance.json。
Native 微基准更快仅是该构建的观察，不作为普适性能收益，也不与既有 opt 收益相加。

Windows private bytes 中位数从 139,264,000 增至 139,309,056 字节（+44 KiB），
配对差值 median +52 KiB、MAD 46 KiB。加载前后增量的 median 均为 0，堆复用意味着
该短测试不能证明零分配。两个测试 GameAssembly 分别为 30,679,552 / 31,208,448
字节；此差异包含候选测试壳独有的并发选择等 AOT 保留代码，不能解释成纯库体积成本。
没有 Android PSS/RSS 结论。每组只有 50 个进程，不声明 P99 硬门禁通过。

首次朴素绑定的接口微基准曾慢约 26%；直接内部调用、内联入口与移除转发目标逐步
降低了该成本。旧结果只保存在 artifacts/ar2 的早期目录，不能替代当前身份数据。
本报告接受它作为可继续接入验证的候选，保留上面的实际启动和接口成本；没有声明
“正常 DHE 性能完全不变”或全平台生产可发布。

## 正式维护线、剩余边界与回滚

| 正式维护线（本轮未变） | HEAD | runtime tag |
|---|---|---|
| hybridclr optimize/v8.13.0 | 9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071 | v8.13.0-opt8 |
| il2cpp optimize/unity2022-v8.11.0 | e426adc57c283865126423b169051558b339388c | v2022-8.11.0-opt8 |
| il2cpp optimize/unity2021-v8.1.0 | 10cbacd02b3ed291af6a6a34d444ae50c85462e0 | v2021-8.1.0 |
| il2cpp optimize/tuanjie-1.10-v8.13.0 | 52968ad6c88416f212d09d919b9a1b6afdc8a53b | v2022-tuanjie-8.13.0-opt4.1 |
| package optimize/v8.13.0 | f2946d5ba35a879724b76afd857176feb1a4adca | 不使用 package tag |

该组合需显式按冻结 native/package 组合安装；package 的正式版本清单未指向候选。
不能只迁移候选托管 API、仍安装旧 opt8 runtime，就期望启动选择生效。
普通 AOT 不得静态引用 deferred 热更程序集，Base 中不得序列化其对象。完整
AssetBundle 序列化兼容、另外两条引擎线的主动选择 hooks、移动端/WebGL Player 与
ARM64 性能仍是剩余范围。共享解释器/GC/IL2CPP 自身的缺陷不由此开关回滚，详见
[实现审计](AOT-Mode-Production-Audit.md) 与 [接入接口](AOT-Mode-Integration.md)。

具备此能力的 Base：项目保存下次 Interpreter 选择，重启后加载同一 Current。
撤销源码候选：恢复上表 Unity 2022 runtime/package 三个正式提交并重建 Base；
不能在已选择的进程中卸载 metadata 或重新绑定模式。未包含此功能的旧 Base 不能
只通过热更 DLL 增加此能力。

五个候选工作树和三个当前正式 checkout 均 clean，没有创建 stash。C 盘曾耗尽，
本任务旧 as/as2/as3/as4 项目与 Library 缓存、ar1 失败项目已移至
`E:/hybridclr-aot-mode-cache-20261006` 保留；报告、旧 Player 和当前 ar2 证据未删除。
