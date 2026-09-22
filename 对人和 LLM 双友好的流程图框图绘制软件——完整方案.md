# 对人和 LLM 双友好的流程图/框图绘制软件——完整方案

## 一、设计哲学

**统一操作模型**：人和 LLM 通过同一套命令接口操作同一份文档，能力完全对等。GUI 能做的，LLM 通过工具都能做；LLM 能表达的，GUI 都有对应入口。区别只在操作来源标记和可选的冲突策略。

**语义与视觉分离**：内部表示（IR）是唯一事实源，只存语义和布局意图；Sidecar 拆分 `layout.json`（可重建）和 `user.json`（不可丢）；DSL 是 LLM 的可选序列化视图。布局引擎负责把语义求解成视觉结果，人工拖动作为约束反馈。

**尽量少造轮子**：手写只有 DSL 解析器和命令总线。其余全部使用当前活跃、新版本、AOT 兼容的第三方库。

**面向 Agent 执行**：任务原子化、依赖显式化、验收自动化、上下文自包含。人类只写任务描述、审 PR、做决策，不写代码。

**先验证后开发**：Phase 0 共 3.5 周技术验证，锁定依赖版本与 AOT 能力，避免后期返工。


## 二、技术选型

| 层次 | 主选 | 精确版本 | 备选 |
|---|---|---|---|
| UI 框架 | Avalonia | 12.1.2 | Avalonia 11 LTS |
| 渲染 | SkiaSharp | 4.148.0 | — |
| 布局引擎（主） | Mermaider 内置 Sugiyama | 0.12.2 | Mostlylucid.Dagre 2.0.1 |
| 布局引擎（备） | Mostlylucid.Dagre | 2.0.1 | MSAGL 可选包 |
| LLM 集成 | Microsoft.Extensions.AI | 10.7.0 | 直接调 OpenAI SDK |
| MCP Server | ModelContextProtocol | 2.2.0 | 自实现 JSON-RPC |
| PDF 导出 | QuestPDF / PdfSharpCore / 位图嵌入 | Phase 4 验证 | — |
| Skill 机制 | SEP-2640 Skills Extension | Final | — |

**版本锁定**：`Directory.Packages.props` 全部精确版本，不允许通配。

**Phase 0 验证清单**：NuGet 版本核查、Avalonia + SkiaSharp AOT 最小应用、Mermaider Sugiyama 约束支持验证、Mostlylucid.Dagre 复合图 + AOT、ModelContextProtocol 传输与 initialize 自定义字段验证、性能基线、备选方案评估。


## 三、整体架构

**入口层**包含五个入口：GUI 画布、GUI 属性面板、内部 LLM、外部 MCP Agent、文件导入导出。

**核心层**统一处理：IR 内部表示、命令总线（同步 + 异步 + 内部锁 + 嵌套检测）、版本日志、审计日志、历史栈、会话提供者、变更广播器、版本检查请求、校验器、冲突策略、Sidecar 管理、Mermaid 导入导出。

**服务层**包含：布局引擎（四级降级 + 路径预算 + 级别唯一性 + 携带尝试记录）、文本测量、上下文摘要生成器。

**渲染层**包含：Avalonia Canvas 渲染、SkiaSharp 文本与图形、四叉树视口虚拟化、导出（SVG/PNG/PDF）。

**两条数据通路**：人工通路从 GUI 到命令再到 IR/Sidecar、布局、渲染；LLM 通路从 Tool Call 或 DSL 到命令再到同样后续。两条通路在命令接口处汇合，后续完全一致。


## 四、核心数据模型

### 4.1 内部表示（IR，语义层）

**DiagramDocument（根对象）**：

- Id：文档唯一标识，GUID 字符串
- Kind：图类型（flow / flowchart / block / state）
- Direction：主方向（LR / TB / RL / BT），是方向的唯一来源
- Version：版本号，仅命令总线可递增
- StructuralHash：结构哈希，仅命令总线可更新
- VisualHash：视觉哈希，仅命令总线可更新
- 九个集合：Pages、Layers、Nodes、Edges、Composites、Tags、Actions、Fonts、TextPresets，全部对外只读，修改只能通过命令
- 三个子对象：Palette、Layout、Canvas
- 三个方法：TakeFullSnapshot、RestoreFromSnapshot、ApplySnapshot

**节点（NodeDef）**：Id、Label、Shape、Parent、Layer、StyleToken、Style（NodeStyle）、Text（TextStyle）、Ports（端口列表）、RichText、MathMode、Desc、Meta 字典。

**文本样式（TextStyle）**：FontFamily、FontSize、FontWeight、Italic、Underline、Strikethrough、FontColor、TextBackgroundColor、TextAlign、VerticalAlign、WordWrap、LineHeight、TextIndent、Padding、LabelPosition、WritingDirection。

**边（EdgeDef）**：Id、From、To、FromPort、ToPort、Label、Style（EdgeStyle）。

**边样式（EdgeStyle）**：Line、Arrow、Color、Weight、Route、LabelPos、StyleToken。

**节点样式（NodeStyle）**：Fill、Stroke、Text、Border、Weight、Radius、Opacity、Badge。

**端口（PortDef）**：Name、Side、Offset、IsCustom。

**组合定义（CompositeDef）** 是抽象基类，派生 GroupDef、LaneDef、SubflowDef、ComboDef，各自包含 Members 列表、Direction、Collapsed、Style、LocalLayout。

**TagDef**：Id、Members。

**ActionDef**：Id、Event、Kind、Target、Parameters。

**FontDef**：Name、Path、IsMono。

**TextStylePreset**：Name、Style。

**Palette**：Entries 字典（令牌名到调色板条目）。

**CanvasSettings**：Grid、GridSize、PageSize、Orientation、Background、Infinite。

### 4.2 布局约束

**ConstraintOwner 枚举**：Auto（引擎自动推导）、Llm、Human。

**Constraint 泛型记录**：Value（约束值）、Owner（归属）、CreatedAt（创建时间）。

**LayoutHints（布局提示）**：

- NodeSpacing（默认 40）、LayerSpacing（默认 70）
- SameRank 列表（同层约束）
- Order 列表（层内顺序约束）
- Align 列表（对齐约束）
- Place 列表（相对位置约束）
- 六个方法：GetSameRank、GetOrder、GetAlign、GetPlace（按 Owner 过滤）、HasAny（是否有某归属的约束）、Without（排除指定归属，返回新实例）

**LayoutHintsDefaults** 只提供 Create 方法（返回新实例），**不提供可变单例**。

### 4.3 Sidecar 拆分

三个文件与文档同名同目录：

- `.dsl`：语义文本，LLM 纯文本模式的入口，人工不直接接触
- `.layout.json`：自动布局结果，可丢弃、可重算
- `.user.json`：人工产物，包含 pinnedNodes（id 到位置的映射）、pinnedEdges（边 id 到 waypoints 的映射）、customPorts（节点 id 到端口列表的映射），不可丢

备份按时间戳命名：`user.{yyyyMMdd-HHmmss}.bak`，保留最近 10 个或 30 天。清理时机：启动时、保存时、每小时定时。

**加载转换**：`user.json` 的 pinnedNodes 在加载时转换为只读字典。键大小写敏感（与 IR 中 id 一致）；缺失坐标的条目视为孤儿，移入 orphans 区。转换细节见 `docs/IR-Schema.md`。

