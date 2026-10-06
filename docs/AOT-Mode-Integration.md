# AOT 启动选择：候选接入接口

本接口使用普通 AOT 启动代码和 IL2CPP internal call。库不读 HTTP、文件、注册表、
PlayerPrefs 或浏览器存储，不创建各平台启动器。下一次启动的配置由游戏保存。

Base 构建时开启 `HybridCLRSettings.enableAotModeSelection`，并配置
`dheAotAssemblies`。必须使用对应的 HybridCLR、il2cpp_plus 和 package 组合重建 Base。
这个能力不能通过热更 DLL 加到已经发布、没有包含该能力的 APK 中。

在项目已有的普通 AOT 启动流程中，读取本次要采用的模式，再调用：

```csharp
var wanted = ReadProjectStartupChoice(); // 项目读取本地配置或等待 HTTP 的结果。
var status = HybridCLR.RuntimeApi.SelectExecutionMode(wanted);
if (status != HybridCLR.ExecutionModeSelectionResult.Success)
    throw new InvalidOperationException("启动模式选择失败：" + status);

if (wanted == HybridCLR.ExecutionMode.Interpreter)
    Assembly.Load(currentDllBytes);
else
    LoadWithExistingDheResourceLoader(currentResources);
```

`DifferentialHybrid=1`、`Interpreter=2`；查询使用 `GetExecutionMode()`。
选择结果分别是 `Success=0`、`AlreadySelected=1`、`InvalidMode=2`、`NotSupported=3`。
第二次调用即使传入同一个模式也返回 AlreadySelected，重试不会改写本次选择。
Editor shim 返回 NotSupported，不能用 Editor Play 验证此 native 生命周期。

Windows IL2CPP 已使用普通 AOT 的 `BeforeSceneLoad` 回调验证入口。项目可以使用更早的
AOT 回调，但同一阶段多个回调的顺序不应作为保证；最好由一个启动流程明确调度。
异步读取配置时，先等待选择完成，再启动热更加载与热更逻辑。不要让场景或另一启动回调
抢先访问热更代码。

远程配置晚到时，项目比较 `wanted` 与 `GetExecutionMode()`，保存下一次启动的选择，
再决定何时提示玩家重启。库没有“本进程切换”或“保存到某平台”的 API。

同一份 Current DLL 同时供两条路使用。正常 DHE 继续使用原来的 MV 和兼容校验；
传统模式使用标准 Assembly.Load，不读取 DHE MV。无需为两个模式维护两套业务 DLL，
但每次发布的兼容 Current 应分别跑两条路的 correctness 门禁。

当前候选的强制边界：

- 普通 AOT 不能静态引用可切换热更程序集，包括签名、泛型参数、特性与方法体；公共接口
  和启动壳放普通 AOT，通过反射或 AOT 接口调用热更代码。检查在 stripping 前执行。
- 热更程序集不能包含 RuntimeInitializeOnLoadMethod 或 MonoPInvokeCallback 注册。
- 当前拒绝热更程序集中的 MonoBehaviour/ScriptableObject 派生类型；Unity 原生序列化、
  组件注册和资源引用尚未完成双模式验证。普通 AOT 组件可以调用加载后的热更业务逻辑。
- 本次选择发生在 Assembly.Load/补充 AOT metadata 加载前。错用 DHE/传统加载入口会拒绝。
- 引擎可在 AOT 回调之前准备内部 Base 泛型描述符；它们保持未发布。对生成代码的 Base
  metadata usage 发布另设检查，不能以“内部对象已创建”等同于“业务已获准访问”。

正常 DHE 没有新增按模式值判断的每指令分支，但启用此能力的 Base 在扩展入口使用
函数表和 acquire 读取，存在间接调用成本。当前结果没有给出零开销或性能收益承诺。

这是源码候选接口。完整共享代码审计、补充 AOT metadata、Unity 资源与组件、三引擎和
Android/WebGL/iOS 门禁仍需完成，不能把 Windows 的结果作为其他平台的发布证据。
