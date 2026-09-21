# AGENTS.md — Agent 约定

本文件是**契约**，不是说明文档。改动前先读完《对人和 LLM 双友好的流程图/框图绘制软件——完整方案》。

## 项目定位

人和 LLM 通过**同一套命令接口**操作**同一份文档**，能力完全对等。
IR 是唯一事实源；GUI 做的每一件事，LLM 通过命令层都能做。

## 仓库布局

| 路径 | 作用 | 状态 |
|---|---|---|
| `DuetDiagram.Core` | IR、命令总线、日志、历史、广播、序列化 | 垂直切片已落地 |
| `DuetDiagram.Core.Tests` | Core 的单元与约束测试 | 已落地 |
| `DuetDiagram.Layout` | 布局引擎封装与约束补齐 | Phase 1 P1-11 已落地 |
| `DuetDiagram.Layout.Tests` | 布局不变量测试 | 已落地 |
| `DuetDiagram.Render` | 渲染与导出（当前只有视口索引） | Phase 1 P1-13 已落地 |
| `DuetDiagram.Render.Tests` | 空间索引测试 | 已落地 |
| `DuetDiagram.Mermaid` | Mermaid 词法、语法与图类型识别 | Phase 1 P1-08 已落地 |
| `DuetDiagram.Mermaid.Tests` | 词法/语法用例与冻结语料回归 | 已落地 |
| `DuetDiagram.Dsl` | 自有 DSL 的词法、语法与语义映射 | Phase 1 P1-16 / P1-17 已落地 |
| `DuetDiagram.Dsl.Tests` | 词法/语法/映射用例与冻结语料回归 | 已落地 |
| `DuetDiagram.AotSmokeTest` | 原生编译冒烟（多态 Memento + IR 往返） | 已落地（本机缺 C++ 工作负载，未完成发布） |
| `DuetDiagram.App` | 界面主程序 | 技术栈验证脚手架，含自检模式 |
| `DuetDiagram.Benchmarks` | 性能基线 | Phase 0b 已落地 |
| `docs/` | 架构、IR Schema、错误码、命令清单 | 已落地 |
| `tasks/` | 面向 coding agent 的任务 YAML | 已落地 |
| `reports/` | 阶段验证结论与取证数据 | 已落地 |
| `tools/LocCounter` | LOC 统计（不进 sln） | 已落地 |
| `tools/Poc/LayoutCandidates` | 布局引擎选型取证（不进 sln） | 已落地 |
| `tools/Poc/McpTransport`、`SharedTools` | 协议与工具共用的依赖验证（不进 sln） | 已落地 |
| `tools/CompareHarness` | 对比测试的语料生成与盲评装置（不进 sln） | 已落地 |
| `DuetDiagram.Llm` / `.Mcp` | 后续 Phase | 未创建 |

**不要提前创建后续 Phase 的空项目。** 每个 PR 只引入该任务真正需要的项目。

**依赖验证脚手架用完即删。** 结论固化进正式工程与文档之后，留着两份实现迟早会分叉——
分叉之后测试仍然全绿，而那份脚手架已经不能反映真实行为了。

## 不可违反的约束

以下 13 条由方案附录得出，每条都有对应的测试或 CI 门禁。**违反即视为回归。**

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
- **不引入选型清单（方案 §2）之外的依赖。** 需要新依赖先在 `Directory.Packages.props` 登记精确版本，
  并在 PR 里说明理由。
- **版本必须精确。** 不得出现 `*` 或区间。Phase 0 P0-01 已核实全部主选依赖存在。

## 验收命令

本仓库使用 **Microsoft.Testing.Platform**（`global.json` 的 `test.runner`），
因此方案里的 `dotnet test --filter Category=xxx` 实际写法是 `--filter-trait`：

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
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj -- --filter-trait "Category=QuadTree"
dotnet test --project DuetDiagram.Mermaid.Tests/DuetDiagram.Mermaid.Tests.csproj -- --filter-trait "Category=MermaidParsing"
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
`ConflictPolicy`、`FieldMetadata`、`Sidecar`、`SidecarBackup`、`Layout`、`LayoutFallback`、`QuadTree`、`MermaidLexing`、`MermaidParsing`、`MermaidCorpus`、`DslLexing`、`DslParsing`、`DslCorpus`、`DslMapping`、`DslLayoutIntent`

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

## 与方案的已知差异

| 方案位置 | 差异 | 原因 |
|---|---|---|
| §4.4 `IDiagramCommand.Undo` | 未提供该方法，由 `RestoreMemento` 承担 | 撤销所需信息全在 memento 中；`Undo()` 需要命令自身缓存状态，会和 Redo 的「重新捕获 memento」语义打架 |
| §4.4 `CommandError.Payload` | 类型为 `string?` 而非「可选对象」 | AOT 源生成下不需要多态注册；需要富载荷时再引入注册的联合类型 |
| §4.4 `FieldChange` 值 | 统一 `string?`，未引入 `PositionValue` / `SizeValue` | 坐标类命令属 Phase 2 |
| §4.5 派生名 | `EmptyDiff` / `InvalidDiff` / `EntriesDiff`（方案为 `Empty` / `Invalid` / `Entries`） | 避免与静态成员及类型名冲突 |
| §4.9 Context 依赖 | 8 项（缺 `Sidecar` / `Layout` / `Renderer`） | 三者尚未实现；「结构变更重布局，否则重绘」暂由 `CommandResult` 的两个布尔标志外化给宿主 |
| §4.9 步骤 3/4 | 填充的是 `ChangeContext` 的 `Timestamp` / `SessionId` | 这是唯一读得通的解释 |
| §4.1 IR 字段 | 九个集合与三个子对象已实现，但 `PageDef` / `LayerDef` 的字段取最小集合 | 方案只列出集合存在、未给字段；等对应交互开工时再补，见 `docs/IR-Schema.md` |
| §4.1 快照方法 | 只实现 `TakeFullSnapshot` / `RestoreFromSnapshot`，`ApplySnapshot` 未实现 | 它的语义方案未定义，协作同步设计清楚之前不硬猜 |
| §4.2 布局提示 | 已进 IR，但尚未接到布局引擎 | 约束补齐逻辑已落地，接线属 Phase 1 后续任务 |
| §12 冒烟工程只引用 Core | 现在也引用 Layout | 方案写这一条时还没有别的可发布组件。布局依赖的第三方引擎**是否原生友好分析器看不出来**——分析器只看我们自己的代码，只有真的发布一次才知道 |
| §12 工程命名 | 用 `DuetDiagram.*` 而不是 `Diagram.*` | 与解决方案文件名的前缀一致 |
| §4.3 Sidecar | 未实现 | Phase 1 P1-06 |
| 项目结构树 | 多一个 `DuetDiagram.Dsl` / `.Dsl.Tests` | 树里有 `Diagram.Compare.Tests` 与 P1-16/P1-17 两条任务，却没有承载它们的工程；命名按仓库约定加前缀，与 `DuetDiagram.Mermaid` 对称 |
| 测试框架 | xunit.v3 + Microsoft.Testing.Platform；`--filter` → `--filter-trait` | .NET 10 SDK 起 `dotnet test` 不再支持 VSTest 目标 |
| 测试断言库 | FluentAssertions **7.2.2** | 8.x 起改为商业许可；7.2.2 是最后一个 Apache-2.0 版本 |
| 解决方案文件 | `DuetDiagram.slnx` | 本轮约定 |