### 4.4 命令与结果

**ChangeSource 枚举**：Human、Llm、System、Import、Mcp、Undo、Redo。Source 由工具层设置，命令总线不修改（Undo/Redo 除外）。

**ChangeContext 记录**：Source、ActorId、SessionId、Reason、Timestamp（仅命令总线可填充）。

**ISessionProvider 接口**：CurrentActorId、CurrentSessionId。SessionId 格式：`gui:{windowId}`、`mcp:{tokenId}:{sessionId}`、`llm:{conversationId}`、`import:{filename}`。

**IDiagramCommand 接口**：CommandId、Context、Validate、Apply、Undo、CaptureMemento、RestoreMemento、WithContext。

**DiagramCommandBase 抽象基类**：CommandId 通过构造函数注入，Context 私有 set，WithContext 调用抽象 CloneWith。

**CommandMemento 抽象记录**：InverseChanges、AffectedIds。每个命令定义 sealed 派生类型（如 AddNodeMemento 包含 NodeDef 和 PreviousIndex）。通过多态序列化注册所有派生类型。

**CommandResult 记录** 包含 8 个字段：IsSuccess、IsNoOp、Message、Errors、AffectedIds、FieldChanges、StructuralChanged、VisualChanged。

派生属性：IsEffectiveSuccess（成功且非空操作）、IsRetryable（错误码为 VERSION_CONFLICT 或 MCP_RATE_LIMITED）。

四个工厂方法：

- Ok：成功，可传 affected、changes、structural、visual、message
- NoOp：空操作，IsSuccess=true 且 IsNoOp=true
- Fail：失败，只传 errors
- FailWith：失败，带 message 和 errors
- Conflict：根据 DiffResult 类型分发，Invalid 时返回 INVALID_EXPECTED_VERSION，其他返回 VERSION_CONFLICT

**CommandError 记录**：Code、Payload（可选对象）。

**ValidationResult 记录**：IsValid、Errors（CommandError 列表）。

**FieldChange 记录**：ElementId、Field、OldValue、NewValue、Kind。

**ChangeKind 枚举**：Added、Modified、Removed、Moved、Resized。

**PositionValue / SizeValue** 记录，用于 FieldChange 的值序列化。

### 4.5 版本日志

**VersionLogLimits**：MaxEntries=100、BulkChangeThreshold=100、MaxAffectedPerEntry=200、MaxTotalChangesForDiff=500。

**VersionLog** 内部维护容量 100 的队列。

**Record 方法**：判断 IsBulkChange（Changes 超 100 或 AffectedIds 超 200），超限时裁剪 Changes 和 AffectedIds 为空并记录原始计数，然后入队，超出容量时出队。

**BuildDiff 方法** 按以下顺序判断：

1. from == to 返回 Empty
2. from > to 返回 Invalid
3. 队列空返回 FullSnapshot
4. from 小于队列最早版本返回 FullSnapshot
5. 过滤出范围内容，空则返回 Empty
6. 调用 ExceedsThreshold 检查是否超阈值（已包含 IsBulkChange 检查），超则返回 FullSnapshot
7. 若 clientStructuralHash 非空且等于当前 StructuralHash，返回 Reference（包含 Version、BaseVersion、StructuralHash、AffectedIds）
8. 否则返回 Entries

**ExceedsThreshold 辅助方法**：累加 OriginalChangeCount，遇到 IsBulkChange 或累计超阈值则返回 true。

**VersionEntry 记录**：Version、CommandId、Source、ActorId、SessionId、Timestamp、AffectedIds、Changes、IsBulkChange、OriginalChangeCount。

**DiffResult 抽象记录** 通过多态序列化注册五种派生：Empty、Invalid、FullSnapshot（Version + FullJson）、Reference（Version + BaseVersion + StructuralHash + AffectedIds）、Entries（列表）。

### 4.6 审计日志

**AuditKind 枚举**：Executed、Failed、Rejected、Cancelled、Undone、Redone。

**AuditEntry 记录**：CommandId、Source、ActorId、SessionId、Timestamp、Reason、Kind、Errors（可选）。

**AuditLog**：容量 1000，用链表存储。Record 方法入队并裁剪。Recent(count) 从尾部回溯，最多取 count 条，反向后返回。

### 4.7 历史栈

**HistoryEntry 记录**：Command、Memento、Result、Timestamp、SessionId。

**HistoryStack**：容量 500，用两个链表（撤销栈和重做栈）。

方法：

- UndoCount / RedoCount：栈大小
- PeekUndo / PeekRedo：查看栈顶
- UndoEntries / RedoEntries：返回快照列表
- PushUndo：压入撤销栈，超容量时移除最旧
- TryPopUndo：弹出撤销栈
- PushRedo / TryPopRedo：重做栈同理
- ClearRedo：清空重做栈（新命令执行时调用）
- Clear：清空两个栈（**仅用于同一文档重新加载**，不重置 Version；打开新文档应创建新的 DiagramDocument 和 CommandBus）

### 4.8 变更广播

**ChangeNotification 记录**：DocumentId、Version、AffectedIds、Source、Timestamp。

**Timestamp 用途**：仅用于日志和审计，**不用于排序**。排序统一用单调递增的 Version（多进程场景时钟可能不同步）。

**IChangeBroadcaster 接口** 继承 IAsyncDisposable：Enqueue（非阻塞写入）、Subscribe（返回 IDisposable 用于取消订阅）。

**InProcessBroadcaster 实现**：

- 内部使用**有界** Channel（容量 1024，满时 DropOldest）
- ConcurrentDictionary 存储订阅者
- 构造函数启动后台分发任务
- Enqueue 通过 TryWrite 写入 Channel
- Subscribe 分配 GUID 并加入字典，返回可取消订阅的句柄
- 分发循环遍历订阅者并调用，单个订阅者异常不影响其他
- **DisposeAsync 等待分发任务结束，超时 2 秒**

**NullChangeBroadcaster** 提供 Instance 单例，Enqueue 和 Subscribe 为空实现。用于 GUI 单窗口或导入场景。

### 4.9 命令总线

**VersionCheckRequest 记录**：ClientVersion、ClientStructuralHash。

**DiagramCommandBusOptions**：

- RequiresVersionCheck（私有 init）
- 三个工厂：ForGui（false）、ForMcp（true）、ForImport（false）

**DiagramCommandBusContext** 封装 11 项依赖：Document、Sidecar、History、VersionLog、AuditLog、Layout、Renderer、Session、Broadcaster、Clock、Options。

**DiagramCommandBus** 实现 IDisposable：

字段：内部 Context、SemaphoreSlim 门锁、AsyncLocal 命令深度计数器。

构造函数接收 Context，**校验 MCP 模式必须有非 NullChangeBroadcaster 的广播器**（用 `is NullChangeBroadcaster` 判据，不用 null 判据）。

暴露 Context 属性供 Workspace 访问。

**Execute（同步）** 流程：命令深度大于 0 抛异常 → 获取门锁 → 递增深度 → ExecuteCore → 递减深度 → 释放门锁。

**ExecuteAsync（异步）** 同理，用 await WaitAsync 支持取消。

**ExecuteCore** 流程：

