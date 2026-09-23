# AGENTS.md — Agent 约定

本文件是**契约**，不是说明文档。改动前先读完根目录那份《对人和 LLM 双友好的流程图/框图绘制软件——完整方案》，下文称**规格文档**。

## 项目定位

人和 LLM 通过**同一套命令接口**操作**同一份文档**，能力完全对等。
IR 是唯一事实源；GUI 做的每一件事，LLM 通过命令层都能做。

## 仓库布局

| 路径 | 作用 | 状态 |
|---|---|---|
| `DuetDiagram.Core` | IR、命令总线、日志、历史、广播、序列化、工作区与文档锁、软锁与图层级权限 | 垂直切片已落地；P2-12 加了文档锁与心跳；Phase 3 P3-01 / P3-02 / P3-03 / P3-04 补齐了命令层（断边、布局、调色板、组合与图层、页面与标签与动作与文档设置）；P3-14 加了文档级软锁与权限模型；P4-02 加了 `set-layer-visible` / `set-layer-locked` 与 `LAYER_LOCKED` |
| `DuetDiagram.Core.Tests` | Core 的单元与约束测试 | 已落地 |
| `DuetDiagram.Layout` | 布局引擎封装与约束补齐 | Phase 1 P1-11 / P1-12、Phase 2 P2-09 已落地 |
| `DuetDiagram.Layout.Tests` | 布局不变量测试 | 已落地 |
| `DuetDiagram.Render` | 绘制列表、文本度量、视口变换、视口虚拟化与换档、帧时采样器、图层对画面的影响（谁不画、谁画了但点不中、谁画在谁上面） | Phase 2 P2-01 / P2-03 / P2-04 已落地（画布控件在主程序）；P4-02 让渲染层真的读图层 |
| `DuetDiagram.Render.Tests` | 空间索引、绘制列表与场景快照、剔除判据、换档编排与帧时采样 | 已落地 |
| `DuetDiagram.Mermaid` | Mermaid 词法、语法、图类型识别、导入与导出 | Phase 1 P1-08 / P1-09 / P1-10 已落地 |
| `DuetDiagram.Mermaid.Tests` | 词法/语法用例与冻结语料回归 | 已落地 |
| `DuetDiagram.Dsl` | 自有 DSL 的词法、语法与语义映射 | Phase 1 P1-16 / P1-17 已落地 |
| `DuetDiagram.Dsl.Tests` | 词法/语法/映射用例与冻结语料回归 | 已落地 |
| `DuetDiagram.AotSmokeTest` | 原生编译冒烟（多态 Memento + IR 往返） | 已落地（本机缺 C++ 工作负载，未完成发布） |
| `DuetDiagram.App` | 界面主程序：画布、视口变换、状态栏、布局失败提示、布局约束入口、多窗口与只读呈现、帧率基准、菜单栏与工具栏（条目按注册表组织）、图层开关与锁定的写入口 | Phase 2 P2-02 / P2-03 / P2-07 / P2-08 / P2-10 / P2-11 / P2-12 已落地，含自检模式与 `--open`；Phase 4 P4-01 / P4-02 已落地 |
| `DuetDiagram.E2E.Tests` | 无头模式下的端到端用例（起窗口、送输入、抓一帧、比像素） | Phase 2 P2-02 / P2-03 / P2-07 / P2-08 / P2-10 / P2-11 / P2-12 已落地；Phase 4 P4-01 / P4-02 已落地 |
| `DuetDiagram.Benchmarks` | 性能基线 | Phase 0b 已落地 |
| `docs/` | 架构、IR Schema、渲染管线、错误码、命令清单 | 已落地 |
| `tasks/` | 面向 coding agent 的任务 YAML | 已落地 |
| `reports/` | 阶段验证结论与取证数据 | 已落地 |
| `tools/LocCounter` | LOC 统计（不进 sln） | 已落地 |
| `tools/Poc/LayoutCandidates` | 布局引擎选型取证（不进 sln） | 已落地 |
| `tools/CompareHarness` | 对比测试的语料生成、盲评装置、谓词评分与人工评分汇总（不进 sln） | 已落地 |
| `tools/UserStudy` | 用户测试的量表、拉丁方顺序分配、录入模板、四条统计判定与结论换算（不进 sln） | 已落地；真人数据未收 |
| `tools/McpHarness` | MCP 的协议层验收装置：拿真的服务端可执行文件把八条判据逐条跑一遍，另印出接真实代理要用的命令行（不进 sln） | Phase 3 P3-16 已落地 |
| `DuetDiagram.Llm` | 八个粗粒度工具的定义、参数 schema 与参数校验；一份定义两处派生；归一化上下文摘要与 `diagram_read`；八个工具的动作分发、样式白名单与幂等键；图层级权限判定（在动作参数上判这次写入点名了哪个图层）；导出（Mermaid）、整体校验与撤销重做；错误码到修复建议的映射表与错误回环；内部模型那条通路的客户端（工具调用往返 + 失败回灌 + 把这一轮匹配上的 Skill 正文接在系统提示后面） | Phase 3 P3-05 ~ P3-11 / P3-14 / P3-15 已落地；模型那条通路已经能跑通，凭据与真实模型验收未开工 |
| `DuetDiagram.Llm.Tests` | 工具表、参数约束、上下文摘要、动作分发、样式白名单、布局组合动作、图层级权限、导出校验、历史栈、错误回环与两侧派生一致性的门禁 | 已落地 |
| `DuetDiagram.Mcp` | MCP Server：把注册表里那八个工具挂到协议上，走标准输入输出或 HTTP 两条传输；另把两份 Skill 按 `skill://` 挂成资源，第三层就是那份语法文档本身（构建时嵌入程序集）；会话状态从每条请求现读、标准输入输出下日志改道标准错误；网络那一档前面挡着认证、限流、权限档、版本预判与工作区，另有一条按版本号补差的变化源端点，审计与命令层告警写标准错误 | Phase 3 P3-12 / P3-13 / P3-14 / P3-15 / P3-16 已落地；跨机器传输（TLS、多实例共享变化源）未开工 |
| `DuetDiagram.Mcp.Tests` | 起子进程走标准输入输出验工具发现与调用、会话状态与协议层；起真端口走 HTTP 验无状态、五种拒绝、冲突返回与变化源的门禁；另有一组十个 agent 真并发写同一份文档 | 已落地 |

**阶段状态**：Phase 0a / 0b、Phase 1、Phase 2、Phase 3 的代码都落地了
（P0-02、P1-14、P2-13 三处分别卡在缺 C++ 工作负载与缺真人）。
Phase 3 的 16 个任务（命令层、工具定义、上下文摘要、八个工具的执行体、错误回环、模型通路、
标准输入输出那条传输、网络传输与它前面那道关、并发三层、Skill 机制、实机验收与多 agent 并发）
都是 `done`。P3-16 里"用真的 Codex CLI / Claude Code 接上服务端"那一步需要人看着跑，
结论记在 `reports/phase3-mcp.md`——装置齐了不等于测过了，那一步没跑成就不写成通过。
Phase 4 的任务集（`P4-01` ~ `P4-22`，丰富功能）已产出。**`P4-01`（工具栏与菜单栏骨架）
与 `P4-02`（图层的可见性、锁定与渲染消费）已落地**（`done`），其余 `pending`，
其中 **`P4-20`（DSL 导出）标 `blocked`**，卡在决策门 1 上——留则照做，不留则整条删掉。
两个决策门（DSL 去留、布局引擎主选）都还开着，卡在 `reports/compare-blind/` 的人工评分上——
**那不是「还没做」，是「做不了」**，编码 agent 判不了自己写的东西好不好用。

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
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj

