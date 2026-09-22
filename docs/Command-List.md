# 命令清单

所有入口（GUI、内部 LLM、MCP Agent、导入器）最终都走这里。
`CommandId` 用小写连字符，是审计日志与版本日志里的稳定标识。

## 已实现（垂直切片）

| CommandId | 类 | 前置校验 | 原子性要点 |
|---|---|---|---|
| `add-node` | `AddNodeCommand` | `id` 非空；`id` 不重复 | `index` 越界一律**夹紧**而非抛异常；`CaptureMemento` 与 `Apply` 用同一个 `ResolveIndex`，保证撤销索引一致 |
| `remove-node` | `RemoveNodeCommand` | 节点存在 | 连带删除全部关联边；memento 记录每条边的**原索引**，撤销时按索引升序插回，边顺序逐字节还原 |
| `set-node-field` | `SetNodeFieldCommand` | 节点存在；字段名在已注册节点字段内；值能解析成该字段要的类型 | 节点上除「存在与否」之外的属性全走这里：`label`、`shape`、`parent`、`layer`、`styleToken`、`style`（含七个子字段）、`text`（含八个）、`ports`、`richText`、`mathMode`、`desc`、`meta`。memento 存**改之前的整份节点定义**，撤销时整份换回，不按字段名再拼一次——两处拼接一旦分叉，差异只体现在哈希上。写入值与旧值相同时是 NoOp，不进历史 |
| `connect-edge` | `ConnectEdgeCommand` | `id` 非空且不重复；`from` / `to` 均存在（**节点或组合都可以**） | 校验一次返回全部错误（`EDGE_SOURCE_MISSING` + `EDGE_TARGET_MISSING` 可同时出现） |
| `disconnect-edge` | `DisconnectEdgeCommand` | 边存在 | 波及面只有这一条边：端点节点与别的边都不变。memento 记这条边的**原索引**，撤销按索引插回而不是追加——边的集合顺序有语义（层内次序按出边先后排列），而两个哈希都按标识排序后再遍历，位置错了哈希照样对得上。引用了这条边的布局约束**不在这里清理**，留着让整体校验器报 `LAYOUT_ORDER_EDGE_MISSING` |
| `reconnect-edge` | `ReconnectEdgeCommand` | 边存在；新端点存在；**端点不能是组合**（组合没有端口，连不进具体端口，报 `EDGE_PORT_ON_COMPOSITE`） | 端点解析沿用"先节点、后组合"；重连到同一对端点视为 NoOp，不进历史 |
| `set-edge-field` | `SetEdgeFieldCommand` | 边存在；字段名在已注册边字段内（`label` / `style`）；`style` 须为合法令牌 | 端点之外的边属性走这里；`label` 这一轮只做纯文本（富文本属 Phase 4） |
| `add-layout-constraint` | `AddLayoutConstraintCommand` | 同层与对齐：至少两个成员、无主语、成员不重复、节点都存在；层内次序：有主语、至少两条出边、边不重复且都是主语的出边 | 归属由调用方显式给出，不给默认值——它是降级矩阵的输入。层内次序是**每个主语一条**：同一主语上已有次序时换掉它而不是追加，否则拖几次就积下几十条互相矛盾的次序。memento 整份换回 `LayoutHints` |
| `remove-layout-constraint` | `RemoveLayoutConstraintCommand` | 要删的那一条存在（按内容找，不按序号） | 不存在时报 `LAYOUT_CONSTRAINT_MISSING` 而**不是** NoOp：那通常说明调用方手上那份列表已经过期，报成功会让它继续拿一份错的列表往下走 |

上面这些命令都返回 `StructuralChanged = true`、`VisualChanged = true`。
布局约束进的是结构哈希，而那个哈希要回答的正是"要不要重新求解布局"——
报成纯外观的话，宿主会只重绘不重排，而画面上的坐标根本没跟着约束变。

> 边的**折点**（bend point）不在命令层里。折点是用户「拖这儿」的产物，与文档结构无关；
> 走命令总线会把一次拖动塞进几百条 IR 记录，撤销栈失真。折点写在 sidecar 的 `pinnedEdges`，
> 由宿主侧直接读写（快照式撤销/重做），不进命令总线——与 P2-06 的 `pinnedNodes` 同一口径。