1. 若 RequiresVersionCheck，检查 check 是否为空（空则返回 EXPECTED_VERSION_REQUIRED），检查 ClientVersion 是否匹配（不匹配则调用 BuildDiff 返回 Conflict）
2. 获取当前时间作为 timestamp
3. 用 with 表达式为 Context 填充 Timestamp
4. 通过 ResolveSessionId 解析 SessionId（优先 ctx.SessionId，为空时 fallback 到 Session.CurrentSessionId；**两者不一致时记录警告日志**）
5. 调用 WithContext 得到规范化命令
6. 调用 Validate 校验，失败时记 AuditLog.Rejected 并返回 FailWith
7. 调用 CaptureMemento 捕获逆变更
8. 在 try 块内调用 Apply，失败时恢复 memento、记 AuditLog.Failed、返回 result
9. 成功后递增 Version、更新哈希
10. 记 VersionLog、AuditLog.Executed
11. 推入历史栈、清空重做栈
12. 结构变更触发重布局，否则只触发重绘
13. 通过 Broadcaster.Enqueue 广播变更
14. catch OperationCanceledException 时恢复 memento、记 AuditLog.Cancelled、重新抛出
15. catch Exception 时恢复 memento、记 AuditLog.Failed（**不携带异常类型名**，仅记录 INTERNAL_ERROR 错误码）、重新抛出

**Undo** 流程：

1. 获取门锁
2. 从历史栈弹出撤销条目，空则返回 NoOp
3. 从 Session 获取 actorId 和 sessionId
4. 用 RestoreMemento 恢复
5. 递增 Version、更新哈希
6. 记 VersionLog（Source=Undo，用 Memento 的 AffectedIds 和 InverseChanges）
7. 记 AuditLog（**Kind=Undone**，Reason 为 "undo of {原CommandId}"）
8. 推入重做栈
9. 结构变更触发重布局，否则重绘
10. 广播变更
11. 返回 Ok，携带 affected、changes、structural、visual

**Redo** 流程：

1. 获取门锁
2. 弹出重做条目，空则返回 NoOp
3. 重新捕获 memento
4. 调用 Apply，**失败时恢复 memento、恢复重做栈、记 AuditLog.Failed、返回 result**（与 Execute 对称的原子性保护）
5. 成功后递增 Version、更新哈希
6. 记 VersionLog（Source=Redo）
7. 记 AuditLog（**Kind=Redone**）
8. 用新 timestamp 推入撤销栈
9. 结构变更触发重布局，否则重绘
10. 广播变更

**UpdateHashes** 私有方法：结构变更时重算 StructuralHash，视觉或结构变更时重算 VisualHash。Phase 1 用全量计算，Phase 5 若性能不足再优化为增量（Merkle 树）。

**嵌套 Execute 约束**：命令内部**不得**异步调用 Execute。AsyncLocal 在 Task.Run 中会传播，导致误报。若确实需要异步触发命令，由宿主在命令外调度。

**Dispose** 释放门锁。

### 4.10 工作区

**DiagramWorkspace** 实现 IAsyncDisposable：

- Document 属性
- CommandBus 属性
- Broadcaster 属性（从 CommandBus.Context 读取）
- 构造函数接收 Context 和 **ownsBroadcaster 标志**
- DisposeAsync：释放 CommandBus；**仅当 ownsBroadcaster 为 true 时释放 Broadcaster**（避免多个 Workspace 共享 broadcaster 时互相破坏）

**推荐用法**：宿主创建 broadcaster 和 Context，传入 `ownsBroadcaster: false`；或 Workspace 工厂创建 broadcaster 并传入 `ownsBroadcaster: true`。


## 五、布局引擎

### 5.1 类型拆分

**EngineLayoutResult 记录**（引擎返回）：Nodes、Edges、Conflicts。

**LayoutResult 记录**（协调器返回）：Nodes、Edges、Conflicts、AppliedLevel、Attempts。

**ILayoutEngine 接口**：Layout 方法返回 EngineLayoutResult。

**LayoutCoordinator.Compute** 包装引擎结果为 LayoutResult，填充 AppliedLevel 和 Attempts。

### 5.2 四级降级 + 路径预算 + 级别唯一性

**LayoutFallbackLevel 枚举**：Full、DropLlm、DropAll、PureAuto。

**LayoutPlan 记录** 用不可变数组存储（级别，预算）元组列表。Default 提供四级默认预算：300ms、200ms、150ms、150ms。

**LayoutAttempt 记录**：Level、TimedOut、Elapsed、Conflicts。

**LayoutFailurePayload 记录**：Attempts（**不含 AnyAttempted**，调用方用 Attempts.Count 判断）。

**LayoutFailedException** 携带 CommandError。

**LayoutCoordinator**：

- 默认总预算 800ms
- 构造函数注入引擎、冲突日志、时间提供者、可选的 LayoutPlan
- Compute 方法：
  1. 计算截止时间
  2. 遍历计划中的级别
  3. 剩余时间不足则 break
  4. 若级别为 DropLlm 且文档无 LLM 约束，跳过
  5. 调用 ApplyFallback 生成布局输入
  6. 用剩余时间和级别预算的较小值创建 CTS
  7. 调用引擎布局
  8. 成功时记录 LayoutAttempt（TimedOut=false），降级时记冲突日志，返回携带 AppliedLevel 和 Attempts 的 LayoutResult
  9. 超时记录 LayoutAttempt（TimedOut=true），继续下一级
  10. 全部失败抛 LayoutFailedException

**路径预算**：拖拽中不调用；拖拽松手 200ms；结构变更 800ms；手动重布局 2s。

**保留项矩阵**：

| 级别 | Direction | Pinned | Spacing | Human 约束 | Llm 约束 | Auto 约束 | Nudge |
|---|---|---|---|---|---|---|---|
| Full | 保留 | 保留 | 保留 | 保留 | 保留 | 保留 | 保留 |
| DropLlm | 保留 | 保留 | 保留 | 保留 | 丢弃 | 保留 | 保留 |
| DropAll | 保留 | 保留 | 保留 | 丢弃 | 丢弃 | 丢弃 | 丢弃 |
| PureAuto | 保留 | 丢弃 | 引擎默认 | 丢弃 | 丢弃 | 丢弃 | 丢弃 |

### 5.3 ApplyFallback

**LayoutSidecar** 内部持有 `IReadOnlyDictionary<string, PinnedPosition>`。

**ApplyFallback 方法**：

1. 根据级别选择 LayoutHints：Full 用原样、DropLlm 用 Without(Llm)、DropAll 用 Without(Llm, Human, Auto)、PureAuto 用 LayoutHintsDefaults.Create()
2. PureAuto 时 pinned 为空字典，否则用原字典
3. 返回 LayoutInput

**LayoutInput 记录**：Document、Hints、PinnedPositions、Level。

### 5.4 GUI 处理布局失败

保留上一次成功布局；状态栏红色提示；三选项：重试（更长预算）、手动布局模式、简化图（提示冲突的约束）。


## 六、LLM 集成

### 6.1 八个粗粒度工具

| 工具 | 覆盖能力 | 确定性逻辑 |
|---|---|---|
| diagram_read | 读取当前图 | 返回归一化摘要，不注入原始坐标 |
| diagram_edit | 所有结构编辑 | 内部按 action 分发到 40+ 命令 |
| diagram_style | 所有样式编辑 | 内部展开调色板、校验白名单 |
| diagram_layout | 所有布局操作 | 内部解析端口、处理 pin、调度重布局 |
| diagram_composite | 组合操作 | 分组/泳道/子流程/组合节点 |
| diagram_export | 导出 | SVG/PNG/PDF/DSL 统一入口 |
| diagram_validate | 校验 | 返回结构化错误 + 修复建议 |
| diagram_undo_redo | 撤销重做 | 混合人和 LLM 的历史栈 |

