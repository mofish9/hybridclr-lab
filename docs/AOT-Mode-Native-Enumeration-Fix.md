# Unity 2022 原生类型枚举修复边界

场景：原生插件使用 il2cpp_image_get_class_count/get_class 遍历 Current 程序集。
目标：新增类型可见、删除类型不可见、count/index 一致；选择前和 Current 发布前不缓存最终列表。
范围仅 Unity 2022；项目配置、模式选择、解释循环和内部 metadata token/index 语义不变。

实现限定公开原生 API。仅已发布 DHE 的非 interpreter image 构建逻辑列表，以 canonical assembly
为键让稳定公开别名共享同一列表；保留原生 API 的 <Module> 项，过滤删除类型并追加 supplemental types。
程序集只能一次发布（DheRuntime 拒绝重复 registration），列表在 metadata 锁下完整构建后插入，
后续查询在同一锁下取指针；vector 不再改变、map 节点稳定，随进程存活。无诊断计数。

主验收为 native count/index 的新增、嵌套、泛型、删除、重复及并发首次枚举；普通解释、功能关闭
DHE 同时覆盖。旧 opt8 作为原生枚举负对照，保留其缺陷，不冒充正对照。
性能按既有 DHE/fixed 严格门限，fallback 继续披露成本；单个 index 不重复构造完整类型列表。
每个被枚举 DHE 程序集增加一份类型指针数组及 map 节点，普通解释不建立缓存。
用 Windows IL2CPP 与实际 Unity headers 验证，不外推 Android/WebGL/iOS。

回滚为撤销本次 Unity 2022 Image/API 改动并重建 Base，会恢复已知 P2；业务解释兜底本身不受该缺陷影响。
正式整体回滚仍是原 opt8 三仓组合。修复、验证、review 后更新准备锁，不创建标签或发布。
