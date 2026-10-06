# 启动模式选择：近期已提交改动的全面 review

> 本文保留发现问题时的结论和复现证据。后续修复与当前资格见
> [review 修复结果](AOT-Mode-Review-Fixes-Result.md) 和 candidate lock。

结论：暂不建议将当前组合冻结为正式新版本。确认 1 项 P1 运行时回归和 1 项 P2 验收工具问题。
本次发现覆盖了前次测试未包含的边界，因此应撤回之前“版本准备完成”的无条件结论。
这不是网络或打标签问题；本轮仅 review，不发布，也不修改生产实现。

## 审查范围

审查已提交的完整 diff，不以工作树 clean 作为正确性依据：

| 仓库 | 比较范围 | 提交数 |
|---|---|---:|
| hybridclr | v8.13.0-opt8 → 643c4d537eb9ceb5fc40a45a2f3342312d17907e | 15 |
| il2cpp_plus | v2022-8.11.0-opt8 → 124f90294beb45a3c4cba8a525ac2488f82170f8 | 4 |
| hybridclr_unity | f2946d5ba35a879724b76afd857176feb1a4adca → da9eac383cd953aab58ad0a8c7d851db5e0a36dc | 4 |
| lab | 相关生成器、采样/性能门禁、完整回归与冻结脚本，当前 HEAD 5e5848b | 按相关文件历史检查 |

范围保持 Unity 2022，上游基线保持 8.13.0 / v2022-8.11.0。
Windows IL2CPP 是本轮实际 Player 验证范围，Unity 2021/团结不列为前置工作。

## P1：公开占位 image 无法解析 Current 新增类型

引入位置：hybridclr `afe1757` 的 metadata/Assembly.cpp:105–108，及 il2cpp_plus `124f902`
对 il2cpp_assembly_get_image / il2cpp_domain_assembly_open 的接入。

新的 GetDeferredUnityImage 返回稳定的 placeholder image。BindDeferredUnityImage 拷贝 Base image 内容，
但保留 placeholder 的地址，因此它不等于 canonical Base image。
MetadataModule.cpp:674–681 的 GetDheSupplementalImage 仍要求
`homologous->GetTargetAssembly()->image == image`，从而拒绝这个公开句柄。
`il2cpp_class_from_name` → `Image::ClassFromName` 查不到 Base 名字后调用 FindDheSupplementalType，
此时补充类型查找被拒绝，Current 新增类型返回 null。

已在现有最终 Player 上复现，未重建 Player、未改冻结的 Current DLL：

| 运行路径 | 托管反射能找到 AddedType | 原生 API 能找到已有 Entry | 原生 API 能找到新增 AddedType |
|---|---|---|---|
| 同一 candidate Player，DHE | 是 | 是 | **否** |
| 同一 candidate Player，Interpreter | 是 | 是 | 是 |
| 同提交 runtime，关闭选择功能的 DHE | 是 | 是 | 是 |

原生插件只调用现有导出 API：domain_get → domain_assembly_open → assembly_get_image → class_from_name。
返回 mask=3 表示已有类型可见、新增类型不可见；正确结果应为 7。
candidate 两路 GameAssembly SHA 相同：425bcebc7f3ceffe468351dda7fe8af36cf1a7bd9d31e19740868809cd001f1e。
功能关闭对照 SHA：45b9d5cd9f551bb8e8953d535bce79e7766e0fa2078894db84ffda9454384782。

这直接破坏了新增类型的原生查找；Current 新增 MonoBehaviour/ScriptableObject 经 Unity 资源系统
解析也处于风险中。本次没有把普通新增类型的复现冒充新增组件 bundle 的实际复现。
现有 bundle 测试里的 Worker/Data 都已存在于 Base，所以旧测试通过不能覆盖该缺陷。

建议修正：保留 Unity 所需的稳定公开句柄，在 DHE 语义查询入口正确识别/归一化这一别名。
不能简单撤销稳定句柄，也不能把所有共享 assembly 的隐藏 Current image 一律当作 Base image。
补充上述原生查询回归，并覆盖 Current-only 组件/ScriptableObject 的 bundle 加载。

证据：E:/hclr/ar5/review-comprehensive-20261006/native-image-probe-v3/summary.json。
测试源 Probe.cs 在同目录；原生 Lookup.cpp/Lookup.dll 在 native-image-probe-v2。
前两版探针受冻结 Player 未生成对应 P/Invoke 桥接签名限制，尚未到达被测操作；
最终探针复用 Player 已有的 Winapi uint() 桥接，三条路径均到达原生查询，不把探针适配失败算作产品问题。

## P2：性能门禁对缺失指标错误放行

位置：lab/scripts/check-aot-mode-performance.mjs:11。

