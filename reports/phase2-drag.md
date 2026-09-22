# Phase 2 节点拖拽与固定

- 验收时间：2026-09-22
- 运行时：.NET 10.0.11，x64 RyuJIT（x86-64-v3），.NET SDK 10.0.303
- 用途：Phase 2 拖拽判据的取证（单帧拖拽处理 ≤ 16 ms、拖动中不发命令、松手只落定一次、重叠落点被挡）

## 复现

```bash
# 命中测试：点中节点/边、点中空白、重叠时谁在上、不受视口剔除影响
dotnet test --project DuetDiagram.Render.Tests/DuetDiagram.Render.Tests.csproj -- --filter-trait "Category=HitTest"

# 拖拽：无命令、只定一个、被拒、整次拖拽算一步撤销、画布预览偏移
dotnet test --project DuetDiagram.E2E.Tests/DuetDiagram.E2E.Tests.csproj -- --filter-trait "Category=Drag"

# 单帧拖拽处理耗时：一千节点上开一条拖拽、连续两百次采样每帧处理拖拽输入的中位
dotnet run --project DuetDiagram.App -c Release -- --benchmark-drag --nodes 1000 --samples 200
```

## 验收口径达成情况

| 口径 | 要求 | 实测 | 余量 |
|---|---:|---:|---:|
| 单帧拖拽处理耗时 | ≤ 16 ms | **11.321 ms**（中位，1000 节点 / 200 样本） | 1.4 倍 |
| 拖动中不发命令 | 总线历史条目数不变 | `Category=Drag` **5/5**，含「移动中历史不变、松手前固定集合为空」 | — |
| 松手只落定一次 | 固定恰好一个节点、重布局恰好一次 | `Releasing_a_drag_pins_exactly_one_node`：PinnedCount == 1 | — |
| 重叠落点被挡 | 落点与另一固定节点重叠则拒绝 | `A_drop_that_overlaps_a_pinned_node_is_rejected`：Rejected、固定集合不增 | — |
| 整次拖拽算一步撤销 | Undo/Redo 一次退回/恢复 | `Undo_reverts_a_drag_as_one_step`：Pinned 增删一致 | — |
| 命中测试 | 全通过 | `Category=HitTest` **10/10** | — |

## 设计

固定位置是用户「放这儿」的产物，与文档结构无关，因此**只在 user sidecar 的 `pinnedNodes`、
不进 IR、也不走命令总线**。一次节点拖拽由宿主在松手时写入固定位置并触发一次重布局，
而不是发一条改坐标的 IR 命令。这样：

- 「拖动中不发命令」直接用总线历史条目数不变来验；
- 「松手只落定一次」用「固定恰好一个节点、重布局恰好一次」来验；
- 撤销/重做把整次拖动当成一步——`DiagramSession` 持两份 `PinSnapshot` 栈
  （`_pinUndo` / `_pinRedo`），与命令总线历史完全独立。

### 一条拖拽的链路

| 环节 | 谁负责 | 关键约定 |
|---|---|---|
| 按下点选 | 画布 → `DragController.Press` → `DiagramSession.BeginDrag` | 先问命中测试器「点到了谁」；`SelectionSet.ResolveDragSet` 决定这一拖动的是谁：非加选只动被点中的，加选模式下点中已在集合里就整批拖 |
| 移动预览 | 画布 → `DragController.Move` → 视图模型 `UpdateDrag` | 只把被拖元素的绘制指令整体偏移一个 `DragDelta`；布局与文档都不碰。边只重算与被拖节点相连的那些 |
| 松手落定 | `DragController.Release` → `DiagramSession.CommitDrag` | 用松手时的偏移算出落点，与「其他已固定节点」比重叠；不重叠才写 `pinnedNodes` 并重布局，返回 `DragCommit.Pinned`；重叠返回 `Rejected`；偏移为零返回 `Ignored` |
| 取消 | `OnPointerCaptureLost` / `OnLostFocus` → `CancelDrag` | 指针被抢走（弹菜单、失焦）时清掉预览，不写任何固定位置 |

### 重叠在写入侧挡

`WouldOverlapPinned` 只比**其他已固定节点**：自动布局出来的位置随时会变，拿它当判据会让
一次正常的拖动莫名其妙被拒；固定位置是用户明确「放这儿」的，两个固定节点互压才是真矛盾。
这份检查是拖拽的一部分，不是布局的职责——布局阶段只能如实报出无解的输入，能在入口挡住的
就不该留到出口。判据用 `PlacedNode.Overlaps`，边框相切不算重叠。

### 预览只搬被拖元素

视图模型在帧首把 `_draggedElements` 里的绘制指令偏移 `_dragDelta`，其余原样。偏移量在文档
坐标里，与视口无关——视口换算仍由那一处纯函数管，拖拽不另立一套换算。`SelectionBounds`
在拖拽进行中同样按 `_dragDelta` 平移，选中框跟着手指走。

## 基准数据

一千个节点、968 条连线。开一条针对 `start` 节点的拖拽，每帧把偏移推进一小段，连续两百次
采样 `CanvasViewModel.BeginFrame` 在偏移下的处理耗时（预热后取中位）：

| 运行 | 单帧拖拽处理中位 |
|---|---:|
| 实测 | 11.321 ms |

单帧处理只做「把被拖元素的指令整体偏移、其余原样」这一件纯计算，与节点总数无关——
规模化后的耗时来自布局与绘制列表构建，那是一次性的，不在每帧里。所以这条基准量的是
「拖拽这一帧有没有偷偷多做事」，而不是「千节点渲染有多快」（那一条在 `--benchmark-frames`）。

## 已知取舍与未验证

| 项 | 说明 |
|---|---|
| 固定位置不走命令总线 | 原任务设想里有一条「拖动落定与 pin 的命令」，未采用。理由见 `tasks/phase2/P2-06-node-drag.yaml` 的 `scope_note`：走命令总线会把一次拖动塞进几百条 IR 记录，撤销栈失真；绕开后两个界面完全对等 |
| 画布上的选中高亮还没做 | P2-06 把点选接到命中测试、选中可驱动拖拽，但选中元素在画布上还没有可见的高亮框，那一项在变更高亮（P2-08） |
| 重叠只比已固定节点 | 不比自动布局位置，是有意的：自动布局位置会变，当判据会让正常拖动被误拒 |
| 命中测试的取证 | `Category=HitTest` 在上一轮落地，本轮不重复造；这一轮只接上「画布指针 → 命中测试器 → 选中/拖拽」这一桥 |
| 原生编译下的表现 | 本机缺 C++ 工作负载，AOT 发布没能验收。拖拽链路是纯计算 + 界面事件转发，风险在界面框架那一侧 |
