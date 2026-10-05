# DHE / 传统 HybridCLR 启动选择设计 v0.1

日期：2026-10-05；更新：2026-10-06（Asia/Shanghai）

状态：设计草案；Windows 双 Player 启动流程实验已通过。单可执行文件内的双后端切换、其他平台及性能仍未验证；见第 17 节和 Windows 实验报告。

候选分支：`research/dhe-startup-fallback-v1`。本文件不是发布报告。

## 1. 目标与明确约束

同一份平台 Base 发行产物预置 DHE 与传统 HybridCLR 两条运行路径；同一份该平台的兼容 Current DLL 和业务资源可在两条路径运行。首次启动选择必须早于运行配置注册；本次 native 进程/WebGL 实例不可切换。库提供选择下一次启动路径及查询本次实际路径的接口。正常 DHE 不增加持续的模式开关检查。

传统模式必须按普通 HybridCLR 规则从 Current 创建解释程序集、类型、字段、方法和静态状态，不依赖 DHE MetaVersion 差异、Current-to-Base 映射、物理 Current 布局适配、DHE 分派、字段外挂或 frozen AOT adaptation。

HTTP、资源下载、远程配置优先级、灰度、提示及触发重启由游戏项目负责。库不下载配置、不主动结束进程、不迁移活对象、不在失败后同进程自动换后端。

“同一份资源”不要求不同平台的条件编译 DLL 相同；要求同一平台两模式的 Current SHA-256 相同。整个资源包可以预置两配置所需的附属 AOT metadata，但不维护两份业务 Current。资源必须通过两模式各自的兼容性和正确性验收。

尚未包含本能力的已发布 Base 不能仅靠资源更新获得 native 双路径。能力应随新 Base 预置，用于后续运营兜底，不移动或重标历史 runtime tag。

## 2. 当前源码证据与身份

| 仓库 | 审计分支 | 精确 HEAD |
|---|---|---|
| hybridclr | optimize/v8.13.0 | 9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071 |
| il2cpp_plus | optimize/unity2022-v8.11.0 | e426adc57c283865126423b169051558b339388c |
| hybridclr_unity | optimize/v8.13.0 | f2946d5ba35a879724b76afd857176feb1a4adca |
| lab | optimize/assembly-load-metadata-lab-v8.13.0 | 09436e96df0be3ed8f6b26ec935bfe549ec5a590 |

上述工作树在审计开始时 clean。候选文档在独立 lab worktree 中创建，不修改正式 runtime、package 或旧锁文件。

已核实的事实：

- `il2cpp_plus/libil2cpp/vm/Runtime.cpp:180`：`g_CodegenRegistration()` 发生在 `MetadataCache::Initialize()` 前。库选择须在第一次注册入口执行之前完成；普通托管启动代码调用已太晚。
- `libil2cpp/vm/MetadataCache.cpp:160`：初始化静态 image/assembly 表、关联 codeGenModule 并注册 AOT assembly；`:988` 按名字优先查静态表。仅隐藏公开程序集列表不能消除静态类型身份。
- `hybridclr_unity/Editor/BuildProcessors/FilterHotFixAssemblies.cs:82`：DHE 特意保留热更程序集为 AOT 输入，传统构建则排除。这是两配置不同的构建根，而非一个 DLL 加载参数。
- `hybridclr/metadata/Assembly.cpp:264`、`:295`：DHE SUPERSET 创建隐藏 InterpreterImage 并关联 Base；不等价于普通 `Assembly.Load`。
- 当前 `il2cpp-api.cpp`、Class、Image、Reflection、Field、GlobalMetadata、GenericMethod 等入口含 DHE hook。只保留两组注册表、共用当前 DHE runtime，不能直接宣称已独立于 DHE。
- 本次比较 opt3 到当前 Unity 2022 HEAD，相关 vm/metadata/API 文件共 16 个，394 行新增、62 行删除；这只是选定路径的 diff 统计，不是全部改动或工作量估计。
- 比较的 `il2cpp-class-internals.h`、`il2cpp-object-internals.h`、`il2cpp-api-functions.h` 未显示差异。这仅说明这些文件在所比较提交间一致，不证明完整 Unity ABI 已兼容。
- 现有接入文档明确区分 Windows trial 与 Android/iOS/其他引擎门禁；现有文档不能作为 WebGL 双路径证明。

