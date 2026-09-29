# Phase 5 滚动条——去掉空格手势，横竖两条可拖动的滚动条

- 测量时间：2026-09-29
- 测量机器：AMD Ryzen 5 6600H（6 物理核 / 12 逻辑核），16 GB
- 系统：Windows 11 专业版 10.0.26200
- 运行时：.NET 10.0.11，x64 RyuJIT，.NET SDK 10.0.303
- 用途：按产品方的两条口径——「平移只留中键加滚动条」与「滚动范围留余量」——加两条滚动条，删掉空格手势
- 基线提交：`601c9cd`（下称「改前」）

## 复现

```bash
dotnet build DuetDiagram.slnx -c Release

dotnet test --project DuetDiagram.Render.Tests -c Release --no-build -- --filter-trait "Category=ScrollRange"
dotnet test --project DuetDiagram.Render.Tests -c Release --no-build -- --filter-trait "Category=Viewport"
dotnet test --project DuetDiagram.E2E.Tests    -c Release --no-build -- --filter-trait "Category=ScrollBar"
dotnet test --project DuetDiagram.E2E.Tests    -c Release --no-build -- --filter-trait "Category=Canvas"
dotnet test --project DuetDiagram.E2E.Tests    -c Release --no-build -- --filter-trait "Category=Marquee"
dotnet test --project DuetDiagram.E2E.Tests    -c Release --no-build -- --filter-trait "Category=Accessibility"
dotnet test --project DuetDiagram.Core.Tests   -c Release --no-build -- --filter-trait "Category=CommentDiscipline"
dotnet run --project tools/LocCounter -c Release -- --root "$PWD" --check
```

**注意：本机 `dotnet test` 不能带 `--nologo`**，带了之后参数被当成未知项转给测试应用，
结果是「运行了零个测试」加退出码 5——看着像整个测试套件坏了（所有工程都这样），其实是参数问题。
另外**不要与别的 `dotnet` 命令并行跑**：并行时汇总里会凭空多一次失败（本次实测到一次，
`Phase4ScenarioTests` 的 500 节点渲染从 3.0 秒抖到 3.31 秒，单独重跑 245/245 全绿）。

## 要解决的是什么

产品方先报「画布无法拖动」，接着要求「左键直接就可以拖动，去除空格键」。
查下来**左键在空白处拖动已经是框选**，而框选是「框选右键 → 创建分组 / 泳道 / 子流程」
在界面上唯一的入口（方案 §9.2），把它改成平移等于删掉那个入口。
产品方于是改口为「添加滚动条，上下的，左右的，可以拖动滚动条」，并确认两件事：

1. **平移只留中键加滚动条**——空格那一条整个删掉；
2. **滚动范围留余量**——图完整显示时拖滚动条也能挪动视图。

## 范围怎么算

每轴独立。记 `内容长` 为内容在这一轴上的文档长度、`视口长` 为 `Viewport.Width` / `Height`：

```
余量     = max(24, 视口长 / 4)                      // 24 是已有的适配留白
范围总长 = max(内容长 × 缩放, 视口长) + 2 × 余量
可滚距离 = 范围总长 − 视口长
位置     = clamp(范围总长 / 2 − (内容中心 × 缩放 + 平移量), 0, 可滚距离)
反解     = 范围总长 / 2 − 内容中心 × 缩放 − 位置
```

**为什么以内容中心为对称中心。** 按内容外接框算的话，图一装进窗口范围就是零、
拇指占满整条轨道、拖上去一动不动——而「图装得下」恰恰是最常见的那一档
（示例文档在默认的 1280×820 窗口里就装得下），用户会以为滚动条坏了。
以内容中心对称之后还得到一条好用的性质：适配把内容摆正中间，
所以**拇指必然落在轨道正中间**，与内容多大无关。这一条写成了第一个判据。

数值算例（`缩放 = 1`，余量 24）：