**工具描述用 JSON Schema 的 pattern 硬约束**（如节点 ID 必须匹配 `^[a-z0-9-]+$`），不只靠文字说明。

### 6.2 确定性增强措施

结构化输入校验、内部状态机、默认值填充、端口自动解析、样式白名单、原子性（一个工具调用要么全部成功要么全部回滚）、幂等键。

### 6.3 工具定义共用

**ToolDescriptor 记录**：Name、Description、Parameters（JsonSchema）、Handler（异步委托，接收 JsonElement 和 CancellationToken，返回 Task<ToolResult>）。

**ToolRegistry 类** 提供 Tools 列表、ToMeaiFunctions（转为 MEAI 的 AIFunction 数组）、ToMcpTools（转为 MCP 的 McpServerTool 数组）。Phase 0 P0-09 验证可行性，不可行则退化为命令层共用 + 工具定义各自维护。

Handler 异步仅用于 IO（日志、网络、外部 LLM），命令执行本身通过 ExecuteAsync。

### 6.4 上下文注入

每次调用 LLM 前生成归一化布局摘要，只注入决策所需的最小信息：

- 节点列表：id、label、形状、样式令牌
- 边列表：from→to、label、线型
- 布局摘要：方向、层数、同层分组
- 锁定列表：被 pin 的节点
- 可用样式令牌
- 最近修改（谁、什么时候、改了什么）

不注入原始坐标、sidecar 或完整 DSL。坐标用"第几层第几列"量化表达。

### 6.5 LLM 上下文摘要示例

```
图状态：
- 节点：start(开始), check(校验,diamond), pass(成功), fail(失败)
- 边：start→check, check→pass"是", check→fail"否"
- 布局：LR, 4层, pass/fail同层
- 锁定：无
- 可用样式：primary, success, warning, danger, muted
- 最近修改：fail.style = danger (human, 2分钟前)
```


## 七、MCP Server

### 7.1 传输

- stdio：本地 CLI agent（Codex CLI / Claude Code）
- HTTP long-polling + WebSocket：远程 agent

**long-polling 超时处理**：创建 linked CTS，同时启动 30 秒延迟任务和命令执行任务，先完成者胜出。超时则取消命令执行，返回空响应。

### 7.2 安全设计

| 层面 | 措施 |
|---|---|
| 传输 | HTTPS（TLS 1.3） |
| 认证 | Bearer Token，最小权限，支持轮换 |
| 授权 | 图层级 ACL 绑定认证主体 |
| 速率限制 | 每 token 每分钟 N 次，超限 429 |
| 审计 | AuditLog + 调用记录，日志脱敏 |
| 输入校验 | JSON Schema 硬约束 |
| 工作区隔离 | --workspace 限定，路径逃逸检测 |
| 资源限制 | 单次调用 30s 超时，内存上限 |

### 7.3 会话初始化与 clientState

**initialize 请求**携带 protocolVersion、clientInfo、clientState（含 documentId、hasVersion、structuralHash）。

**initialize 响应**返回 clientStateAck（含 accepted、serverVersion、serverStructuralHash）。

**accepted=false 时的 reason 及客户端行为**：

| reason | 客户端行为 |
|---|---|
| document_not_found | 放弃会话，提示用户 |
| version_too_old | 清空本地状态，请求 FullSnapshot，重新 initialize |
| hash_mismatch | 同 version_too_old |

**工具调用**可覆盖 clientState，MCP 层转换为 VersionCheckRequest 传给命令总线。

### 7.4 冲突返回策略

| 条件 | 返回 |
|---|---|
| Agent 未声明 clientState | FullSnapshot |
| hasVersion 等于 currentVersion | Empty |
| hasVersion 在版本日志范围且 hash 匹配 | Reference |
| hasVersion 超范围或 hash 不匹配 | FullSnapshot |

### 7.5 并发三层机制

| 层级 | 机制 | 粒度 |
|---|---|---|
| 乐观并发 | VersionCheckRequest + RequiresVersionCheck 开关 | 文档级 |
| 文档软锁 | TTL 30 秒，无操作自动释放 | 文档级 |
| 权限分级 | read / edit / full + 图层级 | 文档级 + 图层级 |

### 7.6 与内部 LLM 统一

内部 LLM 和外部 agent 共用同一份 ToolRegistry，ChatOptions.Tools 和 WithTools() 引用同一来源。工具描述、参数 schema、错误格式全部一致。


## 八、Skill 机制

两个 Skill 存于 `Diagram.Mcp/Skills/`：

- `diagram-workflow`：操作顺序、常见错误、最佳实践
- `diagram-syntax`：DSL 完整语法和示例

**渐进式披露三层**：

| 层级 | 内容 | 加载时机 | 覆盖率 |
|---|---|---|---|
| 工具描述 | 一句话做什么 + 何时用 + 限制 | 始终在上下文 | 约 80% 场景 |
| Skill 正文 | 操作顺序 + 常见错误 | 任务匹配时 | 约 15% 场景 |
| 资源文件 | 精确语法 + 罕见形状 | 需要时 | 约 5% 场景 |

遵循 SEP-2640 Skills Extension，通过 `skill://` URI 暴露。


## 九、GUI 设计

### 9.1 布局

```
┌─────────────────────────────────────────────────────┐
│ 菜单栏  │ 工具栏（选择/连线/形状/对齐/布局/导出）    │
├─────────┼──────────────────────┬──────────────────┤
│  图层    │                      │   属性面板        │
│  面板    │      画布             │   ├ 形状         │
│         │    (Canvas)           │   ├ 样式/调色板   │
│  ────   │                      │   ├ 文本/字体      │
│  页面    │                      │   ├ 布局约束       │
│  标签栏  │                      │   ├ 端口          │
│         │                      │   └ 动作/链接      │
├─────────┴──────────────────────┴──────────────────┤
│ 状态栏（坐标/缩放/变更提示/Agent 活动指示）         │
└─────────────────────────────────────────────────────┘
```

### 9.2 核心交互

| 操作 | 行为 |
|---|---|
| 拖节点 | 更新 sidecar，松手后 pin |
| 框选右键 | 创建分组 / 泳道 / 子流程 |
| 从端口拖出 | 连线，自动记录 fromPort |
| 拖边端点 | 重连到新端口 |
| 拖边中间点 | 添加 waypoints |
| 双击节点 | 编辑标签（富文本模式） |
| 右键节点 | 编辑链接、添加动作、pin/unpin、图层归属 |
| Ctrl+Z | 撤销（混合人和 LLM 的操作） |

### 9.3 变更高亮（非颜色化）

| 手段 | 说明 |
|---|---|
| 脉冲动画 | 元素边框 1.5 秒脉冲 3 次，不改变填充色 |
| 角标 | 元素右上角小圆点（人工蓝、LLM 紫、冲突橙） |
| 虚线轮廓 | 变更元素外部叠加虚线轮廓 |
| 边栏 diff | 右下角折叠面板，默认摘要，点击展开详情 |

**Undo/Redo 高亮**：继承原命令的 Source 颜色，加 ↶ 或 ↷ 符号。

