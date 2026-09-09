# DHE 静态值存储：双 Base 资源验证

2026-09-09，Unity 2022.3.62f3 / Windows x64，源码候选有条件通过。
两个不同静态值布局的不可变 Base 加载同一份真实 Unity Current DLL/MV 后，
每个 Player 的 49 条记录均与 CLR 完全一致。普通 AOT 引用对象调用及静态邻接字段
读取也一致。本轮补齐已验证组合的静态值类型扩容，完整 DHE 目标仍未完成。

## Review 与能力边界

1. 对声明不变而内联值布局变化的静态字段，使用 Current 字段定义、地址和 GC 描述的
   实际分配，不向 Base 的小分配写大结构体。此次测试的持有者实例布局不变；其余
   静态字段继续使用 Base 存储。反射隐藏被替代的 Base 字段并返回 Current 物理字段，
   DeclaringType 保留逻辑身份。准备阶段不运行 cctor、不修改 Base 字段内容，映射
   仍在既有 DHE release/acquire 发布协议下生效。
2. 初始化状态继续由同一个 canonical owner 管理。但调用 cctor 时必须解析选定的
   Current 方法，避免通过 Base MethodInfo 创建旧调用栈。cctor 固定为 static void()，
   这个修复没有把变化的值参数传过旧 AOT ABI。
3. 编译器新增 current-static-value-storage-v1 能力要求；分析普通 AOT 的固定定义，
   不为其生成可热更 MV；核心基本类型按固有布局处理，避免 Int32.m_value 自递归。
   MV 格式未更名或改版。整个工具和复现流程仍为 C#，未新增 ps1。
4. 以下门禁仍保留：普通 AOT 值布局/方法 ABI、普通 AOT 所有者的静态值字段，
   ThreadStatic 与 RVA 静态值字段。用户未要求普通 AOT 程序集可热更；下一步需要
   解决其固定代码与 hotfix 变化值类型的交互，不能改用最新普通 AOT DLL 冒充适配。
5. 尚未验收实例与静态布局同时变化的持有者、已运行对象/静态状态的在线迁移，
   新增/删除 cctor 等完整组合。本轮的泛型证据包括 string/int 持有者，以及
   变化的 StaticPayload 作为泛型静态字段参数。GC 保活用例不等于内存泄漏或压力
   测试结论。无性能、尾延迟或 ARM64 结论。

保留普通 AOT ABI 拒绝项，未放宽为“整个项目所有结构变化均已可用”。先完成
Unity 2022，再移植团结，由用户补 Android；不再安排 Unity 2021。

## 锁定提交与身份

| 仓库 | 候选分支 | 验证提交 |
|---|---|---|
| hybridclr | research/dhe-value-layout-v8.13.0 | 65f8777c76590dc89653ec662619e98645d152da |
| il2cpp_plus | research/dhe-evolution-unity2022-v8.13.0 | bda33548e37ec79f11996b2e26cd2f2ed044fead |
| hybridclr_unity | research/dhe-evolution-v8.13.0 | ca796845b6917e21ffa6001ddd303bf73de91463 |
| lab | research/dhe-value-layout-v8.13.0 | 2b0d44c2f04825b3496f4f7fa49e511ab7ec3dcc |

报告的后续文档提交不冒充构建提交。没有更新正式分支、tag、远端、Installer 默认
版本或 CAT。package 没有 opt tag。不得将本报告的失败实验母包列为已验证的兼容 Base。

- HybridCLR tree：`8FE99581F5CA3BE29C6D43C036E43135D9C28B643E64422EC297367A0EAFE453`
- IL2CPP tree：`2E2DE2C72D2284FBC888ECB7DD48680136E2F5B8EE99DBE5CC71A79D52737279`
- package tree：`F7C2C1F10827F163C3FFE6EA14C0C46AE2D53B64D88D0F5F36E912E13211D7D8`
- assembled runtime tree：`7F89496B2883D9AC161424A4F46769BA65CFF2059EF1911B36610A5C86264796`
- runtime manifest：`9E903EAE4A567399114BD55F8423D1250413AFEC4C36F07381FEDE91B0B4E6C0`
- tool SHA：`D0820DFCDB9751FB0B34CBB43BCD6C6FC499E2CD8193FE5F3335CE7622A9B4E3`
- fixture host SHA：`9EBB848877F43AF338BFD7093B00925BB4701E5F7356786727EDE92120DCAAC5`

## 当前证据

根目录：`C:/hybridclr_optimize/artifacts/dhe-static-value-20260909`。

