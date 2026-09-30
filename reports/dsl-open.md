# 打开一份 DSL 绘图文件 — 验证结论

任务 `tasks/phase1/P1-19-dsl-open-as-document.yaml`，依赖 P1-16 / P1-17（DSL 的词法、语法与映射）、
P2-12（跨进程所有权与只读退让）、P4-19（把 Mermaid 当片段导入那条路）。状态 `done`。

这一轮同时把 **Mermaid 的导出方向整个删掉**（只留导入），理由与取证见
`reports/phase1-mermaid-export.md` 顶上的后补与 `AGENTS.md` 的「与规格文档的已知差异」表。

## 一、交付内容

### 两种「把别的东西弄进来」是两件事

| | 导入片段（P4-19） | 打开文档（本轮） |
|---|---|---|
| 入口 | 「导入 Mermaid…」菜单项 | 「打开…」菜单项、`Ctrl+O`、`--open <路径>` |
| 落到哪儿 | 拼进**当前**这份文档 | 新开一份**独立**文档 |
| 文档与总线 | 复用当前的 | 新的 `DiagramDocument` + 新的 `DiagramCommandBus` + 新的 `DiagramWorkspace` |
| 撤销 | 整份算一条命令，一次撤销退回 | 有自己独立的撤销栈 |
| 保存 | 存的是当前那份（不涉及被导入的文件） | 写回**打开的那一个文件** |

**为什么不合起来。** 导入要的是「把一段画好的东西接到我正在弄的图上」，打开要的是
「换一张图来弄」。合成一条路的话，用户没法表达「我只是想再拿一个片段」——每次都会
多出一份文档、多一个窗口。`MainWindow.Open` 与 `MainWindow.Import` 是两段代码，
各自的端到端用例分开验（`Category=Open` 与 `Category=Import`）。

### 读：一份 `.dsl` 读成一份 `DiagramDocument`

`DuetDiagram.App/Services/DslFile.cs` 的 `Read` 走 `DslParser.Parse` → `DslMapper.Map`，
与导入那条路共用格式层，差别只在两处：

- **`MappingOptions.Owner` 给 `ConstraintOwner.Human`。** 打开一份文件是人在界面上做的动作。
  给成模型的话，用户手工写的布局约束会被下一次自动重排冲掉——用户每次微调都会白做。
  有一条用例专门盯这一点（`A_same_rank_intent_lands_in_the_document_layout`）。
- **文档标识取文件名。** 文本描述的是图，不是文档，里面没有文档标识；`DocumentId` 由
  `Path.GetFileNameWithoutExtension` 给。

**「这不是 DSL」要自己判。** `DslParser` 对坏输入是宽松的——认不出的行记成诊断而不是抛异常，
于是任意文本都能解析出一棵（多半是空的）树。判据是
**「一条元素都没认出来，而且解析器还报着看不懂」**：

```
节点数 == 0 && 边数 == 0 && 组合数 == 0 && （文本空白 或 诊断数 > 0）→ 拒绝
```

只写了头部与方向的空文档是**合法**的，照常打开——坏文件与空文档不是一回事。
拒绝时抛 `InvalidDataException`，窗口不开，理由摆在报告面板上（一句带行号列号的话）。

读进来之后一律过一遍 `DiagramValidator.Validate`，与 `DocumentFile` 同一口径：
文件是外部输入，不经过命令层，而上一个进程还可能是写到一半被强杀的。校验只报告不修改。

### 固定位置在首帧就生效

`pin` 意图落到 sidecar 的 `pinnedNodes`。`DiagramSession.Shared(...)` 多接一个 `pins` 参数，
在**第一次算绘制列表之前**就把它们播进 `_pinned`，所以首帧里那个节点已经在文本写的位置上。

晚一步的话，第一次打开看到的图与文本写的不是一回事——用户以为 pin 没写对，于是再去拖一遍，
而拖出来的东西又和文本里那句不一样。有一条用例盯这一点
（`A_pin_in_the_text_is_already_in_the_first_frame`）：断言首帧布局里那个节点的左上角
落在 `pin a at 420, 200` 写的那两个数上。

固定位置**随文本走**（就是 `pin` 那一行），不写 `user.json`。所以 `.dsl` 与同名的
`<stem>.dgm` 共用 `<stem>.user.json` 这件事在本轮不冲突，语义写进了 `docs/Sidecar.md`。

### 写回：保存写回打开的那一个文件

`DslFile.Write` 把文档导出成 DSL 文本，写临时文件再同卷改名替换（与 `DocumentFile` 同一套
原子替换）。`MainWindow.Save()` 按后缀分流：`.dsl` 走 `DslFile.Write`，其余走 `DocumentFile.Write`。

