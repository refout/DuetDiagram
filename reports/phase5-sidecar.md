# Phase 5 人工产物——固定位置、固定折线与自定义端口不再随保存丢掉

- 测量时间：2026-09-30
- 测量机器：AMD Ryzen 5 6600H（6 物理核 / 12 逻辑核），16 GB
- 系统：Windows 11 专业版 10.0.26200
- 运行时：.NET 10.0.11，x64 RyuJIT，.NET SDK 10.0.303
- 用途：按用户「完善保存功能」的要求，把 `user.json` 的读写接上界面
- 基线提交：`f8832f5`（下称「改前」）

## 复现

```bash
dotnet build DuetDiagram.slnx -c Release

dotnet test --project DuetDiagram.E2E.Tests  -c Release --no-build -- --filter-trait "Category=Sidecar"
dotnet test --project DuetDiagram.Core.Tests -c Release --no-build -- --filter-trait "Category=Sidecar"
dotnet test --project DuetDiagram.E2E.Tests  -c Release --no-build
dotnet test --project DuetDiagram.Core.Tests -c Release --no-build -- --filter-trait "Category=CommentDiscipline"
dotnet run --project tools/LocCounter -c Release -- --root "$PWD" --check
```

**注意：本机 `dotnet test` 不能带 `--nologo`**，带了之后参数被当成未知项转给测试应用，
结果是「运行了零个测试」加退出码 5。另外**不要与别的 `dotnet` 命令并行跑**：
并行时汇总里会凭空多一次失败。

## 要解决的是什么

`DuetDiagram.Core/Sidecar/` 那一整套是完整的：路径约定、两类文件的读写、
四档加载状态、孤儿报告、备份的建立与轮替、按备份恢复。而它在 `DuetDiagram.App` 里
**一个调用点都没有**。后果是四条，每一条用户都能撞上：

| 现象 | 根因 |
|---|---|
| 拖过的节点位置，保存之后下一次打开没了 | `Save` 不写 `user.json`；打开也不读 |
| 边上的折点两种形态都存不下来 | 折点只在会话里（`_pinnedEdges`），IR 与 DSL 都装不下 |
| 备份从不轮替 | 没人调 `SidecarBackup.Save` / `Prune` |
| `user.json` 坏了没人问 | `ShowSidecarRecovery` 的注释写着「打开文档那条路还没做，所以现在没有调用点」 |

## 三样人工产物各自走哪条路

| 内容 | 存在哪 | 谁读它 |
|---|---|---|
| 固定位置（节点 id → 坐标） | `user.json` 的 `pinnedNodes`；DSL 那一形态另写在文本的 `pin` 里 | 布局（作为不可移动的锚点） |
| 固定折线（边 id → 点列） | `user.json` 的 `pinnedEdges` | 路由 |
| 自定义端口（节点 id → 端口列表） | `user.json` 的 `customPorts`；DSL 那一形态写在节点声明上 | **还没有人读** |

**自定义端口是这一条里唯一「只进不出」的东西。** 端口本身写在节点的 IR 字段上
（属性面板改的就是那一份），而 `user.json` 里那一份是同一件事的另一个记法，
布局与路由都还没读它（`docs/Sidecar.md` 里原来就记着这件事）。
整份原样带进带出，为的是「打开一份别人给的文档、改一处、保存」不会顺手清掉他文件里那一节——
**内容丢了是重算不回来的**。

## DSL 那一条为什么不读也不写

两种形态同名同目录共用同一份 `user.json`——`SidecarPaths.Stem` 只去一层后缀，
所以 `a.dsl` 与 `a.dgm` 会撞上同一个 `a.user.json`。

`.dsl` 的固定位置写在文本的 `pin` 里，读 `user.json` 的话：打开一份 `.dsl`
会把另一份 `.dgm` 的固定位置带进来，保存时再写回去。这是两份文档的人工产物互相串台，
而串台之后没有任何提示。

**代价是折点在这一形态下仍然存不下来。** 如实记在 `docs/Sidecar.md` 的「尚未实现」表里，
没有写成已解决。用例 `Saving_a_dsl_document_does_not_touch_the_sidecar` 把这条决定钉住了：
打开一份 `.dsl`、保存，旁边不该多出 `user.json`。

## 空文件那一条分两半