## 计划中（Phase 1 P1-03，方案称 40+ 条）

> **这张表是目标，不是承诺，而且它已经不准确了。** 实际创建以任务 YAML 为准，
> 每完成一条就在本表登记实现状态。
>
> 已知的不准确有三处，都是 2026-09-22 起草 Phase 3 任务时对着代码核出来的：
>
> - **节点与样式那两组里的多数条目不需要新命令。** P1-05 的字段注册表落地之后，
>   `set-node-field` 按字段名分发，label、shape、parent、layer、styleToken、style、
>   text、ports、richText、mathMode、desc、meta 都在它名下；边只有 label 与 style 两个。
>   所以 `update-node-label`、`move-node-layer`、`set-node-shape`、`apply-style-token`、
>   `set-node-style`、`set-text-style`、`set-edge-style` 都不必各立一条命令。
>   判断标准是**被改的值挂在元素上还是挂在文档上**：挂在元素上的由字段表覆盖。
> - **`add-edge-waypoints` 与 `set-edge-route` 不该有命令。** 折点写在 sidecar 的
>   `pinnedEdges`，不进 IR、不走命令总线——理由是折点是用户「拖这儿」的产物，
>   走总线会把一次拖动塞进几百条 IR 记录、撤销栈失真。
> - **`reorder-node` 现在不该有命令。** 节点集合顺序进不了结构哈希也进不了外观哈希
>   （两处都先把集合按标识排序再遍历），不改变布局坐标（同层内按横坐标排序、并列时
>   用标识断掉），也因为布局保证同层不重叠而看不出绘制次序。它要成为真功能，前提是
>   先给节点加一个 z 序字段并定义它在绘制里的语义，那是 IR 的改动。判断标准是
>   **被改的值挂在元素上还是挂在文档上**之外的第三条：**这个改动有没有人能看见**。
>
> 剩下的条目仍然有效，落在 `tasks/phase3/P3-01` ~ `P3-04`。

按工具层的 action 分组，便于 `diagram_edit` 内部按 action 分发：

| 分组 | 命令（`CommandId`） |
|---|---|
| 节点 | `update-node-label`、`move-node-layer`、`set-node-shape`（已由字段表覆盖，见上）；`reorder-node`（**待 IR 加 z 序字段**，见上） |
| 边 | `disconnect-edge`（P3-01）；`set-edge-route`、`add-edge-waypoints`（**不该有命令**，折点走 sidecar，见上）；`reconnect-edge` 与 `set-edge-label` 已在 Phase 2 P2-07 落地，分别走 `reconnect-edge` 与 `set-edge-field` 的 `label` 字段 |
| 样式 | `apply-style-token`、`set-node-style`、`set-text-style`、`set-edge-style` |
| 布局 | `set-direction`、`pin-node`、`unpin-node`、`set-spacing`、`set-place`（`set-same-rank` / `set-order` / `set-align` 已在 Phase 2 P2-10 落地，三类合成两条命令：`add-layout-constraint` 与 `remove-layout-constraint`） |
| 组合 | `create-group`、`create-lane`、`create-subflow`、`create-combo`、`dissolve-composite`、`move-into-composite` |
| 图层 / 页面 | `create-layer`、`rename-layer`、`reorder-layer`、`assign-layer`、`create-page`、`delete-page` |
| 调色板 | `define-palette-entry`、`update-palette-entry`、`remove-palette-entry` |
| 标签 / 动作 | `add-tag`、`remove-tag`、`add-action`、`remove-action` |
| 文档 | `set-kind`、`set-canvas-settings` |

## 新增命令的检查清单

见 `AGENTS.md`「新增一个命令的检查清单」。核心三条：

1. `Validate` 返回结构化 `CommandError`，不要在 `Apply` 里抛异常表达业务失败。
2. `CaptureMemento` 与 `Apply` 对同一文档必须给出**一致**的索引/位置。
3. 新增 Memento 记录 → 补 `[JsonDerivedType]`，否则 AOT 下反序列化失败。