写出去丢了几类，状态栏就带一句「有 N 类内容写不进去」——见下面第三节。

### 打开入口：后缀分流只写在一处

`DocumentLaunch` 多一个 `Dsl(path)` 工厂，与 `File(path)` 并列，形状完全一样
（`File.Exists` 检查 → `DocumentLock.Acquire` → 读 → 拿不到锁就退只读 → `Create`），
只是读的那一步换成 `DslFile.Read`。另加：

```csharp
public static DocumentLaunch FromPath(string path, ...) =>
    IsDsl(path) ? Dsl(path, ...) : File(path, ...);

public static bool IsDsl(string path) =>
    string.Equals(Path.GetExtension(path), ".dsl", StringComparison.OrdinalIgnoreCase);
```

**「按后缀挑读法」只写在 `FromPath` / `IsDsl` 一处。** `--open`、菜单选择器、
`MainWindow.Save` 三处都问它，抄成三份的话迟早有一处对不上，而对不上的表现是
「打开了、但保存写成了另一种格式」。

入口有三条：

- 菜单「打开…」（`file.open`，`Ctrl+O`，`MenuSurface.Menu`），排在「新建窗口」与「导入」之间；
- `MainWindow.BeginOpen()` → 选择器（`*.dsl` / `*.json` / `*.dgm`）→ `Open(路径)`；
- `--open <路径>` 在起界面之前就按 `FromPath` 定形态。

`MainWindow.Open(路径)` 返回新开出来的那个窗口（开不起来返回空，理由摆到报告面板上），
它与点菜单那条路是**同一段代码**——只有「文件从哪儿来」不同。选择器在无头模式下打不开，
所以能验的那一段全压在 `Open` 上。

### 报告：只在有话要说的时候摆

一份干净的文件打开之后，那个窗口本身就是结果；再叠一块面板说一句「打开了」只是噪音。
所以报告面板只在 `DslFile.Notes(report)` 非空时才摆——四类「文本与图对不上」的地方各说一句：

| 来源 | 说的是什么 |
|---|---|
| `Diagnostics` | 解析没看懂的地方，带行号列号 |
| `Renames` | 图上那个标识与文本里写的不是同一个（标识非法或与容器撞名，改名并贯穿引用） |
| `CreatedNodes` | 图上凭空多出来的方块（解析器读到连线时给两端补的节点） |
| `UnresolvedIntents` | 写了但没落地的布局意图（约束少了一项） |

面板标题按入口参数化：导入用 `Strings.ImportTitle`，打开用 `Strings.OpenTitle`，
关闭按钮的可访问名统一是「关闭报告」。

### 只读退让沿用既有那一套

另一个进程占着这份文件时，`DocumentLaunch.Dsl` 走与 `File` 完全相同的退档：
`ReadOnly: true` + 一句 `Reason`。文档**照常读进来**——退成只读不等于打不开。
界面上存盘入口禁用，但「打开」照常可用：打开另一份文件不改这一份，与「再开一个窗口」同一类。

## 二、验收

| 命令 | 结果 |
|---|---|
| `dotnet test --project DuetDiagram.E2E.Tests -- --filter-trait "Category=Open"` | 9/9 通过（7.3 秒） |
| `dotnet build DuetDiagram.slnx -c Release` | 0 警告 0 错误 |
| `dotnet run --project tools/CoverageAudit -c Release` | 覆盖率 87.2%（89 / 13 / 15），退出码 0 |

`Category=Open` 的九条覆盖：

1. `Opening_a_dsl_file_brings_its_nodes_edges_and_groups_in` —— 节点、边、分组都落地，
   图类型与方向跟着文本走，且**当前这个窗口一个字没动**（打开的是一份独立文档）；
2. `A_pin_in_the_text_is_already_in_the_first_frame` —— `pin` 在首帧就生效，
   且它在会话里也是固定位置，保存时会再写出去；
3. `A_same_rank_intent_lands_in_the_document_layout` —— `same-rank` 落进 `document.Layout`，
   `Owner` 是 `Human`；
4. `A_clean_file_opens_without_a_report_panel` —— 没有要交代的不摆面板；
5. `Saving_writes_dsl_text_back_to_the_same_file` —— 写回的是 DSL 文本（不含 IR JSON 的形状），
   连边与 `pin` 都在，放掉之后再打开一次与保存前等价；
6. `The_report_says_every_place_where_the_picture_and_the_text_disagree` —— 诊断带行号、
   改名、补出来的节点、没落地的意图各有一条；
7. `A_file_that_is_not_dsl_is_refused_without_opening_a_window` —— 一份 IR JSON 当 DSL 打开被拒，
   窗口不开、当前文档不动、理由在顶上一句里；
