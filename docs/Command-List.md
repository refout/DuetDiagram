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
| `set-direction` | `SetDirectionCommand` | 方向是已定义的枚举值 | 方向是文档自己的属性、不是元素字段，所以单立一条。改成同一个值是 NoOp；变更明细的归属元素写文档标识——这次改动没落在任何元素上 |
| `set-spacing` | `SetSpacingCommand` | 两个间距至少给一个；给的那个必须是大于零的有限数 | 两个参数都可选，传空表示不动那一项。**每改一项发一条变更明细**：冲突判定按"哪个元素的哪个字段"算，粒度由变更明细决定而不由参数个数决定 |
| `set-place` | `SetPlaceCommand` | 两个节点都存在；主语与参照不是同一个；位置关系是已定义的枚举值 | 第四类布局约束，形状是**函数式关系**（同一对节点只该有一条），所以单列一条做设置与清除，而不并进按组增删的那两条。按归属方各留一条，同一归属方下再设一次是替换而不是追加。写入合并进 `LayoutHints`，不整份换掉 |
| `define-palette-entry` | `DefinePaletteEntryCommand` | 条目名非空；条目名不重复 | 重名报 `DUPLICATE_ID` 而**不覆盖**：覆盖会把一个正被几百个节点引用的令牌悄悄换掉外观，而调用方以为自己只是加了个东西。只计外观，不触发重排 |
| `update-palette-entry` | `UpdatePaletteEntryCommand` | 条目存在；字段名在已注册的调色板字段内（`palette.fill` / `palette.stroke` / `palette.text` / `palette.weight`）；值能解析成该字段要的类型 | 一次只改一个成员，与改节点、改边同一个形状。条目不存在时报 `PALETTE_ENTRY_MISSING` 而**不顺手新建**——顺手建会造出一个只有一半成员的条目 |
| `remove-palette-entry` | `RemovePaletteEntryCommand` | 条目存在；**没有元素还在用这个样式令牌** | 被引用时报 `PALETTE_ENTRY_IN_USE` 并把引用者写进载荷，界面据此把它们标出来。这与删边留下的悬空引用**刻意相反**：那边有整体校验器会报，这边渲染层只是静默兜底，没有任何东西会报 |
| `create-composite` | `CreateCompositeCommand` | 标识非空且**在九个集合里都没被占用**；外层存在；成员都存在；成员不含自己；不超深度上限 | 四种组合（分组 / 泳道 / 子流程 / 组合框）合成这一条，种类由记录类型表达。成员会被**从旧容器里摘出来**再挂到新组合上——一个节点只能属于一个组合。归属是两处表达（成员列表 + 父级字段），两处一起写 |
| `dissolve-composite` | `DissolveCompositeCommand` | 组合存在 | 成员回到**父级**而不是顶层。成员在父级成员列表里的落点是原组合占的那一位——一律追加到末尾会改变条带次序，而这件事界面上看不出异常。只动归属，节点与组合一个都不删 |
| `move-into-composite` | `MoveIntoCompositeCommand` | 成员存在（节点或组合）；目标存在（传空表示搬到顶层）；目标不是它自己也不是它的后代；整棵子树不超深度上限 | 归属的唯一入口。直接改父级字段会留下一份自相矛盾的文档。成环不挡的话布局会无限递归，而栈溢出的现场离这条命令很远 |
| `create-layer` | `CreateLayerCommand` | 标识非空且**在九个集合里都没被占用** | 次序由命令算出来（当前最大值加一），不由调用方给——调用方手上那份列表可能已经过期，它算出来的次序可能与现有某个图层撞上 |
| `rename-layer` | `RenameLayerCommand` | 图层存在 | 只改名字，不动标识与次序。标识是引用它的那个字段写的东西，改它会让所有引用一起失效 |
| `reorder-layer` | `ReorderLayerCommand` | 图层存在 | 改的是**次序字段**而不是集合位置：图层集合在视觉哈希里按标识排序后遍历，集合位置进不了任何哈希。次序值整体重排成连续的 0、1、2……，顺带治好从文件里读进来的重复次序 |
| `set-layer-visible` | `SetLayerVisibleCommand` | 图层存在 | 藏起来只是**不画**，不改坐标：被藏起来的元素仍然参与布局，连线仍然绕着它走。让它退出布局的话，藏一个元素会让整张图重排。与 `set-layer-locked` 分开两条，因为字段名要去字段注册表里查，而图层这三个字段都不在那张表里。只计外观 |
| `set-layer-locked` | `SetLayerLockedCommand` | 图层存在 | 锁定的元素**照常画出来，但点不中、改不了**。与"藏起来"合成一个开关的话，「我想看着它但别动它」就表达不出来。写入被拒时报 `LAYER_LOCKED`（不是 `LAYER_FORBIDDEN`——那是凭据够不着这一层）。只计外观 |
| `create-page` | `CreatePageCommand` | `id` 非空且**在九个集合里都没被占用** | 次序由命令算出来（当前最大值加一），不由调用方给——调用方手上那份列表可能已经过期。只计外观，不触发重排 |
| `delete-page` | `DeletePageCommand` | 页面存在；**不是最后一页** | 最后一页删不得：页面集合为空之后渲染层无页面可画，而那不是一次「删掉了一个东西」能解释的状态。撤销时把整份集合换回去，被删的那一页要插回原来那一格——追加到末尾会改变次序，而两个哈希都按标识排序，这个错在哈希上看不出来 |
| `add-tag` | `AddTagCommand` | `id` 非空且**在九个集合里都没被占用**；成员都存在 | 标签与元素的关系是**单向**的：成员列表挂在标签自己身上，元素上没有回指字段。所以不存在的成员不会被任何别的地方发现，必须在写入前挡住。标签色取令牌名但不检查令牌是否存在，否则「先打标签、后定义令牌」就做不到了 |
| `remove-tag` | `RemoveTagCommand` | 标签存在 | **不需要顺带摘掉任何引用**：删掉的就是那份成员列表本身，不存在悬空引用。要删的标签不在时报 `TAG_MISSING` 而不是无操作——那通常说明调用方手上那份列表过期了 |
| `add-action` | `AddActionCommand` | `id` 非空且**在九个集合里都没被占用**；目标存在（目标允许为空） | **唯一一条两个变更标志都报假的命令**：动作不进任何哈希，加一条动作既不改变坐标也不改变像素。两个标志都假**不等于无操作**——版本照推、历史照进、广播照发，变的只是宿主那一侧不必重排也不必重绘 |
| `remove-action` | `RemoveActionCommand` | 动作存在 | 与新增同一口径：两个标志都假，且没有悬空引用要清理——目标引用挂在动作自己身上，被指向的元素上没有回指字段 |
| `set-kind` | `SetKindCommand` | 图类型是已定义的枚举值 | 图类型是文档自己的属性，不在元素字段表里。它算**结构**变更：换了类型之后分层的语义与可用形状都可能变。改成同一个值是 NoOp |
| `set-canvas-settings` | `SetCanvasSettingsCommand` | 六项至少给一项；枚举值已定义；网格尺寸与纸张宽高都是大于零的有限数 | 六项各自可选，传空表示不动那一项——整份替换会逼调用方先把其余五项读回来再写回去，而那份值可能已经过期。**每改一项发一条变更明细**：一边调网格、一边调背景色是两次互不相干的修改。背景色用**空串表示清除**、空引用表示不动。只计外观 |

