# Current storage and execution metadata: native checkpoint

本轮是已有 struct 布局演化的原生基础实现，尚未形成可交付的结构体热更能力。
Unity 2022、团结 2022 的真实 headers compile/CTest 有条件通过；没有本轮身份的
Windows Player、性能、内存或 ARM64 结论。公共资源加载流程仍未传入 Current
storage plan，C# 工作流的已有值类型布局拒绝检查仍然保留。

## 精确身份

| 组件 | commit |
|---|---|
| HybridCLR | 9b980c6c0206b1bcdc882db9e7a43142877cf37d |
| native 测试和锁 | fdb1823db5f091632d22a789244040d050323c7a |
| Unity 2022 IL2CPP，复用 | 3482c81998b5e382b59a3d6f4e5fec2d0988d22b |
| 团结 2022 IL2CPP，复用 | df8d0123d9f5a9fce79283fe5261dba21e802aa0 |
| package，复用 | bc319e5e376d97ed89fdd0f127d4256fa466f1ef |

HybridCLR canonical source SHA-256：
3083E4B03A6EEC07D660ADFB9F1D15CF62D8F5F577086E497689F2059EAE830A。

源码及测试在上述提交 clean 时构建。报告提交只增加说明，不替代测试身份。
组装和 native gate 仍用归档 host-native-descendants-bound/HybridCLR.DheTool.dll。
没有改 CAT、正式维护分支、runtime tag、Installer 默认版本或 package 源码。

## 本轮实现及 review

- BuildCurrentImagePlan 按稳定身份匹配 Base/Current 类型与方法，保留各自 token。
  选中的已有存储类型自动包含其已有可执行成员；显式选择额外方法用于处理 IL
  不变但依赖布局变化的调用者。计划绑定两份 DLL hash，不改变 MV 指纹或 schema。
- SuperSet 保留 Base 类型用于逻辑身份和方法签名匹配；InterpreterImage 在解析
  签名和布局前，根据选择使用物理 Current 类型。选中类型的字段使用 Current
  FieldDefinition，不复用旧偏移，也不注册 reference sidecar。
- 内部 Assembly 加载入口可接收计划，先校验 DLL hash 和 token 范围；方法发布前
  重验 MV 选择、隐式成员覆盖、Base 类型和方法身份，再导出 CurrentMethodExecution。
  公共 RuntimeApi 已消费镜像导出的绑定，但尚未产生/传入这些计划，因此此入口
  目前仍走无计划路径。不能把内部接口存在写成资源加载能力已经开放。
- 调用、newobj、函数指针和 constrained call 的转换使用执行签名查询。
  Review 发现共用 token cache 还服务 ldtoken，因此将执行查询移到独立入口，
  保留缓存中的逻辑方法句柄。这个源码修正有 compile gate，尚无相关 Player 实证。
- 继续使用既有方法准备快照、异常回滚和 release/acquire dispatch 发布。
  本轮不证明整个 metadata image/cache 加载事务可回滚。

## 验证与证据限制

以下路径相对 C:/hybridclr_optimize/artifacts/dhe-evolution-20260908。

| 证据 | 范围 |
|---|---|
| runtime-current-storage-reviewed/{profile}/runtime-manifest.json | 锁定上表源码、真实 Editor headers 与组装内容 |
| native-current-storage-reviewed/{profile}/native-gate.json | 两引擎 passed=true、mergeReady=true、surrogateExternalHeadersUsed=false、nativeExitCode=0 |
| native-current-storage-reviewed/{profile}/native-test.log | 原生计划选择、已有 dispatch/回滚测试、错误 Base frame 的实际异常拒绝 |

Unity gate SHA-256：
AD53B650270088415723EB0ACAA088DE87399896907B781C6BD890F7955B78F0。
团结 gate SHA-256：
9773C5736497D072137AD02CBEA87B689C4457C5234C074A061E307418815676。

审计已重新计算两个 runtime manifest 的 hash，与各 gate 的引用相同，并核对
HybridCLR commit、dirty=false、真实 headers 和 nativeExitCode。
两个执行 session 已输出通过结果，随后确认句柄已不存在，没有遗留构建进程。

新增单元测试覆盖同一 Current 对不同 Base 的 token 选择、类型成员隐式选择、
IL 不变的方法选择、no-op、跨版本 token 碰撞、重复/未知 token、错误程序集、
缺失身份、值/引用种类变化和 native-only 成员拒绝。失败不修改输出计划。
测试还检查 Current 执行指针、未变化方法保留、tombstone 查询不提前抛异常。
旧 typed Base frame 实际调用 ShouldDispatchToInterpreter，要求抛指定
ExecutionEngineException；这通过默认关闭的线程局部 fake-VM 异常捕获验证。

存储相关 metadata 源文件在 native gate 中是编译目标，未在这个 standalone
测试进程中加载真实 Current DLL。不能用这些单元测试证明字段复制、数组步长、
装箱、GC、反射或普通 AOT 边界已经正确。

保留中间 checkpoint：runtime-current-storage、native-current-storage 对应
HybridCLR d5fa324 和 lab 6d5bd23；二者也通过，但在逻辑/执行 token cache 拆分之前。
上一轮 runtime-value-execution-rollback/native-value-execution-rollback 对应
HybridCLR c0247f4，验证了 Current 方法准备和抛异常时的回滚，不是布局 Player 证据。
所有更早的失败报告、Player、DLL/MV 和 stash 均保留。

## 后续关键路径

1. 接入完整 hotfix 集合的 per-Base 依赖计划。除方法体外，必须涵盖内嵌值字段、
   静态字段存储、泛型实参、调用签名和布局受影响的调用者；不能仅取直接改变的 struct。
2. 分阶段准备跨程序集 Current 类型视图，解决尚未加载的依赖和循环引用被提前缓存
   成 Base 类型的问题。当前 TypeRef 路径只可查询自身和已经准备的外部镜像。
3. 完成泛型上下文/表示转换和普通 AOT 边界。普通 AOT DLL 不允许更新；旧 native
   struct 复制与内嵌存储不能通过只转换旧字段来保留 Current 新字段语义。
4. 把冻结的完整 value-layout fixture 接入两引擎 Windows Player，逐项执行复制、
   新增引用、数组、装箱、byref、Nullable、跨程序集及 NativeRoundTrip。
5. 冻结能力足够的新 Base，再验证新旧布局 Base 消费同一最新资源的连续/跳版本流程。
   一个缺少原生布局能力的历史 Base 无法仅靠 DLL/MV 获得新增 C++ 实现。

前一个完整 Windows checkpoint 仍是 HybridCLR 760634e 与 lab 821b011；其十 Base
报告重新确认 passed=true、30 个唯一 PID、71 组演化/次、failedRuns=0，文件 hash
仍为 DD7DC5FA3AB705F3D2CC2D7548D50A023A511B048C6CFA028DBE3FEA45DD6119。
该报告及 220 differential cases 的历史结果不能转写为本轮 Current storage 结果。

回滚边界是独立 research/dhe-value-layout-v8.13.0 工作树；当前候选未合入主研究线。
可回到前一个完整 Windows 源码/产物组合及其兼容资源，保留本轮候选用于继续研究。
