# 命令清单

所有入口（GUI、内部 LLM、MCP Agent、导入器）最终都走这里。
`CommandId` 用小写连字符，是审计日志与版本日志里的稳定标识。

## 已实现（垂直切片）

| CommandId | 类 | 前置校验 | 原子性要点 |
|---|---|---|---|
| `add-node` | `AddNodeCommand` | `id` 非空；`id` 不重复 | `index` 越界一律**夹紧**而非抛异常；`CaptureMemento` 与 `Apply` 用同一个 `ResolveIndex`，保证撤销索引一致 |
| `remove-node` | `RemoveNodeCommand` | 节点存在 | 连带删除全部关联边；memento 记录每条边的**原索引**，撤销时按索引升序插回，边顺序逐字节还原 |
| `connect-edge` | `ConnectEdgeCommand` | `id` 非空且不重复；`from` / `to` 均存在（**节点或组合都可以**） | 校验一次返回全部错误（`EDGE_SOURCE_MISSING` + `EDGE_TARGET_MISSING` 可同时出现） |
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

按工具层的 action 分组，便于 `diagram_edit` 内部按 action 分发：

| 分组 | 命令（`CommandId`） |
|---|---|
| 节点 | `update-node-label`、`move-node-layer`、`set-node-shape`、`reorder-node` |
| 边 | `disconnect-edge`、`set-edge-route`、`add-edge-waypoints`（`reconnect-edge` 与 `set-edge-label` 已在 Phase 2 P2-07 落地，分别走 `reconnect-edge` 与 `set-edge-field` 的 `label` 字段） |
| 样式 | `apply-style-token`、`set-node-style`、`set-text-style`、`set-edge-style` |
| 布局 | `set-direction`、`pin-node`、`unpin-node`、`set-spacing`、`set-place`（`set-same-rank` / `set-order` / `set-align` 已在 Phase 2 P2-10 落地，三类合成两条命令：`add-layout-constraint` 与 `remove-layout-constraint`） |
| 组合 | `create-group`、`create-lane`、`create-subflow`、`create-combo`、`dissolve-composite`、`move-into-composite` |
| 图层 / 页面 | `create-layer`、`rename-layer`、`reorder-layer`、`assign-layer`、`create-page`、`delete-page` |
| 调色板 | `define-palette-entry`、`update-palette-entry`、`remove-palette-entry` |
| 标签 / 动作 | `add-tag`、`remove-tag`、`add-action`、`remove-action` |
| 文档 | `set-kind`、`set-canvas-settings` |

> 这张表是**目标**，不是承诺。实际创建以 `tasks/phase1/P1-03-*.yaml` 为准，
> 每完成一条就在本表登记实现状态。

## 新增命令的检查清单

见 `AGENTS.md`「新增一个命令的检查清单」。核心三条：

1. `Validate` 返回结构化 `CommandError`，不要在 `Apply` 里抛异常表达业务失败。
2. `CaptureMemento` 与 `Apply` 对同一文档必须给出**一致**的索引/位置。
3. 新增 Memento 记录 → 补 `[JsonDerivedType]`，否则 AOT 下反序列化失败。