非 DHE reference 候选：HybridCLR `v8.13.0-opt3` peel 为 `f40c6f08ccd0391ad9285276b4cc21ada3a180ab`；Unity 2022 IL2CPP `v2022-8.11.0-opt3` peel 为 `bf15337e189ae7da5876aa51c9b896a36c52a155`。两者已确认是各当前 HEAD 的祖先，前者无 `hybridclr/DheRuntime.cpp`。这些是待重新验证的 reference 身份，不是已验收的双路径后端，也不自动指定为最终传统后端。

`lab/manifests/repo-lock.json` 仍指向历史 opt3/团结组合，不能拿它代表上表当前审计身份。设计阶段不更新发布锁或赋予新 runtime contract。

Unity 2022 upstream 固定为 package/HybridCLR 8.13.0、IL2CPP v2022-8.11.0；不借本设计升级 upstream。Unity 2021 和团结的 reference 必须分别核查、编译，不能照搬 Unity 2022 的后端。

## 3. 架构决策与否决项

| 方案 | 结论 | 原因 |
|---|---|---|
| 把 DHE 方法全部标为解释执行 | 不满足目标 | 仍依赖 DHE 类型与字段机制 |
| 隐藏 AOT assembly 后直接加载同名 Current | 不作为正式方案 | 静态 metadata、生成代码、泛型与初始化入口仍可能引用旧类型 |
| 两套生成配置，共用未隔离的当前 DHE runtime | 仅可研究，不能作独立兜底结论 | 普通反射和类型入口仍调用 DHE 实现 |
| 两套完整后端，启动只激活一套 | 作为强隔离目标 | 传统端源码和状态不依赖 DHE；静态链接与 Unity 接入必须先证明 |

推荐的逻辑结构：独立 Startup 控制层 + DHE 后端 + LegacyInterpreter 后端 + 公共资源访问契约。只激活一个后端，Unity 只看到一个活动 IL2CPP runtime、一个 metadata universe 和一个托管 heap。

“两套后端”必须同时隔离生成代码和 runtime 实现，不只是 DLL/metadata。后端单元至少包括：runtime 实现、生成代码、code registration、metadata registration、codegen options、global metadata、桥接、reverse P/Invoke/interop 注册、模块与静态初始化入口，以及依赖的构建资源。

传统后端从明确的非 DHE reference 单独构建。DHE 文件和 hook 不应通过该后端的链接依赖进入执行路径。库允许保留经独立验证的 opt1/2/3 解释器优化；“传统”定义的是加载和执行语义，并非无条件使用最新 upstream 或退回任意旧二进制。

这不是 Unity 已支持“双 runtime 同时运行”的声明。当前源码含单 runtime 假设；要验证的是包装两个可选实现、最终只运行一个的可行性。无法证明引擎边界隔离时，该平台为未支持，不以弱化的解释强制方案替代。

## 4. 正常模式无持续开关检查

选择仅发生于 Startup 控制层。生成托管方法直接调用所属后端；解释器指令、字段访问、virtual/delegate/generic 分派不读取 Startup 模式。现有 DHE guard 不因此新增模式条件。

引擎到 IL2CPP 的 API 入口优先在启动时绑定真实后端函数地址。静态链接下若只能使用 thunk，thunk 只能调用一次选定、不可变的函数表，不可每次 `if (mode)`。函数指针间接调用仍可能增加开销，必须单独测量，不能把“不检查开关”写成“完全零开销”。

不通过运行时机器码修改、可写可执行页面或热补丁实现入口切换。不得以 Android 的动态库加载成功替代 iOS/WebGL 静态接入证明。

## 5. 启动入口与状态机

```text
Unselected --PrepareStartup--> Selected --BeginInitialization--> Initializing
                                                        |             |
                                                        |             +--> Running
                                                        |             +--> Failed
任意 Selected/Initializing/Running/Failed：当前选择不可修改
任意状态：可提交“下次启动”选择，当前选择不变
```

控制层位于托管 heap 和任一后端初始化之前，不依赖 `DheRuntime.Reset`、DHE runtime plan、Unity `PlayerPrefs`、反射或 Current DLL。

