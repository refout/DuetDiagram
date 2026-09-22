# AGENTS.md — Agent 约定

本文件是**契约**，不是说明文档。改动前先读完根目录那份《对人和 LLM 双友好的流程图/框图绘制软件——完整方案》，下文称**规格文档**。

## 项目定位

人和 LLM 通过**同一套命令接口**操作**同一份文档**，能力完全对等。
IR 是唯一事实源；GUI 做的每一件事，LLM 通过命令层都能做。

## 仓库布局

| 路径 | 作用 | 状态 |
|---|---|---|
| `DuetDiagram.Core` | IR、命令总线、日志、历史、广播、序列化、工作区与文档锁 | 垂直切片已落地；P2-12 加了文档锁与心跳 |
| `DuetDiagram.Core.Tests` | Core 的单元与约束测试 | 已落地 |
| `DuetDiagram.Layout` | 布局引擎封装与约束补齐 | Phase 1 P1-11 / P1-12、Phase 2 P2-09 已落地 |
| `DuetDiagram.Layout.Tests` | 布局不变量测试 | 已落地 |
| `DuetDiagram.Render` | 绘制列表、文本度量、视口变换、视口虚拟化与换档、帧时采样器 | Phase 2 P2-01 / P2-03 / P2-04 已落地（画布控件在主程序） |
| `DuetDiagram.Render.Tests` | 空间索引、绘制列表与场景快照、剔除判据、换档编排与帧时采样 | 已落地 |
| `DuetDiagram.Mermaid` | Mermaid 词法、语法、图类型识别、导入与导出 | Phase 1 P1-08 / P1-09 / P1-10 已落地 |
| `DuetDiagram.Mermaid.Tests` | 词法/语法用例与冻结语料回归 | 已落地 |
| `DuetDiagram.Dsl` | 自有 DSL 的词法、语法与语义映射 | Phase 1 P1-16 / P1-17 已落地 |
| `DuetDiagram.Dsl.Tests` | 词法/语法/映射用例与冻结语料回归 | 已落地 |
| `DuetDiagram.AotSmokeTest` | 原生编译冒烟（多态 Memento + IR 往返） | 已落地（本机缺 C++ 工作负载，未完成发布） |
| `DuetDiagram.App` | 界面主程序：画布、视口变换、状态栏、布局失败提示、布局约束入口、多窗口与只读呈现、帧率基准 | Phase 2 P2-02 / P2-03 / P2-07 / P2-08 / P2-10 / P2-11 / P2-12 已落地，含自检模式与 `--open` |
| `DuetDiagram.E2E.Tests` | 无头模式下的端到端用例（起窗口、送输入、抓一帧、比像素） | Phase 2 P2-02 / P2-03 / P2-07 / P2-08 / P2-10 / P2-11 / P2-12 已落地 |
| `DuetDiagram.Benchmarks` | 性能基线 | Phase 0b 已落地 |
| `docs/` | 架构、IR Schema、渲染管线、错误码、命令清单 | 已落地 |
| `tasks/` | 面向 coding agent 的任务 YAML | 已落地 |
| `reports/` | 阶段验证结论与取证数据 | 已落地 |
| `tools/LocCounter` | LOC 统计（不进 sln） | 已落地 |
| `tools/Poc/LayoutCandidates` | 布局引擎选型取证（不进 sln） | 已落地 |
| `tools/Poc/McpTransport`、`SharedTools` | 协议与工具共用的依赖验证（不进 sln） | 已落地 |
| `tools/CompareHarness` | 对比测试的语料生成、盲评装置、谓词评分与人工评分汇总（不进 sln） | 已落地 |
| `tools/UserStudy` | 用户测试的量表、拉丁方顺序分配、录入模板、四条统计判定与结论换算（不进 sln） | 已落地；真人数据未收 |
| `DuetDiagram.Llm` / `.Mcp` | 后续 Phase | 未创建 |

**不要提前创建后续 Phase 的空项目。** 每个 PR 只引入该任务真正需要的项目。

**依赖验证脚手架用完即删。** 结论固化进正式工程与文档之后，留着两份实现迟早会分叉——
分叉之后测试仍然全绿，而那份脚手架已经不能反映真实行为了。

## 不可违反的约束

以下 13 条由规格文档附录得出，每条都有对应的测试或 CI 门禁。**违反即视为回归。**

1. **集合对外只读，修改只能通过命令。**
   `DiagramDocument` 的 `Nodes` / `Edges` 是 `IReadOnlyList<T>`；`Version` / `StructuralHash` / `VisualHash` 的 setter 是 `internal`。

