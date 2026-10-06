# 启动选择正式候选：实现边界

库提供 `SelectExecutionMode` 和 `GetExecutionMode`。普通 AOT 启动壳先读取项目配置，
选择成功后再加载补充 AOT metadata 和 Current。下一次启动的配置、HTTP 和重启提示
全部由项目实现；无需给 Android、iOS 或 WebGL 增加库内原生启动器。

选择在 IL2CPP metadata 锁下串行执行。模式和 83 个扩展入口共用一个不可变函数表；
初始化后一次 release 发布，调用方 acquire 读取。第二次选择返回 AlreadySelected。
项目必须等选择调用返回，再启动热更加载、查询和业务线程。

普通解释路径按原 HybridCLR 的程序集、类型、方法、字段身份执行。Base 热更程序集
在 native 初始化时保留私有描述，但不注册为公开程序集。解释模式通过标准 Assembly.Load
加载 Current；DHE 模式才注册 Base 并使用原有 DHE loader。提前加载、错误 loader 和
generated AOT 使用 Base 热更 metadata 都在入口拒绝。

对 opt3 之后共享代码的审计结论：

| 位置 | 普通解释路径 |
|---|---|
| 方法、类型、字段、反射、虚调用扩展 | 绑定 identity/false/普通字段 token 查找，不进入 DHE overlay 实现 |
| `Assembly.cpp` SUPERSET | 使用保留自 opt3 的 TraditionalAOTHomologousImage，不构造 DHE hidden interpreter image |
| `InterpreterImage` 分阶段初始化 | 普通加载仍依次执行全部阶段；无 homologous image，无 DHE batch 标记 |
| 跨程序集类型准备 | DHE loader 才建立 thread-local PreparationScope；普通加载没有该准备集合 |
| `Image` token 与执行类型 | 普通表返回原始类型/方法；body image 与 resolve image 回到同一普通 image |
| `TransformContext` 调用与字段 | 普通解释方法仍使用解释入口；frozen source 为 false，不生成 DHE 字段或 frozen 校验路径 |
| 泛型、finalizer、特性编码 | 保留与 DHE 状态无关的 correctness 修复，不复制另一套完整解释器 |
| 反射缓存 | metadata/execution 身份在普通模式一致；缓存结构仍为共享实现，不能声称二进制等同 opt3 |

这项隔离覆盖 DHE 扩展和热更程序集加载路径。解释器、GC、IL2CPP 的共享缺陷仍需正常修复；
启动选择不会替换已安装的 native 二进制。

构建检查拒绝普通 AOT 对 deferred 程序集的静态依赖，以及 deferred 程序集中的自动
RuntimeInitializeOnLoadMethod/MonoPInvokeCallback 注册。场景、Resources、preloaded
assets 中预先序列化的 deferred 对象也被拒绝。加载 Current 后动态创建 Unity 组件和
ScriptableObject 是单独验证项。当前测试不等价于完整 AssetBundle 序列化兼容认证。

实验观察器、故障注入入口和无条件 lab internal call 已从 runtime 删除。DHE 诊断计数
在生产构建关闭，ResetForTests 仅在测试/诊断宏内存在。测试程序及 Windows 内存采样
位于 lab，不进入用户 package。函数表仍有一次原子读取和间接调用成本，必须实测。
已通过选择表进入的 DHE guard、虚调用和接口调用内部使用直接调用，避免重复查表。

同一份兼容 Current DLL 供两种模式使用；传统加载不依赖 MV。运营不需要两套业务资源，
但每次发布需要让同一组业务用例分别通过两种模式。Base 构建时开启此能力，已发布且未
包含此能力的 Base 仍需更新安装包。

原生编译兼容与启动选择资格分别记录。Unity 2021 和团结现有维护线的功能关闭编译检查，
不代表这些维护线已经具备 Unity 2022 的 deferred 注册/发布接入。移动端及 WebGL 的
Player 正确性、ARM64 内存序和性能证据也不能从 Windows 结果外推。