`PrepareStartup` 顺序：读取本地请求/显式启动请求 → 验证所请求后端确实在发行清单中 → 验证所选代码/metadata 身份绑定 → 完成所有可失败的入口准备 → 发布 immutable Selected snapshot → 允许后端注册及初始化。选定后即使初始化失败也不得在本次实例选择另一后端。

线程内的启动顺序不足以证明 ARM64 发布正确；多线程入口必须在锁内准备完整快照，以 release 发布，读方 acquire 获取。调用业务不使用这份快照做分派。首个准备期间其他准备调用返回 Busy；完成后重复 prepare 只返回既有选择，不重新读存储。

`il2cpp_shutdown`、Unity domain reload、退出到登录界面、重新下载 Current 均不重置选择。原生重新初始化同进程不受支持。WebGL 用完整页面重载及新的实例；不能复用旧 heap/cache。

必须审计 Unity 在 `il2cpp_init` 之前调用的 API。配置目录、数据目录、memory callback 等可复制到控制层，再重放给所选端。所有权、字符串及数组必须复制或明确约定生命周期。分配内存、返回 managed/class/method 指针或触发初始化的入口，必须先完成选择；不能从未选定的后端取指针再交给另一端。

## 6. 库接口草案

接口以下均为拟议契约，当前源码中尚不存在。

```csharp
public enum HybridExecutionMode { DHE = 1, LegacyInterpreter = 2 }

public static class HybridStartup
{
    // native 已锁定的事实；普通 C# 调用不能改本次启动模式。
    public static HybridExecutionMode EffectiveMode { get; }
    public static StartupInfo GetStartupInfo();
    public static Task<NextStartupResult> RequestNextStartupModeAsync(
        HybridExecutionMode mode);
    public static Task<NextStartupResult> ClearNextStartupModeAsync();
    public static Task<NextStartupSelection> ReadNextStartupSelectionAsync();
}
```

`StartupInfo` 至少含协议版本、控制层状态、effectiveMode、profileId、distributionId、selectionSource（Persisted/Explicit/Default）、supportedModes、restartRequired。effectiveMode 只在 Selected 后有效；native 在此之前返回 NotSelected，托管 getter 不自行推断默认值。

`NextStartupResult` 至少含 status、committedMode（清除时为空）、generation、restartRequired、平台错误；status 区分 Persisted、Busy、InvalidMode、UnsupportedMode、StorageUnavailable、StorageError。成功后的 restartRequired 由“本次实际模式与提交后的下次模式是否不同”计算。

native/JS 提供等价的 `PrepareStartup(context, optionalExplicitMode)`、`GetStartupInfo` 以及读取、写入、清除请求入口。native 调用只传固定协议 DTO，不跨后端暴露对象、metadata 或 C++ allocator 所属指针。startup info 通过 caller-owned buffer 复制。

API 放在独立 `HybridStartup`，不放进 `DheRuntime`。公共 AOT package API 在两个后端均存在；传统构建不包含依赖 DHE 的业务启动分支。共同 SDK 外观只在加载入口选择一次 adapter；传统 adapter 用普通 HybridCLR API，不调用 DHE source batch/MV parser。

设定下次模式从任何线程提交，但 v1 只允许一个进行中的持久化操作，其他写/清除返回 Busy，避免异步写入乱序。查询返回已经提交的值；进行中的写入不是下次有效选择。读取当前模式不依赖存储 IO。

当前启动的显式选择覆盖本次本地记录，但不会自动改写记录；项目如需长期保存，须调用 next-startup 接口。尚未准备时的原生写入成功亦只存储请求，之后 prepare 才能生效。库不设自动 HTTP 优先级。

## 7. 下次启动记录与存储

记录由 Startup 控制层与平台存储 adapter 共同维护，任一后端只是调用者。示意：

```json
{
  "schemaVersion": 1,
  "applicationScope": "project-selected-stable-scope",
  "generation": 17,
  "requestedMode": "LegacyInterpreter"
}
```

applicationScope 由项目在初始化时显式提供，并在 native/JS 和两套托管构建中一致。不能依赖 Android applicationId、iOS bundleId、网页域名自然相等。scope 可在兼容客户端升级后保持不变；不以某个 Current hash 或每次变化的 runtime commit 作 key。发行清单仍负责验证当前可用后端，旧记录不能绕过它。

