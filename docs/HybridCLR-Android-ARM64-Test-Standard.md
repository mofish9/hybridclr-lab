# HybridCLR Android ARM64 测试标准

## 1. 目标与边界

本标准用于确认 HybridCLR 8.13.0 社区版与 Candidate 在真实 ARMv8-A
(`arm64-v8a` / AArch64) 设备上的正确性和性能。Windows x64 仍用于快速迭代，
但不能代替 ARM64 的最终结论。

以下结果不能标记为 ARMv8 性能证据：

- x86/x64 Android 模拟器。
- Windows on ARM 的 x64 模拟进程。
- 只完成 NDK 交叉编译但没有在 ARM64 设备执行。
- Development Build、Profiler/Deep Profiling 或混合 ARMv7/ARM64 APK。

## 2. 固定环境

- Tuanjie `1.10.0` / `2022.3.62t12`。
- HybridCLR package/runtime `8.13.0`。
- Android IL2CPP Release、`OptimizeSpeed`、ARM64 only。
- Android SDK、NDK、JDK 使用 Tuanjie Android Build Support 自带版本。
- Baseline-Clean、Candidate 和 AOT 使用同一 managed workload、policy、设备、
  OS 版本与测试时段。

`build-android-arm64.ps1` 会用 `aapt` 拒绝包含非 `arm64-v8a` ABI 的 APK，
并要求存在 `lib/arm64-v8a/libil2cpp.so`。APK SHA-256、runtime SHA-256、managed
assembly SHA-256、AOT assembly SHA-256、工具链路径和 native library 清单写入
`reports/<profile>-android-arm64-build-manifest.json`。

## 3. 测试层次

### 3.1 AArch64 C++ 单元测试

现有 native test target 使用 NDK Clang 交叉编译为 `ELF64/AArch64`，推送到
`/data/local/tmp/hybridclr-lab-native-<profile>` 后在真机执行。它验证 metadata
解码、opcode、basic block、临时内存、stack copy 和 Candidate instruction
combiner；执行结束后删除设备临时目录。

```powershell
./scripts/run-native-tests-android-arm64.ps1 -Profile Baseline-Clean
./scripts/run-native-tests-android-arm64.ps1 -Profile Candidate
```

只使用 `-BuildOnly` 仅证明 AArch64 可编译，不算测试通过。

### 3.2 Android IL2CPP 正确性门禁

Player 在设备上执行完整 220 项清单，包括 managed-core、AOT/解释器边界、
泛型共享、异常、线程、委托、P/Invoke/Reverse P/Invoke 和真实 IL2CPP ABI。
结果写入 `Application.persistentDataPath`，由 ADB 拉回 JSON，再与 .NET reference
做差分和 feature/layer 覆盖检查。

```powershell
./scripts/run-android-arm64-correctness.ps1 -Profile Baseline-Clean
./scripts/run-android-arm64-correctness.ps1 -Profile Candidate
```

通过条件：

1. Player 明确报告 `platform=Android`、`architecture=arm64`。
2. 220/220 通过。
3. 与 reference 的 differential differences 为 0。
4. Player managed assembly hash 与对应 build manifest 一致。

### 3.3 性能测试

21 项 workload、cold/steady、HybridCLR/AOT 的定义与 Windows 基线相同。
单 profile 诊断入口：

```powershell
./scripts/run-android-arm64-benchmark.ps1 -Profile Baseline-Clean -Mode steady -BenchmarkRuntime hybridclr
./scripts/run-android-arm64-benchmark.ps1 -Profile Candidate -Mode steady -BenchmarkRuntime hybridclr
./scripts/run-android-arm64-benchmark.ps1 -Profile Candidate -Mode steady -BenchmarkRuntime aot
```

正式 Candidate/Clean 稳态结论必须使用成对 runner。它按奇数轮
`Baseline-Clean -> Candidate`、偶数轮 `Candidate -> Baseline-Clean` 交替执行，
每个样本强制停止旧进程、重新安装对应 APK，并保存每轮环境快照：

```powershell
./scripts/run-android-arm64-paired-benchmark.ps1 -Pairs 10
```

正式性能判定使用独立进程的 median ns/iteration；核心几何均值仍排除
`interp_boxing`、`interp_boxing_escape`、`interp_boxing_mixed`、
`interp_string_allocation`、`interp_exception`。cold 结果只做
数量级与加载路径诊断，不用于判断小幅优化。

## 4. 设备条件

执行前人工保证：

- 使用同一台 ARM64 真机，关闭省电模式和厂商游戏加速。
- 固定屏幕、电源、网络与后台应用条件；不要一组充电、一组不充电。
- 测试前让设备回到稳定温度；出现明显 thermal throttling 的批次作废。
- 不修改 governor、CPU affinity 或系统 thermal policy，除非整组实验都采用
  同一设置并在报告中声明。

脚本通过 `capture-android-environment.ps1` 记录型号、SoC、ABI、Android 版本、
build fingerprint、电池、thermalservice、CPU governor/频率（设备允许读取时）
以及 SDK/NDK/JDK。由于厂商 thermal 输出并不统一，脚本负责留证，是否作废
仍需按设备实际字段审核。

## 5. 构建与执行顺序

```powershell
./scripts/check-build-environment.ps1 -Target Android
./scripts/build-android-arm64.ps1 -Profile Baseline-Clean -SkipAssembly
./scripts/build-android-arm64.ps1 -Profile Candidate -SkipAssembly -AllowDirty

adb devices -l
./scripts/run-native-tests-android-arm64.ps1 -Profile Baseline-Clean
./scripts/run-native-tests-android-arm64.ps1 -Profile Candidate
./scripts/run-android-arm64-correctness.ps1 -Profile Baseline-Clean
./scripts/run-android-arm64-correctness.ps1 -Profile Candidate
./scripts/run-android-arm64-paired-benchmark.ps1 -Pairs 10
./scripts/run-android-arm64-benchmark.ps1 -Profile Candidate -Mode steady -BenchmarkRuntime aot
```

当前机器没有连接 ADB 设备时，只允许记录“ARM64 交叉编译和 APK 构建通过”；
不得填写正确性通过、性能倍率或 ARMv8 优化结论。