### 9.4 布局失败处理

保留上一次成功布局；状态栏红色提示；三选项（重试 / 手动布局 / 简化图）。

### 9.5 多窗口

**单进程多窗口**：共享 DiagramWorkspace，通过 InProcessBroadcaster 订阅刷新，无版本冲突。

**多进程**：文档级锁文件（FileShare.None）+ 心跳文件（FileShare.ReadWrite，每 5 秒写入）；超时 15 秒判定崩溃可抢占；第二实例只读 + 状态栏提示。

### 9.6 错误码 GUI 处理

| 错误码 | GUI 处理 |
|---|---|
| DUPLICATE_ID | 高亮冲突 ID |
| EDGE_TARGET_MISSING | 提示"是否创建？" |
| VERSION_CONFLICT | 弹可视化 diff 对话框 |
| INVALID_EXPECTED_VERSION | 状态栏提示"内部错误，已记录日志" |
| EXPECTED_VERSION_REQUIRED | 状态栏提示 |
| LAYOUT_ALL_LEVELS_TIMEOUT | 状态栏红色提示 + 三选项 |
| MCP_UNAUTHORIZED | 提示"认证失败" |
| MCP_RATE_LIMITED | 状态栏提示"请求过于频繁" |

**NoOp**：状态栏灰色显示（如"无可撤销"），不弹窗。

### 9.7 用户测试

12-15 人，交叉设计，定量 + 定性。

**统计四条判定**：Friedman 检验 p < 0.05；Wilcoxon + Holm-Bonferroni 校正 p < 0.05/3；Kendall's W ≥ 0.3；平均排序分 ≥ 4/5。至少三条满足；两条时由 3 人评审组（1 设计师 + 1 工程师 + 1 产品）多数决，成员不得参与方案设计。


## 十、性能策略

四叉树视口虚拟化 + 100px 预取。动态阈值切换（默认 500 节点），切换不瞬时——先预计算 immediate-mode 绘制列表，下一帧切换。性能诊断面板 Ctrl+Shift+P。

**性能目标（基于 Phase 0 基线）**：

| 指标 | Phase 1 DoD |
|---|---|
| 100 节点布局 | ≤ 基线 × 1.2 且 ≤ 50ms |
| 1000 节点布局 | ≤ 基线 × 1.2 且 ≤ 1000ms |
| 冷启动 | ≤ 基线 × 1.2 且 ≤ 2s |
| 1000 矩形 FPS | ≥ 基线 × 0.8 且 ≥ 30 |
| 全量哈希 1000 节点 | ≤ 5ms |
| SerializeFull 1000 节点 | ≤ 20ms |

**哈希策略**：Phase 1 用全量计算，Phase 5 若性能不足再优化为增量（Merkle 树）。

**增量更新**：拖动节点只更新该节点位置和相连边路径；结构变更才触发重布局，样式变更只触发重绘。


## 十一、Sidecar 一致性与恢复

### 11.1 完整性校验

**SidecarIntegrityChecker** 检查四项：

1. 结构哈希是否匹配当前文档
2. 是否有孤儿 pinned 节点（已删除）
3. 是否有孤儿 waypoints（边已删除）
4. documentId 是否一致

### 11.2 五级恢复

1. layout.json 哈希不匹配：静默重布局
2. layout.json 损坏：删除重建
3. user.json 孤儿条目：移入 orphans 区，保留 30 天
4. user.json 损坏：弹窗，从备份恢复或放弃人工调整
5. user.json 严重错误：拦截保存，弹窗

**Sidecar 状态码**（独立于 CommandError）：SIDECAR_HASH_MISMATCH（加载时静默重布局）、SIDECAR_CORRUPT（加载时弹恢复对话框）。


## 十二、项目结构

```
Diagram.sln
├── Diagram.Core              核心：IR、命令、日志、Sidecar、校验
├── Diagram.Mermaid           Mermaid 导入导出
├── Diagram.Layout            布局引擎封装
├── Diagram.Llm               内部 LLM 集成
├── Diagram.Mcp               MCP Server / Client / Skills
├── Diagram.Render            渲染与导出
├── Diagram.App               Avalonia 主程序
├── Diagram.AotSmokeTest      AOT 冒烟测试（只引用 Core）
├── Diagram.Core.Tests
├── Diagram.Mermaid.Tests
├── Diagram.Layout.Tests
├── Diagram.Mcp.Tests
├── Diagram.Render.Tests
├── Diagram.E2E.Tests
├── Diagram.Benchmarks
├── Diagram.Compare.Tests
├── docs/                     架构、Schema、映射、迁移文档
├── reports/loc.md            LOC 脚本生成
├── tools/CompareHarness      对比测试 LLM 客户端（不进 sln）
├── tools/LocCounter          LOC 加总脚本
├── tasks/phase0a/0b/1/       任务 YAML
├── Directory.Packages.props  精确版本锁定
└── AGENTS.md                 Agent 约定
```

**AGENTS.md 关键约定**：

- 集合对外只读、修改只能通过命令
- 所有命令 Apply 必须原子：失败整体回滚
- 每个命令必须有原子性测试
- ChangeContext.Timestamp 由命令总线填充
- CommandMemento 用抽象基类 + 派生密封记录，禁止 object
- 哈希 Phase 1 用全量
- **命令内部不得异步调用 Execute**
- 不引入未在选型清单中的依赖
- LOC 由脚本生成、CI 强制一致


## 十三、执行计划（面向 Coding LLM）

### 13.1 面向 Agent 的任务设计原则

单任务单 PR（不超过 500 行变更）；显式依赖 DAG；上下文自包含（目标、输入、输出、验收、文档）；验收可执行（dotnet test --filter Category=xxx）；禁止跨任务重构；幂等可重跑。

### 13.2 任务描述模板

每个任务用 YAML + Markdown 描述，包含：id、title、phase、depends_on、estimated_loc、files（create / modify / test 三类）、acceptance（命令和期望）、constraints、context。

### 13.3 Phase 0a 依赖验证（1.5-2 周）

| ID | 任务 | 依赖 | 验收 |
|---|---|---|---|
| P0-01 | NuGet 版本核查 + 精确锁定 | — | Directory.Packages.props 全部精确 |
| P0-02 | Avalonia + SkiaSharp AOT 最小应用 | P0-01 | AOT 发布成功，冷启动实测 |
| P0-03 | Mermaider Sugiyama 约束验证 | P0-01 | 固定 2 节点位置被尊重 |
| P0-04 | Mostlylucid.Dagre 复合图 + AOT | P0-01 | 嵌套分组正确，AOT 通过 |
| P0-05 | ModelContextProtocol 传输验证 + initialize 自定义字段 | P0-01 | stdio + HTTP 可用 |

P0-08 / P0-09 从 Day 1 并行启动，不阻塞。

### 13.4 Phase 0b 性能与备选（2 周）

| ID | 任务 | 依赖 | 验收 |
|---|---|---|---|
| P0-06 | 性能基线 | P0-02 | 记录冷启动、1000 矩形 FPS、100/1000 节点布局、SerializeFull |
| P0-07 | 备选方案评估 | P0-03/04/05 | 每个关键依赖至少 1 个备选 |
| P0-08 | LLM 客户端（HttpClient，不用 MEAI，不进 sln） | — | 50 prompt 结果入 Git LFS，预算 30 美元 |
| P0-09 | MEAI + MCP 工具共用 PoC | P0-01 | 一个 ToolDescriptor 同时供两边 |