**结构还是纯外观，取决于被改的值进的是哪个哈希。**

- 改布局（方向、间距、四类约束）与增删元素都是 `StructuralChanged = true`、`VisualChanged = true`。
  这些值进的是结构哈希，而那个哈希要回答的正是"要不要重新求解布局"——
  报成纯外观的话，宿主会只重绘不重排，而画面上的坐标根本没跟着约束变。
- 改调色板、画布设置、页面、标签都是 `StructuralChanged = false`、`VisualChanged = true`。
  它们不改变节点尺寸，已算出的坐标仍然有效；报成结构变更的话，换一次主题、
  加一页、打一个标签都要把整张图重排一遍。
- **动作是两个标志都假。** 它不进任何哈希：不影响坐标，也不影响像素，只影响交互。
  报成视觉变更会让宿主白白重绘一次，而画面上一个像素都不会变。
  注意"两个标志都假"**不等于无操作**：版本照推、历史照进、广播照发，
  变的只是宿主那一侧不必重排也不必重绘。要不要触发副作用的判断一律用
  `IsEffectiveSuccess`，它只看 `IsNoOp`，不看那两个标志。
- **组合的归属进结构哈希，图层只进视觉哈希。** 归属变了要重排；而图层现在只是文档里的一条记录，
  渲染层还没有读它，所以加一个图层、给图层改名、把图层挪个位都不改变任何坐标。
