# P2-11 布局失败处理与错误码呈现 — 验证结论

任务 `tasks/phase2/P2-11-layout-failure-and-errors.yaml`，依赖 P2-03 与 P1-12。状态 `done`。

## 交付内容

### 呈现方式由一张表决定

`DuetDiagram.App/Services/ErrorPresenterTable.cs` 是**唯一**的对照表：错误码 → 呈现方式。
`ErrorPresenter` 只做翻译（把结果里的标识、是否可重试、差异填进那份呈现），不做决定。

呈现方式只有七种，因为用户能做的事只有这几种：

| 呈现 | 用户看到什么 |
|---|---|
| `HighlightTargets` | 相关元素被标出来，不弹窗 |
| `PromptCreate` | 被问一句「要不要创建」，由用户决定 |
| `VersionDiffDialog` | 弹差异对话框，看得到冲突在哪 |
| `StatusBar` | 状态栏给一句话 |
| `StatusBarMuted` | 状态栏灰显一句话，**不弹窗** |
| `AuthFailed` | 提示认证失败 |
| `LayoutFailureDialog` | 弹布局失败提示，给三个选项 |

**表必须覆盖全部 25 个错误码。** 漏一个就是一次没有反馈的失败——用户点了一下，
什么都没发生，也没有一句话。漏项由 `Category=ErrorPresentation` 挡住，它拿 `ErrorCodes`
里的常量逐个查表；同一个门禁还拿 `docs/Error-Codes.md` 里那张表逐行核对，
两张表分头维护的话，外部代理照着文档写代码而实际行为是另一套。

认不出的码退回「内部错误，已记录日志」：既不抛也不静默，至少让用户知道出事了。
内部错误只给一句笼统的话——异常类型名与堆栈只写应用日志，不摆给用户，
那句话对他没有任何可操作的信息，却会让人以为问题出在他自己身上。

### 失败时画面不清空

`DiagramSession.Reload` 捕获 `LayoutFailedException`，**保留上一次成功的 `Scene`**，
另发一条 `LayoutFailed` 通知让界面去提示。

清空画面的表现是用户看到一片空白，第一反应是"图丢了"，于是去找备份——
而真正该做的是去掉那条算不出来的约束。`The_picture_stays_on_the_last_good_layout`
直接断言 `Scene` 在失败前后是同一个对象。

会话建立时那一次布局**不走**这套兜底：那时还没有"上一次成功的结果"可以退守，
算不出来就如实抛出。留一份空画面让人以为文档是空的，比抛出来更难查。

### 三个选项

| 选项 | 做什么 | 实现 |
|---|---|---|
| 重试 | 换成长的那一档总预算再排一次 | `RetryLayout()` → `Reload(LayoutBudgets.ManualRelayout)`，2 秒 |
| 手动布局 | 不再自动重排，位置冻住 | `EnterManualLayout()`，见下 |
| 简化图 | 把这一轮降级丢掉的约束摆出来 | 提示的补充说明换成 `ConflictSummary` |

**简化图只指出来，不替用户去掉。** 自动去掉一条的话，去掉的可能是他真正在乎的那条，
而重排之后图变了却看不出是为什么。丢掉的那几条来自降级过程自己的记录
（`LayoutFailurePayload.Attempts` 的 `Conflicts`），不在界面侧另算一份。

失败时状态栏变红（`StatusSeverity.Error`），提示浮在画布正中间；重试成功或排布成功之后
两者一起收掉。只收提示的话，图已经排出来了、红字还挂在下面，看起来像失败还在。

### 手动布局模式只冻坐标

进入之后 `Reload` 不再问引擎，改成"按当前文档重画一遍绘制列表，沿用上一次的坐标"。
标签与样式仍然跟着文档走——两者一起冻的话，用户改了标签却看不到变化，会以为编辑没生效。

上一次布局里没有的元素画不出来，这是这一模式的本意：不重排就不会有它的位置。
`SceneBuilder` 按标识逐个取坐标，取不到的直接跳过，不会画出一个零坐标的方块。

`Manual_layout_stops_asking_the_engine` 同时断言三件事：引擎调用次数不增、坐标不变、
标签变了。

### 引擎可注入

