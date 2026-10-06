# Unity 2022 启动选择：后续 review

审查身份：HybridCLR `da9403a8c0f6421cd9afa45fdb234c867e0dac9f`、Unity 2022 il2cpp_plus
`124f90294beb45a3c4cba8a525ac2488f82170f8`、package `da9eac383cd953aab58ad0a8c7d851db5e0a36dc`。
检查已提交实现；本轮不改运行时、不发布。

## 新发现：P2 原生按索引枚举仍遗漏 Current 新增类型

`il2cpp-api.cpp:1419–1426` 的 `il2cpp_image_get_class_count` / `il2cpp_image_get_class`
直接调用 `Image::GetNumTypes` / `Image::GetType`。后者只返回 Base 的 `typeCount` 和定义表项，
没有像 `Image::GetTypes` 那样追加 DHE supplemental types。

当原生插件使用这对 API 扫描程序集注册类型时，Current 新增类型不会被发现；同一 image
经 `il2cpp_class_from_name` 却能查到这些类型。这里不把可工作的按名称查询或 bundle 加载说成失败。
托管反射 `Assembly.GetTypes` 使用另一条合并路径，不受这个遗漏直接影响。

在保留原 GameAssembly 和 global-metadata SHA 的 Player 副本上，用独立测试 DLL 验证：

| Player / 模式 | 原生名称查询 | 原生枚举缺失的类型 |
|---|---|---|
| 当前 candidate / DHE | 5 个既有/新增类型全部可见 | AddedType、AddedWorker、AddedData |
| 同一 candidate / Interpreter | 全部可见 | 无 |
| 当前 runtime，关闭选择 / DHE | 全部可见 | AddedType、AddedWorker、AddedData |
| 独立 opt3 / Interpreter | 全部可见 | 无 |
| opt8 / DHE | 全部可见 | AddedType、AddedWorker、AddedData |

因此这是继承自 opt8 的 DHE 缺陷，不是本轮模式选择的新增回归，也不是 da9403a 的别名修复失效。
影响使用原生枚举的插件/引擎接入，现有 fixture 的资源加载和 managed correctness 仍通过。

建议统一这对公开 API 的逻辑类型视图，覆盖新增类型与删除类型，并保持 count/index 顺序一致。
不要直接改 `Image::GetNumTypes/GetType` 为合并枚举：`Image::GetTypes` 自身调用它们并追加
supplemental types，简单修改会重复追加。也需避免每取一个 index 都重新构造整个类型集合。
本轮尚未实现此修复；删除类型属于建议补测边界，没有冒充已经复现。

## 补充验证通过的边界

- 选择前主动查询并建立空 image 的名称缓存，候选两路均不暴露 Base 类型；选择和加载后缓存不会遮蔽真实类型。
- Entry、AddedType、Worker、AddedWorker、AddedData 的 `class_get_image` 都等于选择前缓存的公开 image。
- 这 5 个类型的 `class_get_type` → `class_from_type` 往返和类名一致，枚举出来的已有类也具有正确 image 身份。
- 上述 5 组进程原有 21 项 correctness、mask=63、bundle=1298 均通过。
- 静态复核一次性模式发布、传统 SUPERSET 分流、错误 loader 所有权、模块初始化 guard 和构建依赖/资产校验，未确认其他新增 P0/P1。

这里只证明检查到的边界。没有重跑性能或将测试 DLL 的执行成本加入生产性能结果。
本轮没有改生产源码，原 ar6 性能身份有效；扩展探针只装入独立 Player 副本，冻结产物未修改。

## 证据及准备状态

证据目录：`E:/hclr/ar6/review-followup-20261006`。
`expanded-summary.json` 保存精确 build 身份、探针和运行脚本 SHA、5 组逐项观察；
`run-expanded.cjs`、`probe/Lookup.cpp`、原生 DLL 和逐进程 JSON/NDJSON/日志均保留。
早期 `summary.json` 只检查身份，没有列出枚举类名；最终判断使用 expanded-summary。
探针首次编译缺少 initializer_list 头文件，补齐后通过，未将该编译错误归为产品问题。

上轮“准备完成”在既有门禁范围内成立，但没有覆盖原生枚举。当前保留源码候选和全部通过证据，
撤回不带该限制的定版状态，新增 1 项未解决 P2，等待修复或明确处理其发布范围。
这不是 Android/WebGL/iOS 的外部门禁，也不是要求现在打标签。
Unity 2022 / Windows 范围保持不变；未打标签、推送或切换安装清单。