- **图类型进结构哈希。** 它决定分层的语义与可用形状，不是纯外观。

**还有一类不是失败的成功：`IsNoOp`。** 命令合法、但没什么可做（值没变、要删的本来就不在）。
它算成功，但不推进版本、不进历史、不广播。所以真正要触发副作用的判断用
`IsEffectiveSuccess` 而不是 `IsSuccess`。

> 边的**折点**（bend point）不在命令层里。折点是用户「拖这儿」的产物，与文档结构无关；
> 走命令总线会把一次拖动塞进几百条 IR 记录，撤销栈失真。折点写在 sidecar 的 `pinnedEdges`，
> 由宿主侧直接读写（快照式撤销/重做），不进命令总线——与 P2-06 的 `pinnedNodes` 同一口径。
>
> **节点的固定位置同样不在命令层里。** IR 里没有任何地方能存节点的绝对坐标：
> 节点的字段里没有位置，布局提示的四个列表全是相对约束。DSL 的 `pin` 意图也是落到
> sidecar 的 `pinnedNodes` 的，理由是**坐标属于渲染结果**——存进 IR 会让同一份语义
> 在不同机器上产生不同的文档内容。

## 计划中（Phase 1 P1-03，方案称 40+ 条）

> **这张表是目标，不是承诺，而且它已经不准确了。** 实际创建以任务 YAML 为准，
> 每完成一条就在本表登记实现状态。
>
> 已知的不准确有五处，都是 2026-09-22 起草 Phase 3 任务时对着代码核出来的：
>
> - **节点与样式那两组里的多数条目不需要新命令。** P1-05 的字段注册表落地之后，
>   `set-node-field` 按字段名分发，label、shape、parent、layer、styleToken、style、
>   text、ports、richText、mathMode、desc、meta 都在它名下；边只有 label 与 style 两个。
>   所以 `update-node-label`、`move-node-layer`、`set-node-shape`、`apply-style-token`、
>   `set-node-style`、`set-text-style`、`set-edge-style` 都不必各立一条命令。
>   判断标准是**被改的值挂在元素上还是挂在文档上**：挂在元素上的由字段表覆盖。
> - **`assign-layer` 同样被字段表覆盖。** `NodeDef.Layer` 登记在节点名下，
>   `NodeFieldValue` 里有它的读写分支，属性面板也认它。再立一条命令，
>   同一个效果会有两条路。注意它与 `move-into-composite` 的区别：
>   那个父级字段是**冗余的那一份**，真正说了算的是容器的成员列表，
>   所以归属要有一条自己的命令；图层归属只有一个存放处，走字段表就够了。
> - **`add-edge-waypoints` 与 `set-edge-route` 不该有命令。** 折点写在 sidecar 的
>   `pinnedEdges`，不进 IR、不走命令总线——理由是折点是用户「拖这儿」的产物，
>   走总线会把一次拖动塞进几百条 IR 记录、撤销栈失真。
> - **`reorder-node` 现在不该有命令。** 节点集合顺序进不了结构哈希也进不了外观哈希
>   （两处都先把集合按标识排序再遍历），不改变布局坐标（同层内按横坐标排序、并列时
>   用标识断掉），也因为布局保证同层不重叠而看不出绘制次序。它要成为真功能，前提是
>   先给节点加一个 z 序字段并定义它在绘制里的语义，那是 IR 的改动。判断标准是
>   **被改的值挂在元素上还是挂在文档上**之外的第三条：**这个改动有没有人能看见**。
> - **`pin-node` 与 `unpin-node` 不该有命令。** IR 里没有任何地方能存节点的绝对坐标：
>   `NodeDef` 的字段里没有位置，`LayoutHints` 的四个列表全是相对约束。DSL 的 `pin`
>   意图早就落到 sidecar 的 `pinnedNodes` 了，理由是**坐标属于渲染结果**——
>   存进 IR 会让同一份语义在不同机器上产生不同的文档内容。命令层再立一条 pin
>   会与这条原则直接冲突，同一个节点还会因此有两个互相矛盾的固定位置。
>
> 剩下的条目落在 `tasks/phase3/P3-01` ~ `P3-04`，四条都已落地，下面的表已经没有待办。

