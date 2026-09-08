# 已有 struct 布局演进：影响分析与验证合同

## 当前结论

已经复现并补上分析层的缺口：原来的单程序集方法指纹不能完整识别值类型布局的
跨程序集、嵌套与泛型依赖。13 个冻结调用方方法的方法体及原有 MV 版本均未变，
但其调用约定或访问布局会受到其他程序集的 struct 变化影响。

新增 DheValueLayoutImpact.cs 以程序集身份区分类型，计算内嵌值字段、泛型实参、
引用类型实例字段和基类布局的影响，再扫描方法签名、局部变量、字段访问、
调用目标签名、泛型方法实参及间接调用签名。它区分需要解释执行的 hotfix 方法、
需要检查实际泛型上下文的方法，以及普通 AOT 的兼容边界。
这是实现运行时前的分析工具，尚未接入资源加载的执行决策；不能作为结构热更成功证据。

当前只完成了这一步及测试合同。现有运行时仍拒绝已有 struct 增删字段或改变布局，
没有关闭拒绝检查，没有改变 MV 格式或方法指纹算法，没有新的 Player 验证结论。
最新完整 Windows checkpoint 仍是主研究线的十 Base 报告。

## 精确源码和输入

本轮位于 research/dhe-value-layout-v8.13.0：
lab 为 worktrees/hybridclr-lab-dhe-value-layout-v8.13.0；
runtime 为 worktrees/hybridclr-dhe-value-layout-v8.13.0，仍为未改动的 760634e。
最终测试源码和 fixture 构建身份为：
b46a202e440fa6dbfdc268d138bc888620423cd8。

继承的 Windows checkpoint：lab 821b011、HybridCLR 760634e、
Unity 2022 IL2CPP 3482c819、团结 2022 IL2CPP df8d0123、package bc319e5。
具体完整 SHA 和回滚组合见 dhe-native-descendants-windows.md。
本轮没有改动 engine/package、scratch Demo、CAT、正式分支或 tag。
两个新增工作树收尾时 clean；没有新增或删除 stash，也没有遗留构建进程。

以下路径相对 C:/hybridclr_optimize/artifacts/dhe-evolution-20260908：

- value-layout-verified-input：冻结的 Base/Current 和 build.json。
- runner-value-layout-verified：干净源码上重新编译的验证工具。
- value-layout-verified-base-reference.json、value-layout-verified-current-reference.json：
  两版 CLR 参考，均通过 14 组值语义和冻结调用方的执行。
- value-layout-verified-impact.json：35/35 分析与边界检查通过。
- value-layout-verified-audit.json：独立复核输入、参考和源码/工具哈希。
- value-layout-archived-mv/report.json：从原始 DLL 重建的 40 个历史 MV 均逐字节相同；
  此项在 1152cb96322c07fce51630c6c8ea7a4ae5346ebe 上执行，
  此后 MetaVersion.cs 未改动。不能把它标为新的 Player 结果。

最终影响分析报告 SHA-256：
16B997198F211551676D4EDC19287D5813D5D638E98B3DEEEE92E8920A21F165。

三个 hotfix fixture 程序集为 ValueLayoutModel、ValueLayoutOther、ValueLayoutConsumer。
ValueLayoutNative 始终属于普通 AOT 输入。Consumer 和 Native 均只对 Base Model 编译一次，
同一 DLL 原样搭配 Current Model 执行；没有用重编调用方掩盖布局依赖。
Other 程序集故意声明同名 Payload，验证不会因名字碰撞误判其布局变化。

## 测试覆盖

直接变化涵盖增长并加入引用字段、删除字段、重排字段、字段类型替换和泛型字段新增。
值语义检查涵盖默认初始化、独立复制、嵌套值、闭合泛型、数组元素和别名、
Array.Copy/Clone、nullable、装箱、反射、List/Dictionary、引用类型及派生类字段、
删除/替换成员反射和 GC 保活。CLR 参考只定义语义，尚不证明原生 GC 描述符正确。

13 个未变化调用方分别覆盖直接/嵌套/跨程序集复制、泛型及 nullable、
调用栈上的返回值与实参、数组元素、ref 传递、类字段偏移、普通 AOT 调用，
以及完整字段复制检查。后者通过反射比较 Current 全部字段，要求复制保留新引用的
身份和新增标量值，不能只保住 Base 的旧 Count 字段就判成功。
新分配返回值则检查 Current 字段完整，不能要求它与另一份新分配共享引用对象。

分析同时确认六类无关入口保持无布局回退要求：普通整数方法、未变化 struct、
另一程序集的同名 struct、已知安全的本地/外部泛型实例，以及仅保存对象引用的容器。
开放泛型明确要求进一步检查实际实参；普通 AOT 方法不会被标成可热更。
no-op 无布局回退义务、输入顺序确定性、遗漏 Current 程序集拒绝均通过。
这些检查不证明分析已覆盖所有非托管 ABI 或任意泛型递归形式。

保留早期输入和报告。value-layout-contract-current-reference.json 曾因 fixture
把两次新分配对象的引用身份作相等比较而失败；现区分复制与新分配。
该旧失败没有被覆盖，不是 DHE 运行时失败或成功的证据。

## 后续运行时实施要求

1. 保留单份最新 DLL/MV 和每个 Base 自己的不可变身份，不能为了增加依赖信息而让
   旧 DLL 重建出不同的 Base MV。需要让客户端得到或计算完整布局依赖关系，
   再统一准备受影响类型、方法和泛型实例。
2. Current 现有类型引用仍经 InterpreterImage 的 homologous 映射回 Base；
   SuperSetAOTHomologousImage 的新增实例字段路径仍拒绝值类型。
   必须实现 Current 存储和字段选择，覆盖复制、装箱、数组、nullable、类内嵌值、
   GC 描述符和公开类型身份。只删除上述检查会导致旧尺寸存储被当作新布局使用。
3. 普通 AOT 的按值操作也要保留新增字段。例如原生 Echo 仅复制 Base 大小，
   只在边界转换旧字段并不能自动恢复新字段的复制语义。需要验证母包预先准备
   兼容执行路径的方案，例如保留这类调用者的原始 IL，必要时解释执行固定的
   Base IL；这不授权热更普通 AOT DLL。外部原生 ABI 仍需明确的编组边界。
4. 泛型实际实参检查必须进入执行路径，不能把开放泛型的“需要检查”当作已安全。
   新状态准备完成后才发布，失败需回滚；不能改变当前 release/acquire 约束。
5. 原生实现形成后，再锁定提交、跑两引擎真实头文件 compile/CTest、Windows
   Base/no-op/更新，以及把已有布局演进编入新 Base 后继续消费最新资源的世代。
   未完成前不迁移正式分支，不把之前 30 次回放的数据归给本轮 struct 能力。

性能、AOT 保留率、尾延迟、内存和 Android 真机验证继续作为后续门禁。
完整 DHE 目标保持未完成。
