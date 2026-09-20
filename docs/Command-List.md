# 命令清单

所有入口（GUI、内部 LLM、MCP Agent、导入器）最终都走这里。
`CommandId` 用小写连字符，是审计日志与版本日志里的稳定标识。

## 已实现（垂直切片）

| CommandId | 类 | 前置校验 | 原子性要点 |
|---|---|---|---|
| `add-node` | `AddNodeCommand` | `id` 非空；`id` 不重复 | `index` 越界一律**夹紧**而非抛异常；`CaptureMemento` 与 `Apply` 用同一个 `ResolveIndex`，保证撤销索引一致 |
| `remove-node` | `RemoveNodeCommand` | 节点存在 | 连带删除全部关联边；memento 记录每条边的**原索引**，撤销时按索引升序插回，边顺序逐字节还原 |
| `connect-edge` | `ConnectEdgeCommand` | `id` 非空且不重复；`from` / `to` 均存在 | 校验一次返回全部错误（`EDGE_SOURCE_MISSING` + `EDGE_TARGET_MISSING` 可同时出现） |

三个命令都返回 `StructuralChanged = true`、`VisualChanged = true`。

## 计划中（Phase 1 P1-03，方案称 40+ 条）

按工具层的 action 分组，便于 `diagram_edit` 内部按 action 分发：

| 分组 | 命令（`CommandId`） |
|---|---|
| 节点 | `update-node-label`、`move-node-layer`、`set-node-shape`、`reorder-node` |
| 边 | `disconnect-edge`、`set-edge-label`、`set-edge-route`、`reconnect-edge`、`add-edge-waypoints` |
| 样式 | `apply-style-token`、`set-node-style`、`set-text-style`、`set-edge-style` |
| 布局 | `set-direction`、`pin-node`、`unpin-node`、`set-spacing`、`set-same-rank`、`set-order`、`set-align`、`set-place` |
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
