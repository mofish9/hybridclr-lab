# HybridCLR 开源版性能优化任务设计文档

状态：Draft 0.2  
工作目录：`C:\hybridclr_optimize`  
文档目的：固定优化项目的版本、边界、测试方法和验收标准，避免在没有基线的情况下直接修改运行时。

## 1. 目标

基于 HybridCLR 社区开源版，建立一套可以重复执行的性能优化流程：

```text
固定源码和引擎版本
        -> 创建 hybridclr-lab 仓库和测试协议
        -> 建立纯 C# 用例及 .NET 参考执行器
        -> 创建最小 Tuanjie 测试工程
        -> 构建未修改的 Clean Baseline
        -> 运行正确性和差分测试
        -> 固化性能测量基线
        -> 创建 Instrumented Baseline 定位热点
        -> 针对热点修改源码
        -> 运行全量回归
        -> 保存结果并推送可复现提交
```

本项目不尝试复刻商业版的闭源实现，只针对开源代码中可以测量、验证和维护的部分进行优化。

## 2. 范围

### 2.1 包含

- HybridCLR 解释器执行性能。
- IL 到解释器指令的转换性能。
- 方法体缓存、解释方法缓存和预热策略。
- AOT 与解释器之间的调用桥。
- HybridCLR 与 IL2CPP 的元数据和方法指针集成。
- MethodBridge 等 C++ 生成器的输出质量。
- Windows x64 和 Android ARM64 的正确性及性能对比。

### 2.2 不包含

- 当前游戏业务代码。
- Lockstep、战斗逻辑和业务程序集。
- 当前项目的资源、场景和业务配置。
- Unity/Tuanjie 原始引擎源码的重新发布。
- 通过反编译商业版实现来复制其代码或算法。

## 3. 固定基线

当前实际工程版本来自 `C:\mofish_cat\cat\ProjectSettings\ProjectVersion.txt`：

```text
Tuanjie editor version: 1.10.0
Unity compatibility version: 2022.3.62t12
HybridCLR package: 8.13.0
```

8.13.0 package 的 `Data~/hybridclr_version.json` 对团结引擎的映射为：

```text
unity_version: 2022-tuanjie
hybridclr: v8.13.0
il2cpp_plus: v2022-tuanjie-8.13.0
```

实际编辑器路径：

```text
C:\Program Files\Tuanjie\Hub\Editor\2022.3.62t12
```

完整 commit 锁定见 `manifests/repo-lock.json`。所有性能结果必须记录这份锁定信息，不能只记录 package 版本。

## 4. 仓库职责

| 仓库 | 当前基线 | 本地开发分支 | 责任 |
|---|---|---|---|
| `hybridclr_unity` | `v8.13.0` | `optimize/v8.13.0` | Unity/Tuanjie Package、Installer、生成器 |
| `hybridclr` | `v8.13.0` | `optimize/v8.13.0` | 解释器、转换器、缓存、解释器调用桥 |
| `il2cpp_plus` | `v2022-tuanjie-8.13.0` | `optimize/tuanjie-1.10-v8.13.0` | Tuanjie 1.10.0 的 IL2CPP 集成和运行时 Hook |

本地仓库位置：

```text
C:\hybridclr_optimize\repos\hybridclr_unity
C:\hybridclr_optimize\repos\hybridclr
C:\hybridclr_optimize\repos\il2cpp_plus
```

三个仓库均使用：

```text
origin   -> mofish9 的公开 Fork
upstream -> focus-creative-games 官方仓库
```

## 5. Installer 和构建工作流

Installer 的有效流程是：

```text
读取 Data~/hybridclr_version.json
        |
        +-- clone hybridclr@v8.13.0
        |
        +-- clone il2cpp_plus@v2022-tuanjie-8.13.0
        |
        +-- 将 hybridclr/hybridclr 移入 il2cpp_plus/libil2cpp/hybridclr
        |
        +-- 复制 Tuanjie Editor 自带的 il2cpp 工具链
        |
        +-- 用合并后的 libil2cpp 覆盖本地副本
        |
        +-- 清理 Il2cppBuildCache
        |
        +-- 生成并记录 runtime 版本信息
```

