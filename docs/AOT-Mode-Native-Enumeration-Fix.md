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

## 已实现的改动

Unity 2022 il2cpp_plus 提交 `135f18fd5a26906e7800e0c4313826eaf2a7756c`：3 个文件，
50 行新增、2 行删除。`il2cpp-api.cpp` 将两个公开接口接到 `Image::GetPublicTypeCount/GetPublicType`；
`Image.cpp` 增加按程序集缓存的逻辑视图，`Image.h` 声明这两个内部实现入口。
HybridCLR 保持 `da9403a8c0f6421cd9afa45fdb234c867e0dac9f`，package 保持
`da9eac383cd953aab58ad0a8c7d851db5e0a36dc`，均没有新增改动。

后续每个 index 只做现有 DHE 身份查询、锁内缓存查找和 vector 索引，不重复遍历类型或分配列表。
首次枚举会构造整个可见列表，因此 count 本身不再是纯字段读取；完整一次遍历的类型工作为线性，
缓存查找随被查询程序集数为对数复杂度。未向解释器执行循环、模式选择或普通托管反射加入新缓存。

## 新身份验证

原始产物位于 `E:/hclr/ar7`，原 ar6（含失败复现）保持不变。Hotfix fixture 新增 Base-only
RemovedType/RemovedNested 和 Current-only Nested/AddedGeneric，用相同 Current DLL/bundles 跑各对照。

- 正常 DHE、解释兜底、功能关闭 DHE、独立 opt3：原生枚举完整位图 255，包含新增、嵌套、泛型、
  删除类型过滤、无重复/空项、稳定顺序和 image 身份检查。
- 12 个线程并发查询得到一致结果；加载 Current 前先枚举 Base，再验证加载后的最终视图，避免缓存旧 Base。
- opt8 原生枚举位图保持 192，明确作为缺陷负对照；不计入枚举正确性通过。
- 6-profile 启动、原生名称查询 63、Unity bundle 1298 均通过；完整回归 8 个正对照各 220/220、差异 0。
  opt8 两组仍准确保留原 P/Invoke 的两个失败。
- Unity 2022.3.62f3 实际 headers：选择开/关 compile + CTest 通过，Image.cpp 已加入 compile gate。
- 新身份的真实 Player 场景拒绝门禁通过；package 资产 9 项、依赖 7 项沿用相同 package/validator 的已有证据。
- 性能检查器 12 项正反测试通过。完整性能及最终准备状态见 candidate lock。

完整回归的“无补充”仍只表示核心库/BoundaryContracts；启动 smoke 仍补充 StartupAotSupport。
原生探针、并发测试只在 lab correctness 中运行；性能 Player 的 benchmark 场景不调用探针。

## 实现复核

缓存只接受已经发布 DHE 的非 interpreter image，隐藏 Current image 和普通解释路径保持物理索引。
Base canonical image 和 Unity 公开别名以同一个 assembly 为键，不各自生成一份列表。
完整构造后才 emplace，异常不会留下半成品；返回的是稳定 map 节点中的不可变 vector，后续添加其他
程序集不会使其失效。重复注册同一程序集由既有 DHE registration 拒绝，不需要新增全局 epoch。
读写都经过 metadata 锁，DHE 是否完成的判断沿用 acquire 发布语义，未用 x64 实测替代 ARM64 资格。
内部 GetTypes 仍只在原物理枚举之后追加一次 supplemental types，避免新增类型被重复追加。

本轮只做本地版本准备，不打标签、不推送、不切换 package 清单。Android/WebGL/iOS Player 仍未验证。