请求为持久选择，不在读取后自动删除。这样选择一次传统模式后，后续重启不会意外回到 DHE。清除请求后，下次使用发行产物声明的默认模式；本次不受影响。

缺失记录使用明确的发行默认值，初始默认 DHE。记录损坏、scope 不匹配、持久化请求的后端不可用等错误必须返回给启动层，不偷偷当成“没有记录”回到 DHE。宿主可以显式处理错误后选择模式；库不自动决定运营降级策略。

Android/iOS 默认 adapter 可使用应用私有、预 Unity 可访问的位置。写入采用临时记录、完整校验、原子替换并尽可能 flush；启动读旧或新完整记录，不读半写内容。具体断电持久性以平台证明为准。

WebGL 由预 Unity JS adapter 读取同源存储。v1 默认可研究 localStorage，必须处理 SecurityError、QuotaExceeded、iframe/origin 隔离；需同步写并回读再返回结果。宿主提供其他 adapter 时必须满足同一提交语义。浏览器清理/驱逐存储不属于库可保证的持久性，不能把平台接受写入宣称为磁盘 fsync。

保留 host-provided store 接口供小游戏容器、定制启动器使用；必须由启动前宿主实现，不能依赖 Unity 初始化后的 SDK 才读到记录。library 所提供的是存储/选择协议，不是远程策略。请求成功不触发退出，也不等于另一个后端已运行成功。

## 8. 各平台封装候选

| 平台 | 启动选择位置 | 封装方向 | 必须证明 |
|---|---|---|---|
| Android | Java/JNI/native 在 IL2CPP 注册前 | APK/AAB 包含两后端，优先启动时绑定其真实 API；是否动态封装由 Unity 加载链决定 | namespace、各 ABI、JNI/reverse callback、APK/AAB identity 与 metadata 路径 |
| iOS | UnityFramework 初始化前的原生 adapter | 两套静态对象/库实施完整符号隔离，共同引擎入口只绑定所选端 | Mach-O 链接、全局构造器、GC/OS 状态隔离、Unity 内部非公开依赖、Xcode/LTO/dead-strip/reverse PInvoke |
| WebGL | `createUnityInstance` 之前的 JS loader | 优先验证同一发行 manifest 下的两套独立 WASM/framework/data profile，只加载其中一套 | Unity loader/data/metadata 配对、缓存身份、JS exports、桥接与 callbacks、完整刷新后新实例 |

WebGL 的“同一发行产物”可为一个不可变发布目录/manifest，包含两套 profile；它不等于必须在一个 `.wasm` 中链接两个 runtime。两模式共享 Current 和业务资源，profile 专属引擎数据允许分别绑定。若产品另有“单 wasm”限制，则列为额外约束，不能默认已满足；同 wasm 静态双 runtime 的体积、符号与状态隔离需要另行证明。

iOS 完整符号隔离不仅是 C++ namespace：包含 `il2cpp_*`、全局数据、生成方法、P/Invoke wrapper、GC/OS 库、registration constructor 与所有预初始化入口。重命名少数 exported functions 不能算完成。未选端仅允许经审计的无外部副作用静态数据准备，不允许抢占公共 registration、启动线程、创建 managed heap 或执行业务初始化。

双生成代码的库加载构造器不能各自覆盖公共 `g_CodegenRegistration` 或 metadata cleanup 函数。需要将自动注册改造成 profile 专属 descriptor/入口，由 Startup 选定后才安装一套。仅避免调用第二个 `il2cpp_init` 不足以隔离库加载时的副作用。

现有 public `il2cpp-api-functions.h` 只能作为入口清单起点，不能假设 Unity 仅使用这些导出。需实际采集宿主、native plugin、引擎生成 bindings 和预初始化调用关系。若引擎直接静态引用内部实现且无法经受支持的构建集成隔离，必须报告此封装失败，不宣传已经全平台可用。

## 9. Current、AOT metadata 与 Unity 资源