- **三样都空、而且原先没有这个文件** → 不建它。一份从没被拖过、没改过折线的文档旁边
  多出一个空的 `user.json` 对用户没有意义，而它会被拷来拷去、被版本管理盯着。
- **原先有内容、而现在清空了** → 照写。只按上一条做的话，用户撤销掉固定位置再保存，
  旧内容会留在文件里，**下次打开又把它装回来**。

判据在用例里就是「拖一下 → 保存 → 撤销 → 保存 → 重新打开 → 没有固定位置」。

## 改了什么

| 文件 | 改动 |
|---|---|
| `DuetDiagram.App/Services/DocumentLaunch.cs` | 多带一份 `SidecarLoad<UserSidecar>?`；IR JSON 那一条读它，DSL 那一条给空；读文件本身失败也收成「读不出来」 |
| `DuetDiagram.App/Services/DiagramSession.cs` | 多收一份侧车并装进三样东西；加 `ApplyUserSidecar`（装完重排一次）；加 `CustomPorts` 读数 |
| `DuetDiagram.App/MainWindow.axaml.cs` | `Save` 顺带写人工产物；构造里清一次备份、排一只每小时清一次的表；接上恢复提示的两个选择 |
| `DuetDiagram.E2E.Tests/SidecarTests.cs` | 新建。13 条，见下 |
| `docs/Sidecar.md` | 「尚未实现」表里收掉两件（`.dsl` 读 `user.json` 改成一条明确的决定、恢复对话框已接上） |

**附属文件读不动不让整份文档打不开。** `SidecarStore.LoadUser` 按契约把读写异常照常抛出
（那是环境问题，静默吞掉会让它变成更难查的「保存没生效」）。要不要紧由调用方定，
而 `DocumentLaunch` 给的答复是「不要紧，但要说出来」：收成 `Unusable`，
文档照常打开，提示摆出来。

## 判据

**端到端 `Category=Sidecar`，13 条。**

读三条：

- `Opening_a_document_puts_the_pinned_nodes_back`——`user.json` 里的固定位置在首帧就生效
- `Opening_a_document_puts_the_edge_bends_and_custom_ports_back`——折点与自定义端口同样装回来
- `A_corrupt_sidecar_shows_the_recovery_dialog`——坏内容弹提示，而**文档照常打开**

写七条：

- `Saving_writes_the_pins_next_to_the_document`
- `A_pinned_position_survives_a_save_and_a_reopen`——**这一条是整块的目的**：
  拖一下 → 保存 → 关掉 → 重新打开 → 位置还在
- `A_document_with_nothing_manual_gets_no_sidecar`
- `Saving_a_second_time_keeps_a_backup_of_the_first`（第一次保存时没有旧内容可备份，所以那一次是空的）
- `Saving_after_clearing_the_pins_clears_them_from_the_file`
- `A_read_only_window_does_not_write_the_sidecar`
- `Saving_a_dsl_document_does_not_touch_the_sidecar`

恢复两条：

- `Restoring_from_a_backup_puts_the_pins_back`——点「从备份恢复」之后位置真的回到图上
- `Discarding_the_recovery_leaves_the_document_alone`——什么都不装，坏文件留在原地

备份轮替一条：

- `Opening_a_document_prunes_the_backups_beyond_the_policy`——攒 12 份、开一次窗口、剩 10 份

**断言落在文件与重新打开的窗口上，不落在「某个方法被调过」。** 只断言「读过了」的话，
一个读完就把结果丢掉、或者写了但写错地方的实现照样能过，而用户丢的是他摆过的位置。

**孤儿条目原样留下，不丢。** 它们指向的节点这会儿不在文档里，因此是惰性的；
直接丢掉等于让用户的一次误删顺手毁掉他手工调整过的东西。方案 §11.2 那一级写的是
「移入 orphans 区，保留 30 天」，本仓 Core 的做法是**如实报告、丢不丢由调用方定**，
而界面这一侧的选择是不丢。**没有单独存一份「孤儿区」**，那一条差异见下。

## 「把实现撤掉它就红」

验过四次，四次都只改实现、用例一个字不动：

| 撤掉什么 | 结果 |
|---|---|
| 构造里不装侧车（`Seed(sidecar)` 传空） | 13 条里红 3 条（三条读的） |
| `Save` 里不写人工产物 | 13 条里红 5 条（往返、备份、清空、DSL 之外的写路径、从备份恢复） |
| 构造里不弹恢复提示 | 13 条里红 3 条（坏内容、恢复、放弃） |
| 打开时不清备份 | 13 条里红 1 条（备份轮替） |