最终参与构建的不是某个单独仓库，而是合并后的目录：

```text
Tuanjie Editor il2cpp 工具链
    + il2cpp_plus/libil2cpp
    + hybridclr runtime
    + 项目生成的 MethodBridge.cpp 等文件
```

构建时必须通过本地 IL2CPP 路径使用这份合并结果。`LocalIl2CppData-*`、`Library/Il2cppBuildCache` 和生成的 C++ 文件属于构建产物，不作为源码修改入口。

后续应增强版本记录，使其同时保存：

```text
package commit
hybridclr commit
il2cpp_plus commit
generated C++ hash
Tuanjie editor version
target platform
build configuration
```

### 5.1 Candidate 装配方式

默认 Installer 的版本清单只引用官方 tag，不会自动使用本地 `optimize/*` 分支。因此第一阶段不依赖修改版本清单，而是在 `hybridclr-lab` 中提供统一装配脚本：

```text
读取 repo-lock.json 和本地仓库 commit
        -> 验证工作目录状态
        -> 复制 il2cpp_plus/libil2cpp 到 staging
        -> 复制 hybridclr/hybridclr 到 staging/libil2cpp/hybridclr
        -> 计算 staging 内容 hash
        -> 调用 Installer 的 InstallFromLocal
        -> 生成 MethodBridge 等项目相关 C++ 文件
        -> 输出 build-manifest.json
```

同一脚本必须支持三种运行时配置：

| 配置 | 来源 | 用途 |
|---|---|---|
| `Baseline-Clean` | 官方 tag 对应的锁定 commit，无观测代码 | 最终正确性和性能对照 |
| `Baseline-Instrumented` | 从 Clean Baseline 派生，只增加观测 | profiling，不参与最终性能结论 |
| `Candidate` | `optimize/*` 分支的锁定 commit | 待验证优化版本 |

后续 package Fork 可以增加精确 commit 安装能力，但第一阶段的正确性不依赖这一改造。

## 6. 独立测试实验室

测试工程不放入当前游戏工程。测试基础设施使用独立 Git 仓库 `mofish9/hybridclr-lab`，计划目录如下：

```text
C:\hybridclr_optimize\lab              hybridclr-lab Git 工作区
  docs                     设计文档和测试协议
  manifests                版本、构建和测试清单
  unity-test-project       最小 Tuanjie 工程
  managed-cases            不引用 Unity API 的 C# 测试程序集
  native-unit-tests        纯 C++ 算法测试
  runners                  .NET 和 Tuanjie Player 测试宿主
  scripts                  安装、构建、运行、比较脚本
  reports                  JSON、JUnit 和性能报告
```

`hybridclr-lab` 纳入 Git 的内容：

- `Assets`、`Packages`、`ProjectSettings` 中构成最小测试工程的源码和配置。
- managed cases、runner、构建脚本和结果 Schema。
- `repo-lock.json`、`build-manifest.json` 模板和性能测量策略。
- 小体积、可审阅的基线摘要报告。

不纳入 Git 的内容：

- `Library`、`Temp`、`Logs`、`obj` 和 Player 构建目录。
- `HybridCLRData`、`LocalIl2CppData-*` 和 Tuanjie Editor 原始 IL2CPP 副本。
- 大体积原始 profiler trace；只保存其 hash、工具版本和归档位置。

### 6.1 Tuanjie 工程的创建时机

Tuanjie 工程不在测试协议尚未定义时创建，也不能推迟到开始优化之后。创建时机固定为：

```text
完成测试结果 Schema
        + 完成测试清单格式
        + 完成第一批纯 C# smoke cases
        + 完成 .NET reference runner
        -> 创建最小 Tuanjie 工程
        -> 实现 Player runner
        -> 验证 Clean Baseline
        -> 才允许开始 runtime 优化
```

这样前期不会被 Tuanjie 工程生成物和构建流程拖住，同时又保证任何优化发生前已经具备真实 IL2CPP 正确性门禁。

### 6.2 C++ 单元测试

适合脱离 Tuanjie 测试的内容：

