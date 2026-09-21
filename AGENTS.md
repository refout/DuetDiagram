# AGENTS.md — Agent 约定

本文件是**契约**，不是说明文档。改动前先读完根目录那份《对人和 LLM 双友好的流程图/框图绘制软件——完整方案》，下文称**规格文档**。

## 项目定位

人和 LLM 通过**同一套命令接口**操作**同一份文档**，能力完全对等。
IR 是唯一事实源；GUI 做的每一件事，LLM 通过命令层都能做。

## 仓库布局

| 路径 | 作用 | 状态 |
|---|---|---|
| `DuetDiagram.Core` | IR、命令总线、日志、历史、广播、序列化 | 垂直切片已落地 |
| `DuetDiagram.Core.Tests` | Core 的单元与约束测试 | 已落地 |
| `DuetDiagram.Layout` | 布局引擎封装与约束补齐 | Phase 1 P1-11 / P1-12、Phase 2 P2-09 已落地 |
| `DuetDiagram.Layout.Tests` | 布局不变量测试 | 已落地 |
| `DuetDiagram.Render` | 绘制列表、文本度量、视口索引与剔除 | Phase 2 P2-01 已落地（画布控件在主程序） |
| `DuetDiagram.Render.Tests` | 空间索引、绘制列表与场景快照 | 已落地 |
| `DuetDiagram.Mermaid` | Mermaid 词法、语法、图类型识别、导入与导出 | Phase 1 P1-08 / P1-09 / P1-10 已落地 |
| `DuetDiagram.Mermaid.Tests` | 词法/语法用例与冻结语料回归 | 已落地 |
| `DuetDiagram.Dsl` | 自有 DSL 的词法、语法与语义映射 | Phase 1 P1-16 / P1-17 已落地 |
| `DuetDiagram.Dsl.Tests` | 词法/语法/映射用例与冻结语料回归 | 已落地 |
| `DuetDiagram.AotSmokeTest` | 原生编译冒烟（多态 Memento + IR 往返） | 已落地（本机缺 C++ 工作负载，未完成发布） |
| `DuetDiagram.App` | 界面主程序 | 技术栈验证脚手架，含自检模式 |
| `DuetDiagram.Benchmarks` | 性能基线 | Phase 0b 已落地 |
| `docs/` | 架构、IR Schema、渲染管线、错误码、命令清单 | 已落地 |
| `tasks/` | 面向 coding agent 的任务 YAML | 已落地 |
| `reports/` | 阶段验证结论与取证数据 | 已落地 |
| `tools/LocCounter` | LOC 统计（不进 sln） | 已落地 |
| `tools/Poc/LayoutCandidates` | 布局引擎选型取证（不进 sln） | 已落地 |
| `tools/Poc/McpTransport`、`SharedTools` | 协议与工具共用的依赖验证（不进 sln） | 已落地 |
| `tools/CompareHarness` | 对比测试的语料生成、盲评装置、谓词评分与人工评分汇总（不进 sln） | 已落地 |
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

# 按分类
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=Atomicity"
dotnet test --project DuetDiagram.Layout.Tests/DuetDiagram.Layout.Tests.csproj -- --filter-trait "Category=Layout"
dotnet test --project DuetDiagram.Layout.Tests/DuetDiagram.Layout.Tests.csproj -- --filter-trait "Category=OrderAlign"
dotnet test --project DuetDiagram.Layout.Tests/DuetDiagram.Layout.Tests.csproj -- --filter-trait "Category=LayoutFallback"
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj -- --filter-trait "Category=QuadTree"
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

# 界面栈自检（脱屏渲染一帧后退出，用退出码表达结果）
dotnet run --project DuetDiagram.App -c Release -- --selftest --out reports/phase0a-selftest.png

# 界面帧率测量
dotnet run --project DuetDiagram.App -c Release -- --benchmark-frames --rectangles 1000 --frames 60

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

# 依赖验证脚手架（结论固化后可删，见仓库布局表）
dotnet run --project tools/Poc/LayoutCandidates -c Release
dotnet run --project tools/Poc/McpTransport -c Release
dotnet run --project tools/Poc/SharedTools -c Release

# LOC 一致性
dotnet run --project tools/LocCounter -- --root . --check
```

### 已有测试分类

`RoundTrip`、`Atomicity`、`UndoRedoVersion`、`DiffBoundary`、`MementoRegistration`、
`NestedExecute`、`NestedExecuteCrossThread`、`Broadcaster`、`SessionIdResolution`、
`UndoStress`、`Workspace`、`McpMode`、`CorePurity`、
`IrHashing`、`IrSnapshot`、`IrReadOnly`、`IrValidator`、`IrConstruction`、
`ConflictPolicy`、`FieldMetadata`、`Sidecar`、`SidecarBackup`、`Layout`、`OrderAlign`、`LayoutFallback`、`QuadTree`、`DrawList`、`SceneSnapshot`、`MermaidLexing`、`MermaidParsing`、`MermaidCorpus`、`MermaidImport`、`MermaidExport`、`MermaidRoundTrip`、`DslLexing`、`DslParsing`、`DslCorpus`、`DslMapping`、`DslLayoutIntent`

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
| 项目结构树 | Phase 2 起多一个 `DuetDiagram.E2E.Tests` | 树里有这个工程，但 Phase 0a 的界面栈自检是用主程序自己的 `--selftest` 开关做的——那时只有一帧要验，起一个测试工程不值。开始有交互要验（缩放、平移、点选）之后，再用命令行开关表达就得给主程序加一堆只为测试存在的参数 |
| §三 整体架构 / §九 GUI 设计：渲染层 | 「Avalonia Canvas 渲染」**不进** `DuetDiagram.Render` | 渲染层只做到**绘制列表**为止（纯数据、可逐字节比较、文本度量可注入），画布控件放在主程序。带上窗口与面板之后快照测试就不再是「同一份输入永远同一份输出」——测试得起一个无头界面进程。而 §15.1 的端到端测试本来就单列了一层，用界面框架的无头模式，那是另一件事，不该和绘制列表的回归混在一起 |
| §15.1 快照测试的工具 | 用仓库里的冻结文本加一段比对代码，没有引入测试分层表里列的那个快照库 | 那个库不在 §2 的技术选型表里，而这里要的东西只有两样：逐字节可比、差异能定位到元素。一份规范文本加一段比对就够了，多一个依赖反而多一处会随版本变的行为。绘制列表的快照在 `DuetDiagram.Render.Tests/Scenes/`，比对代码在 `Snapshot.cs` |
| 测试框架 | xunit.v3 + Microsoft.Testing.Platform；`--filter` → `--filter-trait` | .NET 10 SDK 起 `dotnet test` 不再支持 VSTest 目标 |
| 测试断言库 | FluentAssertions **7.2.2** | 8.x 起改为商业许可；7.2.2 是最后一个 Apache-2.0 版本 |
| 解决方案文件 | `DuetDiagram.slnx` | 本轮约定 |
| §16 对比测试报告 `reports/compare.md` | 落在 `reports/compare-blind/`，且拆成结论与证据两份 | 一份文件装不下：解析统计、语义拒绝率、检查项可判定性、谓词口径各是一份证据，各有各的复现命令。混在一起之后没人知道哪一段该跟着哪条命令重新生成。另外装置不进 sln（见 `tools/CompareHarness` 那一行），所以它也没有 `Diagram.Compare.Tests` |
