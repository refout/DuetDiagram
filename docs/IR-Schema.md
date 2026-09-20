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
| `structuralHash` | `string` | `internal set` | 见下文"哈希覆盖范围" |
| `visualHash` | `string` | `internal set` | 同上 |

### 九个集合

九个集合全部对外只读，修改只能通过命令。它们**共用一个命名空间**：
成员列表里的标识不区分它是节点还是组合，引用关系也都不带类型前缀。
因此新增任何定义之前都要用整体的标识占用检查，而不是只查自己那一个集合。

| 集合 | 元素类型 | 计入 |
|---|---|---|
| `pages` | `PageDef` | 视觉 |
| `layers` | `LayerDef` | 视觉 |
| `nodes` | `NodeDef` | 结构 + 视觉 |
| `edges` | `EdgeDef` | 结构 + 视觉 |
| `composites` | `CompositeDef` | 结构 + 视觉 |
| `tags` | `TagDef` | 视觉 |
| `actions` | `ActionDef` | **都不计**——只影响交互 |
| `fonts` | `FontDef` | **结构**——字体改变标签宽度 |
| `textPresets` | `TextStylePreset` | 视觉 |

除 `actions` 外，所有集合的顺序都有意义。

### 三个子对象

| 子对象 | 类型 | 计入 |
|---|---|---|
| `palette` | `Palette` | 视觉 |
| `layout` | `LayoutHints` | **结构**——改了间距就必须重排 |
| `canvas` | `CanvasSettings` | 视觉 |

### 快照

| 方法 | 行为 |
|---|---|
| `TakeFullSnapshot()` | 深拷贝所有集合与子对象，连同版本号与两个哈希 |
| `RestoreFromSnapshot(snapshot)` | 整体替换内容，**连版本号与哈希一起恢复** |
| `ApplySnapshot` | **未实现**，语义待澄清 |

`RestoreFromSnapshot` 恢复版本号而不是留给命令总线推进：这个方法的用途是
"把文档退回某个已知状态"，版本号如果被重新推进，退回去的那份内容就再也无法与历史上的
版本号对上，增量同步会失去参照。

快照的文档标识与目标不一致时抛异常。跨文档套用会得到一个自相矛盾的对象——
标识是甲的、内容是乙的。打开另一份文档应当新建实例。

## 各类型的字段

### NodeDef

| 字段 | 类型 | 默认 | 计入 |
|---|---|---|---|
| `id` | `string`（required） | — | 结构 |
| `label` | `string` | `""` | 视觉 |
| `shape` | `NodeShape` | `Rect` | 视觉 |
| `parent` | `string?` | `null` | 结构 |
| `layer` | `string?` | `null` | 视觉 |
| `styleToken` | `string?` | `null` | 视觉 |
| `style` | `NodeStyle?` | `null` | 视觉 |
| `text` | `TextStyle?` | `null` | 视觉 |
| `ports` | `IReadOnlyList<PortDef>` | 空 | **结构** |
| `richText` | `bool` | `false` | 视觉 |
| `mathMode` | `MathMode` | `None` | 视觉 |
| `desc` | `string?` | `null` | 视觉 |
| `meta` | `IReadOnlyDictionary<string,string>` | 空 | **都不计** |

### EdgeDef

| 字段 | 类型 | 默认 | 计入 |
|---|---|---|---|
| `id` / `from` / `to` | `string`（required） | — | 结构 |
| `fromPort` / `toPort` | `string?` | `null` | 结构 |
| `label` | `string` | `""` | 视觉 |
| `style` | `EdgeStyle` | 空 | 视觉 |

`Line` / `Arrow` / `StyleToken` 是 `style` 上的只读便捷属性，不是独立字段。
**这个形状与节点不同**：节点的样式令牌留在顶层，边则全部收在样式里。
不对称是方案本身的规定，改动它要同时改线格式。

### CompositeDef 及其派生

抽象基类，派生 `GroupDef`（分组）、`LaneDef`（泳道）、`SubflowDef`（子流程）、
`ComboDef`（组合框）。多态标签写在 `$composite` 字段上。

| 字段 | 类型 | 计入 |
|---|---|---|
| `id` / `parent` | `string` | 结构 |
| `label` | `string` | 视觉 |
| `members` | `IReadOnlyList<string>` | 结构 |
| `direction` | `Direction?` | 结构 |
| `collapsed` | `bool` | 结构 |
| `style` | `NodeStyle?` | 视觉 |
| `localLayout` | `LayoutHints?` | 视觉（其约束内容另计结构） |

**成员关系的两处表达**：`CompositeDef.members` 与 `NodeDef.parent` 说的是同一件事。
约定**以 `members` 为准**，`parent` 是便于查询的冗余索引，两者必须一致。
校验见 `DiagramValidator`。

### 样式与端口

- `NodeStyle`：`fill` / `stroke` / `text` / `border` / `weight` / `radius` / `opacity` / `badge`
- `EdgeStyle`：`line` / `arrow` / `color` / `weight` / `route` / `labelPosition` / `styleToken`
- `TextStyle`：十六个字段，全部可空，逐层继承
- `PortDef`：`name` / `side` / `offset` / `isCustom`

