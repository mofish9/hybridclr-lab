# Unity 2022 opt9 发布前 review

范围仅为 Unity 2022；固定上游为 HybridCLR/package 8.13.0、il2cpp_plus v2022-8.11.0。
Unity 2021 和团结既不实施，也不作为本轮发布前置条件。Windows IL2CPP 是已具备的 Player 验证环境。

本次任务是准备版本，不是执行发布。该范围内的源码、验收、实施说明与回滚准备已经完成。
下文的 opt9 tag 和安装清单仅是发布预案；创建标签、推送或切换安装清单须等待用户另行决定。

## 正确性与边界

本轮发现并修复的发布阻断项：

1. Unity 在 AOT 入口前缓存 image，原延期方案让热更 bundle 组件丢失脚本身份。
   现在保留稳定空 image，选择 DHE 时绑定 Base 视图，解释模式由普通 Assembly.Load 填入 Current。
   同一 prefab、ScriptableObject、additive scene 在两路均通过，Current Awake 合计结果为 443。
2. DHE 的普通 CALL 判定漏掉现有解释标志，导致普通解释 DLL 的 P/Invoke/reverse P/Invoke 两例失败。
   修复发生在 IL 转换期；完整 220 例两路均与 CLR/golden 对齐。
3. 资产遍历未处理 SerializeReference 环，且误把场景 bundle/Editor Resources 当作 Base 资产。
   修复遍历与 Player report 归属；真实 Player 中的 deferred 对象仍被拒绝。
4. 错误 loader 先消耗永久 image index，再拒绝。现改为临时所有权与校验后分配；512 次错误调用后合法加载成功。
5. 正常 DHE deferred Base guard 的间接调用造成超门限退化，现直接绑定 DHE 实现。
   普通 AOT guard 保留选定表，保证解释模式正确执行；没有修改指令循环或添加生产计数。

选择在 metadata 锁下完成。先完成 Base 注册/image 绑定，再 release 发布不可变函数表；
mode 与函数目标在同一对象中，读者 acquire 获取。选择完成后才能开始热更加载、查询和业务线程。
并发选择测试为 12 个调用中仅一次成功，其余 AlreadySelected；本进程不能切换第二次。
普通 AOT 静态依赖、Base 序列化对象和 deferred 自动启动/native callback 在构建阶段阻止。

这里的兜底恢复普通 HybridCLR 加载、解释执行和原 SUPERSET 语义，不能隔离共享解释器/IL2CPP 的所有缺陷。
不复制两套引擎，不承诺把任何不兼容的 Current DLL 变成可用资源。

## 精确源码身份

| 仓库 | 目标正式维护线 | 已验收的源码提交 |
|---|---|---|
| hybridclr | optimize/v8.13.0 | 643c4d537eb9ceb5fc40a45a2f3342312d17907e |
| il2cpp_plus | optimize/unity2022-v8.11.0 | 124f90294beb45a3c4cba8a525ac2488f82170f8 |
| hybridclr_unity | optimize/v8.13.0 | da9eac383cd953aab58ad0a8c7d851db5e0a36dc |

各候选是对应 opt8 维护线的后继，可 fast-forward，不需要冲突合并或重新生成运行时提交。
package 无 opt tag；以上 package 提交是功能源码身份，最终安装清单提交将在 runtime tag 可从远端解析后产生。

## 性能结论及取舍

正常 DHE 与功能关闭路径按原数值门限严格验收。用户明确接受解释兜底额外成本，
兜底仍保留完整正确性、样本数、校验和、程序集/二进制身份要求；原失败报告不改写。

正常 DHE 的 400 对测量包括最先 100 对中的全部慢样本。Windows 回收数值 PID 导致旧采样器
在完成额外 300 对后拒绝汇总；恢复报告包含原始文件 hash、原 100 对报告、完整启动完成日志及
未改变的构建身份。两侧不同 PID 数分别为 398/395，保守计数均超过最低 100 个独立进程。
旧采样器没有记录父进程启动时间戳，恢复报告明确披露；新采样器已交叉核对父子 PID 并记录启动/退出时间。

| 正常 DHE 指标 | opt8 对照 | 候选 | 变化 |
|---|---:|---:|---:|
| native 微基准 P50 | 1.2822 ms | 1.2267 ms | -4.33% |
| changed 微基准 P50 | 1.0610 ms | 1.0677 ms | +0.63% |
| virtual 微基准 P50 | 1.77655 ms | 1.80510 ms | +1.61% |
| 进程到入口 P50 | 127.38685 ms | 126.65435 ms | -0.58% |
| 进程到入口 P99 | 153.27874 ms | 146.99963 ms | -4.10% |
| 选择到入口 P50 | 3.36725 ms | 3.46980 ms | +0.10255 ms |
| private bytes P50 | 139325440 B | 139333632 B | +8192 B |