`SampleDiagram.Build` / `DiagramSession` / `MainWindow` 各收一个可空的 `ILayoutEngine`。
用途只有一个：让"布局彻底失败"这条路径能被真正走到。在真实引擎上构造一份它解不出的图，
那条路径会随引擎的改进而失效；而"失败之后界面怎么办"这件事不能没人走。

测试用的 `SwitchableEngine` 是一个开关而不是一张按调用次数排的剧本：协调器每降一级就问引擎一次，
于是"一次重排"到底问几次取决于降级计划有几级。按次数写死的话，计划一改剧本就错位了，
而错位的表现是"某条用例莫名其妙地不失败"。

### 两条提示都只问不办

`LayoutFailureDialog` 与 `SidecarRecoveryDialog` 都只把选择交回宿主，自己不认识会话也不认识布局。
面板自己去调布局的话，那件事就有了两个入口，两条路径迟早会对同一次失败给出不同的处置。

`SidecarRecoveryDialog` 对应人工产物（`user.json`）损坏时的两个选项：从备份恢复、放弃人工调整。
它没有第三个"取消"——那份内容重算不出来，不选也得选一个。

## 验收

| 命令 | 结果 |
|---|---|
| `dotnet test --project DuetDiagram.E2E.Tests -- --filter-trait Category=ErrorPresentation` | 10/10 |
| `dotnet test --project DuetDiagram.E2E.Tests -- --filter-trait Category=LayoutFailure` | 6/6 |
| `dotnet test --project DuetDiagram.Core.Tests -- --filter-trait Category=IrValidator` | 23/23 |
| `dotnet test --project DuetDiagram.E2E.Tests`（全量） | 58/58 |
| `dotnet test --project DuetDiagram.Core.Tests`（全量） | 248/248 |
| `dotnet test --project DuetDiagram.Render.Tests`（全量） | 145/145 |
| `dotnet test --project DuetDiagram.Layout.Tests`（全量） | 75/75 |
| `dotnet run --project DuetDiagram.App -c Release -- --selftest --out reports/phase2-selftest.png` | 自检通过 |
| `dotnet run --project DuetDiagram.App -c Release -- --benchmark-frames --nodes 1000 --frames 120` | 达标 |

千节点帧率（改状态栏与提示之后复测，与 P2-08 的记录同一口径）：

```
  虚拟化   单帧平均    10.83 ms  → 约   92.4 帧每秒
  虚拟化   单帧中位     8.82 ms  → 约  113.4 帧每秒
  剔除率              86.3 %  （2968 条里只画 406 条）
```

界面栈自检：

```
  节点 / 连线              5 / 5
  绘制指令                17
  画布消费                17
  位图尺寸               900 x 600
自检通过
```

> 后面 P2-10 给这一帧的文档加了两条布局约束，并让自检核对约束条数，
> 于是输出里多了一行「布局约束 2」，而两个文件（`phase2-selftest.png` 与
> `phase2-constraints.png`）此后是同一张图。上面那段是当时跑出来的，
> 不回头改；要复现它得回到 P2-10 之前那次提交。

## 与规格文档的差异

- **手动布局模式只做一层。** 方案 §5.4 / §9.4 把它列为三个选项之一，但它到底意味着什么没写。
  本任务只做"降级之后不再自动重排、位置冻在最近一次成功的布局上"，不做完整的手动摆放工具——
  那是一个独立的编辑器功能，会带着一套自己的坐标编辑、吸附与撤销语义。
- **「重试（更长预算）」换的是总预算。** 每一级分到多少仍由降级计划里的固定值决定，
  而总预算只在"每级预算之和超过它"时才成为约束。把每级预算也一起放大要先定"重试最多等多久"。
- **错误码文档里那张表由测试核对。** 见上文，已登记进 AGENTS.md 的「已知差异」表。
- **Sidecar 恢复提示现在没有调用点。** 打开文档那条路还没做；它由端到端用例直接驱动。

## 后续

1. 打开文档与 sidecar 接线：`SidecarRecoveryDialog` 的触发时机是"读 `user.json` 得到不可用的结果"。
2. 手动摆放工具（拖动落位、吸附、成批对齐）单列一个任务。
3. 布局约束的编辑入口在 P2-10，那时"简化图"指出的约束才有地方去掉。
