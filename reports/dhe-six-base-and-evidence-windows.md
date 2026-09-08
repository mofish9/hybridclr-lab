# DHE 六 Base 世代与证据工具验证（Windows）

## 结论与范围

Unity 2022 和团结 2022 各有 original、evolved、generic-interface 三类 Base。
六个 Base 的构建/no-op/schema 均通过；同一份 first/latest DLL/MV 完成 18 个独立
Player 进程的连续/跳版本更新。每次执行 61 组演化、220 个 differential cases，
差异为 0。该结论覆盖这些具体场景，完整 DHE 目标尚未完成。

只有原本配置为 hotfix 的四个测试程序集参与 DHE；普通 AOT 依赖不纳入热更。
Unity 2021 不再安排新构建或门禁，其已有 registry 身份仍可读取用于历史审计。
这里的连续更新发生在替换资源后的新进程，不是同一进程内卸载已加载程序集。

## 源码与产物身份

运行时与 package 保持上一轮精确身份，本轮没有修改它们：

| 组件 | commit |
|---|---|
| HybridCLR | 0787aad2e3c62d071113d028d050836b0bc68b8d |
| Unity 2022 IL2CPP | 7fa10da3fc29b4a9a78bfa83e5d8bfba9cef32c2 |
| 团结 2022 IL2CPP | 589dad732d34702f7d95432c4fc0ed75453a424f |
| Unity package | f80d4c3a56e57fc61114c922735614b84561a82a |
| original/evolved 构建时的 clean lab HEAD | 24cfbb45871d74a33827f81d8ac608d4ff0648ba |
| generic-interface 构建时的 clean lab HEAD | 0ee45e07e6ed24ef6ed5aafc433f5ea1c30aeceb |
| 完整六 Base replay | dc5396cfb99bfafcba92867e2473edb6a45e6c59 |
| 合并后 mixed-call 冷启动 replay | da31f1835b15e9dce14c38092a67fae1126b6837 |
| 最终资源计数与证据校验工具 | b53cad172ffd52c476e8c39556bb34d06c6c2d2e |

Base 构建使用 host-field-bound / toolchain-field-bound（工具源码 0ee45e0），
Unity 2022.3.62f3 / 团结 2022.3.62t12，Windows x64、OptimizeSize/FGS、无补充 AOT
metadata。native-field-bound 的两份真实头文件 compile/CTest 结果仍属于上述未变的
运行时身份：mergeReady=true、surrogateExternalHeadersUsed=false，不是新增性能结论。
运行时合同为 dhe-runtime-v26；MV 仍为 DHEMETA1 schema 1。

下述产物根均为 C:/hybridclr_optimize/artifacts/dhe-evolution-20260908。

| Base | Base ID |
|---|---|
| base-field-original-u22 | e13f702c98098cf1fcee60d6dec2ffc978841b731b47f8dfec85740f3f71f537 |
| base-field-evolved-u22 | 94477903dd169b6925fecb96d8b710dd33672634a4ad5a3cf12074acfb914980 |
| base-field-generic-u22 | edf415faf8878f9635f697070ba707ce1d783c311b52d6fb7988dc43c587497f |
| base-field-original-tj | 583f44c605fa12f2eb40f4bdf294af6050f07cd9d90889178f95ffa9b504f5d8 |
| base-field-evolved-tj | 71e821c739bb7ef343e21d2008a7709ecfd929f624db437f758d1b813b671a63 |
| base-field-generic-tj | ca39966655eefc841faeb243e8f611d861060107a3b8e96b927fae61234ba899 |

每个 Base 的 no-op 结果均为 changedMethodCount=0、interpreterEntryCount=0、
noOpAotBehaviorValidated=true。原始 Base 文件与回放副本独立复核了 168 个唯一文件，
全部匹配。两个资源包共 16 个 DLL/MV 文件与上一轮两 Base 的 payload 完全一致。

完整回放配置为 manifests/dhe-field-address-six-bases-windows.json；
资源为 resource-field-six-first/latest，registry 为 registry-field-six.json。
replay-field-six-verified/report.json 的 SHA-256：
32B25C258711D314CF4477048F92A0402E99D36F0AEC5C9F67F19A546B7AA90D。
同目录 independent-audit.json 记录独立检查与 18 个唯一 PID。

original Base 每次执行全部 48 项旧成员断言；evolved/generic Base 的 8 项不适用断言
明确标记未执行，不计作成功。这些 Base 仍执行全部 61 组演化与 220 cases。
latest/skipped 每次都有 220 个解释器入口 receipt。first 没有要求所有 case 方法改变。

## 修复的验证问题