上述稳态、启动 P50/P95/P99 与内存均在原门限内；完整配对差异、MAD 在冻结 JSON 中。
首次入口计时包括反射查找、调用及动态 Unity 对象创建；不是整个游戏性能或全部反射 workload 的声明。

解释兜底 100 对相对独立 opt3：启动 P50 110.9704→124.94835 ms（+13.98 ms），
virtual 微基准 P50 5.0802→5.7197 ms（+12.59%），private bytes P50 增加 4165632 B（约 3.97 MiB）。
这不代表整个游戏慢 12.59%。不再为追平独立 opt3 而扩大改动。

候选 GameAssembly.dll 为 31252480 B，opt8 对照为 30722560 B；约增加 517.5 KiB。
独立 opt3 Player 为 9234432 B，与 DHE Base 的生成代码/布局不同，不能把二者大小差当作本开关增量。

## 验收证据

原始产物位于 E:/hclr/ar5，冻结清单记录输入路径和 SHA-256；历史报告保留。

- 实际 Unity 2022 headers：selection 开/关 native compile 与 CTest 均通过，未使用 surrogate headers。
- 同一最终候选 Player 选择 DHE/Interpreter；smoke 共 6 个 profile，包含重复/非法/并发选择、错误 loader、bundle 运行。
- 完整回归 220/220：候选双模式、功能关闭、独立 opt3，各自核心库/BoundaryContracts 补充 metadata 开/关。
  进程仍补充 StartupAotSupport，不能称为整个进程没有任何补充 metadata。
- opt8 220 例中的 2 个互操作失败作为明确负对照保留，不算正确性通过。
- 最终 package：资产 9 例、依赖 7 例、真实 Player 场景拒绝均通过。
- 最终 package 的功能关闭 Player 单独重建；其原构建及原性能报告保留，不能改写为新构建证据。

## 发布和回滚

仅发布 Unity 2022 时，拟使用 runtime annotated tags `v8.13.0-opt9` 和 `v2022-8.11.0-opt9`。
先推送对应正式维护分支和 runtime tags，确认远端 peel 到上述提交，再更新 package 清单中的
Unity 2022 一项；其他引擎条目保持既有版本。package 不打 tag。最后同步 lab lock/报告。
当前无新 runtime tag、无远端推送；发布状态见随附 candidate lock 和维护线验收记录。

本地三条上述维护线已 fast-forward 到表中提交，合入后重新执行 Unity 2022 实际 headers
开/关编译与 CTest、6-profile smoke 和完整 220 例矩阵，全部通过。功能关闭的最终 package
构建也通过 100 对严格性能验收；所有相关源码工作树 clean。未重建或重新标记正常 DHE 的既有二进制。
最初复用 candidate 的 CMake cache 因 lab 源目录变更被拒绝，随后使用全新构建目录通过，失败日志保留。

此前只读远端检查遇到 GitHub 连接重置/443 超时。远端可用性不构成本次版本准备的阻塞，
不再为此自动重试或推进发布。
`manifests/aot-mode-opt9-publication-plan.json` 记录候选提交和建议 tag 名称；
`aot-mode-opt9-proposed-package-versions.json` 是预备清单，只改 Unity 2022 一项，未应用到 package。
正式发布仍需按顺序重新确认远端、发布 runtime tags、应用清单并记录新 package commit/tree。

每次热更只需一份兼容 Current DLL 与资源，但发包回归应覆盖两种模式。
项目在普通 AOT 入口读取配置并调用 Select/Get；下一次启动配置、HTTP、存储、重启 UX 由项目实现。
运营回滚：保存下次 Interpreter 并重启。没有该能力的旧 Base 仍需先更新 Base。

源码回滚并重建 Base：HybridCLR `9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071`，
Unity 2022 il2cpp_plus `e426adc57c283865126423b169051558b339388c`，
package `f2946d5ba35a879724b76afd857176feb1a4adca`（原 opt8 组合）。
本轮没有新增 stash，历史 FGS/opt3 stash 原样保留。

Android ARM64、WebGL、iOS 的 Player correctness、平台 ABI、内存及尾延迟尚未验证。
Windows 通过只能支持本范围的有条件通过，不能称为这些平台或小游戏生产可发布。
