# opt6 Unity 2022 项目打包试用交付

2026-09-13：三个正式 repos 已提交并推送；两个新的不可变 runtime tag 已远端核验。
本轮目标“准备到可进入项目打包流程”有条件通过。工具仍为 Exploratory，未签发
完整 Release 资格，未修改 cat。操作说明见 `docs/HybridCLR-DHE-Opt6-Project-Trial.md`。

## 正式来源与交付包

| 仓库 | 正式分支 | commit | runtime tag |
|---|---|---|---|
| hybridclr | optimize/v8.13.0 | 04bd9b5cd5153a31e76815d65bc1a9b5655a5a05 | v8.13.0-opt6 |
| il2cpp_plus | optimize/unity2022-v8.11.0 | 57f3a065e84e3b1e7e13c1012752e27f23e4c041 | v2022-8.11.0-opt6 |
| hybridclr_unity | optimize/v8.13.0 | dff191350c250c60d9a9fb910b1e612e049fd445 | 无 package tag |

三个维护线均 fast-forward 合入，不引用候选 worktree。runtime tag 对象分别为
`09e39065e3f9ac93e6a08415d7e140d07fc80839` 和
`56024d3b70e29156953cac47a90509d692b40bbf`，远端 peel 均为上表提交。
先推 runtime 分支/tag，确认远端解析，再更新与推送 package。opt5 历史引用未移动。

上游仍是 HybridCLR/package 8.13.0、IL2CPP v2022-8.11.0；未同步更高 upstream。
不允许的 IL2CPP 祖先 `11251b938d2ce7fa865165130bf257ca239db69f` 仍不在当前维护线。
Unity 2021 保持官方版本，团结继续保留上一轮选择，本轮未发布团结 opt6。

迁移 ZIP：`F:/hybridclr_artifacts/dhe-opt6/hybridclr-unity-opt6-project-trial.zip`。
共575个 tracked 文件，其中随包工具85文件，保留 @8.13.0，无 bin/obj/.git 混入。
ZIP、bundle、源码和全部证据哈希锁定于
`manifests/dhe-unity2022-project-trial-lock.json`。

## 本轮实现收口

修复 constrained 值类型和装箱枚举器的 Current 分派：方法声明 owner 经 metadata
映射到实际物理执行 owner，再比较引用父链或精确 box 类型，并校验具体调用帧。
原 ABI 拒绝保护保留，未合入旧实验 a7c4cbe 的 blanket bypass。

新增 `HYBRIDCLR_DHE_HAS_PHYSICAL_RECEIVER_DISPATCH`，IL2CPP hook 与 package
构建入口要求配套 runtime；managed/build contract 升为 dhe-runtime-v34，MV
schema 不变。资源生成对物理布局、conditional generic 与 frozen AOT 适配要求
`physical-current-receiver-dispatch-v1`。真实旧 opt5 Base 的资源生成被准确拒绝，
原因仅为缺少该能力；没有运行旧 Player 或修改其 identity。

前期 package 候选的工具源码归包、随包 DLL、普通 Installer receipt、C# 完整
BuildBase、失败恢复、默认 ordinary guards、Base 所属 metadata、泛型调用闭包、
TLS 与严格空方法证明，现已进入正式 package 分支。项目不需要复制独立 Lab 工具。
安装 URL 仍取自项目设置，版本 JSON 只选 tag；新的安装校验绑定两个 runtime 的
927个源码文件及 canonical SHA，防止混装。错误 Release 证据仍被拒绝。

## 当前提交对应的验证

产物根为 `F:/hybridclr_artifacts/dhe-opt6`。以下是本轮新生成证据，不复用旧 opt5
Player 通过结果来冒充新提交验收。

| 检查 | 结果 |
|---|---|
| native compile / CTest | 真 Unity 2022.3.62f3 headers；mergeReady=true；FGS=true；surrogate=false |
| 普通 Install + 完整 C# BuildBase | 两份新 Base，revision59 /61，各通过 no-op 资源运行 |
| 构建失败恢复 | before 失败、after 回调、错误 identity 拒绝与模板恢复，两份均通过 |
| 普通 AOT guard coverage | 每 Base 40 程序集、49,650 请求、missing=[] |
| package 安装/来源 | 最终 package 30项通过，包括缺失/混装/旧契约拒绝与 schema |
| 托管加载与状态 | 126项通过；native 调用是模拟，不作 Player 证据 |
| 随包工具 / 发布拒绝 | Unity 自带 host 26项；错误 Release 证据6项 |
| 新能力协商 | 真实 metadata 5项；另有真实旧 opt5 Base 生成拒绝 |
| 原始 boxed Current A | 两 Base 各47项，与 CLR 完整顺序相同；篡改拒绝、恢复通过 |
| 资源类型演进 Current B | 两 Base 各47项，与 CLR 完整顺序相同 |
| 独立只读资源审计 | A校验125文件，B校验117文件；原 Base、Current、Player 不变 |
| 资源 authoring/来源 | 实际 Unity 生成新增类型的 Prefab/Scene AssetBundle；16项来源检查 |
| Delivery 托管语义 | 49项通过 |
| Delivery 真实 Player | 两 Base 各47项业务与42项资产检查；另4次错误输入在prepare被拒绝 |
| public preparation/recovery | 两 Base 各11项失败语义，恢复后47项业务与17项生命周期通过 |
| A→B→A 回退 | 两个不同 Current 集合，两个 Base，各3个新进程；完整序列及所有输入哈希保持 |