# 端到端（无头模式，起窗口、送输入、抓一帧；需要绘图后端，本机可直接跑）
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj

# 按分类
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=Atomicity"
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=CommandGroups"
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=Composite"
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=IrHashing"
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

# 工具表：八个工具全部登记、名称唯一、说明与参数 schema 非空；
# 模型侧与代理侧的名称、说明、参数 schema 逐字相同
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=ToolRegistry"

# 参数约束：标量参数上的正则进 schema 的 pattern、数组参数的进 items；
# 越界的标识被拒，错误里带上参数名与期望形式，且一条命令都没发出去
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=ToolSchema"

# 上下文摘要：六块内容齐、不含原始坐标、同一份文档逐字节相同；
# 令牌实时取自调色板，最近修改读的是版本日志
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=ContextSummary"

# 动作分发：动作表里每个动作都有执行体、每个 CommandId 都在「已实现」表里、
# 动作表与命令清单里那张对照表逐行一致；未知动作列出可用动作；
# 命令被拒时回的是命令层错误码；同一个幂等键重复调用不施加第二次变更
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=ToolDispatch"

# 样式白名单：不在调色板里的令牌被拒且列出可用的那些，调色板改了之后白名单跟着变；
# set-style 与 set-text 各认自己的字段前缀；画布六个成员各改各的
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=StyleWhitelist"

# 布局动作：方向、间距、三类约束与相对位置各落到一条命令上；约束种类与成员形状对得上；
# 归属方只认 llm 与 auto，human 被拒；改完之后结构变更标志为真
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=LayoutTool"

# 组合动作：四种组合各建出自己的记录类型、移入会把成员从旧容器摘出来、
# 解散把成员交给父级；成环与超深度由命令层拒掉
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=CompositeTool"

# 导出：Mermaid 文本逐字节可复现、丢失清单原样带出、导出不动版本号也不进撤销栈；
# 自有 DSL 的导出方向与位图 / PDF 返回结构化「尚未支持」
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=ExportTool"

# 校验：问题逐条带上错误码、相关标识与修复建议（建议来自校验器那一份，工具层不另写一张表）；
# 校验不动版本号也不进撤销栈
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=ValidateTool"

# 撤销重做：人和模型共用同一条历史栈、多步、栈见底停下并说清撤了几步、
# 返回里说明撤掉的是哪一条命令、哪几步是自己改的
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=HistoryTool"

# 错误回环：一次失败翻成码、出错参数、可用形式与一句可执行的建议；
# 同一个错误连着来第二次时多一句「上一次这么改也不行」；
# 到上限停下并把整段往返记录写进应用日志
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=ErrorLoop"

# 修复建议表：覆盖命令层与工具层两套错误码、点名的参数真的是某个工具的参数、
# 每句建议都写得完；错误码文档里那张表与代码里的映射逐行一致
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=RepairHint"

# 模型通路：工具调用真的改了文档、结果真的回到模型手里、每一轮都把八个工具带上；
# 命令被拒时回灌的是结构化错误（码、出错参数、可用取值、一句建议）；
# 同一个错误连着来第二次多一句提醒；到上限停下且不再问第三次
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=ChatClient"

# 两侧同源：模型侧与代理侧的名称、说明、参数 schema 逐字相同（按原文比，不按结构比）；
# 可空参数的数组形式两侧一致
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=ToolParity"

# 标准输入输出：起一个真的子进程连上去，工具发现与工具调用都走通、写入落到文档上、
# 失败回的是结构化错误、日志落在标准错误上（标准输出被协议占着）、换个实例重跑结果一样
dotnet test --project DuetDiagram.Mcp.Tests/DuetDiagram.Mcp.Tests.csproj -- --filter-trait "Category=McpStdio"

# 会话状态：声明从请求元数据与参数顶层两处都读得到、三种形态都认、
# 调用级声明盖过会话级、旧声明被挡下且文档一个字节没动、版本超前算参数错误不算冲突、
# 没声明只能读不能写、每条请求都自带声明
dotnet test --project DuetDiagram.Mcp.Tests/DuetDiagram.Mcp.Tests.csproj -- --filter-trait "Category=McpSession"

# 协议层：会话建立那一次请求按位置取样而不是按方法名匹配（协议改名之后断言照样成立）、
# 自定义字段挂在能力声明的扩展位上、两个方向用各自的字段名、挂上去的工具与模型侧同源
dotnet test --project DuetDiagram.Mcp.Tests/DuetDiagram.Mcp.Tests.csproj -- --filter-trait "Category=McpProtocol"

# 网络传输：在真端口上起服务端，无状态（换一个实例、不做任何握手，同一个请求照样成立）、
# 版本声明随每条请求带上、旧声明被挡下且文档一个字节没动、从文件起服务端
dotnet test --project DuetDiagram.Mcp.Tests/DuetDiagram.Mcp.Tests.csproj -- --filter-trait "Category=McpHttp"

# 网络那一档前面的那道关：无凭据 401、只读凭据改文档 403、超限 429 且带重试间隔、
# 图层级权限（改得动别的层、改不动禁止的那一层）、路径逃逸被拒（`..` 与符号链接都算）、
# 没有时间预算时一条命令都不发、审计不记凭据与完整路径
dotnet test --project DuetDiagram.Mcp.Tests/DuetDiagram.Mcp.Tests.csproj -- --filter-trait "Category=McpSecurity"

# 变化源：有更新的版本立刻回、没有就长轮询到超时回空、挂在等待里的调用方被另一条连接上的
# 改动叫醒、断了一段的调用方一次请求拿到当前版本、报的版本已经最新就等
dotnet test --project DuetDiagram.Mcp.Tests/DuetDiagram.Mcp.Tests.csproj -- --filter-trait "Category=ChangeFeed"

# 冲突时回什么：四种情形逐条对上（未声明→全量、版本相等→空、范围内且哈希一致→清单、
# 超范围或哈希不一致→全量），版本更靠前算参数错误不算冲突；
# 网络那一档回 409 且正文里带差异，撤销重做不接受版本声明
dotnet test --project DuetDiagram.Mcp.Tests/DuetDiagram.Mcp.Tests.csproj -- --filter-trait "Category=Conflict"

# 软锁：拿锁、被他人持有时的行为、续期把到期时刻往后推、无操作到期自动释放、
# 过期之后续不上也放不掉；判据是"最后一次操作"，所以每二十九秒动一次的持有者不算过期
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=SoftLock"

