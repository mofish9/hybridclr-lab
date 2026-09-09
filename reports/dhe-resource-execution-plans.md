# DHE 资源选择计划接入状态

本轮已将 per-Base executionPlan 接入 package 资源校验与加载，并增加 C# 计划生成器、
staging 一致性检查和资源 schema。宿主验证有条件通过；完整资源构建到 Windows
Player 的闭环仍未验收，布局更新的发布门禁仍关闭。目标仍是原 hotfix 程序集在 Base
中 AOT 化后支持后续资源热更，并让不同 Base 使用最新 Current；没有缩减为只改方法体。

## 实现

- DheExecutionPlan 绑定精确 Base MV/Current MV SHA-256，记录有序、唯一的 Current
  类型和方法 token。新增方法不冒充已有 Base 入口，普通 AOT 程序集不参与 hotfix 注册。
- manifest、validation 和 runtime plan 的每个 Base 选择表必须一致。package 使用
  嵌入 Base MV 再校验选择，批量加载和重试都传递同一计划。没有布局计划时保留原 MV 路径。
- 生成器识别未修改 IL、但因值布局依赖而需要解释执行的方法；保留无关方法的 AOT 资格。
  实例字段布局只在有明确物理类型选择时放开对应兼容性检查，其他类型/引用身份检查保留。
- 静态值字段、普通 AOT 值布局/调用边界、没有 IL 实现的存储类型成员会列出独立义务。
  resource-update 将候选选择写入兼容性报告，staging 同时核对选择及 MV 哈希。
- 三份资源 schema 记录 executionPlan。修复了 runtime-plan schema 历史重复 allOf
  导致发布条件被覆盖的问题，合并保留发布条件与 Base 选择条件；重复键回归现已覆盖。

## 源码与证据

| 仓库 | 候选分支 | commit |
|---|---|---|
| package | research/dhe-evolution-v8.13.0 | c439f2733ce23a33ef1fe6ecbc873c962f124d8c |
| lab 构建/验证源码 | research/dhe-value-layout-v8.13.0 | f7e53d4fa43ca5a5cd2248061d5baa98781799e0 |
| HybridCLR（本轮未改） | research/dhe-value-layout-v8.13.0 | 0b5252cde1192194b17705d037ef5c9dab762701 |
| Unity 2022 hooks（本轮未改） | research/dhe-evolution-unity2022-v8.13.0 | 80ee2ce13208b0f8b1984e1d78b3a29105472160 |

package canonical tree SHA-256：4AF497DEC789989EEC55E10C61211303A1496481B789B7B09B78B9C8AAD0F34C。
lab 的 package/runtime/repo locks 已同步此身份。候选在提交、工作树 clean 后构建验证；
后续报告提交不换绑证据。

证据目录：C:/hybridclr_optimize/artifacts/dhe-resource-execution-20260909。
workflow-host-final.json：49/49 检查通过。宿主包含实际 package C# 源码，以适配器提供
JSON/资源读取并记录原生调用参数；这些调用没有执行 IL2CPP，不能计作 Player correctness。

- 新旧 Base 共用 Current；传入顺序改变仍选择正确 token；不需要布局计划时保留 MV 路径。
- 错误 Base/Current MV 绑定、缺失/重复 token、重复 Base、三份表不一致均在原生调用前拒绝。
- 校验失败后不调用 Reset 的有效重试通过。
- 编译器与 package 绑定格式一致，未改动的布局依赖方法被选择，无关方法未被选择。
- 静态值字段与静态引用字段区别、普通 AOT 义务、无 IL 成员及新增方法分类通过。
- staging 的真实规范化函数、三份真实 schema 的合法/非法计划、重复键和发布条件检查通过。

宿主 DLL SHA-256：56ED430BE857773DEE3D73FDBAD7C5D81C72188FF66C7498F5753BBADA375703。
DheTool DLL SHA-256：CE30E9D8689B41E33055D78AE92D7388000FC9208DBF28532BBF37B5B08DA38A。
报告还记录了 package 的四个编译输入文件哈希。
legacy-impact.json 为既有 35 项影响分析回归，35/35 通过；它不证明本轮 package 原生加载。
上一轮 Windows 五种场景及 27 项审计仍属于其原 package 提交 710f969，见
dhe-public-storage-api.md，不能换成 c439f27 的 Player 证据。

复现：在 lab 使用 dotnet build tool/fixtures/execution-plan/ExecutionPlanTests.csproj
-c Release -p:DhePackageRoot=<package绝对路径>；运行生成的 ExecutionPlanTests.dll，依次
传入上一轮 public-reflection 证据目录、新报告路径、实际 HybridCLR.DheTool.dll 路径。
程序还生成新报告路径加 .inputs 的二进制 mutation fixtures，拒绝覆盖现有输出。

## 尚需推进

1. Base 归档目前绑定 DHE DLL 和普通 AOT 名称集合，没有与 Base 身份绑定的完整普通
   AOT 分析快照。生成器无法据此证明不存在遗漏的原生布局调用，因此输出
   current-storage-aot-boundary-inventory-not-bound 并拒绝发布布局资源。补齐捕获、归档、
   身份与最终 Player 一致性验证后再解除该门禁。普通 AOT 快照用于分析边界；热更范围
   仍是原 hotfix 集合。设计快照时须处理生成的 BuildIdentity 本身会改变普通程序集字节
   的循环依赖，不能简单把最终程序集哈希塞回它自己后宣称身份一致。
2. 现有探针用 Unity stripped Base 对比 SDK 生成的 Current，含框架引用重定向和程序集
   元数据差异。本轮最初将其误当成完整兼容性输入，三个检查失败；修正测试后明确只验证
   布局选择解除对应布局拒绝，同时要求保留引用身份拒绝。公共闭环必须使用正式 Unity
   工作流的 Current 编译产物，不能用关闭所有引用检查来迁就探针。
3. 补充静态存储及普通 AOT ABI 的实际执行能力和有区分力的测试，再运行完整公开资源
   构建/staging/package loader/Windows Player 流程，验证多 Base、跳版本与失败恢复。

没有修改 CAT、正式分支、runtime tag 或 Installer 默认版本；本轮四个相关工作树 clean，
未创建 stash，也未删除产物。C 盘约余 195 GiB。继续 Unity 2022 优先，之后才移植团结；
无性能、内存或 Android/iOS/小游戏生产发布结论。

回滚本轮扩展可使用 package 710f969 与 lab 12f4e1e 的上一组候选和对应锁；native 两仓
不变。不要只回退 package 而保留要求 executionPlan 能力的新资源。当前没有线上发布。
