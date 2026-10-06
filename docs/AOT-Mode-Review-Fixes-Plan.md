# Unity 2022 review 修复与重新冻结

目标：修复公开 image 无法查找 Current 新增类型的 P1，以及性能报告缺指标仍放行的 P2。
范围维持 Unity 2022、Windows IL2CPP 实测；基线不升级。用户要求准备可发布版本，明确不发布。

运行时只调整 DHE supplemental image 查询对稳定公开别名的识别。canonical Base 仍是第一条直接路径；
隐藏的 Current interpreter image 不能获得 Base overlay 语义。模式表、发布顺序、解释器循环均不改变。
该修复可单独撤销，不需要平台入口或持久化逻辑。

先保留失败复现与自动门禁负例，再提交实现。验证内容包括：

- 同一 Player 两种模式及关闭选择功能的对照，原生 API 查找 Current-only 普通类型/Unity 类型。
- 在选择前保存 Unity image 句柄，加载后验证地址稳定且查询正确。
- 同一份 Current bundles，包含 Base 已有类型和 Current-only MonoBehaviour/ScriptableObject，覆盖 prefab 和 additive scene。
- 原完整 220 项 managed differential、资产/依赖拒绝、非法和并发选择、错误 loader 512 次、feature 开/关真实 headers 编译和 CTest。
- 性能门禁缺指标、缺百分位、样本数不足、非有限数值均失败；解释兜底的成本豁免不豁免数据完整性。

主指标仍是正常 DHE 的稳态、选择到入口/进程到入口与内存，使用原门限。
解释兜底成本允许披露接受，不为追平 opt3 扩大改动。最终候选使用未插桩 Player，至少 100 个不同 PID/组。
新源码、工作负载和二进制分别锁定，历史报告不改写为新身份结果。

完成后再次 review，消除新发现的阻断项，合入本地维护线并更新准备状态；不创建标签、不推送、不切换安装清单。