`PortDef.isCustom` 区分自动端口与人工端口。不做这个区分的话，
用户手摆的端口每次重排都会跑回默认位置。

### 布局提示

四个约束列表，每项都带归属方与创建时间：

| 列表 | 值类型 |
|---|---|
| `sameRank` | `SameRankConstraint`（节点标识列表） |
| `order` | `OrderConstraint`（节点标识 + 出边次序） |
| `align` | `AlignConstraint`（节点标识列表） |
| `place` | `PlaceConstraint`（节点标识 + 参照物 + 关系） |

归属方取 `Auto` / `Llm` / `Human`，决定冲突时听谁的。
`LayoutHintsDefaults` 只提供返回新实例的 `Create()`，**不提供可变单例**。

## 哈希覆盖范围

### 判据不是"结构变了吗"

两个哈希回答的是两个问题：

| | 问题 | 变了要做什么 |
|---|---|---|
| `StructuralHash` | **现有坐标还有效吗** | 重新求解布局 |
| `VisualHash` | **画出来会不一样吗** | 重绘 |

按"结构变了吗"来理解会把三类东西漏在外面：布局提示改了间距、字体换了宽度、
端口挪了位置——都不改变图形拓扑，却都会让已算出的坐标失效。

名称保留为"结构"是历史原因，读的时候按"要不要重排"理解。

### 包含关系是构造出来的

视觉哈希先算结构部分的内容再追加外观部分，因此**结构哈希的每一次变化必然让视觉哈希变化**。
反过来不成立——只改颜色时结构不变而视觉变。

早先的实现里视觉哈希漏掉了节点的父级，于是"结构变了视觉却没变"。
那种不一致没有任何地方会报错，只能靠实现来保证。现在这条性质由构造方式保证。

### 具体覆盖

| 哈希 | 覆盖 |
|---|---|
| 结构 | `kind`、`direction`；节点 `id` / `parent` / `ports`；边 `id` / `from` / `to` / `fromPort` / `toPort`；组合 `id` / `parent` / `direction` / `collapsed` / `members`；字体全部字段；布局提示的间距与四类约束 |
| 视觉 | 结构部分的全部内容，加上节点 `label` / `shape` / `layer` / `styleToken` / `desc` / `richText` / `mathMode` / `style` / `text`；边 `label` / `style`；组合 `label` / `style` / `localLayout`；标签、文本预设、图层、页面、调色板、画布设置 |

### 三样刻意不覆盖的东西

1. **版本号**。覆盖它会让哈希随无关的版本推进而变化。
2. **约束的创建时间**。覆盖它，重复添加一条一模一样的约束就会触发一次全图重排。
3. **`actions` 与节点 `meta`**。它们只影响交互或由宿主自定义，改了不该引发重绘。

### 排序与规范化

按标识排序后拼接，保证与插入顺序无关（撤销后哈希必然回到原值）。

样式一类的子记录走序列化取规范化文本，**不是手写字段拼接**。
手写拼接的隐患是给记录加了字段却忘了同步改哈希函数，
于是那个字段怎么改都不会被判定为"画出来不一样"。走序列化则让新字段自动进入哈希。

## 一致性校验

`DiagramValidator.Validate` 做整体校验，用于加载外部文件、接收同步结果与测试断言——
这些入口不经过命令层。检查项：

| 码 | 含义 |
|---|---|
| `ID_DUPLICATE` | 标识在九个集合中重复 |
| `EDGE_FROM_MISSING` / `EDGE_TO_MISSING` | 边的端点不存在 |
| `EDGE_PORT_MISSING` | 边指定的端口在节点上不存在 |
| `COMPOSITE_PARENT_MISSING` | 组合的外层不存在 |
| `NODE_PARENT_MISSING` | 节点的父级不是已定义的组合 |
| `MEMBERSHIP_MISMATCH` | 成员列表与父级不一致 |
| `MEMBER_MISSING` | 组合的成员既不是节点也不是组合 |
| `TAG_MEMBER_MISSING` / `ACTION_TARGET_MISSING` | 标签成员或动作目标不存在 |

校验只报告不修改。发现问题时由调用方决定是拒绝加载、丢弃问题部分，还是照常打开并提示。

## 尚有缺口

| 项 | 说明 |
|---|---|
| `ApplySnapshot` | 语义待澄清。协作同步设计清楚之前不实现 |
| `PageDef` / `LayerDef` 的字段 | 方案只列出集合存在，未给字段。现取最小集合，等对应交互开工时再补 |
| 多态序列化的标识符 | 目前每个多态体系各自用 `$` 前缀字段名，尚未统一约定 |

## 尚未实现（Phase 2 ~ Phase 4）

- `NodeDef` 之外的富文本内容模型
- 端口的自动分配算法
- 布局提示到布局引擎的接线（约束补齐逻辑已在验证程序中跑通，尚未接进 Core）