**决策门 0**：全绿（按计划） / 黄（走备选） / 红（人类决策）。

### 13.5 Phase 1 核心引擎（6 周）

| ID | 任务 | 依赖 | LOC |
|---|---|---|---|
| P1-01 | IR + JSON 序列化 | P0 | 400 |
| P1-02 | 命令总线 + 撤销重做 + 版本日志 + 审计日志 + 历史栈 + 会话提供者 + 版本检查 + 广播器 + 上下文 | P1-01 | 800 |
| P1-03 | 40+ 命令 + 原子性测试 + Memento 注册 | P1-02 | 650 |
| P1-04 | 校验器 + 结构化错误 | P1-01 | 300 |
| P1-05 | 冲突策略 + 字段元数据 | P1-03/04 | 250 |
| P1-06 | Sidecar 拆分 + 完整性 | P1-01 | 400 |
| P1-07 | Sidecar 备份 + 恢复 | P1-06 | 200 |
| P1-08 | Mermaid 词法 + 语法 | P1-01 | 500 |
| P1-09 | Mermaid 宽松模式 | P1-08 | 250 |
| P1-10 | Mermaid 导出 | P1-08 | 300 |
| P1-11 | 基础布局引擎封装 | P1-01 | 400 |
| P1-12 | 四级降级 + 路径预算 + 级别唯一性 + 尝试记录 | P1-11 | 300 |
| P1-13 | 四叉树视口索引 | P1-01 | 300 |
| P1-16 | DSL 词法/语法 | P1-01 | 400 |
| P1-17 | DSL 语义映射 | P1-16 | 300 |
| P1-14 | DSL vs Mermaid 对比 | P1-08/10/16/17 + P0-08 | 400 |
| P1-15 | CI + LOC 强制 + AOT 冒烟 | P1-01 | 150 |
| P1-18 | Diagram.AotSmokeTest | P1-02 | 100 |

**LOC 总计**：见 reports/loc.md（脚本生成，CI 强制一致）。

**决策门 1**：DSL 去留（4 项量化阈值）。**决策门 2**：布局引擎主选。

### 13.6 Phase 2 渲染与画布（5 周）

Phase N 的任务 YAML 在 Phase N-1 结束时产出。任务包括：Avalonia Canvas 基础渲染、动态阈值切换、性能诊断面板、拖拽连线属性面板、变更高亮方案、用户测试、中级布局约束。

### 13.7 Phase 3 LLM 与 MCP（5 周）

ToolRegistry + 8 工具、MEAI 集成 + 上下文摘要、错误回环、MCP Server stdio、MCP Server HTTP/SSE、乐观并发 + 软锁 + 广播、图层级权限、Skill、高级布局约束。

### 13.8 Phase 4 丰富功能（5 周）

图层、页面、形状库、模板、组合（分组/泳道/子流程/组合节点）、调色板、富文本与数学排版、导入导出。

### 13.9 Phase 5 打磨（4 周）

布局质量评分、性能优化、主题、i18n、无障碍。

### 13.10 总体排期

```
Phase 0a 依赖验证          第 1-2 周
Phase 0b 性能与备选        第 3-4 周
Phase 1  核心引擎          第 5-10 周
Phase 2  渲染与画布        第 11-15 周
Phase 3  LLM 与 MCP       第 16-20 周
Phase 4  丰富功能          第 21-25 周
Phase 5  打磨              第 26-29 周
```

约 29 周。

### 13.11 Agent 执行流程（9 步）

1. 读任务 YAML 和 Markdown
2. 检查依赖：所有前置任务已合并到 main
3. 从 main 拉分支
4. 读 context 中的文档
5. 实现代码 + 测试
6. 本地跑 acceptance 全部通过
7. 提交 PR：变更摘要 + 验收输出 + 未决问题 + 依赖解锁
8. 人类 review（只审查）
9. 合并后下一个任务自动解锁

### 13.12 PR 模板

包含：任务 ID 和标题、变更摘要、验收命令输出、未决问题、依赖解锁清单。

### 13.13 冲突与失败处理

两 agent 改同一文件时后合并者负责 rebase；冲突超 50 行人类仲裁；失败自动重试 3 次（每次干净分支）；3 次失败后 PR 标记 blocked，人类介入。

### 13.14 风险登记册

| 风险 | 概率 | 影响 | 缓解 | 触发条件 |
|---|---|---|---|---|
| Mermaid 方言差异大 | 中 | 高 | 宽松模式 + 原始片段保留 | 导入失败 > 10% |
| 依赖不可用（AOT / 版本） | 中 | 高 | Phase 0 验证 + 备选 | 决策门 0 红 |
| MCP 规范不对齐 | 中 | 中 | P0-05 验证 | 传输不可用 |
| 布局引擎约束不支持 | 中 | 高 | P0-03 验证 + 后处理 | Pinned 不生效 |
| 对比测试主观 | 中 | 中 | 双盲 + 4 项量化阈值 | 评分者间差异 > 20% |
| 降级预算失控 | 低 | 中 | 路径预算 800ms | 累计超时 > 预算 |
| 版本 diff 性能 | 低 | 中 | 环形日志 + 差距降级 | BuildDiff > 50ms |
| VersionLog 内存 | 低 | 中 | 限制 100 条 + IsBulkChange | > 10MB |
| Phase 0a 时间 | 高 | 中 | 调为 1.5-2 周 | 超 2 周 |
| Redo 非确定性 | 中 | 中 | 重新捕获 memento + 失败恢复 | Redo 结果不一致 |
| SerializeFull 成本 | 中 | 中 | 延迟序列化 | > 50ms |
| MCP 并发冲突 | 中 | 高 | 乐观 + 软锁 + diff | 冲突率 > 10% |
| CommandBus 锁竞争 | 低 | 低 | 命令 < 1ms | 可忽略 |
| 原子性测试覆盖不全 | 中 | 高 | CI 门禁强制 | 覆盖不全 |
| 异步 Handler 线程池耗尽 | 中 | 中 | ExecuteAsync + await | 线程池饥饿 |
| MCP clientState 不一致 | 中 | 高 | 会话级协商 + hash 校验 | hash 不匹配 |
| 嵌套 Execute 死锁 | 低 | 高 | AsyncLocal 检测 | 嵌套调用 |
| Memento 类型未注册 | 中 | 高 | P1-03 强制注册测试 | AOT 运行失败 |
| Broadcast Channel 背压 | 低 | 中 | 有界 Channel 1024 + DropOldest | 积压 |
| LOC 文档不同步 | 高 | 低 | CI 强制脚本生成 | diff 不一致 |


## 十四、验收方案

### 14.1 三级验收

L1 任务级：Agent 自验，acceptance 命令全通过。L2 阶段级：Agent + 人类，Phase DoD 全通过。L3 项目级：人类，端到端场景全通过。

### 14.2 L1 任务级验收命令

每个任务提交前运行：编译通过、本任务测试通过、回归测试通过、格式检查通过、AOT 发布成功。

### 14.3 L2 阶段级 DoD

**Phase 0a**：全部依赖精确锁定、Avalonia + SkiaSharp AOT 发布成功、Mermaider 支持 Pinned/Direction/Spacing、Mostlylucid.Dagre 复合图 + AOT、ModelContextProtocol stdio + HTTP 可用。