| 档 | 内容长 / 视口长 | 余量 | 范围总长 | 可滚距离 | 位置 | 说明 |
|---|---|---|---|---|---|---|
| 放大后内容远大于视口 | 5000 / 800 | 200 | 5400 | 4600 | 200 | 拇指占 14.8%，靠左 |
| 内容约等于视口 | 800 / 800 | 200 | 1200 | 400 | 200 | 居中 |
| 适配后内容远小于视口 | 100 / 800 | 200 | 1200 | 400 | 200 | **居中，不贴边** |
| 窗口改尺寸 | 752 / 800 → 1000 | 200 → 250 | 1200 → 1500 | 400 → 500 | 200 → 350 | 不重新适配，偏移不动 |

**为什么余量取四分之一视口。** 它让图完整显示时**视图还能移动半个视口**
（可滚距离正好是视口长的一半）——再小则拖上去像没反应，再大则内容能被整个推出屏幕、
用户找不回来。视口极小（不到 96 像素，即留白的四倍）时按比例算出来的余量没有意义，
那一档改用适配留白 24。

**为什么位置钳、平移量不钳。** 滚动条只能表达「到头了」，而视口可以停在任意位置：
中键把图拖出屏幕之后，条上的位置停在端点，视口该在哪儿还在哪儿。
钳在条自己身上（构造那条范围的时候）而不是钳在各个调用处——调用处每帧都会读一次，
漏掉任何一处，模型与控件就会各说一个数，而表现是拇指和视图对不上。
代价是：中键把视图拖出范围之后拇指钉在端点，此时去拖拇指会把视图拉回范围内、看上去跳一下。
这是「平移没有边界」的必然结果，换来的是中键永远能自由拖动；
另外它也顺手给了一条退路——图被拖到屏幕外之后，拖一下滚动条就能找回来。

## 改了什么

| 文件 | 改动 |
|---|---|
| `DuetDiagram.Render/ScrollRange.cs` | 新建。一条范围就是三个数：可滚距离、视口长、位置；三个数在构造处收进合法区间 |
| `DuetDiagram.Render/Viewport.cs` | 加 `HorizontalScrollRange` / `VerticalScrollRange` / `ScrollToX` / `ScrollToY` 与私有的 `Pad` / `SurfaceLength` / 正反解。**正反解共用同一份范围总长**，不然浮点上会分叉、拖动时拇指自己抖 |
| `DuetDiagram.App/ViewModels/CanvasViewModel.cs` | 加 `HorizontalScroll` / `VerticalScroll` 两个现算的读数与 `ScrollHorizontal` / `ScrollVertical` 两个写入口；`DrawList` 与 `Viewport` 两处通知各带上这两个名字 |
| `DuetDiagram.App/MainWindow.axaml` | 画布那一格分成两半：画布与全部浮层留在左上（显式写行列号），竖条占右边一条、横条占下边一条，`AllowAutoHide="False"` |
| `DuetDiagram.App/MainWindow.axaml.cs` | 两个 `FindControl`；`DataContext` 之后订阅两条 `Scroll` 写回模型 |
| `DuetDiagram.App/Controls/DiagramCanvas.cs` | 删掉空格那一套（见下）；可访问说明里「空格加左键拖动是平移」改成「中键拖动是平移」 |
| `DuetDiagram.E2E.Tests/HeadlessFixture.cs` | 加 `ScrollBar(window, orientation)` 取用助手，**按「模板父级为空」把左栏与导入报告里那两个滚动容器模板里的条排除掉** |

**空格那一套删了这些**：`_spaceHeld` / `_keyRoot` / `_keyWindow` 三个字段，
`OnAttachedToVisualTree` / `OnDetachedFromVisualTree` / `OnHostDeactivated` / `OwnsSpace` /
`OnHostKeyDown` / `OnHostKeyUp` 六个成员，`UpdatePanCursor` 简化成只看 `_panning`，
`wantsPan` 只剩中键，`OnLostFocus` 里的复位与那段注释，
以及 `using Avalonia.Interactivity;`。留下的私有字段会触发 CS0169，
在 `TreatWarningsAsErrors=true` 下是构建错误，所以字段必须一并删。
`CanvasSmokeTests` 删掉 6 条空格用例与 2 个只被它们用的 helper；
`AccessibilityTests` 里那句「画布上的空格操作要说出来」改成中键。

