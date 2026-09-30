# Phase 5 导出——界面上终于有地方选格式、范围与倍数

- 测量时间：2026-09-30
- 测量机器：AMD Ryzen 5 6600H（6 物理核 / 12 逻辑核），16 GB
- 系统：Windows 11 专业版 10.0.26200
- 运行时：.NET 10.0.11，x64 RyuJIT，.NET SDK 10.0.303
- 用途：按用户「完善导出功能」的要求，把界面上那条永远拒绝的占位条目换成真的导出对话框
- 基线提交：`9522bfb`（下称「改前」）

## 复现

```bash
dotnet build DuetDiagram.slnx -c Release

dotnet test --project DuetDiagram.E2E.Tests -c Release --no-build -- --filter-trait "Category=Export"
dotnet test --project DuetDiagram.E2E.Tests -c Release --no-build
dotnet test --project DuetDiagram.Llm.Tests -c Release --no-build -- --filter-trait "Category=ExportTool"
dotnet test --project DuetDiagram.Llm.Tests -c Release --no-build -- --filter-trait "Category=ToolSchema"
dotnet test --project DuetDiagram.Mcp.Tests -c Release --no-build -- --filter-trait "Category=SkillDisclosure"
dotnet test --project DuetDiagram.Core.Tests -c Release --no-build -- --filter-trait "Category=CommentDiscipline"
dotnet run --project tools/LocCounter -c Release -- --root "$PWD" --check
```

**注意：本机 `dotnet test` 不能带 `--nologo`**，带了之后参数被当成未知项转给测试应用，
结果是「运行了零个测试」加退出码 5。另外**不要与别的 `dotnet` 命令并行跑**：
并行时汇总里会凭空多一次失败。

## 要解决的是什么

界面上那条「导出…」从 Phase 2 起就是一条**永远拒绝的占位条目**——它点下去只在状态栏上留一句
「还没接上」。而同一时刻，四档格式的导出器（SVG / PNG / PDF / DSL）、工具那条通路
（`diagram_export`）与脱屏自检都已经能用了。缺的一直是界面这一层：**选文件、选格式、选范围**。

这一条与 P5-11 是同一件事的两半。用户对范围的答复是「两边都给」：

1. 界面给格式 / 范围 / 倍数；
2. 工具那条路同时加参数，不出现「界面能设、工具不能设」的偏差。

P5-11 落了共用的词汇与工具参数，这一条落界面。

## 哪个格式认哪个旋钮

这张表只写一份，在 `DuetDiagram.Core/Model/ExportRequest.cs` 里：

| 格式 | 范围（content / page） | 倍数 |
|---|---|---|
| `dsl` | 不认 | 不认 |
| `svg` | 不认 | 不认 |
| `png` | 认 | 认 |
| `pdf` | 认 | 不认 |

**为什么 svg 两个都不认。** `SvgOptions` 只有 `IncludeBackground` 与 `Padding`——
矢量图的画布就是内容外接框，两种范围对它是同一件事。

**为什么 pdf 只认范围、不认倍数。** PDF 的单位是物理长度（点），换算系数由单位的定义定死，
放大它的办法是换纸张尺寸，不是乘一个数。

**给了不认它的格式怎么办：拒绝，并指出该换成哪个格式。** 收下参数再默默按缺省值出图的话，
调用方会以为自己的选择生效了。界面上那一侧对应的是「灰掉」——同一个判据的两种说法：
工具那条路没有界面可以灰，只能拒绝并说清。

## 改了什么