8. `A_dsl_held_by_another_process_opens_read_only` —— 冒充另一个进程先拿住锁，
   这一份退成只读（`IsReadOnly` 为真且有一句说明），文档照常读进来，
   存盘入口禁用而「打开」入口照常可用；
9. `The_file_menu_has_an_open_entry` —— 菜单里有一个「打开」，字从资源取。

持续集成里新增一步「打开门禁」跑 `Category=Open`，排在「多窗口门禁」之后（`app-and-aot` 作业）。
退化的表现是「图打开了但和文本写的不是一回事」或者「保存之后这份文件读不回来了」，
两种在别的门禁里都看不出来——它们盯的是当前文档，不是刚从文件读进来的那一份。

## 三、DSL 装不下的清单（写出去时逐类报出来）

**IR 的表达力严格强于 DSL。** 导出必然有损，差别只在有没有说出来——静默丢失会让用户
以为导出的文本就是全部内容。所以 `DslExportResult.Report` 逐类列出丢了什么，同一类只出一条、
涉及元素收在 `Ids` 里（顺序按首次出现，换个人跑得到同一份报告）。

文档级（`DslExporter` 的 `DropIf`）：

| 类 | 为什么写不进去 |
|---|---|
| 页面 | DSL 没有页面这个概念，多页文档导出的文本只描述缺省页 |
| 图层的可见性、锁定与次序 | DSL 能把节点放进某个图层，但图层本身的定义写不进去 |
| 字体 | DSL 没有字体声明，而字体变化会改变标签宽度与布局结果 |
| 标签 | DSL 没有标签这个集合 |
| 动作 | DSL 没有交互动作的语法 |
| 文本样式预设 | DSL 没有具名文本样式预设 |
| 调色板定义 | DSL 只写令牌名，令牌到颜色的映射由调用方随导入一起给 |
| 画布设置 | DSL 没有网格、纸张与背景这些画布设置 |

元素级（`DropIds`，只在真的涉及某个元素时记）：节点归属的页面、自定义形状的路径、
富文本内容、数学排版模式、节点具体样式、节点文本样式、宿主附加数据、标签里的回车符、
端口的偏移与来源标记、组合框（写成 `group`，不参与布局这个性质丢了）、组合内部的布局方向、
组合的折叠状态、组合样式、组合内部的布局提示、成员次序、边归属的页面、边样式的其余字段、
`order` 约束里指向不存在边的项、约束的归属方、指向不存在节点的固定位置、固定折线、自定义端口。

**报告为空不等于无损**，只等于没有东西落进这份清单；清单是照着 IR 逐字段对出来的，
新加字段时要跟着补——不补不会报错，只会从报告里悄悄消失。有一条用例守着底线：
只用 DSL 表达得出来的文档，导出必须一条丢失都不报（报了说明清单过报）。

`docs/IR-Schema.md` 里那张「自定义形状」的丢失清单也一并从「Mermaid 导出」改成了 DSL——
那半句原来挂在已经删掉的那一档上。

## 四、与规格文档的差异

- **方案把 DSL 说成「LLM 的可选序列化视图」，本仓让它同时是一种绘图文件格式。**
  同一份语法既然能完整描述一张图，它就能承担「一份绘图文件」这个角色。已登记进
  `AGENTS.md` 的「与规格文档的已知差异」表。
- **方案 §二 / §15.2 的「Mermaid 导入导出」与「Mermaid 往返一致」改成了「Mermaid 只导入」。**
  已登记同上。
- **不做**：另存为、最近文件、自动保存、`user.json` 的读写接线。
  `docs/GUI.md` 已把 `user.json` 那条记成缺口，本轮不扩大范围。

## 五、后续

1. `user.json` 的读写接线：本轮 DSL 的固定位置随文本走，`<stem>.user.json` 那份还没接上。
   两种文件同名同目录共存时的语义已在 `docs/Sidecar.md` 写明。
   **做掉了一半（2026-09-30）：** P5-13 把 IR JSON 那一形态的读写接上了（固定位置、固定折线与
   自定义端口随保存落到 `user.json`、打开时读回来），而 **`.dsl` 那一形态仍然不读也不写它**——
   那是落地时定下的一条决定，不是缺口，见 `docs/Sidecar.md` 与 `reports/phase5-sidecar.md`。
2. 自动保存与最近文件：要先把「什么时候算脏」「崩了之后回到哪一版」定下来，
   那些与 §11 的五个恢复档位绑在一起。
3. 界面上那个导出对话框仍是占位（选文件 / 选格式 / 选范围三样）——打开这一条已经通了，
   导出还差那一步。
   **已被取代（2026-09-30）：** P5-12 把对话框做出来了，见 `reports/phase5-export.md`。