## 接法与回环

位置单向从模型来（界面标记里的绑定），用户拖条那一头由 `Scroll` 事件写回模型。

**订阅 `Scroll` 而不是 `ValueChanged`。** `Scroll` 只在用户动条的时候发，
模型把位置推回去时不发，环就断在那里；`ValueChanged` 是路由事件，程序化赋值也发，
用它就得自己防回环。就地编辑标签那个编辑器上的防回环标志（`_syncing`）之所以存在，
是因为 `TextBox.TextChanged` 对程序化赋值也发——滚动条不是这样，所以这里没有那个标志。

**没有抖动**：位置与视图互为精确逆运算（拖动期间范围总长不变），
所以回推的值与用户拖出来的值逐位相等，不会互相顶。

**`Value` 必须写 `Mode=OneWay`**：`RangeBase.ValueProperty` 的默认绑定模式是 `TwoWay`，
而 `ScrollRange.Value` 是只读的，不写 `OneWay` 会在运行期报绑定错误。

**`AllowAutoHide="False"` 是硬要求**，两个理由：默认值是两像素、悬停才展开的细线，
命中区域几乎没有，用户拖不动；关掉它之后条厚恒定，画布那一格不会因为鼠标进出而改尺寸，
而画布一改尺寸视口就跟着动（顺带也绕开了无头时钟不一定推进的 `ShowDelay` / `HideDelay` 计时器）。

## 判据

**渲染层 `Category=ScrollRange`，11 条**（8 个方法，其中一个跑三档）：

- `A_range_leaves_room_even_when_the_content_fits`——图装得下时可滚距离仍是 400 而不是 0
- `The_pad_falls_back_to_the_fit_margin_on_a_tiny_viewport`——视口 80 时余量回落到 24
- `A_larger_content_gives_a_longer_range_and_a_smaller_thumb`——内容 5000 / 视口 800 → 可滚 4600、拇指占 14.8%
- `An_unmeasured_viewport_does_not_produce_a_broken_range`——首次排布前宽高都是零时不产生 NaN 或负值
- `Fit_centers_the_thumb_on_both_axes`——内容小于 / 等于 / 大于视口三档，适配之后位置都等于可滚距离的一半
- `Scrolling_round_trips`——多个位置上正反解往返一致，且不改缩放
- `Panning_past_the_range_pins_the_thumb_without_clamping_the_offset`——平移远距离后偏移量不钳、位置落在端点
- `Resizing_changes_the_range_and_keeps_the_offset`——800 → 1000 时范围 400 → 500、偏移不动、位置 350
- `Zooming_recomputes_the_range_around_the_content`——放大之后可滚距离跟着变长，位置仍在范围内

**端到端 `Category=ScrollBar`，6 条**：

- `The_window_has_a_visible_horizontal_and_a_vertical_bar`——两条都在、都常显、都占得住（宽高大于 4 像素）
- `The_bar_range_matches_the_view_model`——条上的三个数与模型给的三个数一致
- `The_thumb_sits_in_the_middle_after_fit`——适配之后位置正好是可滚距离的一半
- `Moving_the_bar_moves_the_view`——按一格，视图位移正好一格
- `The_bar_stops_at_its_range`——从头滚到底正好挪过一整条范围，再滚一次不动
- `Moving_the_view_moves_the_bar`——中键平移之后条上的位置跟着走

**端到端不合成拖动拇指。** 条上那组「滚到顶 / 滚到底 / 翻页 / 走一格」的公开方法
同样会改位置并抛出 `Scroll`，走的正是「用户动条 → 模型」那条线，
而它们不受命中测试与拖动阈值的影响——本仓现有的合成拖动都作用在自绘的画布上，
没有拖动 `Thumb` 的先例，不值得为一个可替代的路径去赌主题细节。