发行工具建立 PairManifest：distributionId、目标平台/ABI/Editor、selection 协议、默认模式、每端 runtime commit/tree、生成代码与注册数据 hash、metadata hash、AOT inventory、保留策略、bridge/interop inventory、资源构建身份及共享 Current 契约。profileId 区分两端；现有 DHE baseId 不冒充传统端的身份。

资源包记录一次 Current DLL 内容，分别声明两 profile 的适用范围及补充 AOT metadata 集合。传统模式仅加载与其裁剪/构建身份一致的 AOT metadata。两端依赖允许不同，但必须在同一兼容资源包中齐备，不要求玩家另下一个业务热更版本。

不得直接复用当前 `DheRuntime.InitializeFromResourceUpdate` 作为传统模式唯一资源入口：它含 DHE capability/MV/execution-plan/frozen-source 依赖。新公共资源 envelope 负责路径安全、签名/哈希及 profile identity；DHE 子段交给原 DHE adapter，传统子段交给普通加载 adapter。保持现有资源字段向后兼容；新能力资源显式声明新协议，不能把旧 schema 偷改成已支持双路径。

普通 AOT 不得直接依赖将被传统构建排除的热更具体声明。共享 interface/DTO 放在稳定 AOT contract assembly，业务实现用反射或接口边界接入。若项目原来利用 DHE 允许的 native 热更布局依赖，需要在双模式接入时修正，不能由名称映射掩盖。

同一 Current 不能把 DHE 专属 API 当作两模式都存在的业务依赖。确有此类调用时，必须通过稳定公共契约提供两端经验证的实现，或在资源兼容性检查中拒绝；不能等传统端加载后才发现 missing icall。

同一业务资源必须验证 Unity 脚本类型引用、MonoBehaviour/ScriptableObject、场景与 AssetBundle、序列化和泛型类型。两次构建的 script type 顺序/ID、裁剪和类型树可能不同；不能认为 DLL 名相同即可共用引擎 data。先保留明确属于 profile 的构建 data，逐项证明哪些可共享。若同一业务资源无法通过两模式加载，资源不属于本方案的“兼容 Current”。

## 10. 故障范围与启动失败

要求覆盖 DHE 专属差异判断、加载映射、类型/字段适配、reflection overlay、分派和 frozen adaptation 故障；传统端不能链接到这些路径。传统端的指令解释器、Unity、OS、业务代码、启动控制层和公共资源损坏仍是共享/基础风险，不能承诺任意 native crash 可救。

失败后查询仍显示已选择的模式，状态 Failed，不把它伪装成运行成功。Startup 的下一次选择入口在后端失败后仍可供宿主使用；如果进程已崩溃，只能由下一次的启动前宿主处理，托管 API 不可能继续运行。

不自动重试另一个模式、不自动重启、不计数 crash 决定模式。若未来引入故障自动切换，应作为独立运营策略，不混入 v1 的确定性协议。

## 11. 指标和不可退化项

主指标：两模式 Player correctness/differential=0；传统模式普通加载与类型语义等价；DHE 故障注入时传统模式独立工作；正常 DHE 无新增持续模式条件检查。

次指标：启动选择 IO/初始化时长、Load、Load+Entry、Load+Reflection、首次/稳态 P50/P95/P99、模式持久化耗时、包体增量、只激活一个后端时常驻内存和未选端 resident pages、WebGL 选中 profile 下载字节/缓存行为。

不能回退：异常/边界语义、ABI/泛型/反射/GC 正确性、并发发布、同 Current 身份、记录提交顺序、重启后的稳定选择和未选端零业务初始化。性能容忍阈值须在实验 manifest 中预先引用/确定；本草案不伪造 policy 或采样收益。

正式 P99 至少 100 独立进程，校验唯一 PID；WebGL 需独立页面/实例和独立 session 身份，不能把同实例重复加载当独立样本。保留未插桩 baseline；故障注入与计数构建仅做正确性/隔离测试，不作性能结论。

## 12. 先行实验与推进顺序

### G0：reference 与 API 边界审计

按三引擎锁定官方 upstream + 当前 DHE + 非 DHE reference 精确身份。建立 public/private Unity API、预初始化、GC、native plugin、callback、生成代码及静态构造器清单。先定义 reference case，再实施选择控制层。