- Opcode 解码和指令长度。
- BasicBlock 切分和控制流分析。
- IR 优化和 SuperInstruction 融合。
- 常量折叠和局部指令重写。
- 临时内存分配器。
- 方法体缓存的淘汰策略。

不通过大量 Mock 伪造 `Il2CppClass`、GC 和元数据系统。涉及真实 IL2CPP ABI、对象布局或异常展开的逻辑必须进入 Player 集成测试。

现有 HybridCLR 模块并非天然可独立编译。第一阶段不要求为了测试而重构整个解释器；只有新增或抽取纯 IR Pass、缓存策略等逻辑时，才要求同步提供 C++ 单元测试。任何触及真实解释执行路径的修改都必须通过 managed differential tests 和 Player 集成测试。

### 6.3 C# 差分测试

同一份测试程序集分别在以下环境中执行：

```text
.NET reference
官方 HybridCLR 8.13.0
Candidate HybridCLR
```

测试用例不依赖 NUnit 或 Unity Test Framework，由统一注册表暴露给 .NET runner 和 Player runner。测试输出使用结构化结果，不比较不稳定的异常文本或反射顺序：

```json
{
  "case": "generic_struct_call",
  "returnHash": "...",
  "exceptionType": null,
  "sideEffectHash": "...",
  "status": "passed"
}
```

每次运行必须绑定：

```text
test manifest commit
managed assembly hash
runtime build manifest hash
runner version
platform and process architecture
```

差分判定规则：

1. 崩溃、超时、死锁和非确定性结果直接失败。
2. Candidate 的归一化语义结果必须与 Clean Baseline 一致。
3. 对 ECMA-335 明确定义的行为，同时要求与 .NET reference 一致。
4. 官方 HybridCLR 与 .NET 的已知差异放入显式 compatibility allowlist，并记录原因。
5. Candidate 有意修复官方差异时，必须增加独立回归用例并经过人工批准，不能静默更新基线。

### 6.4 Tuanjie Player 集成测试

使用最小的命令行 Player，不使用当前项目场景和业务程序集。它负责：

- 初始化 HybridCLR。
- 加载测试热更新 DLL。
- 执行测试清单。
- 输出正确性和性能 JSON。

只有这层才能可靠验证 GC、泛型共享、异常、AOT/解释器调用桥、P/Invoke 和真实平台 ABI。

Tuanjie 工程只作为执行宿主，不承载测试真值。测试真值、测试清单和比较逻辑位于引擎无关的 `managed-cases` 与 `runners` 中。

## 7. 测试用例矩阵

第一版测试至少覆盖：

- 整数、浮点、双精度、转换、checked/unchecked。
- 分支、循环、比较、switch 和异常控制流。
- 数组、字符串、结构体字段、结构体复制。
- `ref`、`out`、指针和装箱/拆箱。
- 泛型引用类型、泛型值类型、泛型共享。
- 虚调用、接口调用、委托调用和函数指针。
- 解释器内部调用、解释器到 AOT、AOT 到解释器。
- 反射、动态程序集加载、静态构造函数。
- 异常、`finally`、线程、ThreadStatic、async。
- P/Invoke 和 Reverse P/Invoke。

## 8. 性能测量规范

冷启动和稳态必须分开：

| 类别 | 指标 |
|---|---|
| 冷启动 | DLL 加载、元数据注册、首次 Transform、首次调用 |
| 稳态执行 | ns/op、调用次数、P50/P95/P99 |
| 调用边界 | AOT/解释器互调耗时、桥接次数 |
| 内存 | 热更新元数据、方法体缓存、解释器栈 |
| GC | 分配字节、GC 次数、暂停时间 |
| 构建 | 生成时间、C++ 编译时间、Player 体积 |

Windows x64 用于快速迭代，Android ARM64 用于移动端最终确认。每次对比必须使用相同的测试程序集、编译配置、设备和缓存清理策略。

### 8.1 性能执行协议