## 「把实现撤掉它就红」

验过两次，两次都是只改实现、用例一个字不动：

| 撤掉什么 | 跑哪一类 | 结果 |
|---|---|---|
| 余量从「四分之一视口」改成只取适配留白 24 | `Category=ScrollRange` | 11 条里红 4 条 |
| 两条 `Scroll` 订阅 | `Category=ScrollBar` | 6 条里红 2 条 |

第二次顺手发现原有一条判据太松：`The_bar_stops_at_its_range` 原来只断言
「再滚一次位移不变」——而没接线的实现位移**压根不会变**，于是它照样绿。
改成先断言「从头滚到底要正好挪过一整条范围」之后才有牙。

## 门禁读数（本机 Release）

| 门 | 读数 |
|---|---|
| `dotnet build DuetDiagram.slnx -c Release` | 0 警告 0 错误 |
| `Category=ScrollRange`（新） | 11 / 11 |
| `Category=Viewport` | 26 / 26 |
| 整份 `DuetDiagram.Render.Tests` | 339 / 339（改前 328） |
| `Category=ScrollBar`（新） | 6 / 6 |
| `Category=Canvas` | 11 / 11（改前 17，删掉 6 条空格用例） |
| `Category=Marquee` | 12 / 12（框选一条都没碰） |
| `Category=Accessibility` | 10 / 10（`Tab_walks_into_every_panel` 也绿——两条滚动条不占 Tab 顺序） |
| 整份 `DuetDiagram.E2E.Tests` | 245 / 245 |
| `Category=CommentDiscipline` | 1 / 1 |
| `LocCounter --check` | 退出 0 |

CI 加了两条门禁：单元测试作业里一条 `Category=ScrollRange`（与视口、命中测试那几条并列），
端到端作业里一条 `Category=ScrollBar`（与节点拖拽门禁并列）。
CI 运行号与结论见下面「补记」。

## 没验的

1. **真机上拖滚动条的手感、拇指粗细、`AllowAutoHide="False"` 在各主题下是否都常显**——
   装置判不了，与前面几轮同一条口径。
2. **`SmallChange=40` 与 `LargeChange=一页` 两个数只是推测。** 常显之后条的两端会露出箭头按钮，
   而默认的一像素 / 十像素点一下几乎没有反应，所以给了两个数；具体多少合适要人上手。
3. **端点饱和那一下的观感。** 中键把视图拖出范围之后拇指钉在端点，
   此时拖拇指会把视图拉回范围内、看上去跳一下——这是「平移无边界」的代价，是设计选择而不是缺陷，
   但**没有人看着它跑过**。
4. **横向滚轮仍然整条丢掉**（`OnPointerWheelChanged` 在 `Delta.Y == 0` 时直接返回），
   垂直那一轴仍然是以光标为锚点的缩放。有了横向滚动条之后，
   触控板两指横滚要不要接管这一条更值得考虑了，但那是产品决定，这一条没碰。
5. **框选、拖节点、连线、右键菜单那几条手势一条都没动**，`Category=Marquee` 12/12 是旁证。

## 与任务清单的出入

卡里没写这一条（它由产品方在真机上用完一轮之后提出，不是从方案的任务集里派生的），
编号 `P5-10` 由本仓顺延 Phase 5 已有的号定。
它取代了 `P5-09` 修的那个手势：`reports/phase5-pan-modifier.md` 与
`tasks/phase5/P5-09-pan-modifier.yaml` 都**不回头改**，只在前者开头加一条「已被取代」的说明——
报告是历史记录，它记的是当时的事实与当时的读数。
「平移手势只有中键、没有空格」这一条差异登记进了 `AGENTS.md` 的差异表。

## 补记

（CI 运行号与结论在推送后补在这里。）