### G1：先验证 Windows 流程，再验证 Android / WebGL 接入

用户于 2026-10-06 明确暂缺 iOS 环境，首轮验证 Android 和 WebGL。iOS 保留设计和后续独立门禁，不阻塞 Android/WebGL 候选研究；首轮结果不外推 iOS。

用户随后要求先基于 Windows IL2CPP 验证流程。执行顺序调整为 G1a Windows 双 Player/pre-Unity launcher 流程实验，再执行 G1b Android/WebGL 实际封装。Windows 候选只证明其明示的进程级选择流程，不替代一个可执行文件内的双 GameAssembly 接入或其他平台。

Android 首先验证同一个 APK 的启动选择、仅加载所选后端及 metadata 配对；WebGL 首先验证同一发布 manifest 的双 WASM profile 与共享 Current。使用极小普通 AOT bootstrap、稳定 contract 和一个 Current DLL，测 effective/profile identity、一次初始化、加载和退出，接着验证保存下一次选择及真正重启。需要目标真实 Editor、设备/浏览器实际产物；本草案未运行双后端 Player 实验。

G1 未通过时不批量改造各 runtime，不发布同后端强制解释作为替代。可以优化封装方式或引擎接入，但必须保留强隔离验收条件。

### G2：选择与持久化 correctness

missing/default、显式 override、写入/清除、重复 prepare、write Busy、跨线程查询、非法值/不可用后端/坏记录/IO 失败、旧 scope/兼容升级、写入中断、初始化失败后的查询/请求、domain reload 不切换、真正重启才切换。WebGL 增补禁用存储、配额、origin、缓存及完整刷新。

### G3：完整加载和运行语义

managed/native differential=0，case 数与独立传统 Player 一致。覆盖 Current=Base、方法新增/删除/重排、类型和字段演进、module/cctor 初始化与异常、泛型、interface/virtual、delegate/calli、反射属性和泛型反射、跨程序集、补充 metadata、Unity 组件/场景/AssetBundle、GC 与 native callbacks。

同一发行产物、同一 Current SHA 分别运行两模式。对 DHE 入口人为注入失败/错误，证明传统端无调用依赖；结合链接 map、symbol 和 call graph 审计，不能只靠零命中计数推断任何故障均被隔离。

### G4：三引擎与目标平台门禁

Unity 2021、Unity 2022、团结 2022 使用真实 headers，各自 native compile/CTest，`mergeReady=true`、`surrogateExternalHeadersUsed=false`。平台另列 Windows、Android ARM64、iOS ARM64、WebGL wasm32 及实际线程配置；每个组合先确认原 HybridCLR/DHE 可用性，不宣称所有笛卡尔组合已支持。

Android/iOS 真机 correctness、尾延迟、内存、温度与弱核证据分别记录。WebGL 的平台限制、主线程/线程构建和部署缓存单独验收。源码可条件提交，但缺外部门禁时不能称为正式全平台发行。

### G5：性能、冻结和发布

正常 DHE 与当前未改动正式构建配对比较；传统端与独立 reference 比较。审计 no-mode-check 生成代码；单独量化 host thunk 间接开销、包体及未选端内存。绑定精确 runtime、package、生成代码、Current 和产物身份后才冻结。

按测试/控制层、各 runtime、各 engine 接入、package/生成器、lab lock/report 分开提交；运行时新 contract 与不可变 annotated tag 仅在正式维护线合入和正式门禁后创建，package 不打 opt tag。推送顺序遵守工作区 AGENTS.md。

## 13. 仓库边界和回滚

- `hybridclr`：共同 Startup DTO/控制层与查询入口的实现边界；DHE 与传统实现分别编译，控制层不得拥有解释 metadata 或调用 DHE。平台存储部分可在 package adapter 中实现。
- `il2cpp_plus`：三引擎入口绑定、selected backend 注册、API ABI、OS/GC/link 隔离与启动顺序。分别维护所需平台补丁；不在 Unity 生成目录做手工源码修改。
- `hybridclr_unity`：公共托管 API、启动 native/JS adapter、双 profile 构建、符号处理、bridge/link 保留、PairManifest 和资源 envelope；HTTP/下载/提示仍由项目实现。
- `lab`：reference fixture、隔离/故障注入测试、平台矩阵、设备性能和证据身份。