| 文件 | 改动 |
|---|---|
| `DuetDiagram.App/Controls/ExportDialog.axaml` | 新建。格式、范围、倍数三个控件加一行提示与两颗按钮；每个交互控件都带 `AutomationProperties.Name` |
| `DuetDiagram.App/Controls/ExportDialog.axaml.cs` | 新建。只把选择收上来（`ExportRequested`），不自己选文件也不自己写文件 |
| `DuetDiagram.App/Services/ExportService.cs` | 新建。收现成的绘制列表、文档与固定位置，把这一页写成文件；先写 `.tmp` 再覆盖式改名 |
| `DuetDiagram.App/MainWindow.axaml` | 浮层那一格加一个 `ExportDialog`，默认收起来 |
| `DuetDiagram.App/MainWindow.axaml.cs` | 加 `BeginExport` / `ChooseAndExport` / `Export` 与两个后缀映射；选完文件之后那条路是**同步的**，用例走的正是它 |
| `DuetDiagram.App/Services/MenuEntries.cs` | 那一条从「永远拒绝」改成「永远可点」，动作换成摆出对话框 |
| `DuetDiagram.App/Resources/Strings.cs` + 两个 `.resx` | 加 10 条键；删掉 `menu.refusal.export`（那一条再也没有拒绝的理由） |
| `DuetDiagram.E2E.Tests/ExportTests.cs` | 新建。10 条，见下 |
| `DuetDiagram.E2E.Tests/HeadlessFixture.cs` | 加 `ExportDialog(window)` 取用助手 |
| `DuetDiagram.E2E.Tests/I18nTests.cs` | 「第二语言缺一条键就回落」那条改成「两个资源文件都没有的键要报错，而不是给一片空白」 |
| `DuetDiagram.E2E.Tests/ToolBarTests.cs` | 「点一下要么变、要么说话」那份快照补上「对话框开着与否」 |

**删掉 `menu.refusal.export` 的连带影响。** i18n 那条「英文缺一条就回落到中性资源」的判据
原来靠这条键来证（英文资源里故意留空）。这条键删掉之后英文资源是完整的（96 = 96），
**没有一条键再能演示那种回落**，所以那条用例改成验另一件事：两个资源文件里都没有的键
要抛 `MissingManifestResourceException`，而不是给出一片空白——空白在界面上与「这里本来就没字」
分不开。**「英文缺键回落」这件事现在没有用例**，如实记在下面。

## 判据

**端到端 `Category=Export`，10 条。**

对话框那三条：

- `The_export_dialog_offers_every_format`——四档格式与两种范围都列得出来
- `A_format_that_cannot_honor_a_knob_has_it_disabled_in_the_dialog`——逐档断言范围与倍数
  可不可用，判据取自 `HonorsRange` / `HonorsScale` 而不是写死的表
- `The_scale_box_refuses_a_number_that_is_not_positive`——`0` / `-1` / `两倍` / 空串四档都要灰掉按钮并写一句为什么

真的写出文件那七条：

- `Exporting_svg_writes_an_openable_file`——产物里有 `<svg` 与 `</svg>`
- `Exporting_png_honors_the_scale`——二倍图的像素宽高正好是内容的两倍
- `Exporting_png_honors_the_page_range`——按纸张裁时像素尺寸等于文档声明的那张纸
- `Exporting_pdf_writes_a_pdf_and_the_range_changes_it`——两份都是 `%PDF`，且两种范围的字节不同
- `Exporting_dsl_carries_the_pinned_positions`——先拖一下钉住一个节点，再导，文本里要有 `pin`
- `The_export_entry_is_enabled_in_a_read_only_window`——另一个进程拿着文档时那一条仍可点，
  点一下对话框真的摆出来
- `A_failed_export_says_so_on_the_status_bar`——拿一个普通文件当目录用，失败要说出来

**范围与倍数按像素尺寸断言，不按「请求传到了宿主」。** 后者的话，宿主把参数丢掉不用的实现
照样能过，而用户拿到的是一张没放大的图。这一条是这一轮里唯一有牙的判据形态。

## 「把实现撤掉它就红」

验过两次，两次都只改实现、用例一个字不动：

| 撤掉什么 | 跑哪一类 | 结果 |
|---|---|---|
| `ExportService` 里范围与倍数的映射（`Scale` 写死 1、`Crop` 写死 content） | `Category=Export` | 10 条里红 3 条 |
| 菜单条目的拒绝改回「只读时拒绝」 | `Category=Export` | 10 条里红 1 条 |

第一次红的三条正是量像素的那三条（倍数、PNG 的页范围、PDF 的两种范围不同），
第二次红的是只读窗口那一条。两处都复原后 10/10。

## 撞到的两条门禁，都是真的

**一、`Category=SkillDisclosure`：`diagram_export` 的描述超了长度上限。**
上限是两百字，而那条描述算下来 276——它每一轮都在模型的上下文里、八条一起算。
修法是把范围与倍数那句话从工具描述里拿掉，只留在各自的参数描述上：
模型填参数时读到的正是那里。**这是 P5-11 那一轮的账，在这一轮里还的。**

