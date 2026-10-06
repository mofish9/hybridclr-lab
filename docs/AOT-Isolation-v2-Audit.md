# v2 隔离边界审计

传统参照：HybridCLR opt3 `f40c6f08ccd0391ad9285276b4cc21ada3a180ab`，
Unity2022 il2cpp_plus opt3 `bf15337e189ae7da5876aa51c9b896a36c52a155`。
这是判断语义的参照，不是声称候选整个 native 二进制已回到该版本。

当前以一个不可变函数表绑定 83 个入口，完整清单见 runtime 的
`hybridclr/AotModeHooks.inventory.json` 和 lab 的 `scripts/aot-hook-fallbacks.json`。
工具必须为每个入口找到唯一实现；metadata 新入口没有显式 fallback 时生成失败。
函数表和模式共用一个原子指针，MetadataCache 的选择在 g_MetadataLock 下串行化，
注册 Base 后进行一次 release 发布，入口以 acquire 读取得到完整表；不逐项修改函数指针。

已处理的边界：

| 范围 | 传统绑定 |
|---|---|
| DHE 方法、类型、字段、属性、事件、反射、虚调用扩展 | identity、false、无 overlay |
| 解释器方法体与 token resolve image | 保留 opt3 的普通解释器/AOT 补充 metadata 查找 |
| 原生 type handle、引用分配、声明类型等价 | 普通原始类型，关闭 DHE 等价关系 |
| Image 的物理类型映射、泛型执行映射、新增方法回退 | 返回原类型/方法，不运行映射实现 |
| 字段 token 查找 | 独立保留 opt3 查找，不执行 Current 字段 overlay |
| 模块初始化 guard | 未选择与传统模式不启动 Base 热更模块 |
| DHE loader 与普通 Assembly.Load | 入口校验；只有正确模式能发布对应 metadata |
| 普通 AOT 静态依赖与热更 Unity native 注册 | 构建拒绝 |

审计发现的共享改动，仍不能声称已完全恢复旧代码：

- `metadata/Assembly.cpp` 的普通 SUPERSET 补充元数据加载仍构造解释器 fallback image。
  它属于 DHE 后引入的共享改动；当前 Player 用例不加载补充 AOT metadata，不能覆盖此路径。
- `metadata/InterpreterImage.cpp` 的分阶段 metadata 初始化、跨程序集 finalizer 传播、
  特性字符串/枚举编码与类型名称解析存在 opt3 之后的共享改动。
- `metadata/Image.cpp` 的签名与缓存逻辑、`TransformContext.cpp` 的 AOT/解释调用选择，
  以及 `GenericMethod.cpp` 的缺失 AOT 实现判断需继续逐项审计与 differential。
- 反射参数缓存键现在保留 metadata/execution 两份身份。传统绑定使二者都回到原方法，
  但缓存结构与分配行为并不等于 opt3；内存与并发门禁尚未给出发布结论。

因此，故障注入通过只证明这 83 个已绑定实现不会被传统测试链路进入。
它不证明共享解释器、GC、IL2CPP 或尚未隔离的 metadata 代码故障可以被此开关恢复。
`meetsFullFallbackRequirement` 和 `mergeReady` 保持 false，直到这些剩余边界完成。

维护要求：新增 DHE 行为必须明确落入绑定边界，或给出传统路径的等价性证据；
不能仅因函数名没有 Dhe 就把共享修改排除在审计之外。热更业务资源保持一份，
运行时库维护与 CI 需要承担两条路径的验证成本。
