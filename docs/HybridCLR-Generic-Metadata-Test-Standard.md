# HybridCLR 完全泛型共享与元数据优化测试标准

状态：v2，适用于 Tuanjie 1.10.0 / Unity 2022.3.62t12 / HybridCLR 8.13.0

## 1. 目的

本标准把两类收益分开验证：

1. 完全泛型共享是构建与运行配置能力。它的首要结论是，在不加载任何补充
   AOT 元数据 DLL 时，热更新程序集仍能正确调用未显式实例化的 AOT 泛型。
2. 元数据优化是运行时源码能力。它必须分别衡量补充 AOT 元数据加载、热更新
   `Assembly.Load`、反射触达和首次执行，不能用总启动时间代替。

商业版公开数据只作为方向和量级参考，不作为社区版验收真值。所有结论必须由
同一源码、同一引擎、同一测试输入和同一机器上的 Clean/Candidate 对照产生。

## 2. 固定测试矩阵

Windows x64 快速门禁必须覆盖以下四个单独构建或运行配置：

| IL2CPP code generation | 补充 AOT 元数据 | 用途 |
|---|---:|---|
| `OptimizeSpeed` | 加载 | 社区版兼容基线 |
| `OptimizeSpeed` | 不加载 | 2022 完全泛型共享主门禁 |
| `OptimizeSize` | 加载 | 小包构建兼容对照 |
| `OptimizeSize` | 不加载 | 小包 + 完全泛型共享门禁 |

`OptimizeSpeed` 和 `OptimizeSize` 必须由构建脚本显式设置并写入 build manifest，
不能依赖编辑器项目的历史值。补充元数据关闭是运行时真实跳过
`LoadMetadataForAOTAssembly`，不能只是不打包但仍复用已经加载过元数据的进程。
`none` 的原始结果必须同时满足：`aotMetadataMode == "none"`、
`aotMetadataLoadNanoseconds == 0`。空分支的计时开销不得伪装成元数据加载耗时。

Windows 通过后，候选版本至少在 Android ARM64 实机重复
`OptimizeSpeed + none` 和项目实际使用的构建配置。

## 3. 完全泛型共享正确性门禁

每次运行必须是新 Player 进程，并执行完整 managed differential suite。除已有
泛型用例外，AOT/解释器边界至少覆盖：

- AOT 泛型方法接收热更新程序集定义的 class、enum、小/大 struct 和 nullable。
- 泛型参数位于返回值、普通参数、数组、嵌套泛型、`ref` 和 `out` 位置。
- struct 约束、接口约束、泛型接口调用和泛型虚调用。
- 泛型 delegate、反射 `MakeGenericMethod`、装箱/拆箱及异常路径。
- AOT 调解释器和解释器调 AOT 两个方向。

硬门禁：

- Candidate 的 `.NET reference`、有补充元数据 Player、无补充元数据 Player
  全部通过，并分别与 reference 的规范化结果差异为 0。
- supplemental 下 Candidate 与 Clean 的规范化结果差异为 0。若 Candidate 的
  目标就是修复 Clean 的 none 失败，则必须保存 Clean 失败集合，且 Candidate
  不得通过修改 reference 或 allowlist 隐藏差异。
- 无崩溃、超时、`ExecutionEngineException` 或缺失 method pointer。
- `OptimizeSpeed` 与 `OptimizeSize` 的结果一致。
- 每组 raw result 的进程 ID 唯一，managed assembly hash、workload checksum、
  build manifest 和 metadata mode 与汇总报告一致。

## 4. 元数据压力输入

常规 88 KB 功能测试 DLL 小于 Windows 进程噪声，不可用于内存倍率结论。
正式测量使用 `HybridCLR.MetadataStress.dll`。源码由确定性生成器创建，结构参数
来自 `metadata-benchmark-policy.json`，并在报告中记录 DLL SHA-256、字节数、
类型数和成员数。

压力程序集必须包含大量 type/method/field/property/custom attribute、泛型方法、
嵌套泛型类型和接口实现。调整规模后必须视为新测试协议，不能和旧报告直接比较。

## 5. 分阶段测量

一个 metadata benchmark 进程只运行一次。公共前置阶段依次记录：

1. `baseline`：Player 初始化完成并执行完整 GC 后。
2. `aot-metadata-loaded`：逐个加载补充元数据并释放 managed byte array 后。
3. `hot-update-bytes-read`：压力 DLL 已读入 managed byte array。
4. `hot-update-assembly-loaded`：`Assembly.Load` 返回。
5. `hot-update-bytes-released`：释放输入 byte array并完整 GC 后。
之后按场景改变触达顺序：

- `entry-first`：先记录 `entry-executed`，再记录 `reflection-touched`。
- `reflection-first`：先记录 `reflection-touched`，再记录 `entry-executed`。