**二、`Category=ToolBar`：「点一下要么变、要么说话」红了。**
导出那一条点下去是摆出对话框，不改文档、也不进状态栏，而那份快照当时只看
文档版本、绘制列表版本、选中、手动布局、诊断面板与两个间距——浮层不在里面。
修法是**把对话框开着与否加进快照**：它本来就是「点一下之前与之后能观察到的东西」，
漏掉浮层是那份快照不全，不是那条条目没反馈。

## 两个对话框的坑

**一、`RefreshConfirmable` 读了自己的输出当输入。** 它按 `CanConfirm` 判断要不要重算，
于是按钮一旦被禁用就再也放不开。改成从格式与倍数本身重算。

**二、这个版本的 Avalonia 里，程序化赋值 `TextBox.Text` 不发 `TextChanged`。**
`SetScale("0")` 之后按钮仍然是可点的。定位办法是写一条探针直接调 `RefreshConfirmable()`——
它一调就过，于是问题不在判断逻辑而在事件没到。改成订阅 `PropertyChanged`
并按 `e.Property == TextBox.TextProperty` 过滤。

## 门禁读数（本机 Release）

| 门 | 读数 |
|---|---|
| `dotnet build DuetDiagram.slnx -c Release` | 0 警告 0 错误 |
| `Category=Export`（新） | 10 / 10 |
| 整份 `DuetDiagram.E2E.Tests` | 255 / 255（改前 245） |
| `Category=ExportTool` | 42 / 42（P5-11 落的，这一轮没动） |
| `Category=ToolSchema` | 28 / 28 |
| `Category=SkillDisclosure` | 7 / 7 |
| 整份 `DuetDiagram.Mcp.Tests` | 113 / 113 |
| `Category=CommentDiscipline` | 1 / 1 |
| `LocCounter --check` | 退出 0 |

CI 加了一条门禁：端到端作业里一条 `Category=Export`。
CI 运行号与结论见下面「补记」。

## 没验的

1. **选文件那一步没有真机走过。** 系统选择器在无头模式下打不开，用例走的是
   `MainWindow.Export(path, request)`——它与选完文件之后那条路是同一段代码，
   但「对话框长什么样、选完之后回不回得来」装置判不了。
2. **PDF 与 SVG 的产物只断言了文件头与「两种范围不一样」**，没有逐像素或逐指令比对
   （那两样在 `Category=SvgExport` / `Category=PdfExport` 里另有用例管）。
3. **「英文资源缺一条键就回落到中性资源」现在没有用例。** 那条用例原来靠
   `menu.refusal.export` 来证，这条键删掉之后英文资源是完整的，没有一条键再能演示那种回落。
4. **对话框在真机上的观感**：三个控件的宽度、灰掉之后够不够明显、提示那一行会不会挤。
5. **导出的图与屏幕上那一份是否逐像素一致**没有量——两条路都走
   `Session.Scene.DrawList`，但那是读代码得到的结论。

## 与任务清单的出入

`P5-11` / `P5-12` / `P5-13` 三条都不在方案 §13.9 那套编号里——它们由用户
「完善导出功能、保存功能」这一条要求派生，编号顺延 Phase 5 已有的号。
`P5-11` 与 `P5-12` 合起来是用户选定「两边都给」之后的完整落地；
`P5-13` 是「侧车接线」那一档，另见 `reports/phase5-sidecar.md`。

## 补记

推送后 CI 跑过一次：**两个作业都绿、无跳过的步骤**。

| 项 | 读数 |
| --- | --- |
| 运行号 | `36666457310`（提交 `b2db813`，首次尝试，9 分 17 秒） |
| `编译 + 测试 + 契约门禁`（ubuntu） | success，49 步，非 success 0 条、跳过 0 条 |
| `界面栈自检 + 端到端 + 原生发布`（windows） | success，35 步，非 success 0 条、跳过 0 条 |
| 新增的「导出门禁」（`Category=Export`） | 第 19 步，success |

导出门禁放在 windows 那一侧——它要起界面栈，ubuntu 上跑不了；同一次运行里
挨着它的「人工产物门禁」是 `P5-13` 那一条，读数见 `reports/phase5-sidecar.md`。

这条门禁**真跑到了**，不是「写进 workflow 就算数」——本仓 2026-09-28 之前
有 26 次运行全红、作业压根没起来过，所以这一条每次都要点名核对。