Current A 为前一轮原始失败用例的精确字节。Current B 从已有资产演进 fixture
追加装箱枚举器而来；本轮重新编译追加用例、重新生成全部资源/AssetBundle并实际
运行。用例名称不是所有 C# 功能的覆盖证明，也不据此宣称任意结构更新均可执行。

新 Base ID：

- `c445a700233c3d4d6f49fe71266735721eeae1f44fe4e3d51b8d6af4adad15e4`
- `f8dde3f739d6d25d07cc78264394e2c71ff0510960e175a6381db80cd46ea8b4`

Delivery manifest SHA-256：
`BC04DD554F71DF7AB005A70AB6CF23BE89DCCEEEC98133A1631F356220256E42`。
失败资源/错误manifest都先于 metadata commit 和业务入口被拒绝。回退为选择已归档
资源并启动新进程，没有模拟热迁移已运行对象，也没有替项目实现 CDN 渠道切换。

## 证据身份与审核限度

工具源码冻结于339338e，分发于a2b9a47；DLL SHA为
`28CFEC6AE2343232AF9D1666BB018CCE9C70C7F3D39FDD63957B0B19EEEB819E`，
bundle ID 为 `81f07c851487c43dfa7bdc4d1fb1a4ef0e025e1f8af3ed7520cfeb1410e8e53b`。
最终 package dff1913 仅追加一处打包文档纠正：说明 frozen ordinary AOT 兼容资料
也可能随更新分发。两份 Base 的真实 package 提交仍记为a2b9a47；没有重标旧构建。
逐文件比对确认573个其余 tracked 文件与最终 package 完全相同，排除该文档和
预先声明的自动生成 .meta。最终dff1913重新通过完整安装receipt suite，ZIP亦从
其精确Git tree导出。没有因为纯文档更正重复 native/Player 构建。

Lab native执行提交09e7a3b，Base/authoring执行提交3ef0e1a，资源、Delivery、回退
执行提交193df62；报告提交不替代它们。native阶段 runtime-manifest 引用当时正式
package4fb36af，仅用于运行时源码拼装；新 Player 则通过普通 Installer 使用
a2b9a47与opt6。最终从三个正式 repos 和新锁重新拼装成功，其 native tree SHA
`6B4A37DCC805E20F77C4D3EB15C9FA73E4FE5C533C8F64ADA7BA78F6A040EF55`
与native门禁逐字节一致，不能把两个不同manifest的哈希混写。

本輪测试工具修复包括：Delivery原固定46项改为精确CLR完整顺序比对；新增归档
A/B/A回退验证。没有放宽业务断言、忽略返回码或修改测试中的原Player。数据仍包含
旧opt5预期拒绝报告，passed=false是负例成功所需条件，不是新的发行阻塞。

源码审阅检查了物理类型比较、调用帧与byref保护、既有metadata锁和acquire发布，
没有新增无锁发布缓存。Windows回归不证明ARM64内存模型。新增映射的热路径锁开销
未测量，不作性能收益声明。旧工具的部分Lab-only历史维护门禁仍按旧Lab布局定义，
未作为本轮Release资格；项目消费命令、DLL边界及上述完整流程已实际验证。

## 项目责任、剩余门禁和回滚

框架交付已进入三个 repos。项目侧仍需接入自身打包adapter、resource provider、
场景/签名/上传、业务启动与设备用例；这是项目接入工作，不要求往package放cat逻辑。
后续target使用对应平台Current，完整归档每个Base，发一套Current配合所有目标Base
的选择记录。原本普通AOT程序集仍不可发布新版本；frozen原始IL只用于兼容执行。

Android/iOS/macOS/ARM64、团结、性能、内存、P99以及完整Release证据/认证仍未完成，
因此只称“可开始项目打包试用”，不称移动端生产可发布。这个范围遵循用户当前仅有
Windows环境、后续由项目实际打包Android的安排。

对已有opt6 Base，可重启并选择已归档且匹配的资源（本轮已实测A/B/A）。native或
package运行时实现变更则重新构建Base；不能修改旧Player文件补丁化替换、改baseId
或移动tag。opt5不再推荐使用，历史引用仅保留审计用途。三个正式工作树及本轮
候选分支交付时clean；维护工作树保留的ignored bin/obj不在迁移ZIP中，没有新增stash。