2. **每个命令的 `Apply` 必须原子。**
   返回失败或抛异常时，文档必须与调用前逐字节一致；同时恢复调用方的栈（撤销栈或重做栈）。
   `Execute` 与 `Redo` 一视同仁：Redo 不得只恢复重做栈而忽略文档状态。
   门禁：`dotnet test -- --filter-trait "Category=Atomicity"`

3. **Workspace 的 broadcaster 所有权必须显式。**
   共享 broadcaster 时只有创建者传 `ownsBroadcaster: true`；`DisposeAsync` 仅在拥有所有权时释放它。

4. **`ChangeContext.Timestamp` 只由命令总线填充。**
   调用方声明的 `Timestamp` 会被总线用 `ITimeProvider` 覆盖。

5. **`CommandMemento` 用抽象基类 + 派生密封记录，禁止 `object`。**
   新增派生记录必须同时补 `[JsonDerivedType]`，否则 AOT 下反序列化失败。
   门禁：`dotnet test -- --filter-trait "Category=MementoRegistration"`

6. **哈希 Phase 1 一律全量计算。**
   只有 Phase 5 实测不达标才改增量（Merkle 树）。不要提前优化。

7. **命令内部不得异步调用 `Execute`。**
   `AsyncLocal` 在 `Task.Run` 中会传播，因此跨线程也会被判定为嵌套（这是**有意**的保守行为）。
   需要异步触发命令时，由宿主在命令外调度。
   门禁：`dotnet test -- --filter-trait "Category=NestedExecute"`

8. **`ChangeNotification.Timestamp` 仅用于日志和审计。**
   排序一律用单调递增的 `Version`。

9. **`AuditLog` 的 `CommandError.Payload` 不携带异常类型名。**
   类型名只写应用日志（`IDiagnosticsSink`），不写 AuditLog。

10. **`NullChangeBroadcaster` 的判据是 `is NullChangeBroadcaster`，不是 `null`。**
    `RequiresVersionCheck = true` 时构造函数必须拒绝它。

11. **`HistoryStack.Clear()` 只用于同一文档重新加载。**
    打开新文档必须创建新的 `DiagramDocument` + `DiagramCommandBus`。

12. **`InProcessBroadcaster` 使用有界 Channel（容量 1024，`DropOldest`）。**
    不得改成无界。

13. **`InProcessBroadcaster.DisposeAsync` 等待分发任务结束，超时 2 秒；超时后不释放 CTS。**

## 依赖规则

- **`DuetDiagram.Core` 只依赖 BCL。** 永不添加 `PackageReference` / `ProjectReference`。
  门禁：CI 的 `core-bcl-only` 作业。
- **不引入选型清单（规格文档 §2）之外的依赖。** 需要新依赖先在 `Directory.Packages.props` 登记精确版本，
  并在 PR 里说明理由。
- **版本必须精确。** 不得出现 `*` 或区间。Phase 0 P0-01 已核实全部主选依赖存在。

## 写作约定

**代码注释不得引用文档，也不得引用任务编号。** 包括 `///` XML 文档注释、`//` 行内注释、
`.csproj` 里的 XML 注释。

注释要**直接说清实现的逻辑与流程**：这段代码在做什么、为什么这么做、边界条件是什么、
失败时会发生什么。读者应当不需要翻任何外部文档就能看懂。

- 反例：`// 版本冲突时必须带上差异（见某文档的某一节）`
- 正例：`// 版本冲突时必须带上差异。界面要弹差异对话框，
  //        拿不到就只能给用户一句"版本冲突"，而用户无从知道冲突在哪。`
- **不要写文档路径或文件名**，也**不要拿两字简称去指代根目录那份规格文档**——
  读者看不出它指的是什么。要说的内容直接写出来。
- 门禁类注释改为陈述这个测试到底在断言什么、破坏这条不变量会导致什么后果。
- **分节用 `#region`，不要用分隔注释。** 例如不要写 `// ---- 布局提示 ----` 这种横线行，
  写成 `#region 布局提示` 加 `#endregion`。IDE 的折叠与导航认的是 `#region`，
  分隔注释对工具完全不可见——同一个文件里几十个分节，靠横线行只能靠眼睛找。
- **Markdown 文档与任务 YAML 里的引用不受此限**——那些是文档，做引用是本职。
- **字符串字面量不受此限**，例如工具往生成报告里写的正文、路径常量、报错文案。
  门禁只看以 `//` 开头的行，就是为了不误伤这一类。

理由：引用式注释把理解成本推给了读者，而且文档一旦改名或调整编号，注释就变成误导。
本仓的文档是活的，这个风险是实打实的——`docs/IR-Schema.md` 里曾经有六个错误码名与
`ErrorCodes.cs` 对不上，而没有任何东西会报错。