**Phase 0b**：性能基线记录、每个关键依赖至少 1 个备选、LLM 客户端 50 prompt 结果缓存、MEAI + MCP 共用 PoC 成功或降级方案明确。

**Phase 1（35 项判据）**：

| # | 判据 | 验证方式 |
|---|---|---|
| 1 | Diagram.Core 仅依赖 BCL | dotnet list package |
| 2 | IR 往返无损 | RoundTrip |
| 3 | 撤销 1000 次无泄漏 | UndoStress |
| 4 | 版本/日志一致 | UndoRedoVersion |
| 5 | BuildDiff 边界正确 | DiffBoundary |
| 6 | 每个命令原子性 | Atomicity |
| 7 | 规范化 JSON 比较 | AtomicityNormalized |
| 8 | 快照往返一致 | RestoreRoundTrip |
| 9 | Memento AOT 编译 + 运行 | AotSmokeTest |
| 10 | 异步并发无死锁 | AsyncConcurrency |
| 11 | 嵌套 Execute 抛异常 | NestedExecute |
| 12 | Memento 全部注册 | MementoRegistration |
| 13 | Mermaid 标准 100% | MermaidStandard |
| 14 | Mermaid 方言降级 | MermaidLenient |
| 15 | Sidecar 损坏恢复 | SidecarRecovery |
| 16 | 100 节点 ≤ 50ms | LayoutPerf |
| 17 | 四级降级 | LayoutFallback |
| 18 | 四叉树 1000 节点 < 1ms | QuadTreePerf |
| 19 | 对比测试报告 | reports/compare.md |
| 20 | 覆盖率 ≥ 80% | --collect |
| 21 | AOT 发布成功 | dotnet publish |
| 22 | LOC 一致 | LocCounter |
| 23 | Broadcaster 注入验证 | BroadcasterInjection |
| 24 | ChangeNotification 序列化 | ChangeNotificationSerialization |
| 25 | Snapshot 持久化 | SnapshotPersist |
| 26 | Reference 空 relevant 返回 Empty | ReferenceEmpty |
| 27 | PinnedPositions 传入布局 | PinnedPositions |
| 28 | Context 与 Broadcaster 一致 | WorkspaceBroadcaster |
| 29 | SessionId 单一来源 | SessionIdResolution |
| 30 | LayoutResult 携带 Attempts | LayoutResultAttempts |
| 31 | Workspace 可 Dispose | WorkspaceDispose |
| 32 | Broadcaster 不阻塞 | BroadcasterNonBlocking |
| 33 | 嵌套 Execute 跨线程语义 | NestedExecuteCrossThread |
| 34 | Redo 失败恢复 memento | RedoAtomicity |
| 35 | 引擎返回类型与协调器返回类型分离 | LayoutResultSplit |

**Phase 2**：1000 节点 FPS ≥ 30、拖拽延迟 < 16ms、属性面板切换 < 100ms、变更高亮通过用户测试、渲染模式切换无卡顿、虚拟化剔除率 > 80%、中级约束正确。

**Phase 3**：Codex CLI / Claude Code 可读写、多 agent 并发无损坏、409 + diff、软锁 TTL、Skill 加载、内外部工具行为一致。

**Phase 4**：draw.io 覆盖 ≥ 85%、组合全部可用、图层/页面/形状库/模板可用、富文本与数学正确、导入导出无损。

**Phase 5**：布局评分 ≥ 85、1000 节点流畅、内存 < 500MB、冷启动 ≤ 2s、WCAG AA、i18n 预留。

### 14.4 L3 端到端场景

| 场景 | 判据 |
|---|---|
| 从零生成 | 用户说"画登录流程"，LLM 生成人工微调，30 秒出可用图 |
| 外部 agent | Codex CLI 通过 MCP 加节点改布局，GUI 实时反映 |
| 人机并发 | 人工拖节点同时 LLM 改标签，无冲突丢失 |
| 大型架构图 | 导入 500 节点 Mermaid，渲染 < 2s，可交互 |
| 协作导出 | 导出 SVG 给同事 draw.io 打开，视觉一致 |
| 崩溃恢复 | 编辑中杀进程重启，pin 全部保留 |
| 多窗口并发 | 两个窗口同时改同一文档，无数据损坏 |
| MCP 断线重连 | HTTP/SSE 断开后重连，版本同步 |


## 十五、测试方案

### 15.1 七层测试

| 层级 | 工具 | 覆盖目标 | 频率 |
|---|---|---|---|
| 单元测试 | xUnit + FluentAssertions | ≥ 80% | 每次提交 |
| 集成测试 | Testcontainers | 模块接口 | 每次 PR |
| 快照测试 | Verify.NET | 渲染输出 | 每次 PR |
| 性能测试 | BenchmarkDotNet | 关键路径 | 每日 |
| 端到端测试 | Avalonia.Headless | 用户场景 | 每日 |
| 对比测试 | 自定义 + LLM | DSL vs Mermaid | Phase 1 一次 |
| 用户测试 | 手工 | 交互体验 | Phase 2 一次 |

### 15.2 关键测试清单

**IR 与序列化**：往返无损、AOT 编译、旧版本兼容、10,000 节点序列化 < 100ms。

**命令总线**：撤销 1000 次、幂等键、失败无副作用、多线程一致、版本日志环形、冲突 diff、嵌套检测、原子性、规范化 JSON 比较、Redo 失败恢复。

**Sidecar**：哈希不匹配检测、孤儿清理、截断恢复、备份轮替。

**Mermaid**：50 官方示例 100%、方言降级、1000 节点 < 500ms、往返一致。

**布局**：无重叠、100 节点 ≤ 50ms、1000 节点 ≤ 1000ms、四级降级 + 路径预算、Pinned 位置不变、级别唯一性、确定性、引擎返回类型分离。

**四叉树**：1000 节点 < 1ms、增删 O(log n)、跨边界正确。

**MCP**：协议一致、8 工具参数校验、10 agent 并发无损坏、409 + diff、只读无法写、速率限制、审计日志、clientState 握手、accepted=false 处理。

**渲染**：10 场景快照、1000 节点 FPS ≥ 30、虚拟化剔除 > 80%、模式切换无卡顿。

**AOT**：Memento 注册、序列化往返、冷启动。

**广播**：有界 Channel 不积压、订阅者异常不影响、Workspace 正确 Dispose、Broadcaster 所有权。

### 15.3 DSL vs Mermaid 对比测试

**测试集**：50 prompt（20 简单 / 20 中等 / 10 复杂），固定，结果缓存到 reports/raw/。

**流程**：Phase 0 P0-08 生成 → Phase 1 解析评分 → 双盲人工评分。

**量化阈值**：

| 指标 | 阈值 |
|---|---|
| 端到端准确率 | DSL 高出 ≥ 15% |
| 人工修正步骤 | DSL 减少 ≥ 30% |
| 首轮通过率 | DSL 高出 ≥ 20% |
| 解析错误率 | DSL 低于 ≥ 50% |

**判定**：4 项中至少 3 项达标保留 DSL；2 项达标仅用于流式场景；1 项或以下砍掉。

数据收集用双盲评分，评分者培训，冲突由第三方仲裁。产出 reports/compare.md。

### 15.4 用户测试

12-15 人，交叉设计，定量 + 定性。统计方法：Friedman + Wilcoxon + Holm-Bonferroni + Kendall's W + 平均排序分。