检查器只遍历报告已有的 metrics，没有校验必需指标集合。把三份实际报告复制到隔离目录，
只将 metrics 改成空对象、保留正确的样本数/PID 等字段，检查器仍输出 passed=true，进程退出码为 0。
只漏掉一个热点或启动指标同样不会受到检查。冻结脚本随后信任 acceptance.passed，存在静默漏验风险。

建议在数值比较前要求完整的热点、启动和内存指标，校验各指标样本数及有限数值；
缺字段或空 metrics 必须失败。兜底成本豁免不能豁免证据完整性。

当前真实冻结报告没有缺少这些指标：本次重新从全部样本计算统计值，逐项与报告一致，
并重新执行原门限，真实数据仍通过。因此这项问题不等于当前实测数字错误。

证据：E:/hclr/ar5/review-comprehensive-20261006/missing-metrics-check.json、audit.json。

## 其他审查结果

| 要求 | 审查结论 |
|---|---|
| 普通 AOT 入口选择，同进程不可变 | metadata 锁下完成注册，再通过同一个不可变表 release 发布 mode/目标；acquire 读取。现有 12 并发选择测试只允许一次成功。 |
| 保留真正的普通解释路径 | legacy 表使用原身份/普通查找，SUPERSET 独立保留旧实现。与 opt3 源码归一化名称及 feature guard 后，cpp/h 内容等价。 |
| 正常 DHE 无逐指令开关判断 | 指令循环未加入 mode 分支；扩展入口存在原子读/间接调用，不能描述成完全零开销。deferred Base 的 guard 才直接绑定 DHE。 |
| 生成代码可维护 | 隔离重跑生成器，83 项入口（57 metadata、26 runtime）及全部 7 个生成/改写文件与当前源码一致。 |
| 错误 loader 的临时内存 | DLL/PDB 由临时 InterpreterImage 拥有，解析与模式检查后再分配永久索引；测试覆盖 512 次 DHE 错误普通加载后仍能合法加载。 |
| 平台责任边界 | 库未增加平台配置/重启实现；项目负责 HTTP、本地存储、下次选择和重启。 |
| 资源与发包 | 同一个 candidate Player 选择两路并使用同一 Current DLL/资源；不需要两套业务热更包。桥接及补充 AOT metadata 仍是接入前提。 |
| 生产计数 | 新路径无测试计数；诊断代码在编译条件后，DHE diagnostics 默认 0。测试 Player 的采样不等于库生产执行开销。 |

补充接入说明：选择必须早于 LoadMetadataForAOTAssembly，不仅是 Assembly.Load(Current)。
因为 SUPERSET 的实现也依赖所选模式，项目应在选择之后加载补充 metadata。
普通 AOT 静态引用、Base 中 deferred 序列化对象、自动启动/native callback 的限制属于必要前提。

## 现有证据能证明什么

- 14 份锁定证据的 SHA 重新核对通过；来源提交与上述被审查提交一致。
- Unity 2022 实际 headers 下 feature 开/关编译及 CTest 已通过；CTest 本身不证明 startup 行为，相关行为由 Player 验证。
- 完整解释器回归的 8 个正对照组合各 220/220；opt8 的两个互操作失败明确作为负对照，不计入正对照。
- 220 例主要验证普通解释器，不能代替全部 DHE 新增/删除/布局变化语义测试。启动 workload 另覆盖 21 项 DHE/双模式行为。
- bundle、资产 9 项、依赖 7 项及真实 Player 场景拒绝通过，但没有覆盖本次 P1 的新增类型原生查找边界。
- 400 对 DHE、100 对 legacy、100 对 fixed 的统计均从现存样本重新计算一致；DHE 恢复报告的 804 个输入 hash 校验一致。
- 正常 DHE 的 native/changed/virtual P50 变化为 -4.33%/+0.63%/+1.61%；原严格门限通过。
- 解释兜底启动约 +14 ms、virtual 微基准约 +12.59%，遵循用户已接受的成本取舍，不要求追平 opt3。
- Android ARM64、WebGL、iOS Player 尚未验证，不将 Windows 结果外推为这些平台可发布。

## 距离新版本冻结还差什么

1. 修复 P1 公开 image 与 DHE 类型查询身份不一致，增加原生 API 和 Current-only Unity 资源回归。
2. 修复 P2 缺失指标放行，增加缺字段拒绝测试，重新检查现有完整性能报告。
3. 在修复后的准确 runtime/IL2CPP/package 组合上更新门禁和证据身份；性能仅在受影响路径上重新测量，不能继承旧 runtime 的通过结论。
4. 重新 review 后再作版本冻结判断。标签、推送和清单切换是用户另行决定的发布动作，不是本次 review 的任务。

本次 review 没有修改 runtime/package/IL2CPP 源码，没有提交代码、打 tag、推送或切换安装清单。
