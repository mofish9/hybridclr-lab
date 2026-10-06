# opt9 发布记录

已发布 Unity 2022 opt9。验收范围为 Windows IL2CPP；Android/WebGL/iOS 尚未取得 Player 资格。
发布时间（UTC）：2026-10-06T12:35:17.229Z。

| 仓库 | 正式分支 | 提交 | runtime tag |
|---|---|---|---|
| hybridclr | optimize/v8.13.0 | 552bc0b549bbcbad223f22fbfb029b83c46e152f | v8.13.0-opt9 |
| il2cpp_plus | optimize/unity2022-v8.11.0 | 135f18fd5a26906e7800e0c4313826eaf2a7756c | v2022-8.11.0-opt9 |
| hybridclr_unity | optimize/v8.13.0 | 4449974800e449b50c3e5cdab32d77e1f51dca78 | 不创建 package tag |

两个 runtime annotated tag 均已验证远端 tag 对象及 peel 后的精确提交，随后才更新并推送 package。
package 迁移使用上述正式分支/提交；Unity 2022 清单已引用 opt9，其余引擎条目保持原值。

本次 runtime 与 ar11 验收身份完全相同。package 功能验收身份为 da9eac383cd953aab58ad0a8c7d851db5e0a36dc，
发布提交只修改安装清单的两处引用，已核对 diff、JSON、远端引用与 SHA。没有把旧 package 测试写成新构建的结果。

门禁：真实 Unity 2022 headers 选择开/关 compile + CTest、6 组启动回归、8 组各 220/220 完整回归差异 0；
正常 DHE / 功能关闭各 400 对严格性能验收通过，解释兜底 200 对通过已接受的成本策略。
兜底 processToEntry P50 增加 14.95 ms，private bytes 增加约 3.98 MiB。
全部证据及显式复用边界见 [发布锁](../manifests/aot-mode-opt9-release-lock.json) 和 [修复验收](AOT-Mode-Native-Enumeration-Fix.md)。
repo-lock 与 Unity2022 workflow 增加独立 opt9 身份，历史 opt3 基线字段继续服务其原验收。

项目需重新安装匹配 runtime，并构建预先启用 enableAotModeSelection 的 Base；
普通 AOT 启动代码在加载热更程序集/补充 metadata 前读取配置并选择模式，DHE 与普通 Assembly.Load 由项目分流。
配置存储、资源开关、下一次启动选择和重启由项目负责。详见 [实施方案](AOT-Mode-Implementation.md)。

运营降级：保存 Interpreter 并重启；同一进程不可改选。整体源码回滚恢复下列 opt8 三仓组合并重建 Base：

- hybridclr：9c607a3c3d45f88ee83ff9dc5bb0f5ad12c57071
- il2cpp_plus：e426adc57c283865126423b169051558b339388c
- package：f2946d5ba35a879724b76afd857176feb1a4adca

本次没有新增 stash；既有 FGS/opt3 stash 保留。候选 worktree 保留其验收身份，正式 package 额外包含发布清单提交。