- 正式对比只使用非 Development、关闭 Deep Profiling 的 Release Player。
- 首次调用和稳态循环使用不同进程测量，避免预热状态串扰。
- 每个稳态 case 先预热，再运行固定批次数；计时区间内不写日志和 JSON。
- 以独立进程为统计样本，记录样本数、P50、P95 和置信区间。
- Baseline 与 Candidate 交替运行，避免温度、后台任务和系统状态产生单向偏差。
- Android 记录设备型号、系统版本、CPU 温度和电量状态。
- 第一份基线用于估计噪声区间，并生成 `benchmark-policy.json`；性能提升必须超过噪声阈值才视为有效。
- 任何未声明的核心 case 回退都阻止合并，允许的权衡必须在变更记录中逐项批准。

### 8.2 构建环境前置检查

在创建 Tuanjie 工程前，脚本必须验证：

- Tuanjie 1.10.0 编辑器和许可证可用于 batchmode。
- Windows IL2CPP Build Support 已安装。
- Android Build Support、SDK、NDK 和 JDK 版本可记录并可重复使用。
- C++ 编译工具链版本已固定。
- 编辑器路径和构建模块与 `repo-lock.json` 一致。

## 9. 优化路线

### Phase 0：Clean Baseline

不修改三个 runtime 仓库，完成构建链、正确性测试和正式性能基线。该版本是后续所有优化的最终对照。

### Phase 0.5：Instrumented Baseline

从 Clean Baseline 派生观测分支，只加入计时器、计数器和缓存命中统计。它用于定位热点，不与 Candidate 做最终性能结论。

### Phase 1：低风险运行时调优

验证方法体缓存大小、内联阈值、PreJIT 和线程栈参数，确认不修改 C++ 行为即可获得的收益。

### Phase 2：`hybridclr` 解释器优化

重点研究：

- `Interpreter_Execute.cpp` 指令分发。
- IR 布局和 SuperInstruction。
- 数值、数组和结构体热点指令。
- TransformContext 的重复查找和临时分配。
- 方法体、解释方法和调用信息缓存。

### Phase 3：调用桥和生成器

联合评估：

- `hybridclr/interpreter/MethodBridge.*`。
- `hybridclr_unity` 的 `MethodBridge.cpp` 生成器。
- 泛型、值类型和 Reverse P/Invoke 的专用桥。

### Phase 4：`il2cpp_plus` 集成层

只有 profiling 证明瓶颈位于 IL2CPP Hook、元数据查询、method pointer/invoker 或 ABI 边界时，才修改 `il2cpp_plus`。

### Phase 5：平台专项优化

在 Windows 基线稳定后，再处理 ARM64 调度、编译器优化和移动端内存行为。

## 10. 优化验收标准

每项优化必须满足：

1. 优化前已经存在可运行的 Clean Baseline 和 managed differential test 门禁。
2. 适用的 C++ 单元测试通过。
3. .NET reference、Clean Baseline 和 Candidate 的差分结果符合判定协议。
4. 全量语义和兼容性测试无未记录的差异。
5. 冷启动、稳态、内存和 GC 指标分别记录。
6. 性能提升超过 `benchmark-policy.json` 记录的噪声阈值并可重复得到。
7. Windows 通过后，关键场景在 Android ARM64 完成回归。
8. 代码、测试、配置、build manifest 和 commit 锁定信息一起提交。

## 11. 当前状态和下一步

当前已完成源码 Fork、版本锁定、独立 workspace 和 Tuanjie 版本校正，尚未修改 runtime 源码，也尚未创建 Tuanjie 测试工程。

下一步执行顺序固定为：

1. 创建并 clone `mofish9/hybridclr-lab` Git 仓库。
2. 提交本文档、结果 Schema、测试清单格式和构建 manifest 格式。
3. 建立第一批纯 C# smoke cases 和 .NET reference runner。
4. 完成 Tuanjie 构建环境前置检查。
5. 创建最小 Tuanjie 1.10.0 工程并纳入同一个 lab 仓库。
6. 实现本地 runtime 装配脚本和 Player runner。
7. 使用未修改的三个 Fork 构建并验证 Clean Baseline。
8. 固化第一版测试与性能基线后，才开始 Instrumented Baseline 和 runtime 优化。