# 权限模型：read 改不动、edit 改不动图层级禁止的部分、full 不受限；
# 没点名图层的写入不算图层级写入；认不出的主体按只读；主体名重复拒绝装载
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=Permission"

# 图层级判定在工具层：建 / 改 / 排序三条图层命令各判一次，
# 改节点归属报的是 value（图层写在 value 上），被挡住时文档与版本号都没动
dotnet test --project DuetDiagram.Llm.Tests/DuetDiagram.Llm.Tests.csproj -- --filter-trait "Category=Permission"

# Skill：两份都能按 skill:// 取到、正文非空且带版本，清单里带着"什么时候该取"，
# 两条传输取回来的是同一份；URI 爬不出这一层（`..`、编码过的 `..`、反斜杠都算）；
# 资源文件里每一段 DSL 例子交给真解析器，要求零诊断
dotnet test --project DuetDiagram.Mcp.Tests/DuetDiagram.Mcp.Tests.csproj -- --filter-trait "Category=Skill"

# 渐进式披露的三层边界：工具描述单行且不超长、Skill 正文不含精确语法、
# 资源文件才含语法；始终在上下文里的那一份不含任何一段 Skill 正文；
# 内部那条通路不传 Skill 时一个字都不加，传了才接在系统提示后面
dotnet test --project DuetDiagram.Mcp.Tests/DuetDiagram.Mcp.Tests.csproj -- --filter-trait "Category=SkillDisclosure"

# 十个 agent 真并发写同一份文档：版本号不重号不跳号、节点数与成功次数对得上、
# 写完之后文档仍然校验通过。顺序跑十次验不到版本检查的竞态，而竞态正是这一条要挡的
dotnet test --project DuetDiagram.Mcp.Tests/DuetDiagram.Mcp.Tests.csproj -- --filter-trait "Category=MultiAgent"

# MCP 的协议层验收：拿真的服务端可执行文件把八条判据逐条跑一遍（协议一致、参数校验、
# 十 agent 并发、409 + 差异、只读无法写、速率限制、审计日志、会话应答）
# 退出码表达整体结果。取证见 reports/phase3-mcp.md
dotnet run --project tools/McpHarness -c Release -- protocol

# 印出接真实代理（Codex CLI / Claude Code）要用的命令行与配置。它不替人跑代理：
# 那两个是别人的程序，要装、要凭据、要有人看着，跑没跑过由报告照实记
dotnet run --project tools/McpHarness -c Release -- agent

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

# 工具栏：四档的次序固定，每一条点下去要么变了、要么说了话，不能点的把理由挂在提示上
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj -- --filter-trait "Category=ToolBar"

# 菜单栏：两处对同一条给出同样的启用状态，没选中时给理由，只读时该挡的挡住、不该挡的留着
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj -- --filter-trait "Category=MenuBar"

# 图层：两个开关各自的作用范围互不干涉，都只算外观变更，失败不改文档、撤销回得去
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=LayerVisibility"

# 图层怎么影响画面：不可见的不画、锁定的画了但点不中、按次序前后、缺省层固定在最底下
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj -- --filter-trait "Category=LayerRender"
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj -- --filter-trait "Category=LayerRender"

# 跨进程所有权：独占、拿不到就退只读、心跳过期判定、抢占前先试删锁文件
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=DocumentLock"

# 从文件打开一份文档；另一个进程拿着它时这一份退成只读。
# 文档不在、读不出来、或者抢占之后校验不过时，退出码 2 且错误写在标准错误上
dotnet run --project DuetDiagram.App -c Release -- --open path/to/doc.json

# 依赖验证脚手架（结论固化后可删，见仓库布局表）
dotnet run --project tools/Poc/LayoutCandidates -c Release

# 用户测试的装置：统计量在构造好的已知答案上算得对不对（不需要真人数据）
dotnet run --project tools/UserStudy -c Release -- verify

# 量表、拉丁方顺序分配表与录入模板。已有的录入文件不覆盖
dotnet run --project tools/UserStudy -c Release -- template

# 读录入数据，算四条判定，写结论报告。没有数据时明确报"还没有数据"且什么都不生成——
# 生成一份空的或占位的报告，后来的读者会以为测过了。数据收上来之前这条命令不产出报告
dotnet run --project tools/UserStudy -c Release -- analyze

# LOC 一致性
dotnet run --project tools/LocCounter -- --root . --check