门禁：`dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=CommentDiscipline"`

## 验收命令

本仓库使用 **Microsoft.Testing.Platform**（`global.json` 的 `test.runner`），
因此规格文档里的 `dotnet test --filter Category=xxx` 实际写法是 `--filter-trait`：

```bash
# 编译
dotnet build DuetDiagram.slnx -c Release

# 全量测试
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj
dotnet test --project DuetDiagram.Layout.Tests/DuetDiagram.Layout.Tests.csproj
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj
dotnet test --project DuetDiagram.Mermaid.Tests/DuetDiagram.Mermaid.Tests.csproj
dotnet test --project DuetDiagram.Dsl.Tests/DuetDiagram.Dsl.Tests.csproj

# 端到端（无头模式，起窗口、送输入、抓一帧；需要绘图后端，本机可直接跑）
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj

# 按分类
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=Atomicity"
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=LayoutConstraint"
dotnet test --project DuetDiagram.Layout.Tests/DuetDiagram.Layout.Tests.csproj -- --filter-trait "Category=Layout"
dotnet test --project DuetDiagram.Layout.Tests/DuetDiagram.Layout.Tests.csproj -- --filter-trait "Category=OrderAlign"
dotnet test --project DuetDiagram.Layout.Tests/DuetDiagram.Layout.Tests.csproj -- --filter-trait "Category=LayoutFallback"
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj -- --filter-trait "Category=QuadTree"
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj -- --filter-trait "Category=Viewport"
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj -- --filter-trait "Category=CullingPolicy"
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj -- --filter-trait "Category=ModeSwitch"
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj -- --filter-trait "Category=DiagnosticsSampler"
dotnet test --project DuetDiagram.Mermaid.Tests/DuetDiagram.Mermaid.Tests.csproj -- --filter-trait "Category=MermaidParsing"
dotnet test --project DuetDiagram.Mermaid.Tests/DuetDiagram.Mermaid.Tests.csproj -- --filter-trait "Category=MermaidImport"
dotnet test --project DuetDiagram.Mermaid.Tests/DuetDiagram.Mermaid.Tests.csproj -- --filter-trait "Category=MermaidExport"
dotnet test --project DuetDiagram.Mermaid.Tests/DuetDiagram.Mermaid.Tests.csproj -- --filter-trait "Category=MermaidRoundTrip"
dotnet test --project DuetDiagram.Dsl.Tests/DuetDiagram.Dsl.Tests.csproj -- --filter-trait "Category=DslParsing"
dotnet test --project DuetDiagram.Dsl.Tests/DuetDiagram.Dsl.Tests.csproj -- --filter-trait "Category=DslCorpus"
dotnet test --project DuetDiagram.Dsl.Tests/DuetDiagram.Dsl.Tests.csproj -- --filter-trait "Category=DslMapping"
dotnet test --project DuetDiagram.Dsl.Tests/DuetDiagram.Dsl.Tests.csproj -- --filter-trait "Category=DslLayoutIntent"

# 性能基线（作业长度必须够：短作业的误差棒比均值还大，数字不可用）
dotnet run --project DuetDiagram.Benchmarks -c Release -- --filter "*" --job medium

# 界面栈自检（脱屏渲染示例文档的一帧后退出，用退出码表达结果）
# 位图里应当能看到节点、边与标签；自检还会核对绘制列表被整份消费掉
dotnet run --project DuetDiagram.App -c Release -- --selftest --out reports/phase2-selftest.png

# 布局约束的自检（同一帧，但文档里那两条约束是经命令层加进去的）
# 位图里应当看得出同层的那一组与对齐的那一对；自检还会核对约束条数不低于两条
dotnet run --project DuetDiagram.App -c Release -- --selftest --out reports/phase2-constraints.png

# 界面帧率测量（真实绘制列表：造文档、求解布局、翻译成绘制列表，再两种模式各测一遍）
# 判据是虚拟化那一档的帧率与剔除率，回退档的数字是对照。取证见 reports/phase2-render.md
dotnet run --project DuetDiagram.App -c Release -- --benchmark-frames --nodes 1000 --frames 120

# 加一个开关就把诊断面板挂上，交替量关、开两遍，判面板代价不超过一成
dotnet run --project DuetDiagram.App -c Release -- --benchmark-frames --nodes 1000 --frames 120 --diagnostics

# 原生编译冒烟（需要 VS「使用 C++ 的桌面开发」工作负载，见 reports/phase0a.md）
dotnet publish DuetDiagram.AotSmokeTest/DuetDiagram.AotSmokeTest.csproj -c Release
./DuetDiagram.AotSmokeTest/bin/Release/net10.0/win-x64/DuetDiagram.AotSmokeTest.exe

# 对比测试：冻结语料 → 盲评清单 + 解析统计
# 逐字节可复现（打乱种子写死在代码里）。rater.md / items.json / key.json 不进仓库。
dotnet run --project tools/CompareHarness -c Release -- listings

# 谓词求值器自检（不需要语料、不需要网络，改完谓词求值先跑这个）
dotnet run --project tools/CompareHarness -c Release -- verify

# 谓词口径的端到端准确率（读冻结语料，逐字节可复现）
# 它是一张回归网，不是判定门的答案，理由写在 reports/compare-blind/predicate-report.md
dotnet run --project tools/CompareHarness -c Release -- score

# 抽样：挑一批条目交给人判，用来校谓词。产物 sample-rater.md + human/template.json
dotnet run --project tools/CompareHarness -c Release -- sample

# 人工评分与谓词的一致率。读 reports/compare-blind/human/ratings-*.json，
# 算出 agreement.md 并列出分歧。没有评分文件时不生成报告。
dotnet run --project tools/CompareHarness -c Release -- agreement

# 绘制列表：指令构造、主题解析、标签排版、层叠顺序
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj -- --filter-trait "Category=DrawList"

# 十个场景的绘制列表快照。红了先判断变化是不是有意的，别顺手把 .received.txt 盖上去。
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj -- --filter-trait "Category=SceneSnapshot"

# 换档在真实窗口里的样子：那一帧的耗时，以及换档前后画布区域逐像素一致
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj -- --filter-trait "Category=ModeSwitch"

# 诊断面板在真实窗口里的样子：快捷键开关、面板上的数字跟着帧更新、关着时不记
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj -- --filter-trait "Category=Diagnostics"

# 属性面板在真实窗口里的样子：选节点出六个分节、改字段版本加一、多选显示多个值、被拒的值留在字段上
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj -- --filter-trait "Category=PropertyPanel"

# 属性面板切换耗时：切换选中元素到面板渲染完成，判据是单次切换中位不超过一百毫秒
dotnet run --project DuetDiagram.App -c Release -- --benchmark-panel --switches 200

# 节点拖拽单帧处理耗时：一千节点上开一条拖拽、连续两百次采样每帧处理拖拽输入的中位
# 判据是单帧不超过 16 毫秒。拖动中不改文档、不调布局、不发命令，只搬被拖元素的屏幕偏移。
dotnet run --project DuetDiagram.App -c Release -- --benchmark-drag --nodes 1000 --samples 200

# 变更高亮：在千节点图上给一批节点挂上三种手段（脉冲 + 角标 + 虚线轮廓），量帧率。
# 判据与不带高亮时同一条（虚拟化不低于 30 帧每秒），它验的是"高亮叠加层没有拖垮帧率"。
dotnet run --project DuetDiagram.App -c Release -- --benchmark-frames --nodes 1000 --frames 120 --highlight

# 变更高亮的三种手段：各自独立可辨、叠加时不互相盖住、颜色只是辅助线索
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj -- --filter-trait "Category=Highlight"

# 变更高亮在真实窗口里的样子：一条命令之后对应元素被标记，撤销之后标记继承原来源并带撤销符号
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj -- --filter-trait "Category=Highlight"

# 错误码到界面呈现的对照表：覆盖全部错误码，且与错误码文档里那张表逐行一致
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj -- --filter-trait "Category=ErrorPresentation"

# 布局失败之后：画面停在上一次成功的结果上、状态栏变红、重试 / 手动布局 / 简化图三个选项都在
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj -- --filter-trait "Category=LayoutFailure"

# 布局约束的界面入口：面板上加删同层 / 对齐 / 层内次序，加完之后下一次布局确实按约束走，
# 只有人工加的能删，拖到兄弟节点上落定的是次序且同一主语上只留一条
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj -- --filter-trait "Category=ConstraintEditor"

# 多窗口：同一进程两个窗口看同一份文档，一边改了另一边跟着刷新、版本不分叉；
# 只读那一份逐个写入口都要禁掉，会话那一层再拒一次
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj -- --filter-trait "Category=MultiWindow"

# 跨进程所有权：独占、拿不到就退只读、心跳过期判定、抢占前先试删锁文件
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=DocumentLock"

# 从文件打开一份文档；另一个进程拿着它时这一份退成只读。
# 文档不在、读不出来、或者抢占之后校验不过时，退出码 2 且错误写在标准错误上
dotnet run --project DuetDiagram.App -c Release -- --open path/to/doc.json

# 依赖验证脚手架（结论固化后可删，见仓库布局表）
dotnet run --project tools/Poc/LayoutCandidates -c Release
dotnet run --project tools/Poc/McpTransport -c Release
dotnet run --project tools/Poc/SharedTools -c Release

# 用户测试的装置：统计量在构造好的已知答案上算得对不对（不需要真人数据）
dotnet run --project tools/UserStudy -c Release -- verify

# 量表、拉丁方顺序分配表与录入模板。已有的录入文件不覆盖
dotnet run --project tools/UserStudy -c Release -- template

# 读录入数据，算四条判定，写结论报告。没有数据时明确报"还没有数据"且什么都不生成——
# 生成一份空的或占位的报告，后来的读者会以为测过了。数据收上来之前这条命令不产出报告
dotnet run --project tools/UserStudy -c Release -- analyze

# LOC 一致性
dotnet run --project tools/LocCounter -- --root . --check
```