源码回滚以仓库级独立提交撤销及重新构建 Base 为边界；不能让某个 profile 的 metadata 与另一个版本代码混配。运营回滚通过 next-startup 接口选择已预置且已验收的 profile，重启后加载兼容 Current；不能撤销公共控制层自身的 native 缺陷。资源回滚到经过该双 profile 验收的归档 Current，必要时重启。

## 14. 本轮交付与待证结论

本节保留初版设计时的审计结论；后续 Windows 实验状态见第 17 节。

已完成：当前源码启动边界、AOT 注册/查找和 DHE 公共 hook 审计；架构否决项；强隔离目标；选择/持久化接口与状态机；各平台封装候选、独立验收与回滚边界。

未完成：任何 runtime 实现、native 编译/CTest、Player、macOS/Xcode、WebGL 实际构建与启动、双后端链接、资源共用证明及性能/包体/内存测量。本轮不声明“有条件发布通过”，仅为待验证设计。

首轮最优先的不确定项是 Android 引擎加载链与单 APK 双后端接入，以及 WebGL 两 profile 的 Unity data/资源兼容；接口设计可先稳定，但实现架构须以 G1 证据定案。iOS 静态入口、符号与状态隔离在环境具备后独立验证。

## 15. 热更发布和维护成本

目标是每个平台一份业务源码、一轮 Current 编译、一份业务 DLL/AssetBundle 内容、一次热更版本发布；两后端的 native 构建发生在 Base 制作时，不在每次资源热更时重新编译 native。

```text
Current 一次编译
  + DHE 附属计划/MV及兼容校验
  + 传统模式兼容校验
  + 各后端所需的匹配 AOT metadata
  -> 一个共享资源 envelope
  -> 双模式验收通过
  -> 一个发布版本
```

两模式各自的附属描述和 AOT metadata 可以不同，这不构成两份业务热更包。资源按内容 hash 去重；相同 DLL/bundle 不重复存储。AOT metadata 跟随其 Base 身份归档，支持内容寻址缓存，不要求每次热更重新下载未变化的 metadata；后端变化或兼容性需求变化时必须重新校验，不能无条件视为永久不变。

| 成本面 | 增量与控制方式 |
|---|---|
| 游戏业务开发 | 仍维护一份代码；初次接入需清理不满足传统解释边界的 AOT 依赖和 DHE 专属调用 |
| Base 构建 | 生成并锁定两后端，增加 native 构建、桥接/裁剪及集成验收；用单命令产出 PairManifest，禁止人工配对 |
| 每次热更制作 | Current 一次编译，增加传统兼容性检查和双模式验证；MV/计划等描述由工具自动生成 |
| 每次热更 QA | 两模式必须测试；有 B 个仍支持 Base、T 个目标平台时，最多形成约 2×B×T 条 correctness lane，各 lane 使用各自平台实际 Current；共享 reference 和并行 CI 可以减少等待，但不能删除门禁 |
| 日常运营 | 保持单发布版本、单资源目录/manifest；增加按 Base 范围切模式及观测 mode/profileId 的能力，提示与重启仍由项目负责 |
| 库维护 | 较高：DHE、传统后端、公共控制层与平台封装均需维护；普通解释器 bug 修复需审查是否同步两端，不能将 DHE 提交整批合入传统端 |
| 包体/CDN | Android Base 多一套后端及可能专属 data，增量待测，不能声称整 APK 翻倍；WebGL 服务端保存两套 profile，客户端正常只下载所选套，首次改选会下载另一套所需文件 |
| 客户端内存 | 不应初始化两个 heap，但映射、缓存和动态库副作用仍需测量，不以“只选一套”推断未选端零内存 |

总体判断：运营流程增量较小，发布验收增量明确，库/构建工具初次开发和长期维护成本较高。现阶段不能给出工时、百分比、包体或内存收益承诺。

若同一份业务资源无法同时满足两种类型体系，应修正共享 contract/资源兼容性或判定该资源不支持双模式，不以为两模式分别发布业务 DLL/bundle 来悄悄改变需求。已验证的传统后端可以锁定为稳定线，但稳定线不是无需测试或无需修 bug 的冻结副本。