| 路径 | 结果 |
|---|---|
| native-cctor/DHE-Unity2022/native-gate.json | 真实 Editor headers 编译及 CTest 通过；mergeReady=true、surrogateExternalHeadersUsed=false |
| policy-full-definitions/result.json | 9/9；含实际完整 stripped AOT 快照、静态字段依赖选择、ThreadStatic/普通 AOT 拒绝；lab f9bbffc 的 policy 代码，与最终构建相同 |
| matrix-cctor/result.json | 8/8 综合检查；两 Base 初始、无变化、Current 对照及共用载荷检查全通过 |
| matrix-cctor/player-current-old.json | 49/49，revision=73，ordinaryAotReferenceResult=11000000032，ordinaryAotStaticNeighbor=101 |
| matrix-cctor/player-current-new.json | 同上，独立中间布局 Base |
| matrix-cctor/stage-old.json、stage-new.json | Base MV 与 Snapshot.exe / GameAssembly.dll 的前后 SHA 不变 |

三个 hotfix fixture 程序集均在 DHE 范围，Native 程序集始终是普通 AOT。
Base revision 为 41/51，StaticPayload 分别只有 Count、以及 Count+Extra；Current
增加 Reference。Current 由独立 Unity 项目生成 stripped DLL，CLR 读取同一批实际
输入。普通 AOT 和相邻字段检查不依赖 SDK 参考程序替代 Player。

49 条记录包含上一轮 31 条回归及 18 条静态值记录：初值、独立副本、ref 修改、
嵌套值、反射取值/设置/逻辑父类型/字段类型、GC 后读取与弱引用保活、整值清零、
初始化计数、相邻字段、泛型持有者和泛型值参数。相关 cctor 计数均为 1。

Base IDs：

- old：`9e7cc69b3270d859b38a444d1fb975efc6c2d0865707fef4976b4d55f5443743`
- new：`1a98a97cad10a3ccfd7fddc77795cf6fd9a157f6455d5e019a954ef3e0d48418`

同一 Current set：`09ebf86240f3bc6c784e0ebc8c42bf5f2476dc2fd04ccff3be2071f7ece98f00`。
资源 manifest：`EE3FF1C50BFE78BA7C6720932EC8F6008899D9E2C648A57AC74D8AD79309999F`。

另核对 `matrix-cctor/base-old/project/Library/Bee/artifacts/WinPlayerBuildProgram/il2cppOutput/cpp/HybridCLR.ValueLayoutNative.cpp`
中的 StaticNeighbor：原生函数直接读取 Base static_fields 的 UnchangedStorage，没有
DHE 解释器分派。该 cpp SHA 为 `3E12C8A8F6BC8F7D68989D5376E1E1CB0EE207DF9B8BC1BE6A29A04542FA3D0D`。

## 失败记录与复现

- `matrix`：两个 Base 初始构建通过，真实资源分析在 Int32 递归布局处失败。
- `policy-full`：普通 AOT 被错误送入 hotfix MV 生成，遇到重复 System AssemblyRef。
- `matrix-full-snapshot`：完整资源生成与 staging 通过，Player 的泛型 cctor 被旧
  AOT 调用栈保护拒绝。该对母包使用修复前的 IL2CPP，不是通过版本。
- `early-cctor-*`：最终 old Base 的提前验证，加载上一轮真实 Unity Current，
  49/49 一致；最终双 Base 结论以上表 matrix-cctor 为准。

构建 tool、value-layout fixture、aot-snapshot fixture（后者传入
`-p:DhePackageRoot=<package绝对路径>`），按锁定源 assemble-runtime、native-tests。

```text
dotnet AotSnapshotTests.dll evolution-workflow <lab> <package> <Unity.exe> <runtime manifest> <全新输出>
dotnet ValueLayoutTests.dll static-storage-policy <Base DLL目录> <Current DLL目录> <全新输出> [完整AOT快照assemblies目录]
```

仅修改工具时，第一条命令可追加 `<既有不可变matrix目录>`，复用已通过初始构建的
Base 和真实 Current；先检查 package/runtime/Player SHA，再重新生成资源并对照。
native 改动必须新建 Base，本轮未覆盖旧母包文件。

## 后续与回滚

下一步为普通 AOT 固定代码中的变化值类型调用/存储边界；继续使用真实 Windows
Player 和同一 Current 多 Base 证据，不能仅删除 native ABI 门禁。

回滚本轮需整组恢复上一轮组合：HybridCLR `8cbff343f44ca9c677fb4e8d4f9e33747bc7f5d8`、
IL2CPP `9d4e4f6a22f7059cd7046e71ef9639ee7de49bbb`、package
`083dc32a1fd44e06a2b239dedf78bc1935b0d9e7`、lab
`7de3533f6f2a1e98b0f08f035f13a8a0fb3473e3`。那一组会拒绝静态值扩容，并有本轮
记录的完整快照/选中 cctor 边界问题，只作为研究回滚点。

报告提交后四个候选工作树 clean，无新增 stash；C 盘约剩 133 GiB，本轮未清理。
所有历史失败目录、源码、不可变母包和当前证据保留。