按工具层的 action 分组，便于 `diagram_edit` 内部按 action 分发。
**做完的从这里划掉、挪进「已实现」表**，两边同时改：

| 分组 | 还缺的命令（`CommandId`） |
|---|---|
| 节点 | 无。`update-node-label`、`move-node-layer`、`set-node-shape` 已由字段表覆盖（见上）；`reorder-node` 待 IR 加 z 序字段（见上） |
| 边 | 无。`set-edge-route`、`add-edge-waypoints` 不该有命令（折点走 sidecar，见上） |
| 样式 | 无。`apply-style-token`、`set-node-style`、`set-text-style`、`set-edge-style` 四条都已由字段表覆盖（见上） |
| 布局 | 无。`pin-node`、`unpin-node` 不该有命令（见上） |
| 组合 | 无。四类合成一条 `create-composite`（P3-03）；`dissolve-composite` 与 `move-into-composite` 已落地 |
| 图层 / 页面 | 无。图层三条（P3-03）与页面两条（P3-04）都已落地；`assign-layer` 由 `set-node-field` 的 `layer` 字段覆盖（见上） |
| 调色板 | 无。三条已落地（P3-02） |
| 标签 / 动作 | 无。四条已落地（P3-04） |
| 文档 | 无。`set-kind` 与 `set-canvas-settings` 已落地（P3-04） |

**命令层到此补齐。** 还差的不是命令，是三类东西各自的界面入口与消费方：
图层已经有命令也有渲染消费（`set-layer-visible` / `set-layer-locked` 两条，
渲染按它们决定画不画、点不点得中），缺的是把它摆出来给人点的面板；
页面的翻页与缩略图、标签与动作在画布上的呈现还没有接。
它们要么是 IR 字段已就位而界面未接，要么是渲染层尚未消费，都不该在这里凭空造一条命令。

## 工具层的动作对照

工具层的每个动作落到一条命令上。下表与 `ActionTable` 里的声明逐行对应，
由 `Category=ToolDispatch` 的用例两边核：表里的动作名要与代码里的动作表完全一致，
`CommandId` 必须在上面「已实现」表里出现过。两边分头维护的话，加了一条命令而忘了挂动作，
表现是模型调用时得到「未知动作」——而命令本身是好的，查起来要绕一大圈。