入口计时拆成 `entryResolve` 与 `entryExecute`。前者只包含
`Assembly.GetType/GetMethod`，后者只包含 `MethodInfo.Invoke`；不得再把解析成本混入
业务首次执行。Reflection 可使用 selective 离散类型集合或 exhaustive 全量类型、
成员和自定义特性契约。

每个快照至少包含：进程 private bytes、working set、两者峰值、managed heap、
Unity allocated/reserved memory。每个阶段单独记录耗时。内存主指标为 full GC 后的
private bytes 增量；working set 只作辅助，因为它受操作系统页面回收影响较大。

## 6. 采样与统计

- Release、非 Development Player，关闭 Deep Profiling。
- 每个配置至少 10 个独立进程；Clean 与 Candidate 交替运行。
- 使用 median 作为主值，同时报告 minimum、P95、maximum、MAD。
- 测量前记录 build manifest、runtime tree hash、输入 DLL hash、机器和编译器。
- 禁止把 instrumented runtime 的时间或内存作为最终结论。
- 首轮 Clean 重复运行用于校准每个指标的噪声；未校准前不得宣称小幅提升。
- 观测漂移写入独立 calibration report，不能写回构建策略形成循环哈希；当前
  Windows x64 数据见 `reports/metadata-benchmark-calibration-v2.json`。

## 7. 指标与验收

完全泛型共享的直接收益单独报告：

- 面向部署的无元数据构建中，补充 AOT 元数据 payload 字节数从基线值降为 0；
  为四象限测试而保留双模式 payload 的构建不得宣称已获得这部分包体收益。
- `aotMetadataLoadNanoseconds` 从基线值降为 0。
- Player 总体积、`GameAssembly.dll`、数据目录和补充元数据 payload 分项大小。
- 稳态泛型 workload，防止 `OptimizeSize` 带来的性能权衡被包体收益掩盖。

FGS 稳态测量每个配置至少使用 10 个独立 Player 进程。主值为进程中位数的
median，同时报告 P95、relative MAD 和校准后的噪声阈值。metadata mode 之间或
Candidate/Clean 之间的差异未超过噪声阈值时，只能判定为性能相近；单一 workload
上的 codegen 差异不得外推成整体泛型性能结论。

四组汇总使用以下脚本做契约、样本和 none 零加载审计：

```powershell
./scripts/compare-full-generic-sharing-benchmarks.ps1
```

元数据源码优化的主指标：

- `Assembly.Load` median 时间。
- load-only full-GC private-bytes 增量 / 热更新 DLL 字节数。
- reflection touch 后和 entry execute 后的 private-bytes 增量。
- reflection touch 与首次执行时间，约束延迟初始化不能只把成本后移。
- `loadAndReflection` 与 `throughReflection` 复合时间，判断延迟成本是否抹平或反转
  `Assembly.Load` 收益；两种场景必须按实际顺序计算对应阶段。

Candidate 硬门禁：正确性全部通过；任何主指标不得同时回退超过 5% 且超过
8 MiB（内存）或超过 Clean 校准噪声阈值（时间）。单项优化只有在 10 个独立
进程的 median 改善超过该指标噪声阈值时才接受。当前 Windows x64 校准后的
时间收益或回退必须同时达到 10% 和 1 ms；内存收益必须同时达到 5% 和
1 MiB，才可宣称有效。项目级目标可参考官方公开的
加载时间和内存量级，但不得把官方商业版数字写成社区版已达到的结果。

正式对照由脚本执行，并且必须同时提供各自的 build manifest。脚本会拒绝策略
哈希、压力程序集、触达契约、构建配置或进程数不一致的输入：

```powershell
./scripts/run-metadata-comparison.ps1 `
  -BaselineProfile Baseline-Clean `
  -CandidateProfile Metadata-Candidate `
  -AotMetadataMode supplemental
```

成对 runner 在奇数轮按 Clean/Candidate、偶数轮按 Candidate/Clean 运行，确保每边
至少 10 个独立进程。已有 summary 也可直接交给
`compare-metadata-benchmarks.ps1` 复核。

## 8. 优化进入顺序

1. 固化 Clean 四配置正确性结果与 metadata benchmark 报告。
2. 先验证完全泛型共享是否能直接移除项目所需的补充元数据集合。
3. 对 `InterpreterImage::InitRuntimeMetadatas` 分阶段插桩，找出实际时间与分配热点。
4. 优先做临时容器释放、容量精确化和可证明不改变可观察行为的延迟初始化。
5. 每次只合入一个可归因变化，跑完整正确性、load-only、reflection 和 execute 门禁。

运行时实验应从锁定 tag 创建独立 `research/metadata-v8.13.0` worktree。当前
`optimize/*` 工作区含有解释器实验，不作为元数据 Clean 或 Candidate 输入。
完全泛型共享实验同样使用独立 `research/full-generic-sharing-v8.13.0` worktree，
并单独记录 HybridCLR 与 `il2cpp_plus` 的 dirty 状态。Android APK 编译成功只能
作为工具链门禁，不能替代 ARM64 真机 correctness、native test、RSS/PSS 或性能
验收。