### 已有测试分类

`RoundTrip`、`Atomicity`、`UndoRedoVersion`、`DiffBoundary`、`MementoRegistration`、
`NestedExecute`、`NestedExecuteCrossThread`、`Broadcaster`、`SessionIdResolution`、
`UndoStress`、`Workspace`、`McpMode`、`CorePurity`、
`IrHashing`、`IrSnapshot`、`IrReadOnly`、`IrValidator`、`IrConstruction`、
`ConflictPolicy`、`FieldMetadata`、`Sidecar`、`SidecarBackup`、`Layout`、`OrderAlign`、`LayoutFallback`、`LayoutConstraint`、`QuadTree`、`Viewport`、`CullingPolicy`、`ModeSwitch`、`DiagnosticsSampler`、`DrawList`、`SceneSnapshot`、`Canvas`、`Diagnostics`、`PropertyPanel`、`HitTest`、`Drag`、`Connect`、`EdgeEdit`、`EdgeField`、`Highlight`、`ErrorPresentation`、`LayoutFailure`、`ConstraintEditor`、`MultiWindow`、`DocumentLock`、`MermaidLexing`、`MermaidParsing`、`MermaidCorpus`、`MermaidImport`、`MermaidExport`、`MermaidRoundTrip`、`DslLexing`、`DslParsing`、`DslCorpus`、`DslMapping`、`DslLayoutIntent`