1. 新增调用方法没有 Base token，IsDifferentialMethodChanged 正确返回 false。
   DHE 边界计数也不统计所有解释器内部调用。宿主原先把这两个值当成新增方法必须
   经过 Base guard 的证据，导致 12 次旧世代回放误判失败。现按 Base/Current
   方法及声明类型的存在性区分新增调用与保留 AOT 调用；已有未变化调用仍强制要求
   nativeChanged=false 和正数 AOT 入口证据。没有修改 DLL、MV 或 Player 结果。
   详见 ../docs/HybridCLR-DHE-Caller-Evidence.md。真实失败记录加入回归，50 项通过。

2. 正式工具原先要求固定入口本身改变，误拒绝 AOT 入口调用解释器方法的情况。
   新校验器验证方法身份、Base 成员关系、Current 版本、native 标志和执行计数。
   从 Base workflow 与资源 manifest 解析 DLL，并复算 DLL/MV 哈希与被选中的
   Base 身份绑定。错误方法、错误 Base、错误路由、缺少 receipt、无执行或计数异常
   都会被拒绝。

3. 资源计数必须包含修改、新增、删除；原生 guard 数只用于原生覆盖检查。
   bodyless 接口声明会产生差异但不需要方法体 guard，已删除方法仍参与运行时差异。
   original/evolved/generic Base 对 latest 的变化数分别为 866/765/619。
   六个真实 Player 均与修复后的工具计数匹配。31 项实际 payload/registry/计数
   回归全部通过；修复前 mixed-count-before.json 的 8 项计数失败保留。

最终工具位于 toolchain-resource-count / host-resource-count，Package ID：
b2f0525262ae0cce029e111ed1fc1bd846e3ebf432ae5393c3aca829ce77e22b。
它保持 Exploratory、releaseReady=false。

合并后额外运行两引擎的 first 冷启动，保留当时的 staging，避免随后 latest 更新
使旧 first stage 路径内容发生变化。SingleUpdateEvidenceOnly 明确不能用于结构世代
验收；它只验证 mixed-call 证据，不替代前述 18 次完整回放。
replay-mixed-integrated/report.json SHA-256：
83B7BAA1B640E0C0FA044BD85E3C6B5671F67BB3ACBC8559859F18B06024158B。

最终工具成功生成 evidence-resource-count 下八份证据：六 Base 的 latest，以及
两 generic Base 的 first。八份文档的 schema gate 通过，无跳过文档；它们仍是
Exploratory 证据，不是完整 Release ledger/authority/source 门禁的通过声明。
mixed-count-after.json SHA-256：
EAF588D81A8E0DC822F25278B123056930C526AE2EBF500BD246CD393EAAA71A。

## 保留的失败与工作区

replay-field-six 保留资源构建尚未完成时的前置检查失败；replay-field-six-run
保留 6 成功、12 宿主路由判断失败的完整记录。replay-formal-first 保留 focused
配置与资源 registry 不一致的失败。没有覆盖这些报告或先前原生崩溃档案。

源码已收拢到主研究工作树。mixed-evidence 独立工作树的修复提交已 cherry-pick
到主线，保留其 clean 状态用于复核；后续删除方法计数修复在主研究线完成。
四个 runtime/package 源工作树、两份 Demo scratch 及主/独立 lab 工作树均已整理
为 clean；没有本任务活动 Unity/Player 进程。本轮没有推送正式分支或 runtime tag。

evolved 输入可从以下新增 stash 恢复；原有 stash 全部保留：

| Scratch | stash |
|---|---|
| Unity 2022 | f46b3cb85e31af9144ff982ed7ad1852895f0ea9 |
| 团结 2022 | dfa1226baedf81d2f67409106ea7250daa38a2b7 |

## 未完成的目标与回滚

资源分析器仍明确拒绝已有普通类新增虚/抽象方法、部分已有类型的布局/vtable 改变，
以及已有值类型增删实例字段。这些不是只缺测试的功能；它们仍需要运行时实现。
下一阶段优先推进已有类的虚方法/继承演进，再处理值类型布局和 Unity 行为。
并发/GC/ABI 的更广覆盖、AOT 保留率与性能/内存、完整 Release 工具资格和 Android
验证也未完成，不能据此宣布整套 DHE 可用于任意项目改动。

回滚本轮工具可使用精确冻结的 host-field-bound / toolchain-field-bound（0ee45e0）；
运行时/package 身份不变，但旧工具不具备这轮 corrected mixed-call/计数验证能力。
更早运行时需配合其自身已验证的资源，不能重标当前证据。Base 原生代码的修复仍
需要新 Player，不能通过资源包更新。当前全部是研究候选，没有改变项目安装默认值。
