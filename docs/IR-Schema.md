# IR Schema

IR 是唯一事实源。本文件描述**当前已实现**的字段，以及尚未实现的部分。

序列化由 `DuetDiagram.Core/Serialization/DiagramJsonContext.cs` 的 AOT 源生成完成，
选项为 `camelCase` + 字符串枚举 + 忽略 null。

## DiagramDocument

| 字段 | 类型 | 可变性 | 说明 |
|---|---|---|---|
| `id` | `string` | 构造后只读 | 文档唯一标识 |
| `kind` | `DiagramKind` | `internal set` | `Flow` / `Flowchart` / `Block` / `State` |
| `direction` | `Direction` | `internal set` | `LR` / `TB` / `RL` / `BT`，方向的唯一来源 |
| `version` | `int` | `internal set` | 仅命令总线可递增；初始 0 |
| `structuralHash` | `string` | `internal set` | 仅命令总线可更新 |
| `visualHash` | `string` | `internal set` | 仅命令总线可更新 |
| `nodes` | `IReadOnlyList<NodeDef>` | 只读 | 修改只能通过命令 |
| `edges` | `IReadOnlyList<EdgeDef>` | 只读 | 修改只能通过命令 |

反序列化走 `[JsonConstructor]`。**构造函数的参数类型必须与属性类型完全一致**，
否则 System.Text.Json 以 `constructor parameter must bind to an object property` 拒绝整个类型
（参数类型放宽到 `List<T>` 就踩过这个坑）。

## NodeDef

| 字段 | 类型 | 默认 |
|---|---|---|
| `id` | `string`（required） | — |
| `label` | `string` | `""` |
| `shape` | `NodeShape` | `Rect` |
| `parent` | `string?` | `null` |
| `layer` | `string?` | `null` |
| `styleToken` | `string?` | `null` |
| `desc` | `string?` | `null` |

`NodeShape`：`Rect` / `Rounded` / `Stadium` / `Diamond` / `Circle` / `Hexagon` / `Parallelogram` / `Cylinder`

## EdgeDef

| 字段 | 类型 | 默认 |
|---|---|---|
| `id` | `string`（required） | — |
| `from` | `string`（required） | — |
| `to` | `string`（required） | — |
| `fromPort` | `string?` | `null` |
| `toPort` | `string?` | `null` |
| `label` | `string` | `""` |
| `line` | `LineStyle` | `Solid` |
| `arrow` | `ArrowStyle` | `Arrow` |
| `styleToken` | `string?` | `null` |

`LineStyle`：`Solid` / `Dashed` / `Dotted`
`ArrowStyle`：`None` / `Arrow` / `OpenArrow` / `Circle` / `Cross`

## 示例

```json
{
  "id": "login-flow",
  "kind": "Flowchart",
  "direction": "LR",
  "version": 5,
  "structuralHash": "3f1a…",
  "visualHash": "9c02…",
  "nodes": [
    { "id": "start", "label": "开始", "shape": "Stadium" },
    { "id": "check", "label": "校验", "shape": "Diamond" }
  ],
  "edges": [
    { "id": "e1", "from": "start", "to": "check", "label": "是", "line": "Solid", "arrow": "Arrow" }
  ]
}
```

## 尚未实现（Phase 1 ~ Phase 4）

按方案 §4.1，以下内容**不在**当前 IR 内。引入时同步更新本文件与 `DiagramJsonContext`。

- 九个集合：`Pages`、`Layers`、`Composites`、`Tags`、`Actions`、`Fonts`、`TextPresets`（`Nodes` / `Edges` 已实现）
- 三个子对象：`Palette`、`Layout`、`Canvas`
- `NodeDef` 的 `Style`（`NodeStyle`）、`Text`（`TextStyle`）、`Ports`、`RichText`、`MathMode`、`Meta`
- `EdgeDef` 的 `Style`（`EdgeStyle`）中的 `Color` / `Weight` / `Route` / `LabelPos`
- `DiagramDocument` 的 `TakeFullSnapshot` / `RestoreFromSnapshot` / `ApplySnapshot`
- 布局约束：`Constraint` / `ConstraintOwner` / `LayoutHints` / `LayoutHintsDefaults`

`LayoutHintsDefaults` 落地时牢记：**只提供 `Create()` 返回新实例，不提供可变单例。**

## 哈希的覆盖范围

| 哈希 | 覆盖 | 不覆盖 |
|---|---|---|
| `StructuralHash` | 节点 `id` + `parent`；边 `id` / `from` / `to` / `fromPort` / `toPort` | 标签、形状、样式、`version`、哈希自身 |
| `VisualHash` | `kind` + `direction` + 节点 `id` / `label` / `shape` / `layer` / `styleToken` / `desc` + 边 `id` / `from` / `to` / `label` / `line` / `arrow` / `styleToken` | `version`、哈希自身 |

两处都按 id 排序后拼接规范化字符串再 SHA-256，保证与插入顺序无关（撤销后哈希必然回到原值）。
