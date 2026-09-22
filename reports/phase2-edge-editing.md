# P2-07 连线与边的编辑 — 验证结论

任务 `tasks/phase2/P2-07-edge-editing.yaml`，依赖 P2-06 与 P1-03。状态 `done`。

## 交付内容

### 命令层（走总线、可撤销、AOT 多态注册齐备）

- `reconnect-edge`（`ReconnectEdgeCommand`）：端点重连。端点解析沿用「先节点、后组合」；
  重连目标是组合时拒绝并回报 `EDGE_PORT_ON_COMPOSITE`（组合没有端口，连不进具体端口）；
  重连到同一对端点视为 NoOp，不进历史。
- `set-edge-field`（`SetEdgeFieldCommand`）：改边的非端点属性，目前开放 `label` 与 `style`
  两个已注册字段；`style` 校验令牌合法性。
- 既有 `connect-edge`（`ConnectEdgeCommand`）在交互层被真正用起来：从端口拖出、空白取消、
  自身被拒三类路径全部覆盖。

两个新 Memento（`ReconnectEdgeMemento` / `SetEdgeFieldMemento`）已补 `[JsonDerivedType]`，
`MementoRegistration` 门禁通过。

### 折点（sidecar，不走总线）

折点是用户「拖这儿」的产物，与文档结构无关。走命令总线会把一次拖动塞进几百条 IR 记录、
撤销栈失真，与 P2-06 的 `pinnedNodes` 同一口径。折点写在 sidecar 的 `pinnedEdges`，
宿主侧用快照栈（入栈 `BendSnapshot`）管撤销/重做。**增删折点是「一条」操作**，不是
「删一条再加一条」——否则撤销要按两次而用户眼里是同一次操作。

> 落点：`DuetDiagram.Core` 的 `UserSidecar.PinnedEdges` 字段已存在，本任务在
> `DuetDiagram.App` 的 `DiagramSession` 里接通了读写与快照栈。**布局求解与绘制列表尚未
> 消费 `pinnedEdges`**，即固定折点目前写入了 sidecar 但画面还没按它弯——列为后续接线。

### 交互层

- `ConnectSession`：按下时记 `(sourceId, sourcePort, startPoint)`，移动只更新预览，
  松手才 `CommitConnect`。端口标识在按下那一刻进会话，不等到松手再去猜。
- `EdgeHandleHitTest`：端口锚点 + 边端点/中点把手命中；端点优先于中点、就近取边。
- `EdgeAdorner`：端口把手、连线预览（虚线折线）、折点把手，全部以叠加层 `DrawCommand` 表达。
- `DiagramCanvas` 指针处理在节点拖拽**之前**先判端口与边把手：`OnPointerPressed` 命中则
  进入连线/重连/折点手势，`OnPointerMoved` 走预览叠加层，`OnPointerReleased` 提交，
  `OnPointerCaptureLost` / `OnLostFocus` 取消并清叠加层。`CanvasViewModel` 新增 `_overlay`
  层，在每帧基命令之上合并。

### 顺带修复的回归

P2-06 给主窗口接属性面板时，把 `InitializeComponent` 改成了手写，只 `FindControl` 了
`PropertiesView` 而漏掉 `Canvas`，导致 `Canvas.Session = Session` 在构造期抛空引用——
所有依赖 `MainWindow` 的无头测试（CanvasSmoke / ModeSwitch / Diagnostics / PropertyPanel）
在构造期就死，而 P2-07 自己的 `Connect` / `EdgeEdit` 测试直接构造 `DiagramSession` 所以
各自通过，掩盖了这条全局红。已补 `Canvas = FindControl<DiagramCanvas>(nameof(Canvas))`。

## 验收

| 命令 | 结果 |
|---|---|
| `dotnet test ... --filter-trait Category=Connect` | 4/4 |
| `dotnet test ... --filter-trait Category=EdgeEdit` | 5/5 |
| `dotnet test ... --filter-trait Category=Atomicity`（Core 全量，含 16 条新增边命令原子性/往返） | 全部通过 |
| `dotnet test DuetDiagram.E2E.Tests`（全量，含 P2-06 主窗口回归修复） | 39/39 |
| `dotnet test DuetDiagram.Core.Tests`（全量） | 248/248 |

## 与规格文档的差异

- **不做富文本标签编辑**：§9.2 的「双击编辑标签」写的是富文本，而富文本与数学排版属 Phase 4。
  这一轮 `label` 只做纯文本。
- **折点走 sidecar 而非命令总线**：见上文，与 P2-06 `pinnedNodes` 同口径（已写入 AGENTS.md
  「已知差异」表）。

## 后续接线

1. 把 `pinnedEdges` 接进布局求解（`EdgeRouter`）与绘制列表，使固定折点真正生效。
2. `set-edge-field` 的 `style` 字段目前只校验令牌合法性，渲染端尚未把它映射到具体线型。
