# Unity 2022 启动选择的实施方案

本轮发布范围：Unity 2022，upstream 保持 package/HybridCLR 8.13.0、il2cpp_plus v2022-8.11.0。
使用 Windows IL2CPP 验证。Android/WebGL/iOS 共用库接口，没有增加平台启动器；其 Player 资格另行记录。
Unity 2021 和团结不属于本轮实施或发布前置范围。

## 项目接入

1. 构建 Base 时启用 enableAotModeSelection，将需要延期注册的 DHE 热更程序集列入配置。
2. 使用项目已有普通 AOT 启动代码读取本地/远程配置，在任何热更程序集加载、查询或业务线程启动前调用 SelectExecutionMode。
3. 成功选择 DHE 后走已有 DHE loader；选择 Interpreter 后用普通 Assembly.Load(Current DLL)。
   两条路径使用同一份兼容 Current DLL 和资源；原 HybridCLR 需要的桥接及补充 AOT metadata 仍须具备。
4. GetExecutionMode 查询本次选择。后续 Select 返回 AlreadySelected，进程中不能卸载再切换。
5. 配置后来改变时，游戏保存下次选择并提示重启。持久化、HTTP、平台退出/重开由项目负责。

普通 AOT 启动壳不能静态依赖被延期的热更程序集。可通过普通 AOT 定义的接口/契约在加载后交互。
热更组件和 ScriptableObject 可在 Current 加载后创建或从 bundle 加载；Base 自身不能预先序列化这些对象。

## 库内只有三层工作

| 层 | 实际实现 | 发生时机 |
|---|---|---|
| 延期公开 Base | MetadataCache 初始化时保留描述，暂不注册配置中的热更 Base；DHE 选择才注册，解释选择保持隐藏 | native 初始化、一次选择 |
| 一次绑定 | 在 metadata 锁下选择不可变函数表，把模式与目标一起 release 发布；读者 acquire 读取 | 一次选择，查询/扩展调用读取 |
| 两路执行 | DHE 表指向现有 DHE 扩展；解释表返回原身份/普通查找，不使用 DHE overlay；传统 SUPERSET 使用原实现 | 加载及既有扩展入口 |

Unity 原生资源系统会在 AOT 启动入口之前缓存程序集 image，所以还要保留既有 HybridCLR 的空占位
image 作为 Unity API 的稳定句柄。此时它不包含 Base 类型，managed 查询仍隐藏 deferred Base。
DHE 选择把 Base 的 image 视图绑定到该占位对象；解释模式由原 Assembly.Load 填入 Current。
Unity 的 assembly/class image 查询统一返回这一稳定句柄，内部 DHE 元数据仍使用原 canonical Base。
该边界由 Unity 2022 的 IL2CPP API 接入处理，不引入平台启动逻辑。

执行指令循环没有新增模式分支。已有 DHE 扩展入口使用同一张选定表，热点仍有原子读取和间接调用，
不能宣称零开销。功能关闭时保留原直接调用路径。进入 DHE 实现后的内部调用直接绑定，避免重复查表。

“启动入口+选择冻结”本身很小。完整 diff 较长的原因是已有 DHE 行为横跨类型、方法、字段、反射、
虚调用等 83 个扩展入口，必须逐一隔离，才能避免解释模式仍落入 DHE 逻辑。
AotModeHooks 表与 inventory 大部分由生成器产生；MetadataModule/DheRuntime 的许多改动是机械重命名/转发。
另保留约 500 行原 SUPERSET 实现；没有复制整套解释器，也没有 Android/iOS/WebGL 三套启动实现。

## 本轮 review 修复的边界

- package：序列化遍历对 managedReferenceId 去重，排除 Editor 专用 Resources；只检查真正 Player 构建的场景。
  Unity 的 bundle scene 回调同样提供 report 且 isBuildingPlayer 为 true，因此由 Player preprocess
  记录 report 身份，SessionState 跨脚本 reload 保存，postprocess 清除。
- runtime：临时 image 负责 DLL/PDB 所有权；解析与模式校验通过后才分配永久索引，发布时移交所有权。
  错误 loader 拒绝不会泄漏副本或耗尽索引，执行热点没有新逻辑。
- lab：循环/共享引用、正反资产边界、bundle 构建与运行、重复拒绝后合法加载等回归。
  并发/计数/内存采样均属于测试 Player，不进入库生产执行路径。

## 验收与发布

同一 candidate Player 分别选择两路，与固定 DHE opt8、独立传统 Player、CLR 对照。
完整 Current DLL 和 bundle 身份锁定；功能开启/关闭编译及 Player 边界单独记录。
性能测量包括选择到首次入口，正常 DHE steady-state 和解释模式；旧报告只能作为旧提交证据。
220 项完整托管套件分别验证核心库/BoundaryContracts 补充 metadata 开启和关闭。
独立的启动 smoke 仍补充 StartupAotSupport，不能把这个组合称为整个进程完全没有补充 metadata。

源码按 runtime、Unity 2022 hook、package、lab 独立提交。完成当前身份门禁后合入对应正式维护线，
发布 runtime annotated tags，再更新 package 的 Unity 2022 清单与 lab lock；package 不打 opt tag。
不更新本轮未实现的引擎条目。

业务回滚是保存下一启动 Interpreter 并重启，不能回滚共享解释器/IL2CPP 本身的缺陷。
源码回滚恢复 opt8 Unity 2022 runtime/package 组合并重建 Base；没有此能力的旧 Base 不能只靠热更 DLL 增加开关。