四处都复原后 13/13。

## 门禁读数（本机 Release）

| 门 | 读数 |
|---|---|
| `dotnet build DuetDiagram.slnx -c Release` | 0 警告 0 错误 |
| `Category=Sidecar`（界面侧，新） | 13 / 13 |
| `Category=Sidecar`（核心侧，未改动） | 20 / 20 |
| 整份 `DuetDiagram.E2E.Tests` | 268 / 268（改前 255） |
| `Category=CommentDiscipline` | 1 / 1 |
| `LocCounter --check` | 退出 0 |

CI 加了一条门禁：**两侧各跑一次**（核心层验存与取，界面层验接上了没有），
在一个 `shell: bash` 步骤里按 `STATUS` 累积退出码——少累一次的话，
前一条失败而后一条通过时这一步会绿。
CI 运行号与结论见下面「补记」。

## 没验的

1. **每小时那一次清理与「关窗口时停表」没有用例。** 那只 `DispatcherTimer` 是私有的、
   没有可观察的出口，用例只验到了「打开时清一次」。定时器本身要靠读代码与真机长时间挂着看，
   这两条都还没做。
2. **孤儿条目的 30 天保留期没有落地。** 界面这一侧的选择是原样留着（惰性、无害），
   而方案那一级写的是「移入 orphans 区，保留 30 天」——**没有孤儿区，也没有计时**。
   下一次清理孤儿该按什么口径，是一个还没定的问题。
3. **`.dsl` 那一形态的折点仍然存不下来**（见上）。这是刻意的取舍，不是遗漏。
4. **同一目录下 `a.dsl` 与 `a.dgm` 并存时会发生什么**没有用例。文档里写着
   「两者不该并存」，但这一条只有一句话，没有任何装置拦着用户这么做。
5. **自定义端口仍然没人读。** 只验了「原样带进带出」，没有验「它真的生效」——
   布局与路由那一侧的工作还没做。
6. **`user.json` 很大时的读写耗时**没有量。三样东西都按元素个数线性长，
   而一次保存会走「读旧内容 → 解析 → 拷贝一份 → 写新的」四步。

## 与任务清单的出入

`P5-13` 不在方案 §13.9 那套编号里——它由用户「完善保存功能」这一条要求派生，
编号顺延 Phase 5 已有的号。用户选定的范围是「侧车接线」这一档：
**只接 `user.json`，不做另存为、脏标记、关闭确认、最近文件与自动保存**——
方案对那五样是沉默的，所以它们不在这一条里，也没有记成「未做」。

两处与任务卡的字面出入，都是落地时按方案与 `docs/Sidecar.md` 定的：

- 卡里写「在两条读法后调 `LoadUser`」，落地时**DSL 那一条不读**——
  `docs/Sidecar.md` 里已经写着「`pin` 意图随 DSL 文本走，不写 `user.json`，
  所以打开 `.dsl` 再保存不会动到 `user.json`」，那是一条刻意的决定而不是缺口。
- 卡里写「三样都空时跳过写」，落地时**加了「且原先没有这个文件」**——
  只按字面做的话，用户清空固定位置再保存会留下旧内容，下次打开又装回来。

## 补记

推送后 CI 跑过一次：**两个作业都绿、无跳过的步骤**。

| 项 | 读数 |
| --- | --- |
| 运行号 | `36666457310`（提交 `b2db813`，首次尝试，9 分 17 秒） |
| `编译 + 测试 + 契约门禁`（ubuntu） | success，49 步，非 success 0 条、跳过 0 条 |
| `界面栈自检 + 端到端 + 原生发布`（windows） | success，35 步，非 success 0 条、跳过 0 条 |
| 新增的「人工产物门禁」（`Category=Sidecar`） | 第 20 步，success |

那一步是一个 `shell: bash` 的小脚本，把 Core 与 E2E 两个工程里的
`Category=Sidecar` 依次跑一遍，各自失败都置 `STATUS=1`、最后统一 `exit $STATUS`——
这样前一个失败不会被后一个的成功盖住。它**真跑到了**，不是「写进 workflow 就算数」：
本仓 2026-09-28 之前有 26 次运行全红、作业压根没起来过，所以这一条每次都要点名核对。