## 十六、安全与隐私

| 层面 | 措施 |
|---|---|
| 传输 | HTTPS（TLS 1.3） |
| 认证 | Bearer Token，最小权限，轮换 |
| 授权 | 图层级 ACL 绑定主体 |
| 速率限制 | 每 token 每分钟 N 次 |
| 审计 | AuditLog + 调用记录，脱敏 |
| 输入校验 | JSON Schema 硬约束 |
| 工作区隔离 | --workspace 限定，路径逃逸检测 |
| 资源限制 | 30s 超时，内存上限 |
| 日志脱敏 | 不含文件内容 |


## 十七、数据迁移

IR JSON 用 version 字段逐版本迁移。Sidecar 旧格式自动升级。DSL 语法首行版本声明可选，缺省视为当前版本，解析器记 issue 但不报错。迁移失败时保留原文件并弹窗提示。


## 十八、错误处理

**CommandError 错误码表**（Fail / Conflict 产生）：

| 码 | 类型 | GUI 处理 |
|---|---|---|
| DUPLICATE_ID | 校验失败 | 高亮冲突 ID |
| EDGE_TARGET_MISSING | 校验失败 | 提示创建 |
| EDGE_SOURCE_MISSING | 校验失败 | 提示创建 |
| GROUP_MEMBER_MISSING | 校验失败 | 提示 |
| GROUP_CYCLE | 校验失败 | 高亮循环 |
| VERSION_CONFLICT | 并发冲突 | diff 对话框 |
| INVALID_EXPECTED_VERSION | 参数错误 | 状态栏提示 |
| EXPECTED_VERSION_REQUIRED | 参数错误 | 状态栏提示 |
| LAYOUT_ALL_LEVELS_TIMEOUT | 布局失败 | 三选项 |
| MCP_UNAUTHORIZED | 认证失败 | 提示 |
| MCP_RATE_LIMITED | 速率超限 | 状态栏提示 |
| INTERNAL_ERROR | 内部错误 | 状态栏提示 + 日志（不含异常类型名） |

**Sidecar 状态码**（独立表）：SIDECAR_HASH_MISMATCH（加载时静默重布局）、SIDECAR_CORRUPT（加载时弹恢复对话框）。

**NoOp 消息**（非错误）："无可撤销操作" / "无可重做操作"，通过 NoOp 返回。


## 十九、插件/扩展机制

Phase 4 定义两个接口：IShapeProvider（提供形状定义列表）和 ILayoutEngine（提供布局算法）。第三方实现需通过沙箱验证，不允许访问文件系统或网络。


## 二十、国际化与无障碍

Phase 5：资源字符串预留、RTL 支持、屏幕阅读器、键盘导航、WCAG AA。


## 二十一、完整入口总览

| 入口 | 传输 | 使用者 | 场景 |
|---|---|---|---|
| GUI 画布 / 面板 | 内部 | 人 | 交互编辑 |
| 内部 LLM | MEAI | 软件内 AI | 自然语言生成/修改 |
| MCP stdio | JSON-RPC | 本地 agent | Codex CLI / Claude Code |
| MCP HTTP long-polling / WebSocket | JSON-RPC | 远程 agent | 跨机协作 |
| MCP Skill | skill:// URI | 外部 agent | 工作流指导 |
| MCP Client | JSON-RPC | 软件自身 | 调用外部 agent 服务 |
| 文件导入导出 | 文件系统 | 所有人 | 持久化、Git 协作 |

所有入口最终汇合到命令接口，走同一条校验、应用、布局、渲染、历史管线。


## 附录：关键设计决策（不可违反）

本附录列出 Agent 实现时必须遵守的约束，防止重新引入已识别的 P0/P1 问题。

**命令原子性**：

- 所有命令的 Apply 必须原子。失败时恢复 memento、恢复文档状态、恢复调用方的栈（重做栈或撤销栈），然后返回 result。
- Execute 和 Redo 都必须遵守此约束，Redo 不得只恢复重做栈而忽略文档状态。

**Broadcaster 校验**：

- MCP 模式（RequiresVersionCheck=true）下，Broadcaster 不得为 NullChangeBroadcaster 实例。构造函数校验必须用 `is NullChangeBroadcaster` 判据，不得仅判 null。

**Workspace 所有权**：

- DiagramWorkspace 通过 ownsBroadcaster 标志明确 broadcaster 所有权。
- 多个 Workspace 共享 broadcaster 时，只有创建者传入 true。
- DisposeAsync 仅当 ownsBroadcaster=true 时释放 broadcaster。

**ILayoutEngine 类型契约**：

- 引擎实现 ILayoutEngine，返回 EngineLayoutResult（不含 AppliedLevel 和 Attempts）。
- LayoutCoordinator 包装为 LayoutResult（含 AppliedLevel 和 Attempts）。
- 引擎不得感知降级级别。

**嵌套 Execute**：

- 命令内部不得异步调用 Execute。
- AsyncLocal 在 Task.Run 中会传播，导致误报。
- 若需异步触发命令，由宿主在命令外调度。

**SessionId 解析**：

- 优先 ctx.SessionId，为空 fallback 到 Session.CurrentSessionId。
- 两者不一致时记录警告日志。

**ChangeNotification.Timestamp**：

- 仅用于日志和审计。
- 排序用单调递增的 Version，不用 Timestamp。

**异常信息**：

- AuditLog 的 CommandError.Payload 不携带异常类型名。
- 类型名写入应用日志，不写入 AuditLog。

**LayoutHintsDefaults**：

- 只提供 Create（返回新实例）。
- 不提供可变单例。

**HistoryStack.Clear**：

- 仅用于同一文档重新加载。
- 打开新文档应创建新的 DiagramDocument + CommandBus。

**Channel 容量**：

- InProcessBroadcaster 使用有界 Channel（容量 1024，满时 DropOldest）。
- 不得使用无界 Channel。

**Dispose 等待**：

- InProcessBroadcaster.DisposeAsync 等待分发任务结束，超时 2 秒。
- 不得在分发任务仍运行时释放 CTS。


## 二十二、一句话总结

IR 是唯一事实源，DSL 降级为可选，Sidecar 拆分为可重建与不可丢。人和 LLM 通过同一套命令接口操作同一份文档，能力完全对等。工具精简到 8 个粗粒度接口，每个内部用 action 分发 + 确定性默认值 + 原子性 + 幂等键。布局用 Mermaider 内置 Sugiyama 为主、Dagre 为辅，配四级降级 + 路径预算 + 级别唯一性，引擎返回类型与协调器返回类型分离。渲染用 Avalonia 12 + SkiaSharp 4 + 四叉树虚拟化。LLM 集成用 MEAI 10.7，外部 agent 接入用 ModelContextProtocol 2.2 + SEP-2640 Skills。乐观并发 + 文档软锁 + 图层级权限保证并发安全。变更高亮用脉冲 + 角标 + 边栏。Sidecar 拆 layout.json + user.json，五级恢复 + 时间戳备份。执行计划面向 coding agent：任务原子化、依赖显式化、验收自动化、上下文自包含。LOC 由脚本生成 + CI 强制一致。总排期约 29 周。所有入口汇合到命令接口，人和 LLM 能力完全对等。附录明确列出 12 条不可违反的关键设计决策，防止 Agent 重新引入已识别的 P0/P1 问题。