## 16. 首轮本机预检查（2026-10-06）

可复现探测脚本为 `scripts/probe-dhe-startup-platforms.ps1`，本轮原始报告为 `reports/dhe-startup-platform-preflight-20261006.json`。报告显式使用 `scope=environment-and-loader-inspection-only`、`dualBackendGateStatus=not-run`，并记录实际库 SHA-256、加载符号及源码 HEAD。它不执行 APK 安装、数据清除或 Player 测试。

```powershell
./scripts/probe-dhe-startup-platforms.ps1 -WorkspaceRoot C:/hybridclr_optimize -Output reports/dhe-startup-platform-preflight-20261006.json
```

- Unity 2022.3.62f3：AndroidPlayer、SDK/NDK/ADB 可用；WebGLSupport 未安装。其本地官方 module manifest 指向同版本 WebGL installer，downloadSize=587745280 bytes、installedSize=2733865984 bytes；补模块不需要升级 Editor/upstream，本轮未下载安装。
- Unity 2021.3.45f2、团结 2022.3.62t10/t12：存在 WebGLSupport，可用于各自 profile 的研究，不能替代 Unity 2022.3.62f3 的正式验证。
- 调用 Unity 2022 ADB `devices -l` 未发现设备。无 Android 双后端真机 correctness/重启/内存或性能结果。没有安装 APK、清除设备数据或复用旧结果。
- 实际读取 Unity 2022 Release ARM64 `libunity.so` 的 ELF：存在 `dlopen`、`dlsym` 未定义入口，没有 `libil2cpp.so` 的 DT_NEEDED 项；字符串含 `il2cpp_init`、`il2cpp_set_data_dir`。`libmain.so` 字符串含 `libil2cpp.so`、`libunity.so`。这是运行库加载入口研究证据，不足以证明双后端可替换或所有调用已覆盖。
- 阅读团结 WebGL loader 模板，存在 `frameworkUrl`、`codeUrl`、`dataUrl` 配置；framework 下载从所选 URL 创建 script。说明 profile URL 选择存在研究切入点，不是 Unity 2022 双后端或共享业务资源正确性结果。

上述仅为环境与加载入口预检查；G1 双后端实现、实际构建与运行未完成。Android 设备连接和 Unity 2022 WebGL 模块缺失分别影响真机与目标版本 WebGL 验证，不能写为通过。iOS 本轮明确延期。

## 17. Windows 流程实验（2026-10-06）

详见 `docs/HybridCLR-DHE-Startup-Windows-Experiment.md`。在冻结 fixture 提交 `115bc81c9663d64c234e486b1924ea2986c3fc30` 上，8 个 PID 唯一的实际 Windows IL2CPP Player 进程完成选择、重启、持久化、清除和 DHE 输入故障后的传统解释流程；7 个正常 workload 进程均为 10 cases、CLR differential=0，另一个为预期的 DHE 拒绝。损坏本地选择记录也已验证在启动 Player 前拒绝。

两 Player 从同一发行目录启动，读取同一份 SHA-256 为 `bbc6f6204fcb8659edefc058358a83bf50198e7fa985357e5433f64ae02cab51` 的 Current DLL。传统端无热更 AOT assembly，使用非 DHE opt3 runtime 的普通 `Assembly.Load`；DHE 端验证改变方法选择解释、未改变方法保留 AOT。最终原始报告为 `reports/startup-windows-115bc81/summary.json`。

选择发生在独立 .NET 启动器启动 Unity Player 之前；托管 `EffectiveMode` 来自该 Player 编译时固定的 profile，不能修改。持久化接口在 AOT Player 中真实执行；发现托管命名 Mutex 不受 IL2CPP 支持后，改为 Windows 原生 Mutex adapter 并重建两 Player。

这是库侧契约的 Windows prototype，代码在 lab fixture 中，未迁入正式 runtime/package。它不证明单可执行文件内双 runtime、完整三引擎矩阵、全部 DHE 功能边界或 Unity 资源共用。实验 DHE 开启 dispatch diagnostics、关闭 ordinary AOT guards，限定纯 managed hotfix workload，不可当作生产等价性能或全边界验收。没有新的 runtime/package tag 或正式维护线改动。