## 新增一个命令的检查清单

1. 继承 `DiagramCommandBase`，`CommandId` 用小写连字符（如 `add-node`）。
2. `Validate` 覆盖全部前置条件，返回**结构化** `CommandError`，不要在 `Apply` 里抛异常。
3. `Apply` 只做变更；任何失败路径都必须让文档回到调用前状态。
4. `CaptureMemento` 与 `Apply` 对同一文档必须给出**一致**的索引/位置。
5. `RestoreCore` 必须能还原顺序（边的插入按原索引升序）。
6. 新增 Memento 记录 → 补 `[JsonDerivedType]`（约束 5）。
7. 补原子性测试 + 往返测试。
8. 在 `docs/Command-List.md` 登记。
9. 跑一遍 `Category=Atomicity` 与 `Category=MementoRegistration`。

## 与规格文档的已知差异

| 规格文档位置 | 差异 | 原因 |
|---|---|---|
| §4.4 `IDiagramCommand.Undo` | 未提供该方法，由 `RestoreMemento` 承担 | 撤销所需信息全在 memento 中；`Undo()` 需要命令自身缓存状态，会和 Redo 的「重新捕获 memento」语义打架 |
| §4.4 `CommandError.Payload` | 类型为 `string?` 而非「可选对象」 | AOT 源生成下不需要多态注册；需要富载荷时再引入注册的联合类型 |
| §4.4 `FieldChange` 值 | 统一 `string?`，未引入 `PositionValue` / `SizeValue` | 坐标类命令属 Phase 2 |
| §4.5 派生名 | `EmptyDiff` / `InvalidDiff` / `EntriesDiff`（规格文档为 `Empty` / `Invalid` / `Entries`） | 避免与静态成员及类型名冲突 |
| §4.9 Context 依赖 | 8 项（不含 `Sidecar` / `Layout` / `Renderer`） | `ChangeContext` 只带来源、操作者、会话、理由与时间戳，不带服务引用。Sidecar 与布局后来都实现了，但它们不进命令上下文：「结构变更重布局，否则重绘」由 `CommandResult` 的两个布尔标志外化给宿主，宿主自己决定调谁。命令里塞进布局引擎的话，Core 就不再只依赖 BCL |
| §4.9 步骤 3/4 | 填充的是 `ChangeContext` 的 `Timestamp` / `SessionId` | 这是唯一读得通的解释 |
| §4.1 IR 字段 | 九个集合与三个子对象已实现，但 `PageDef` / `LayerDef` 的字段取最小集合 | 规格文档只列出集合存在、未给字段；等对应交互开工时再补，见 `docs/IR-Schema.md` |
| §4.1 快照方法 | 只实现 `TakeFullSnapshot` / `RestoreFromSnapshot`，`ApplySnapshot` 未实现 | 它的语义规格文档未定义，协作同步设计清楚之前不硬猜 |
| §12 冒烟工程只引用 Core | 现在也引用 Layout | 规格文档写这一条时还没有别的可发布组件。布局依赖的第三方引擎**是否原生友好分析器看不出来**——分析器只看我们自己的代码，只有真的发布一次才知道 |
| §12 工程命名 | 用 `DuetDiagram.*` 而不是 `Diagram.*` | 与解决方案文件名的前缀一致 |
| 风险表「宽松模式 + 原始片段保留」 | **不保留原始片段**，认不出的只进导入报告 | 片段只在元素未被改动时有效。用户或模型改过那个节点之后它就过期了，导出时再吐出来会把旧内容复活——而两个哈希都没变，这种错看不出来。P1-10 的往返按「IR 往返一致」算（同一份 IR 走一圈还是同一份），不是文本逐字节一致 |
| 项目结构树 | 多一个 `DuetDiagram.Dsl` / `.Dsl.Tests` | 树里有 `Diagram.Compare.Tests` 与 P1-16/P1-17 两条任务，却没有承载它们的工程；命名按仓库约定加前缀，与 `DuetDiagram.Mermaid` 对称 |
| §15.1 端到端测试的工具 | 用界面框架的无头模式，但**不用**它配套的 XUnit 集成包 | 那个包绑的是低一个大版本的测试框架（3.2.2），与仓库里的 xunit.v3 4.x 二进制不兼容，装上之后在发现阶段就抛「找不到方法」，连一个测试都跑不起来。会话本身只有几十行，直接用反而少一处会随版本漂移的依赖。用法见 `docs/GUI.md` |
| 项目结构树 | Phase 2 起多一个 `DuetDiagram.E2E.Tests` | 树里有这个工程，但 Phase 0a 的界面栈自检是用主程序自己的 `--selftest` 开关做的——那时只有一帧要验，起一个测试工程不值。开始有交互要验（缩放、平移、点选）之后，再用命令行开关表达就得给主程序加一堆只为测试存在的参数 |
| §三 整体架构 / §九 GUI 设计：渲染层 | 「Avalonia Canvas 渲染」**不进** `DuetDiagram.Render` | 渲染层只做到**绘制列表**为止（纯数据、可逐字节比较、文本度量可注入），画布控件放在主程序。带上窗口与面板之后快照测试就不再是「同一份输入永远同一份输出」——测试得起一个无头界面进程。而 §15.1 的端到端测试本来就单列了一层，用界面框架的无头模式，那是另一件事，不该和绘制列表的回归混在一起 |
| §15.1 快照测试的工具 | 用仓库里的冻结文本加一段比对代码，没有引入测试分层表里列的那个快照库 | 那个库不在 §2 的技术选型表里，而这里要的东西只有两样：逐字节可比、差异能定位到元素。一份规范文本加一段比对就够了，多一个依赖反而多一处会随版本变的行为。绘制列表的快照在 `DuetDiagram.Render.Tests/Scenes/`，比对代码在 `Snapshot.cs` |
| 测试框架 | xunit.v3 + Microsoft.Testing.Platform；`--filter` → `--filter-trait` | .NET 10 SDK 起 `dotnet test` 不再支持 VSTest 目标 |
| 测试断言库 | FluentAssertions **7.2.2** | 8.x 起改为商业许可；7.2.2 是最后一个 Apache-2.0 版本 |
| 解决方案文件 | `DuetDiagram.slnx` | 本轮约定 |
| §16 对比测试报告 `reports/compare.md` | 落在 `reports/compare-blind/`，且拆成结论与证据两份 | 一份文件装不下：解析统计、语义拒绝率、检查项可判定性、谓词口径各是一份证据，各有各的复现命令。混在一起之后没人知道哪一段该跟着哪条命令重新生成。另外装置不进 sln（见 `tools/CompareHarness` 那一行），所以它也没有 `Diagram.Compare.Tests` |
| §P0-06「1000 矩形 FPS」与 §14.3「1000 节点 FPS」 | 帧率基准的开关从 `--rectangles` 改成 `--nodes`，输入换成真实的绘制列表 | 规格文档把这两条分开记：P0-06 量的是矩形，Phase 2 判的是节点。原先那个开关只画矩形，量不出标签度量与折线路由的开销，而千节点上那两样占大头——按矩形量出来的数字明显偏乐观。`reports/phase0b-baseline.md` 里那条命令记的是当时的实现与当时的数，不回头改 |
| §9.2 拖节点更新 Sidecar | 固定位置写在 sidecar 的 `pinnedNodes`、**不进 IR、不走命令总线** | 固定是用户「放这儿」的产物，与文档结构无关。走命令总线会把一次拖动塞进几百条 IR 记录，撤销栈失真；绕开之后「拖动中不发命令」用总线历史条目数不变来验，「松手只落定一次」用「固定一个节点 + 重布局一次」来验。重叠（两个固定节点互压）也在写入侧挡，那是布局的前置条件而不是布局职责 |
| §9.2 拖边中间点加折点 | 折点写在 sidecar 的 `pinnedEdges`、**不进 IR、不走命令总线** | 与 `pinnedNodes` 同一口径：折点是用户「拖这儿」的产物，与文档结构无关。撤了又是一场几百条 IR。增删折点是「一条」操作（不是删一条再加一条），宿主侧用快照栈管撤销/重做；布局与渲染尚未消费 `pinnedEdges`，列为后续接线 |
| §9.3 变更高亮 | 高亮是**叠加层**，不进绘制列表的几何；三种非颜色手段是形状与运动（脉冲、角标、虚线轮廓），颜色只是辅助线索 | 混进绘制列表之后，动画的时间戳会污染快照测试，而那些快照本该是「同一份输入永远同一份输出」。高亮信息来自命令总线的变更通知而不是界面自己比较文档——界面推断的话，LLM 改的东西不会亮，而那条路径根本不经过界面 |
| §5.4 / §9.4「手动布局模式」 | 只做到「降级之后不再自动重排、位置冻在最近一次成功的布局上」，**不做**完整的手动摆放工具 | 手动摆放是一个独立的编辑器功能，与「布局算不出来时别把画面清空」是两件事。顺手塞进来的话，它会带着一套自己的坐标编辑、吸附与撤销语义，而那些语义与 IR、与 sidecar 的边界都还没定 |
| §5.4「重试（更长预算）」 | 重试确实换成长的那一档总预算，但每一级分到多少仍由降级计划里的固定值决定 | 总预算只在「每级预算之和超过它」时才成为约束。把每级预算也一起放大是另一件事：那会让一次重试最多花掉几倍的时间，而用户按下重试时并不知道自己在等多久。真要改，得先定「重试最多等多久」 |
| 计划中命令清单里的 `set-same-rank` / `set-order` / `set-align` | 三类合成**两条**命令：`add-layout-constraint` 与 `remove-layout-constraint`，种类由参数给出 | 三类的成员形状不同（同层与对齐是节点，层内次序是出边），但增删的动作一样。一类一条命令的话，校验、原子性与撤销三套逻辑要各写三遍，而抄漏的那一遍不会报错，只会让某一类约束在某个入口下改不动。`set-place` 仍然单列——相对位置属后续阶段 |
| §9.2「拖节点 → 更新 sidecar，松手后 pin」 | 落点压在**另一个兄弟节点**上时落定的是一条层内次序，不写固定位置；落在空白处才 pin | 落在兄弟节点上说的是「把我排到它旁边」，那是一条相对次序；落在空白处说的才是「把我放这儿」，那是一个绝对位置。两者混成一条的话，用户想做相对调整却得到一个绝对位置，而绝对位置一旦钉住，之后的自动重排就再也动不了它。同一主语上只留一条次序（合并而不是追加），否则拖几次就积下几十条互相矛盾的次序，而求解器取的是列表里的第一条 |
| §9.6 错误码 GUI 处理表 | 呈现方式落在 `ErrorPresenterTable`，**不在各处就地判断**；错误码文档里那张表由 `Category=ErrorPresentation` 逐行核对 | 就地判断的结果是同一个错误码在两个入口给出两种呈现，而用户以为遇到的是两个问题。两张表分头维护则会分叉，而外部代理是按文档写代码的——它照着文档处理，实际行为却是另一套 |
| §11.2 五级恢复里那两级弹窗 | 恢复提示的控件建好了，但只有宿主能触发；打开文档只做了 `--open` 这一条入口，它读文档但不读 sidecar，所以现在仍然没有调用点 | 提示自己去读文件的话，同一份坏文件会在两个地方被解析一遍，而两处的判据迟早会不一样。宿主还没做的这段时间里，那条路径由端到端用例直接驱动 |
| §9.5 多进程「第二实例只读 + 状态栏提示」 | 锁与心跳放在 `DuetDiagram.Core/Workspace`，不放主程序 | 锁是「这个进程在编辑这份文件」，与窗口无关；而 `Category=DocumentLock` 的用例在 `DuetDiagram.Core.Tests` 里，那里只能引用 Core。放主程序的话，判定所有权的那段逻辑就得靠端到端用例去验，而起两个进程才能量到的东西在无头用例里量不出来 |
| §9.5「第二实例只读」 | 只读那一份把所有写入口禁掉，**并且**会话那一层再拒一次（返回新错误码 `DOCUMENT_READ_ONLY`） | 界面上的入口有七处（字段、约束增、约束删、拖拽、连线、重连、折点，另加存盘），漏掉哪一处都不会报错，只会留下一条能造成损坏的路。另立一个错误码而不是复用 `VERSION_CONFLICT`：后者是「同步一下再重试」，这一条是「这份文档上不允许」，两者的处置完全不同 |
| §9.5「心跳超时 15 秒判定崩溃可抢占」 | 心跳过期只**触发去试**，真正判据是「锁文件删不删得掉」 | 心跳过期说的是「对方可能卡住了」，而握着锁句柄的活进程会让删文件那一步失败。把心跳过期直接当成可抢占，就会去抢一个还活着的进程，把它正在写的文档撕掉 |
| 打开文档 | 只有 `--open <路径>` 一条入口，没有自动保存、没有最近文件、sidecar 也没接 | 这一轮要的是「两个进程抢同一份文件」这件事能被验到，而它只需要读得进、写得回。自动保存会带着一套脏标记与恢复档位的语义，那些与 §11 的五个档位绑在一起，得一起定 |
| 存盘 | 只有显式的 `Ctrl+S`，写临时文件再同卷改名替换 | 自动保存要先把「什么时候算脏」「人工产物跟着一起存吗」「崩了之后回到哪一版」定下来；在那之前，一次显式保存至少是用户按下去才发生的 |
| §9.7 用户测试的四条判定 | 结论报告 `reports/phase2-user-study.md` 在数据收上来之前**不生成**，尽管任务 YAML 把它列在产物里 | 装置齐了不等于测过了。十二到十五个人的测试不可能由编码 agent 完成，四条判定的结论必须由人给出；生成一份空的或占位的报告，后来的读者会以为测过了。这与对比测试那一项的处理一致 |
| §9.7「Wilcoxon + Holm-Bonferroni 校正 p < 0.05/3」 | 第二条判定取的是「**领先项与其余每一项**的配对比较经 Holm 校正后都达标」，而不是「三组配对里至少有一组达标」 | 保留一个设计要说明的是它比替代品好，不是「这三者之间有那么一对不一样」。三组配对的原始 p 与 Holm 阈值都逐条写进报告，所以换一种读法也能从报告里重新推出来 |
| §9.7 只写了「至少三条」与「两条」两档 | 不足两条按**砍掉**处理，并在报告里注明协议没有单独写这一档 | 对比测试那一项对同一形状的规则写了三档（≥3 保留 / 2 只用于流式 / ≤1 砍掉），用户测试这一处显然是同一套规则的简写。不写这一档而让它悬着，读的人会以为「一条达标」也要交评审组 |
| §9.3 变更高亮列了四种手段 | 用户测试只测三种非颜色手段（脉冲、角标、虚线轮廓），边栏 diff 不进被测项 | 边栏 diff 是折叠面板而不是画布上的标记，量的是另一件事（读详情），和「一眼看出哪里变了」不是同一个问题，混进同一个量表会让受试者拿两把尺子打同一组分。边栏那一块也没进这一轮的界面 |
| 用户测试的装置 | 不进 sln，也没有对应的测试工程；自检是 `tools/UserStudy -- verify` | 与对比测试同一口径：它是调查装置而不是产品的一部分。它没有可被产品代码引用的接口，也就没有单元测试要写——能验的是统计量算得对不对，那件事由 `verify` 在构造好的已知答案上做 |
| §15.4 的统计方法 | 四个统计量全部自己算（精确条件置换 Friedman、精确符号组合 Wilcoxon、Kendall's W、卡方上尾的级数加连分式），不引第三方统计库 | 判定门的数字必须逐字节可复现，多一个依赖就多一处会随版本变的行为。三个被测项、十来个人的样本量下卡方近似与精确分布能差好几个数量级，而这份数据唯一的用途就是决定设计保不保，所以能精确的地方不近似 |
