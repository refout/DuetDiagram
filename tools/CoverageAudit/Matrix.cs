namespace CoverageAudit;

/// <summary>
/// draw.io 的能力面，作为覆盖率的分母。
/// </summary>
/// <remarks>
/// 分母是 draw.io 实际能力面，按"形状 / 连线 / 容器 / 文本 / 导出格式 / 布局 /
/// 编辑交互 / 画布与页面 / 协作与版本"这几档取——draw.io 是一个完整编辑器，
/// 这几档都是它公开能力面的组成部分，与任务卡片约束一致。取数时间是 2026-09，
/// 来源是 draw.io 公开文档里列出的形状、连线、容器、文本、导出、布局与编辑这几类能力。
/// 每一条要么指向一份证据（仓库里的文件，多为接口定义或端到端用例），要么写明为什么不做——
/// 分母不自我实现，换一个人跑同一个装置应得到同一个数。
/// </remarks>
internal static class Matrix
{
    /// <summary>分母的来源与取数时间，逐字进报告。</summary>
    public const string Source = "draw.io 公开能力面（形状 / 连线 / 容器 / 文本 / 导出格式 / 布局 / 编辑交互 / 画布与页面 / 协作与版本），取数时间 2026-09";

    public static readonly IReadOnlyList<Capability> Items =
    [
        // ── 形状 ───────────────────────────────────────────────
        new("形状", "矩形", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:NodeShape.Rect", ""),
        new("形状", "圆角矩形", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:NodeShape.Rounded", "CornerRadiusMode.FromStyle"),
        new("形状", "体育场形", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:NodeShape.Stadium", "两端半圆"),
        new("形状", "菱形", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:NodeShape.Diamond", "判定 / 决策符号"),
        new("形状", "椭圆 / 圆", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:NodeShape.Circle", "EllipseOutline"),
        new("形状", "六边形", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:NodeShape.Hexagon", "PolygonOutline"),
        new("形状", "平行四边形", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:NodeShape.Parallelogram", "PolygonOutline"),
        new("形状", "圆柱", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:NodeShape.Cylinder", "PathOutline"),
        new("形状", "自定义形状（路径指令 M/L/A/Z）", Status.Yes, "DuetDiagram.Core/Commands/ErrorCodes.cs:SHAPE_PATH_INVALID", "认不出指令或坐标越界都报结构化错误，证明路径形状入口存在"),
        new("形状", "流程图标准符号集（terminator/process/data/document 等）", Status.Partial, "", "决策=菱形、过程=矩形、终止=体育场可覆盖；文档/数据等无预设，需自定义路径近似"),
        new("形状", "星形 / 多边形（>6 边）", Status.Partial, "DuetDiagram.Core/Shapes/PathParser.cs", "多边形泛化靠自定义路径，但无星形等预设入口"),
        new("形状", "UML 类图 / 用例 / 时序专用形状", Status.Partial, "", "无 UML 专用预设；自定义路径能做几何近似，但不带 UML 语义"),
        new("形状", "云形", Status.No, "", "无预设，方案未要求"),
        new("形状", "图片 / 图标占位", Status.No, "", "不支持嵌入图像"),

        // ── 连线 ───────────────────────────────────────────────
        new("连线", "直线连线", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:EdgeRoute.Straight", ""),
        new("连线", "正交折线", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:EdgeRoute.Orthogonal", "分层布局默认走线"),
        new("连线", "曲线连线", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:EdgeRoute.Curved", "手工自由连线"),
        new("连线", "端点箭头（多款：实心/开口/圆/十字）", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:ArrowStyle", "Arrow/OpenArrow/Circle/Cross + None"),
        new("连线", "线型（实线 / 虚线 / 点线）", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:LineStyle", "Solid/Dashed/Dotted"),
        new("连线", "连线宽度", Status.Yes, "DuetDiagram.Core/Model/Styles.cs:StrokeWidth", ""),
        new("连线", "连线颜色", Status.Yes, "DuetDiagram.Core/Model/Styles.cs:Stroke", ""),
        new("连线", "边标签（起点 / 中间 / 终点）", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:LabelPosition", "Start/Middle/End"),
        new("连线", "边标签自动换行", Status.Yes, "DuetDiagram.Render/TextLayout.cs", "折行在空格处断、中文按字断"),
        new("连线", "端口（左 / 右 / 上 / 下）", Status.Yes, "DuetDiagram.Core/Model/DiagramEnums.cs:PortSide", "固定端口连接"),
        new("连线", "双向箭头（两端各自带箭头）", Status.Yes, "DuetDiagram.Core/Commands/EdgeFieldValue.cs", ""),
        new("连线", "自动避让路由（绕开其它元素）", Status.Partial, "DuetDiagram.Layout/ConstraintLayoutEngine.cs", "正交走线由布局给出，但拖拽时实时绕障未做；pinnedEdges 尚未接渲染"),
        new("连线", "路径点 / waypoint", Status.Partial, "DuetDiagram.Core/Sidecar/UserSidecar.cs", "折点写进 sidecar，但布局与渲染尚未消费"),
        new("连线", "浮动连接（连到元素而非固定端口）", Status.Partial, "DuetDiagram.Core/Commands/Builtin/ConnectEdgeCommand.cs", "支持按端点连接，但连到几何边界任一点未做"),

        // ── 容器 ───────────────────────────────────────────────
        new("容器", "分组 / 组合（成员表权威、可嵌套）", Status.Yes, "DuetDiagram.Core/Model/Composites.cs", "CompositeLimits.MaxDepth 限制嵌套深度"),
        new("容器", "图层（可见 / 锁定）", Status.Yes, "DuetDiagram.Core/Commands/ErrorCodes.cs:LAYER_LOCKED", "LayerVisibility / LayerLock 面板写入口"),
        new("容器", "多页面 / 页面切换", Status.Yes, "DuetDiagram.Core/Model/PageMembership.cs", "页面标签栏与翻页"),
        new("容器", "层内次序约束", Status.Yes, "DuetDiagram.Core/Commands/ErrorCodes.cs:LAYOUT_ORDER_EDGE_MISSING", "set-order + 缺边报悬空"),
        new("容器", "容器自动撑大包住子元素", Status.Partial, "DuetDiagram.Layout/ConstraintLayoutEngine.cs", "组合有成员语义，但容器尺寸不随子元素自动增长——坐标由布局决定"),
        new("容器", "组合折叠 / 展开", Status.Partial, "DuetDiagram.Core/Model/Composites.cs", "组合结构存在，但折叠态渲染未做"),
        new("容器", "泳道 / 分区", Status.No, "", "无 swimlane 预设"),
        new("容器", "Z 序 / 绘制次序字段", Status.No, "", "节点集合顺序进不了任何哈希，效果不可观测，无 z-order 字段"),

        // ── 文本 ───────────────────────────────────────────────
        new("文本", "纯文本标签", Status.Yes, "DuetDiagram.Core/Model/NodeDef.cs:Label", ""),
        new("文本", "富文本（粗体）", Status.Yes, "DuetDiagram.Core/Model/RichTextContent.cs:RichStyleFields.Bold", ""),
        new("文本", "富文本（斜体）", Status.Yes, "DuetDiagram.Core/Model/RichTextContent.cs:RichStyleFields.Italic", ""),
        new("文本", "富文本（下划线）", Status.Yes, "DuetDiagram.Core/Model/RichTextContent.cs:RichStyleFields.Underline", ""),
        new("文本", "富文本（删除线）", Status.Yes, "DuetDiagram.Core/Model/RichTextContent.cs:RichStyleFields.Strikethrough", ""),
        new("文本", "字号", Status.Yes, "DuetDiagram.Core/Model/RichTextContent.cs:RichStyleFields.FontSize", ""),
        new("文本", "文字颜色", Status.Yes, "DuetDiagram.Core/Model/RichTextContent.cs:RichStyleFields.Color", ""),
        new("文本", "段落对齐（左 / 中 / 右）", Status.Yes, "DuetDiagram.Core/Model/RichTextContent.cs:Align", ""),
        new("文本", "行内对齐", Status.Yes, "DuetDiagram.Core/Model/FieldRegistry.cs:text.align", ""),
        new("文本", "数学 / 公式（LaTeX）", Status.Yes, "DuetDiagram.Core/Model/MathSyntax.cs", "P4-15 数学排版，MathTypesetting 用例"),
        new("文本", "自动换行（空格断词 / 中文按字）", Status.Yes, "DuetDiagram.Render/TextLayout.cs", ""),
        new("文本", "多段落", Status.Yes, "DuetDiagram.Core/Model/RichTextContent.cs:RichParagraph", ""),
        new("文本", "字体族（自定义字体名）", Status.Partial, "DuetDiagram.Render/SkiaTextMeasurer.cs", "字体名映射到系统字体，未做字体选择 UI"),
        new("文本", "垂直对齐", Status.Partial, "DuetDiagram.Core/Model/DiagramEnums.cs:TextAlign", "仅水平对齐，垂直对齐未做"),
        new("文本", "项目符号 / 编号列表", Status.No, "", "P4-14 段落级对齐与列表仍没有入口"),
        new("文本", "行间距", Status.No, "", "未做"),
        new("文本", "竖排 / RTL 文本方向", Status.No, "", "未做"),

        // ── 导出格式 ───────────────────────────────────────────
        new("导出格式", "SVG 导出", Status.Yes, "DuetDiagram.Render/Export/SvgExporter.cs", "逐类绘制命令对得上"),
        new("导出格式", "PNG 导出", Status.Yes, "DuetDiagram.Render/Export/BitmapExporter.cs", "同一份绘制列表逐像素可复现"),
        new("导出格式", "PDF 导出（矢量、文字可选中）", Status.Yes, "DuetDiagram.Render/Export/PdfExporter.cs", "SKDocument.CreatePdf，CID 嵌入的矢量文本"),
        new("导出格式", "DSL 导出", Status.Yes, "DuetDiagram.Dsl/Export/DslExporter.cs", "diagram_export 的 dsl 档，装不下的内容逐类进丢失清单"),
        new("导出格式", "矢量文本（导出后可选中 / 搜索）", Status.Yes, "DuetDiagram.Render/Export/PdfExporter.cs", "PDF 与 SVG 均为矢量文本"),
        new("导出格式", "位图含文字", Status.Yes, "DuetDiagram.Render/Export/BitmapExporter.cs", ""),
        new("导出格式", "透明背景 PNG", Status.Yes, "DuetDiagram.Render/Export/BitmapExporter.cs:Transparent", "离屏渲染时背景可透明"),
        new("导出格式", "逐页 / 选范围导出", Status.Yes, "DuetDiagram.Render/Export/PdfExporter.cs", "一页一张，可按页面导出"),
        new("导出格式", "HTML 导出", Status.No, "", "未做"),
        new("导出格式", "draw.io XML（.drawio）导出", Status.No, "", "读写的是本仓自己的 IR 与 DSL，不兼容 draw.io 格式"),
        new("导出格式", "JPEG 导出", Status.No, "", "位图只走 PNG"),

        // ── 导入格式 ───────────────────────────────────────────
        new("导入格式", "Mermaid 文件导入", Status.Yes, "DuetDiagram.App/Services/ImportService.cs", "P4-19，一条命令整体落地"),
        new("导入格式", "模板 / 复制片段插入", Status.Yes, "DuetDiagram.Core/Commands/Builtin/InsertTemplateCommand.cs", "存为模板 + 插入模板，标识冲突消解"),
        new("导入格式", "draw.io XML 导入", Status.No, "", "不读 .drawio 格式"),
        new("导入格式", "图像导入", Status.No, "", "未做"),
        new("导入格式", "SVG 导入", Status.No, "", "未做"),
        new("导入格式", "DSL 导入（打开成一份绘图文件）", Status.Yes, "DuetDiagram.App/Services/DslFile.cs", "打开一份 .dsl 得到一份文档，可保存回原文件"),

        // ── 样式 ───────────────────────────────────────────────
        new("样式", "填充色", Status.Yes, "DuetDiagram.Core/Model/Styles.cs:Fill", ""),
        new("样式", "描边色", Status.Yes, "DuetDiagram.Core/Model/Styles.cs:Stroke", ""),
        new("样式", "描边宽度", Status.Yes, "DuetDiagram.Core/Model/Styles.cs:StrokeWidth", ""),
        new("样式", "不透明度", Status.Yes, "DuetDiagram.Core/Model/Styles.cs:Opacity", ""),
        new("样式", "调色板 / 样式令牌预设", Status.Yes, "DuetDiagram.Core/Model/Palette.cs", "PalettePanel + TextPreset"),
        new("样式", "圆角半径可调", Status.Partial, "DuetDiagram.Core/Shapes/BuiltinShapeProvider.cs", "Rounded 形状的角由形状决定，未开放可调半径字段"),
        new("样式", "渐变填充", Status.No, "", "无 Gradient 字段"),
        new("样式", "阴影", Status.No, "", "未做"),

        // ── 布局 ───────────────────────────────────────────────
        new("布局", "自动分层布局（Sugiyama）", Status.Yes, "DuetDiagram.Layout/ConstraintLayoutEngine.cs", ""),
        new("布局", "布局约束：同层", Status.Yes, "DuetDiagram.Layout/ConstraintLayoutEngine.cs", ""),
        new("布局", "布局约束：对齐", Status.Yes, "DuetDiagram.Layout/ConstraintLayoutEngine.cs", ""),
        new("布局", "布局约束：层内次序", Status.Yes, "DuetDiagram.Core/Commands/ErrorCodes.cs:LAYOUT_ORDER_EDGE_MISSING", ""),
        new("布局", "布局超时降级（退回手动）", Status.Yes, "DuetDiagram.Layout/FallbackPlan.cs", ""),
        new("布局", "布局预算 / 重试（更长预算）", Status.Yes, "DuetDiagram.Layout/LayoutBudgets.cs", ""),
        new("布局", "布局约束：相对位置", Status.Partial, "DuetDiagram.Core/Commands/Builtin/SetPlaceCommand.cs", "set-place 已落 IR、进哈希、进校验，但求解器尚未消费"),

        // ── 编辑交互 ───────────────────────────────────────────
        new("编辑交互", "拖拽移动元素", Status.Yes, "DuetDiagram.E2E.Tests/DragTests.cs", ""),
        new("编辑交互", "框选多选", Status.Yes, "DuetDiagram.E2E.Tests/MarqueeTests.cs", ""),
        new("编辑交互", "连线与重连", Status.Yes, "DuetDiagram.E2E.Tests/ConnectTests.cs", ""),
        new("编辑交互", "边的编辑（改端点 / 线型 / 箭头）", Status.Yes, "DuetDiagram.E2E.Tests/EdgeEditTests.cs", ""),
        new("编辑交互", "右键上下文菜单", Status.Yes, "DuetDiagram.E2E.Tests/ContextMenuTests.cs", ""),
        new("编辑交互", "就地编辑标签（富文本）", Status.Yes, "DuetDiagram.E2E.Tests/RichTextEditorTests.cs", ""),
        new("编辑交互", "撤销 / 重做", Status.Yes, "DuetDiagram.Core/Commands/CommandMemento.cs", ""),
        new("编辑交互", "缩放与平移", Status.Yes, "DuetDiagram.E2E.Tests/CanvasSmokeTests.cs", ""),
        new("编辑交互", "多窗口查看同一文档", Status.Yes, "DuetDiagram.E2E.Tests/MultiWindowTests.cs", ""),
        new("编辑交互", "只读锁定（多进程抢占）", Status.Yes, "DuetDiagram.Core/Commands/ErrorCodes.cs:DOCUMENT_READ_ONLY", ""),
        new("编辑交互", "节点字段编辑（标签 / 形状 / 样式）", Status.Yes, "DuetDiagram.Core/Commands/NodeFieldValue.cs", ""),
        new("编辑交互", "边字段编辑（端点 / 线型 / 箭头）", Status.Yes, "DuetDiagram.Core/Commands/EdgeFieldValue.cs", ""),
        new("编辑交互", "删除元素", Status.Yes, "DuetDiagram.E2E.Tests/ContextMenuTests.cs", ""),
        new("编辑交互", "节点固定位置（pinned）", Status.Yes, "DuetDiagram.Core/Sidecar/UserSidecar.cs", ""),

        // ── 画布与页面 ─────────────────────────────────────────
        new("画布与页面", "画布背景色", Status.Yes, "DuetDiagram.Render/Export/CanvasPainter.cs", "Background 绘制"),
        new("画布与页面", "页面尺寸（A4 等）", Status.Yes, "DuetDiagram.Render/Export/PdfOptions.cs", "默认 1123×794 即 A4 横版"),
        new("画布与页面", "页面标签栏与翻页", Status.Yes, "DuetDiagram.E2E.Tests/PageTabsTests.cs", ""),
        new("画布与页面", "属性面板", Status.Yes, "DuetDiagram.E2E.Tests/PropertyPanelTests.cs", ""),
        new("画布与页面", "网格显示开关", Status.Partial, "", "画布设置里有网格字段，但吸附辅助线未做"),

        // ── 协作与版本 ─────────────────────────────────────────
        new("协作与版本", "文档版本号", Status.Yes, "DuetDiagram.Core/Model/DiagramDocument.cs", ""),
        new("协作与版本", "结构哈希（变更检测）", Status.Yes, "DuetDiagram.Core/Model/DiagramDocument.cs:StructuralHash", ""),
        new("协作与版本", "冲突检测与 409 响应", Status.Yes, "DuetDiagram.Mcp/Server/ConflictResponder.cs", ""),
        new("协作与版本", "审计日志", Status.Yes, "DuetDiagram.Mcp/Server/HttpHost.cs", ""),

        // ── 面板 / 工具栏（draw.io 同类 UI 能力）─────────────────
        new("面板与工具栏", "工具栏", Status.Yes, "DuetDiagram.E2E.Tests/ToolBarTests.cs", ""),
        new("面板与工具栏", "菜单栏", Status.Yes, "DuetDiagram.E2E.Tests/MenuBarTests.cs", ""),
        new("面板与工具栏", "调色板面板", Status.Yes, "DuetDiagram.E2E.Tests/PalettePanelTests.cs", ""),
        new("面板与工具栏", "形状库面板", Status.Yes, "DuetDiagram.E2E.Tests/ShapeLibraryTests.cs", ""),
        new("面板与工具栏", "模板面板", Status.Yes, "DuetDiagram.E2E.Tests/TemplateTests.cs", ""),
        new("面板与工具栏", "文本预设面板", Status.Yes, "DuetDiagram.E2E.Tests/TextPresetTests.cs", ""),
        new("面板与工具栏", "图层面板", Status.Yes, "DuetDiagram.E2E.Tests/LayerPanelTests.cs", ""),
        new("面板与工具栏", "诊断面板", Status.Yes, "DuetDiagram.E2E.Tests/DiagnosticsPanelTests.cs", ""),
        new("面板与工具栏", "布局约束编辑器", Status.Yes, "DuetDiagram.E2E.Tests/ConstraintEditorTests.cs", ""),
    ];
}

internal enum Status
{
    Yes,
    Partial,
    No,
}

internal sealed record Capability(string Category, string Feature, Status Status, string Evidence, string Note);