# 任务 YAML 必须能被解析。它们是给 agent 读的，读不了等于任务不存在——
# 已经踩过两次（未加引号的冒号加空格、未加引号的引号），两次都是整个文件静默失效
python -c "import yaml,glob; [yaml.safe_load(open(f,encoding='utf-8')) for f in glob.glob('tasks/**/*.yaml',recursive=True)]; print('ok')"
```

### 已有测试分类

`RoundTrip`、`Atomicity`、`UndoRedoVersion`、`DiffBoundary`、`MementoRegistration`、
`NestedExecute`、`NestedExecuteCrossThread`、`Broadcaster`、`SessionIdResolution`、
`UndoStress`、`Workspace`、`McpMode`、`CorePurity`、
`IrHashing`、`IrSnapshot`、`IrReadOnly`、`IrValidator`、`IrConstruction`、
`ConflictPolicy`、`FieldMetadata`、`CommandGroups`、`Composite`、`Sidecar`、`SidecarBackup`、`Layout`、`OrderAlign`、`LayoutFallback`、`LayoutConstraint`、`QuadTree`、`Viewport`、`CullingPolicy`、`ModeSwitch`、`DiagnosticsSampler`、`DrawList`、`SceneSnapshot`、`Canvas`、`Diagnostics`、`PropertyPanel`、`HitTest`、`Drag`、`Connect`、`EdgeEdit`、`EdgeField`、`Highlight`、`ErrorPresentation`、`LayoutFailure`、`ConstraintEditor`、`MultiWindow`、`DocumentLock`、`MermaidLexing`、`MermaidParsing`、`MermaidCorpus`、`MermaidImport`、`MermaidExport`、`MermaidRoundTrip`、`DslLexing`、`DslParsing`、`DslCorpus`、`DslMapping`、`DslLayoutIntent`、`ToolRegistry`、`ToolSchema`、`ContextSummary`、`ToolDispatch`、`StyleWhitelist`、`LayoutTool`、`CompositeTool`、`ExportTool`、`ValidateTool`、`HistoryTool`、`ErrorLoop`、`RepairHint`、`ChatClient`、`ToolParity`、`McpStdio`、`McpSession`、`McpProtocol`、`McpHttp`、`McpSecurity`、`ChangeFeed`、`Conflict`、`SoftLock`、`Permission`、`Skill`、`SkillDisclosure`

## 新增一个命令的检查清单

1. 继承 `DiagramCommandBase`，`CommandId` 用小写连字符（如 `add-node`）。
2. `Validate` 覆盖全部前置条件，返回**结构化** `CommandError`，不要在 `Apply` 里抛异常。
3. `Apply` 只做变更；任何失败路径都必须让文档回到调用前状态。
4. `CaptureMemento` 与 `Apply` 对同一文档必须给出**一致**的索引/位置。
5. `RestoreCore` 必须能还原顺序（边的插入按原索引升序）。
6. 新增 Memento 记录 → 补 `[JsonDerivedType]`（约束 5）。
7. 补原子性测试 + 往返测试。
8. 在 `docs/Command-List.md` 的「已实现」表登记，**标识与类名两列都要写对**。
9. 跑一遍 `Category=Atomicity`、`Category=MementoRegistration` 与 `Category=CommandGroups`。
   最后一条会拿「已实现」表跟程序集里真正存在的命令逐条比——漏登记、写错类名、或者
   表里留着一条已经删掉的命令，都会红。

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
| §8 Skill 机制「遵循 SEP-2640」 | 落点是协议实现通用的**资源**那一档加一个自定义的 `skill://` 协议，没有 SEP-2640 那个扩展 | 依赖里那个版本的协议实现里没有这个扩展（全文搜不到），而它本来就有资源那一档：清单、读取、内容类型都齐。等扩展成为标准、或者实现接上之后可以换过去——URI 与两份正文都不用动 |
| §8「两个 Skill 存于 `Diagram.Mcp/Skills/`」 | 第三层（精确语法）的那一份不新建文件，直接用仓库里既有的 `docs/DSL-Syntax.md`，构建时作为嵌入资源随程序集走 | 另抄一份的话两边迟早对不上，而对不上的表现是模型照着一份过期的语法写——本仓在错误码名上已经踩过一次同样的坑。接过来之后「文档与实现不分叉」由构造保证，再加一条拿真解析器跑里面每段例子的门禁 |
| §7.3 握手「回 `accepted` 与一个 `reason`」 | 没有 `accepted=false` 这条通路。会话状态随**每条请求**带上来，服务端每次现读现判；对不上时回的是差异或快照（网络那一档 409 加内容），不是一次协商被拒 | 当前协议版本用服务发现请求取代了初始化请求（P0-05 已记过），所以不存在"一次协商被接受或被拒绝"这件事。§15.2 那张清单里的「accepted=false 处理」因此没有落点，替代口径就是 §7.4 那四行——这一条写在 `reports/phase3-mcp.md` 里，不写成「已通过」 |
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
| §4.4 与计划中命令清单的规模（四十余条） | 实际落地的命令远少于此：节点与边的字段写入合成 `set-node-field` / `set-edge-field` 两条，按字段名分发 | 字段注册表落地之后，`update-node-label`、`move-node-layer`、`set-node-shape`、`apply-style-token`、`set-node-style`、`set-text-style`、`set-edge-style` 这些各自再立一条命令的话，同一个效果会有两条路，而两条路的**冲突粒度不一样**——按字段写的那条能认出「一边改填充、一边改描边」可以共存，整份覆盖的那条会把两次互不相干的修改判成冲突。判断标准是**被改的值挂在元素上还是挂在文档上**：挂在元素上的由字段表覆盖，挂在文档子对象上的（`layout`、`palette`）才各立命令 |
| 计划中命令清单里的 `reorder-node` | 不做，也没有对应的任务 | 节点集合顺序在三个可能观察到它的地方都不可见：结构哈希与外观哈希都先把集合按标识排序再遍历，布局引擎在同层内按横坐标排序、并列时用标识断掉，绘制阶段虽然按集合顺序出笔但布局保证同层不重叠。做出来会是一条**效果不可观测**的命令。它要成为真功能，前提是先给节点加一个 z 序字段并定义它在绘制里的语义，那是 IR 的改动 |
| 计划中命令清单里的 `add-edge-waypoints` / `set-edge-route` | 不做，也没有对应的任务 | 折点写在 sidecar 的 `pinnedEdges`，不进 IR、不走命令总线——折点是用户「拖这儿」的产物，走总线会把一次拖动塞进几百条 IR 记录、撤销栈失真。所以工具层也不为折点开 action，它只走宿主侧的入口 |
| 删除元素时对布局约束的处置 | 删边（`disconnect-edge`）不清理引用了它的层内次序，留着让整体校验器报 `LAYOUT_ORDER_EDGE_MISSING` | 命令里替用户把那条约束删掉的话，用户失去的是一条自己设过的约束，而且没有任何提示——比一条能被报出来的悬空引用糟得多。布局侧也是同一口径：求解器把认不出的标识当陈旧数据跳过而**不过滤**，就是为了让这种悬空还能被看出来 |
| 删调色板条目时对引用者的处置 | 还有元素在用这个样式令牌时报 `PALETTE_ENTRY_IN_USE` 并**挡住删除**，把引用者的标识写进载荷 | 这与上面那条**刻意相反**，判据是「这件事有没有第二个地方能看见」。删边留下的悬空引用有整体校验器会报；而删一个被引用的令牌，渲染层只是静默退回元素自己的样式，不报错、不崩，用户看到的是一次「什么都没发生」的删除配一张颜色不对的图 |
| 计划中命令清单里的 `pin-node` / `unpin-node` | 不做，也没有对应的任务 | IR 里没有任何地方能存节点的绝对坐标：`NodeDef` 的字段里没有位置，`LayoutHints` 的四个列表全是相对约束。DSL 的 `pin` 意图早就落到 sidecar 的 `pinnedNodes` 了，理由是**坐标属于渲染结果**——存进 IR 会让同一份语义在不同机器上产生不同的文档内容。命令层再立一条 pin 会与这条原则直接冲突，同一个节点还会因此有两个互相矛盾的固定位置 |
| §4.1 的约束列表 | 多一类**相对位置**（`LayoutHints.Place`）与它自己的命令 `set-place`，不并进 `add-layout-constraint` / `remove-layout-constraint` | 前三类的形状是「一组平级的成员」，增与删是两个动作；相对位置是「一对节点加一个方向」，设置与清除是同一个动作的两个方向。并进去的话，调用方要表达「把这个节点摆到那个节点右边」得先判断该调增还是调删，而那个判断本来就在命令里。同一对节点、同一归属方只留一条（再设一次是替换），因为求解器取的是列表里的第一条，追加会让生效的永远是最早那一次 |
| 相对位置约束的求解 | `set-place` 能写进 IR、进结构哈希、进整体校验、进降级计划的计数，但**求解器不消费它**——设完之后坐标不变 | 这一类约束要落到坐标上，得先定「一侧」在分层布局里是哪个自由度，而那与前三类的关系还没理清。先让命令层与 IR 就位、把「不消费」这一点显式告诉调用方，比硬猜一个语义再改掉要好。调用方不知道的话，会把「命令没生效」当成一次失败 |
| 计划中命令清单里的 `assign-layer` | 不做，由 `set-node-field` 的 `layer` 字段覆盖 | `NodeDef.Layer` 登记在节点名下，`NodeFieldValue` 里有读写分支，属性面板也认它。再立一条会让同一个效果有两条路。**它与 `parent` 不同**：父级字段是冗余的那一份（真正说了算的是容器的成员列表），所以归属要有一条自己的命令 `move-into-composite`；图层归属只有一个存放处，走字段表就够了 |
| 计划中命令清单里的 `create-group` / `create-lane` / `create-subflow` / `create-combo` | 合成一条 `create-composite`，种类由传进来的记录类型表达 | 四个派生记录的字段形状完全一样，差别只在类型名。四类各立一条的话，校验、原子性与撤销三套逻辑要各写四遍，而抄漏的那一遍不会报错，只会让某一种组合在某个入口下改不动。再引入一个种类枚举的话，同一个意思会有两种写法，两者还可能对不上 |
| 组合的嵌套深度 | 新加一条上限（`CompositeLimits.MaxDepth`，顶层是第 1 层），命令层与整体校验器都读它，超了报 `COMPOSITE_TOO_DEEP` | 成环已经单独挡住，所以深度天然有界；上限要防的是**深而窄**的链——一千层逐级嵌套的合法文档会让任何按层递归的遍历一路压到栈底，而栈溢出的现场离肇事的那条命令很远。上限只写一处：两处各写一个数的话，会出现「命令放得进去、校验器却报错」这种自相矛盾的状态 |
| 组合成员一致性的校验方向 | 除「成员列表里列了谁、而那个谁的父级不是本组合」之外，**反向也查**：父级指向某个组合、而那个组合的成员列表里没有它 | 只查前一个方向的话，这种文档不会被报出来——解散那个外层时它也就不会被放出来，于是用户拆了一个分组，却发现里面少了一层。而那条报错本该在写入那一刻就出现 |
| 计划中命令清单里的 `reorder-layer` | 改的是 `LayerDef.Order` 字段，不是集合里的位置 | 图层集合在视觉哈希里按标识排序后遍历，所以集合位置进不了任何哈希——改位置是一条**效果不可观测**的命令，与 `reorder-node` 同一个坑。次序值整体重排成连续的 0、1、2……，顺带治好从文件里读进来的重复次序。`DiagramDocument.Layers` 上原先那句「排列次序决定叠放次序」说的是集合位置，一并改对了 |
| P3-04 起草时写的那条约束「`remove-tag` / `remove-action` 要处理元素上还挂着它」 | **这件事不存在**，两条命令都不需要顺带摘掉任何引用 | 标签的成员列表与动作的目标引用都是**单向**的：被指向的元素上没有回指字段（`NodeDef` 里根本没有标签字段）。删一个标签就是删掉那份成员列表本身，删一个动作就是删掉那份引用本身，两个方向都不会留下悬空引用。反过来那件事——删掉元素之后别处还写着它——由整体校验器报 `TAG_MEMBER_MISSING` / `ACTION_TARGET_MISSING`，与删边留下的悬空次序同一口径。起草任务时是按「引用关系总是双向的」这个直觉写的，对着模型核完才发现不成立 |
| 删调色板条目时的引用者范围 | 除节点与边之外，**标签的颜色**也算引用者 | `TagDef.Color` 取的就是调色板令牌名。`add-tag` 落地之前这个字段没有任何命令能写，所以漏着不显；落地之后它就成了第三条引用路径。不补的话，删掉一个被标签引用的令牌，标签的颜色会静默退回兜底值——正是那条命令自己的说明里要避免的那件事（判据是「这件事有没有第二个地方能看见」）。组合的样式记录里没有令牌字段，所以不算引用者 |
| `add-action` / `remove-action` 的两个变更标志 | 都报假，**而那不是无操作** | 动作不进任何哈希：不影响坐标，也不影响像素，只影响交互。报成视觉变更会让宿主白白重绘一次。版本照推、历史照进、广播照发，变的只是宿主那一侧不必重排也不必重绘——所以判断要不要触发副作用一律用 `IsEffectiveSuccess`，它只看 `IsNoOp`，不看那两个标志 |
| `set-canvas-settings` 的形状 | 六个成员各自可选，背景色用**空串表示清除**、空引用表示不动 | 与 `set-spacing` 同一口径：整份替换会逼调用方先把没打算改的项读回来再写回去，而那份读回来的值可能已经过期——一次「只改网格」的调用会把背景色悄悄改回旧值。清除只能另给一个信号，而空引用在可选参数里已经占了「这一项不动」的意思；空串在字段写入那条路上本来就是「这个字段没有值」的写法，两处一致。六个成员各登记一个 `canvas.*` 字段名，好让变更明细的粒度落到成员上：只用一个 `canvas` 的话，一边改网格、一边改背景色会被判成冲突 |
| 页面、标签、动作的消费方 | 三样都能经命令层增删了，但渲染层与界面都还没读它们 | 翻页、标签着色、动作触发都还没有接线，图层的可见与锁定也仍然没有命令能改。IR 与命令先就位，是为了让「一份带页面与标签的文档能存下来、能往返」这件事先成立；消费方开工时再定它们各自的界面语义 |
| §6.3 的 `ToolRegistry.ToMcpTools` | 与 `ToMeaiFunctions` 同处一地，都在 `DuetDiagram.Llm` 里；代理侧的**传输**仍然留在 `DuetDiagram.Mcp`（P3-13） | 派生代理侧的工具需要代理侧的包，而这一层只有这一个用途；把它挪到 P3-13 的话，P3-11 那条「两侧的 schema 逐字相同」的断言就只能自己拼一个代理侧工具出来，而那正是「定义只写一份」要避免的 |
| 工具层的 action 数少于命令数 | 节点与边的属性各合成一条按字段名分发的命令，所以「改标签」「改形状」「套令牌」「改字号」在命令层是同一条 | 字段表落地时就定下了这个口径。工具层把它拆回一条一个 action 的话，同一个效果会有两条路，而其中一条不进结构哈希——判据是**被改的值挂在元素上还是挂在文档上** |
| 工具层的错误码与命令层的错误码分成两套 | 工具层自己一份（`TOOL_` 前缀），不进 `ErrorCodes` | 命令层那张表由界面呈现的用例逐行核对，把工具层的码并进去会把它判红；而且两者的处置不同——命令层的「字段值不合法」是去改文档，工具层的「参数不合法」是去改这一次调用 |
| 参数约束的注入方式 | 不用 `AIJsonSchemaCreateOptions.TransformSchemaNode`，改成在 schema 生成之后按声明方法的参数表回填 | 实测那个回调对每个 schema 节点都被调用，但路径恒为空、参数特性提供者恒为空，认不出当前节点属于哪个参数。按参数表回填是确定的，也不依赖回调次序 |
| `diagram_layout` 里没有 pin / unpin | 命令层没有这条命令，IR 里也没有能存绝对坐标的地方——节点的固定位置落在 sidecar | 起草 P3-08 时写的「pin 与 unpin 走命令层」与命令层的现状对不上，P3-08 开工时改掉了。连带着那一轮写的「pin 与 sidecar 的固定位置两回事、合并规则要定死」也不成立：只有一处存放处，没有两份要合并 |
| `diagram_layout` 的 `owner` | 缺省写 `llm`；`auto` 放行，**`human` 被拒绝** | 命令层要求显式给出归属，理由是默认值会让模型那条路径悄悄写出人工归属的约束、而降级时被当成「用户设的」保住。工具层就是模型那条路径，所以缺省写 `llm` 是事实；而 `human` 放行的话模型可以伪造最高优先级的归属，把用户自己设的约束挤掉。用户从面板上设的约束由面板自己写，不经过这一层 |
| 增删布局约束的动作名 | 动作叫 `add-constraint` / `remove-constraint`，命令标识是 `add-layout-constraint` / `remove-layout-constraint`，两者不同名 | 动作名短，而命令标识带 `layout` 前缀是为了在命令清单里与别的增删区分开。冻结的参数表说明里举的例子就是 `add-constraint`，照它写才不至于让说明与实际动作名对不上 |
| `DuetDiagram.Llm` 多一条到 `DuetDiagram.Mermaid` 的工程引用 | 导出那一路直接调 `MermaidExporter`，不把导出器当委托注入上下文 | 它与「工具层不引布局引擎」那条口径不冲突：布局引擎是第三方组件，而 Mermaid 是本仓的、只依赖 Core 的纯函数库。反过来注入的话，每个宿主与每条用例都要接一次线，而导出文本本来就是文档的纯函数。对照 P3-06 注入的那两样（层投影、固定标识）：它们**不是**文档的函数，一个在布局结果里、一个在 sidecar 里，所以必须由宿主喂进来 |
| §6.1 的 `diagram_export` 格式 | 只有 Mermaid 接上了；`dsl` / `svg` / `png` / `pdf` 返回结构化「尚未支持」 | 位图与 PDF 要么依赖渲染层、要么依赖排版库，选型表把它们标成 Phase 4 验证。而 **DSL 导出不是「还没排到」，是不该现在做**：`DuetDiagram.Dsl` 只有词法、语法与语义映射三样，没有导出方向，而 DSL 去留那个决策门还开着——判掉之后写出来的导出器要整个删掉 |
| `diagram_validate` 的修复建议 | 照抄 `ValidationIssue.Suggestion`，工具层不另写一张按错误码查的表 | 校验器逐条填了那个字段，那就是「映射表放在一处」里的那一处。工具层再写一张的话，同一个码会在两个入口给出两种建议。`docs/Error-Codes.md` 里那张表是**界面**怎么呈现，由 `Category=ErrorPresentation` 逐行核，与工具层无关 |
| 「撤掉的是哪一条」怎么拿到 | 调撤销之前先窥栈顶（`HistoryStack.PeekUndo` / `PeekRedo`），拿命令标识与会话标识 | `Undo()` / `Redo()` 返回的 `CommandResult` 里没有命令标识，成功路径上也没有消息。不去读版本日志，是因为日志的用途是审计，让它承担「告诉模型刚才撤了什么」是把它当成了另一件事的载体。栈顶那一条的会话与当前会话相同，就说「你自己改的」 |
| `diagram_export` / `diagram_validate` / `diagram_undo_redo` 不在动作对照表里 | 它们的参数不是 `action`，也不发任何一条命令 | 导出与校验是只读的纯函数，撤销重做走历史栈。表里每一条都对应一条命令，所以它们没有位置——硬塞进来的话，「表里的 CommandId 必须在已实现表里出现过」那条核对会要求它们指向某条命令，而它们没有 |
| `steps` 与栈见底 | 栈见底不是失败，是空操作：停下并把实际做了几步写进答复 | 报成失败的话，模型会以为整条调用没生效，然后换个做法重试。多步里的失败才要回滚：把已经成功的那些反向做回去——半途停下的文档比整条失败更难收拾 |
| P3-05 的八个工具一律返回结构化的「尚未接上」 | 参数表与说明先定死，执行体分批接上 | 参数表是接口，分两次定的话先接上的那些调用方要跟着改。接上时替换的是各自的方法体，`DiagramToolset.cs` 会进 P3-06 ~ P3-09 的改动清单（它们现在的 `files.modify` 里还没有它） |
| §6.4 的布局摘要（方向、层数、同层分组） | 方向取自 IR；层数与同层分组由宿主从布局结果量化成层号传进来，缺省时只写方向 | 工具层不引布局引擎（P3-08 定的口径）。自己按拓扑算一个的话，摘要里的层数与实际画面各按一套算法，对不上时没有任何东西会报错，而模型会照着一个错的层数去提要求 |
| §6.4 的锁定列表 | 由宿主传入被固定节点的**标识**，摘要层不读 sidecar 文件 | 固定位置那份人工产物归宿主管。只收标识不收坐标，正好避开「模型照着会过期的数字微调位置」那条 |
| §6.5 的相对时间（「2分钟前」） | 结构化对象里存绝对时间戳，文本渲染时**显式传入参照时刻** | 不收参照时刻的话，同一份摘要两次调用得到不同的文本，「逐字节相同」这条判据根本没法验 |
| §6.5 的示例次序 | 文本的形状照它，**次序不照**：节点与边按标识排序、同层分组按标识排序 | 归一化是硬约束：同一份文档无论集合的插入顺序如何都要给出同一份摘要。示例是手写的，它的次序只是读起来顺 |
| `diagram_read` 的 `pageId` | 给了就返回结构化「尚未接上」，不给则读整份文档 | 页面现在还没有消费方（渲染、界面与摘要都没读它）。认下这个参数而按整份文档回，会让调用方以为它读的是某一页——一个静默的错误答案比一句「还没接上」糟得多 |
| 工具集的构造 | `DiagramToolset.Create` 与 `ToolRegistry.CreateDefault` 都要一个 `DiagramToolContext`（文档、版本日志、固定标识、层投影、时钟） | 八个工具全都作用在某一份文档上。用静态字段兜的话，同一个进程里的两个窗口会互相看到对方的文档，而表现是「摘要里的图不是我这一张」，只在多窗口下出现 |
| 工具声明是实例方法而不是静态方法 | 上下文由实例捕获，**不当成声明方法的一个参数** | 参数表从签名推导，多一个参数就会多一条模型要填的 schema，而它根本不是模型能提供的东西。现有 `Category=ToolSchema` 的用例是这条的回归网 |
| §6.1 的 `diagram_edit` 参数表 | 加了 `event` / `kind` / `targetId` 三个可选参数 | `add-action` 要的是事件名、动作类型名与目标，而表里只有 `id` / `label` / `field` / `value` / `memberIds` / `index`。硬塞进 `field` / `value` 会让那两个参数的说明变成「有时是字段名、有时是事件名」，而说明是模型唯一的依据。加可选参数是加法，已有的调用方一个字都不用改 |
| `diagram_edit` 的 `add-action` 建出来的动作 | 参数表是空的，`ActionDef.Parameters` 这一轮表达不了 | 参数表里没有一个能装键值对的位置。动作还没有真正的消费方（渲染与界面都不读它），先让动作能被增删与往返，等消费方开工时再补 |
| `diagram_edit` 的 `set-kind` | 图类型从 `value` 取，**不新开 `kind` 参数** | 一次标量写入，`value` 的含义正好对得上。新开一个 `kind` 参数会与 `add-action` 的动作类型名撞名，而两个 `kind` 的含义完全不同 |
| 工具层返回的命令产物 | 新增 `CommandOutcome`（版本、是否空操作、受影响标识、两个变更标志、一句话），不含坐标、不含改动后的内容 | 要看改动结果就再读一次摘要。把内容塞进每一次变更的返回里，改一次布局就会把整张图回灌一遍 |
| 幂等键的位置 | 不进参数表，是 `ToolRegistry.Invoke` 的一个可选参数；键与结果存在一张容量 128 的有界表里 | 它是**调用侧的属性**，与文档结构无关，进 IR 会让同一份文档在不同调用历史下得到不同的哈希。放进 schema 也不行：模型没有重试的概念，每次都要它编一个键 |
| §6.2 的样式白名单 | 在动作执行体里判，判据是**文档的调色板**；不在表里时把可用的令牌列出来 | 令牌表是文档里的数据，用户随时可以加一个，所以它进不了 schema 里那份写死的约束。只回一句「没有这个令牌」的话，模型只能猜着重试，而每次重试都是一轮往返 |
| `diagram_style` 的动作范围 | 七个动作都是节点级或文档级的；改边的样式走 `diagram_edit` 的 `set-edge-field` | 边上没有形状、没有文本样式，也没有令牌字段以外的样式入口。给 `diagram_style` 加边的话，它得先在节点与边之间猜一个，而那个判断本来就在调用方手里 |
| 动作表与命令清单的对照 | 文档里新增一张「工具 \| 动作 \| CommandId」的表，由 `Category=ToolDispatch` 逐行核 | 两边分头维护的话，加了一条命令而忘了挂动作，表现是模型调用时得到「未知动作」——而命令本身是好的，查起来要绕一大圈 |
| §9.6 的错误码呈现表与修复建议表 | 是**两张表、两个出口**，不是一张：`ErrorPresenterTable` 回答「怎么呈现给用户」，`RepairHints` 回答「给模型看什么、怎么改」 | 界面那一份在 `DuetDiagram.App`，而 App 不引用 `DuetDiagram.Llm`，反过来引就成了循环；放进 Core 也不行——建议里要出现 `diagram_read`、`create-page` 这些工具层与命令层才有的名字，塞进 Core 就是让纯数据层去认识上层。两张表都从错误码出发、各由一条逐行核对的用例兜住，风险挡在**覆盖**那一层：任何一边漏一个码都会红。`RepairHints` 比呈现表多覆盖一套码（工具层的参数校验），因为回灌给模型的失败两条来源都会出现 |
| 修复建议表里的「可用取值」 | 不进表，由失败现场填进错误自己的期望字段；表里只有「出错参数」与「一句建议」 | 可用的动作名、样式令牌、字段名、已登记的工具名都随文档与工具表变，静态表里写不出来，写死了就是错的。信封上的期望形式取失败现场那一份，取不到才回落到表里——顺序不能反过来 |
| 错误回环的上限 | 数的是**回灌次数**，不是调用次数；缺省三次。到上限时停下，并把整段往返记录写进应用日志 | 不设上限的话，一次参数错误会变成无限次调用，而账单上看得出来、日志里看不出来。记整段而不是最后一条：只看最后一条的话，读日志的人看不出模型撞了几次、撞在什么上。判「同一个错误又来了」只比**上一轮**，不比整段历史——中间换过做法之后又绕回原来那一步，那是另一件事 |
| 模型那条通路的工具调用循环 | 用依赖里现成的那一个，通过 `FunctionInvoker` 这个公开委托接进来；**不自己写循环，也不继承那个客户端类型** | 循环本身已经处理了多轮往返、并发调用、最大轮数与终止条件，重写一遍只会多出一份会漂移的实现。用公开委托而不是继承：继承会把自己的客户端类型钉死在别人的基类上，而那个基类里真正要用的只有一个受保护的虚方法，升一次大版本就可能改名 |
| 工具调用为什么回到注册表而不是直接调那个函数 | 执行体不用 `context.Function`，而是按名字走 `ToolRegistry.Invoke` | 那个函数把结果序列化成 JSON 才交出去，原始的失败结果出不来，而回环要的正是它。走注册表还有第二个好处：模型侧与代理侧从此是同一个入口，参数校验不会分叉 |
| 三层「最大次数」各管各的 | 依赖那两个（往返 40 次、连续失败 3 次）只管停；我们的回环（回灌 3 次）负责把失败翻成结构化内容、写日志、并用终止开关真正停下这一轮 | 合成一个数会让「停下来」与「告诉模型为什么」绑在一起，而两者要的东西不一样。只少回灌一次内容而不停的话，循环仍由依赖那几十次兜底，而日志里那句「停下了」就成了空话 |
| `ScriptedChatClient` 进产品工程 | 假客户端放在 `DuetDiagram.Llm/Chat` 里，不放测试工程 | 它把「工具调用往返」这件事的形状固定在一处，测试与任何离线宿主用的是同一份；放在测试工程里的话，宿主想离线跑一遍就得再写一个 |
| 工具层的错误码表多一个 `TOOL_RETRY_EXHAUSTED` | 到回环上限时回给模型的那一条用它，不复用某个参数错误 | 两者的处置相反：参数错误是改完参数再来，这一条是别再发同一个调用、把这件事报给人。复用的话，模型会照着参数错误的建议继续重试 |
| 两侧 schema 一致性有两条用例 | `Category=ToolRegistry` 那条按规范 JSON 比（容忍键序与空白），`Category=ToolParity` 那条按原文比 | 原文比较更强，能挡住「有人把两侧改成各自维护」之后出现在键序或空白上的差异——那种差异结构比较看不出来，而两边已经开始分叉 |
| §7.3 的会话初始化应答 | 应答里写的是**服务端会话建立那一刻**的状态，是一份握手快照；之后调用方按服务端指令里那句话自己读摘要拿当前版本 | 能力声明在会话建立之前就配好了，逐请求改它要给一个共享对象加可变状态；而这条传输下一个进程只服务一个会话，握手那一刻读到的就是当前值。报成「随时最新的版本」会与实现不符——调用方以为那个数字能用，实际上它只说明会话建立时服务端在哪一版 |
| 把工具挂到协议上的方式 | 挂的是**已经建好的工具实例**，不用按标注扫描静态方法那一套 | 标注那套要求参数由服务端容器解析，而这条通路上服务端不持有容器，非基础类型参数会被当成工具参数、调用直接失败——客户端只看到「调用出错」，原因只在服务端日志里。用实例还顺带保证代理侧与模型侧是同一份定义 |
| §7.4 的冲突返回策略 | 四行逐行实现，但**第四行刻意退成全量**，尽管逐条增量算得出来 | 走到那一步说明调用方声明的结构哈希与服务端对不上，而它的本地副本是否还与它自己声明的那个版本对得上，从这边无从验证。把字段级的增量按在一份来路不明的副本上，正是产生「静默改错」的方式——改完之后两边都不报错，而那份图已经与谁都对不上。逐条增量仍然留给界面那条通路，它手里的副本是本地的 |
| §7.4「未声明 clientState → FullSnapshot」 | 网络那一档按它做（409 加全量快照）；标准输入输出那一档仍然回一个不带内容的 `EXPECTED_VERSION_REQUIRED` | 那条通路没有状态码，回不出「409 加一份内容」这个形状；而协议内的工具结果里塞一份全量快照，会让每一条被拒的写入都往模型上下文里灌一整张图。它那边的做法是拿到码之后自己读一次图 |
| §7.5 的三层并发 | 乐观并发与图层级权限都接上了；**软锁只到「工作区上挂一把、宿主自己拿」**，没有接到某条通路上 | 网络那一档是无状态的，服务端不在请求之间记任何东西，按主体的锁在那里没有落点；接到界面上又会被进程锁管着。锁本身与它的判据都验过了，缺的是一个真的需要它的宿主 |
| §7.2「图层级 ACL 绑定认证主体」 | 绑主体做到了，但**配置入口只有 `HttpHostOptions.Tokens`**，命令行那条 `--token` 还表达不了图层 | 它的形态是 `名字:权限档:凭据`，而凭据本身可能带冒号——再切一刀就会把凭据切坏，而那种坏法只在真正连上来的时候才显形 |
| 冲突判定的位置 | 网络那一档在中间件里**提前判一次**，为的是给出 409 那个状态码；命令总线那一道仍然是说了算的那一道 | 工具调用的失败是协议内的结果，状态码那一层看不见它，所以「409 + 差异」只有在协议之前判才做得出来。提前判完之后到命令真正执行之间还有个窗口，那个窗口里的冲突由总线在门锁内挡下，形状是工具结果里的错误码 |
| §7.5「文档软锁 TTL 30 秒」 | 判据是**最后一次操作**，不是拿到锁的时刻；续期只在没过期时成功 | 只看拿锁时刻的话，一个每二十九秒动一次的 agent 会被判成过期，而它其实正在写——于是两个 agent 同时以为自己在改这份文档。续期在过期之后放行的话，同一个后果 |
| §7.2「授权」的粒度 | 权限分两档判在两处：粗的那一档（能不能改）在传输层，细的那一档（能改哪些图层）在工具层 | 粗的那一档判据是凭据的权限档，只有传输层拿得到；细的那一档判据是"这次写入点名了哪个图层"，藏在动作参数里（改节点归属时写在 `value` 上，建图层与改图层时写在 `id` 上）。把细的也搬到传输层的话，那张对照表要抄一份，而抄漏的那一格会静默放行 |
| 服务端自己的状态挂在哪 | 挂在 `ServerCapabilities.Extensions`，不用协议给自定义字段留的元数据位 | 那个元数据位在当前协议版本下没有入口。扩展位两端都能按类型直接读到，客户端不需要挂拦截器去看原始消息 |
| 会话状态怎么读 | 从**每条请求**现读，服务端不保存任何会话上下文 | 无状态传输下服务端不持有服务容器，保存上下文的话，换个实例、重启一次，同一个调用就得到不同结果。逐条重写还包括「这次没声明」那一种：留着上一条的值会变成一次照着别人的版本改的写入 |
| `DiagramToolContext.ExpectedVersion` 是委托而不是值 | 类型是 `Func<VersionCheckRequest?>`，由传输层填 | 它随每条请求变，而工具是按会话建一次、之后一直复用的。存一个值的话，第二次调用会拿着第一次声明的版本去比对，表现是「第一次改得动、第二次改不动」，且两边都不报错 |
| 声明与应答共用扩展位但**字段名刻意不同** | 调用方报的版本叫 `hasVersion`，服务端报的版本叫 `serverVersion` | 同名的话，一份应答被当成声明读回来时不会被发现——而那条路会拿着服务端的版本当成自己看到的版本去比对，恰好总是相等，版本检查于是静默失效 |
| §7.1「HTTP long-polling + WebSocket」 | 传输用依赖自带的流式 HTTP；**服务端主动推送做成一条独立的变化源端点**（长轮询），不走协议那条路 | 协议那条路在当前版本下走不通：无状态模式明文写着服务端主动发的消息与所有服务端发起的请求都不支持（响应可能落到另一个进程上），而有状态那条路上唯一能拿到会话对象的入口是实验性接口、它的空闲超时又被标为过时（而本仓把警告当错误）。变化源端点是普通的只读端点，形状仍是长轮询 |
| §7.1 的 linked CTS 长轮询超时 | 变化源那条端点用「等信号加一个带截止的取消令牌」实现，没有照抄 linked CTS 的写法 | 那是自建长轮询传输时代的产物。传输已经由依赖提供，这里要的是「等一会儿看有没有变化」，用带截止的令牌表达更直接，也不会在每次唤醒时留下一个没人观察的定时任务 |
| §7.2 的「主动推送」 | 只保存**最新的一份**状态，不保存通知历史 | 断线重连按版本号补差：调用方报上它见过的最后一版，服务端要么立刻回当前版本、要么等。存历史的话，一个断了一小时的调用方会把这一小时里的每一条都收一遍，而它真正需要的只是「现在是第几版、结构哈希是什么」 |
| §7.2 的认证与授权 | 凭据表在启动时给出，权限档三档；越权的判据是「这个工具改不改得动文档」 | §7.5 还写了图层级 ACL，那要等图层能被改——现在还没有那条命令。中间那一档此刻与最高档同义，留着是为了让凭据表能表达这个意图，等有了只给最高档留的入口时改判据而不是改所有凭据的写法 |
| 越权怎么判 | 中间件读一次请求体拿工具名，读完复位 | 工具名只在请求体里。不复位的话协议那一层拿到的是一个已经读空的体，表现是「调用进去了、参数全丢了」。读不出来一律当「不是写调用」——当「是」的话，一个畸形请求会拿到 403 而不是它真正该拿的那个错误 |
| §7.2 的「单次调用 30s 超时」 | 预算在**发命令之前**检查，超了就直接回，一条命令都不发 | 命令是同步的，中途取消观察不到。能给的保证是「不会回一个超时响应、而那条命令还在后台把它改完」——那比放行还糟，因为调用方以为没改成 |
| 新增三个错误码 | `MCP_FORBIDDEN`、`MCP_PATH_ESCAPED`、`MCP_TIMEOUT` 进 `ErrorCodes`，与已有的 `MCP_UNAUTHORIZED`、`MCP_RATE_LIMITED` 同一族 | 四种拒因要的处置完全不同（换凭据、换权限更高的凭据、退避重试、改路径），合成一个「拒绝」的话代理侧没法区分。`MCP_TIMEOUT` 也不是工具层的参数错误：它说的是这一次调用没时间了，处置是把活拆小 |
| 包版本表删掉两项 | `Microsoft.Extensions.Hosting` 与 `Microsoft.Extensions.Logging.Console` 的登记删了 | 网络那一档引入共享框架之后这两个包由框架提供，显式引用会触发「这个包不需要」的编译期错误。那份清单自己的规则是「登记项必须有人用」，留着就多两条没有用户的条目 |
| 两条传输共用一份会话 | 抽出 `SessionCore`，标准输入输出与 HTTP 都从它取文档、总线与声明 | 不抽的话，两份实现会在「每条请求都重写声明，包括这次没声明那一种」上分叉，而那条性质错了两边都不报错 |