| 工具 | 动作 | `CommandId` |
|---|---|---|
| `diagram_edit` | `add-node` | `add-node` |
| `diagram_edit` | `remove-node` | `remove-node` |
| `diagram_edit` | `connect-edge` | `connect-edge` |
| `diagram_edit` | `disconnect-edge` | `disconnect-edge` |
| `diagram_edit` | `reconnect-edge` | `reconnect-edge` |
| `diagram_edit` | `set-node-field` | `set-node-field` |
| `diagram_edit` | `set-edge-field` | `set-edge-field` |
| `diagram_edit` | `create-page` | `create-page` |
| `diagram_edit` | `delete-page` | `delete-page` |
| `diagram_edit` | `create-layer` | `create-layer` |
| `diagram_edit` | `rename-layer` | `rename-layer` |
| `diagram_edit` | `reorder-layer` | `reorder-layer` |
| `diagram_edit` | `add-tag` | `add-tag` |
| `diagram_edit` | `remove-tag` | `remove-tag` |
| `diagram_edit` | `add-action` | `add-action` |
| `diagram_edit` | `remove-action` | `remove-action` |
| `diagram_edit` | `set-kind` | `set-kind` |
| `diagram_style` | `set-shape` | `set-node-field` |
| `diagram_style` | `set-style` | `set-node-field` |
| `diagram_style` | `set-text` | `set-node-field` |
| `diagram_style` | `set-canvas` | `set-canvas-settings` |
| `diagram_style` | `define-palette-entry` | `define-palette-entry` |
| `diagram_style` | `update-palette-entry` | `update-palette-entry` |
| `diagram_style` | `remove-palette-entry` | `remove-palette-entry` |
| `diagram_layout` | `set-direction` | `set-direction` |
| `diagram_layout` | `set-spacing` | `set-spacing` |
| `diagram_layout` | `add-constraint` | `add-layout-constraint` |
| `diagram_layout` | `remove-constraint` | `remove-layout-constraint` |
| `diagram_layout` | `set-place` | `set-place` |
| `diagram_composite` | `create` | `create-composite` |
| `diagram_composite` | `move-into` | `move-into-composite` |
| `diagram_composite` | `dissolve` | `dissolve-composite` |

三个成员动作（形状、样式、文本）落在同一条命令上，区别只在前缀：`set-style` 认
`style.` 开头的成员，`set-text` 认 `text.` 开头的。不查前缀的话，把 `text.fontSize`
传给 `set-style` 也会成功，而两个动作各自的说明就成了一句空话。

`add-constraint` / `remove-constraint` 与它们的命令标识不同名，这是有意的：动作名比
`add-layout-constraint` 短，而命令标识带 `layout` 前缀是为了在命令清单里与别的增删区分开。
三类约束由 `kind` 给出，形状不同：同层与对齐是一组平级节点，层内次序是一个主语加它的出边。

**`diagram_layout` 与 `diagram_composite` 里没有 pin / unpin，也没有折点。** 固定位置写在
sidecar 的 `pinnedNodes`、折点写在 `pinnedEdges`，两者都不进 IR、不走命令总线。给它们开
action 的话，那条 action 要么绕开总线直接改 sidecar（于是撤销栈与广播都不经过），
要么给它们立一条命令（于是推翻已有的决定）。

**动作数少于命令数，这是对的。** 节点与边的属性各合成一条按字段名分发的命令，
所以「改标签」「改形状」「套令牌」「改字号」在命令层是同一条。工具层不把它拆回去——
拆回去之后同一个效果会有两条路，而其中一条不进结构哈希。

**有三个工具不在这张表里：`diagram_export`、`diagram_validate`、`diagram_undo_redo`。**
它们的参数不是 `action`，也不发任何一条命令：导出与校验是只读的纯函数，
撤销重做走的是命令总线的历史栈。表里每一条都对应一条命令，所以它们没有位置——
硬塞进来的话，「表里的 CommandId 必须在已实现表里出现过」那条核对会要求它们指向某条命令，
而它们没有。

## 新增命令的检查清单

见 `AGENTS.md`「新增一个命令的检查清单」。核心三条：

1. `Validate` 返回结构化 `CommandError`，不要在 `Apply` 里抛异常表达业务失败。
2. `CaptureMemento` 与 `Apply` 对同一文档必须给出**一致**的索引/位置。
3. 新增 Memento 记录 → 补 `[JsonDerivedType]`，否则 AOT 下反序列化失败。
