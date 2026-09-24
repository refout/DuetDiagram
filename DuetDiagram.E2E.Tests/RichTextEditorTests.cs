using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Workspace;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 就地编辑标签：双击进编辑、改字、套样式、退出提交。
/// </summary>
/// <remarks>
/// <para>
/// **整次编辑算一条命令。** 这一组盯的就是这件事：编辑期间文档一个字节都不动，
/// 退出时才发一条。撤销一次要能还原整次编辑，而不是退回一个字。
/// </para>
/// <para>
/// **退出即提交，除非明确取消。** Esc 与点别处是两条不同的路，而它们的差别
/// 正是"我的改动去哪了"这个问题——两条都要有用例。
/// </para>
/// <para>
/// 编辑控件改的是会话里的草稿，不是文档：所以这一组还要验"编辑出来的内容与
/// 模型里存的内容逐段一致"——界面不自己发明格式。
/// </para>
/// </remarks>
public sealed class RichTextEditorTests
{
    #region 双击进编辑

    /// <summary>双击节点把编辑器摆到那个节点上，草稿从它当前的标签起。</summary>
    [Fact]
    [Trait("Category", "RichTextEditor")]
    public async Task Double_clicking_a_node_opens_the_editor_on_it()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            DoubleClick(window, canvas, "check");

            window.TextEdit.IsOpen.Should().BeTrue("双击节点进编辑");
            window.TextEdit.NodeId.Should().Be("check");
            Editor(window).IsVisible.Should().BeTrue("编辑器要真的摆出来");
            Field(window, "richtext.text").Text.Should().Be("校验", "草稿从节点当前的标签起");

            // 编辑器盖在节点上：位置与尺寸取的是排版结果那一块。
            var box = window.Model.ScreenBoundsOf("check")!.Value;

            Canvas.GetLeft(Editor(window)).Should().BeApproximately(box.X, 0.01);
            Canvas.GetTop(Editor(window)).Should().BeApproximately(box.Y, 0.01);

            window.Close();
        });
    }

    /// <summary>编辑期间文档一个字节都不动。</summary>
    /// <remarks>
    /// 逐字发命令的话，撤销栈会被一次编辑灌满，而用户眼里那是一次编辑。
    /// 这一条直接盯住"没发命令"这个事实，不靠撤销次数反推。
    /// </remarks>
    [Fact]
    [Trait("Category", "RichTextEditor")]
    public async Task Editing_emits_no_command_until_the_editor_closes()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var before = window.Session.Bus.Context.History.UndoEntries().Count;

            DoubleClick(window, canvas, "check");

            var field = Field(window, "richtext.text");

            foreach (var text in new[] { "校", "校验", "校验通", "校验通过" })
            {
                field.Text = text;
            }

            window.Session.Bus.Context.History.UndoEntries().Count.Should().Be(
                before,
                "编辑期间一个字都不该进历史");
            window.Session.Document.Nodes.Single(node => node.Id == "check").Label.Should().Be(
                "校验",
                "编辑期间文档里还是老标签");

            window.Close();
        });
    }

    #endregion

    #region 退出提交

    /// <summary>点别处提交，整次编辑是一条命令，撤销一次还原它。</summary>
    [Fact]
    [Trait("Category", "RichTextEditor")]
    public async Task Clicking_away_commits_the_whole_edit_as_one_command()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            DoubleClick(window, canvas, "check");
            Field(window, "richtext.text").Text = "校验通过";

            var before = window.Session.Bus.Context.History.UndoEntries().Count;

            // 点别处：不是取消，是提交。
            var spot = EmptySpot(canvas);
            var point = HeadlessFixture.ToWindow(canvas, window, spot);

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);

            window.TextEdit.IsOpen.Should().BeFalse("点别处就算退出");
            window.Session.Document.Nodes.Single(node => node.Id == "check").Label.Should()
                .Be("校验通过", "退出的那一下把草稿提交了");

            window.Session.Bus.Context.History.UndoEntries().Count.Should().Be(
                before + 1,
                "整次编辑只多一条历史");

            window.Invoke("edit.undo");
            window.Session.Document.Nodes.Single(node => node.Id == "check").Label.Should()
                .Be("校验", "撤销一次退回整次编辑，而不是退回一个字");

            window.Close();
        });
    }

    /// <summary>Esc 取消：草稿丢掉，文档不动，历史里不留东西。</summary>
    [Fact]
    [Trait("Category", "RichTextEditor")]
    public async Task Escape_discards_the_draft()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var before = window.Session.Bus.Context.History.UndoEntries().Count;

            DoubleClick(window, canvas, "check");

            var field = Field(window, "richtext.text");

            field.IsFocused.Should().BeTrue("进编辑之后焦点在编辑框上，不然 Esc 到不了它");
            field.Text = "改了但不该留下";

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            window.TextEdit.IsOpen.Should().BeFalse("Esc 取消这次编辑");
            window.Session.Document.Nodes.Single(node => node.Id == "check").Label.Should()
                .Be("校验", "取消之后文档一个字都没动");
            window.Session.Bus.Context.History.UndoEntries().Count.Should()
                .Be(before, "取消不该在历史里留东西");

            window.Close();
        });
    }

    /// <summary>进编辑什么都不改就退出，是一条无操作，历史里不留东西。</summary>
    /// <remarks>
    /// 不留这一条的话，一个"退出时照发一条命令"的实现也能通过上面两条——
    /// 而用户进编辑看了一眼再出来，撤销栈上会多出一步空动作。
    /// </remarks>
    [Fact]
    [Trait("Category", "RichTextEditor")]
    public async Task Leaving_without_changing_anything_is_a_no_op()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var before = window.Session.Bus.Context.History.UndoEntries().Count;

            DoubleClick(window, canvas, "check");
            window.TextEdit.Commit();

            window.Session.Bus.Context.History.UndoEntries().Count.Should()
                .Be(before, "什么都没改，历史不该变长");
            window.Session.Document.Nodes.Single(node => node.Id == "check").RichLabel.Should()
                .BeNull("只改了个字不该顺手把节点变成富文本");

            window.Close();
        });
    }

    #endregion

    #region 样式

    /// <summary>样式按钮作用于选区，不是整个标签。</summary>
    [Fact]
    [Trait("Category", "RichTextEditor")]
    public async Task A_style_button_applies_to_the_selection_only()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            DoubleClick(window, canvas, "check");

            var field = Field(window, "richtext.text");

            field.SelectionStart = 0;
            field.SelectionEnd = 1;

            Click(window, "richtext.bold");

            window.TextEdit.Commit();

            var node = window.Session.Document.Nodes.Single(candidate => candidate.Id == "check");
            var paragraph = node.RichLabel!.Paragraphs.Single();

            paragraph.Runs.Should().HaveCount(2, "一个字加粗，其余不变");
            paragraph.Runs[0].Text.Should().Be("校");
            paragraph.Runs[0].Style!.Bold.Should().BeTrue();
            paragraph.Runs[1].Text.Should().Be("验");
            paragraph.Runs[1].Style.Should().BeNull("没被选中的那一段不该跟着变粗");

            window.Close();
        });
    }

    /// <summary>没有选区时样式按钮什么都不做，而不是把整段变粗。</summary>
    [Fact]
    [Trait("Category", "RichTextEditor")]
    public async Task A_style_button_without_a_selection_does_nothing()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var before = window.Session.Bus.Context.History.UndoEntries().Count;

            DoubleClick(window, canvas, "check");

            var field = Field(window, "richtext.text");

            field.SelectionStart = 1;
            field.SelectionEnd = 1;

            Click(window, "richtext.bold");
            window.TextEdit.Commit();

            window.Session.Document.Nodes.Single(node => node.Id == "check").RichLabel.Should()
                .BeNull("没有选区就没有作用对象");
            window.Session.Bus.Context.History.UndoEntries().Count.Should().Be(before);

            window.Close();
        });
    }

    #endregion

    #region 界面不自己发明格式

    /// <summary>
    /// 编辑出来的内容与模型里存的内容逐段一致。
    /// </summary>
    /// <remarks>
    /// 两段文字、第二段整段加粗：提交之后逐段比对。界面自己发明格式的话
    /// （多一个空段、少一个空片段、把成员全空的样式留下来），这里就对不上。
    /// </remarks>
    [Fact]
    [Trait("Category", "RichText")]
    public async Task What_the_editor_produces_is_what_the_model_stores()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            DoubleClick(window, canvas, "check");

            var field = Field(window, "richtext.text");

            field.Text = "甲\n乙丙";

            field.SelectionStart = 2;
            field.SelectionEnd = 4;

            Click(window, "richtext.bold");
            window.TextEdit.Commit();

            var node = window.Session.Document.Nodes.Single(candidate => candidate.Id == "check");
            var content = node.RichLabel!;

            content.Paragraphs.Should().HaveCount(2, "换行切出两段");
            content.Paragraphs[0].Runs.Should().HaveCount(1);
            content.Paragraphs[0].PlainText.Should().Be("甲");
            content.Paragraphs[0].Runs[0].Style.Should().BeNull();
            content.Paragraphs[1].Runs.Should().HaveCount(1, "同一样式的相邻字符合成一段");
            content.Paragraphs[1].Runs[0].Text.Should().Be("乙丙");
            content.Paragraphs[1].Runs[0].Style!.Bold.Should().BeTrue();

            // 标签是内容的纯文本投影，两者必须逐字相同。
            node.Label.Should().Be(content.PlainText);
            node.RichText.Should().BeTrue("写内容这个动作本身就声明了按富文本渲染");

            window.Close();
        });
    }

    /// <summary>内容与标签对不上的文档过不了整体校验——这一条盯的是提交没有造出那种文档。</summary>
    [Fact]
    [Trait("Category", "RichText")]
    public async Task A_committed_edit_leaves_a_document_that_validates()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            DoubleClick(window, canvas, "check");

            var field = Field(window, "richtext.text");

            field.Text = "甲\n乙丙";
            field.SelectionStart = 0;
            field.SelectionEnd = 1;
            Click(window, "richtext.underline");
            window.TextEdit.Commit();

            DiagramValidator.Validate(window.Session.Document).Should().BeEmpty(
                "提交之后那份文档要自己站得住");

            window.Close();
        });
    }

    #endregion

    #region 写不进去的时候

    /// <summary>只读的文档连编辑都进不去，理由摆在状态栏上。</summary>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public async Task A_read_only_document_refuses_to_open_the_editor()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var path = temp.File("editor.json");

            File.WriteAllText(path, DiagramSerializer.SerializeFull(SampleDiagram.Document()));

            // 冒充另一个进程：先拿住这份文档的独占所有权。
            using var other = DocumentLock.Acquire(path);

            var window = new MainWindow(DocumentLaunch.File(path));

            window.Show();
            window.CaptureRenderedFrame();

            window.Session.IsReadOnly.Should().BeTrue("另一个进程正在编辑这份文档");

            var canvas = HeadlessFixture.Canvas(window);

            DoubleClick(window, canvas, "check");

            window.TextEdit.IsOpen.Should().BeFalse("只读时连编辑都进不去");
            Editor(window).IsVisible.Should().BeFalse("编辑器不该摆出来");
            window.Status.Message.Should().Contain(
                ErrorPresenterTable.For(ErrorCodes.DocumentReadOnly).Message,
                "挡住它的理由要摆出来，而不是点了一下什么反应都没有");

            window.Close();
        });
    }

    /// <summary>
    /// 编辑期间写入被拒时停下：编辑器不关、草稿不丢、文档一个字都不动。
    /// </summary>
    /// <remarks>
    /// 写入被拒这一条要真的走到。开编辑那一刻还没锁，编辑期间锁上——
    /// 只在开头挡的话，锁定就成了"只对还没开始做的事有效"。
    /// 关掉编辑器会让用户刚敲的那一段凭空消失，而它其实一个字都没写进去。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public async Task A_refused_commit_keeps_the_draft_and_writes_nothing()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var window = OpenLayered(temp);
            var canvas = HeadlessFixture.Canvas(window);

            DoubleClick(window, canvas, "b");

            var field = Field(window, "richtext.text");

            field.Text = "改了但不该写进去";

            // 编辑期间这一层被锁上。
            window.Session.SetLayerLocked("top", true).IsEffectiveSuccess.Should().BeTrue();

            // 走编辑器那一条：用户最后敲的那几个字还在编辑框里，编辑器会先收上来再提交。
            Editor(window).RequestCommit();

            window.TextEdit.IsOpen.Should().BeTrue("提交失败时编辑器不关，草稿要留着");
            window.TextEdit.Error.Should().NotBeNullOrEmpty("失败的理由要显示出来");
            window.TextEdit.Text.Should().Be("改了但不该写进去", "草稿一个字都不该丢");

            window.Session.Document.Nodes.Single(node => node.Id == "b").Label.Should()
                .Be("乙", "半截内容不该写进文档");
            window.Session.Document.Nodes.Single(node => node.Id == "b").RichLabel.Should().BeNull();

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>在某个元素的中心上双击。</summary>
    private static void DoubleClick(MainWindow window, DiagramCanvas canvas, string elementId)
    {
        var point = HeadlessFixture.ToWindow(canvas, window, HeadlessFixture.CenterOf(canvas, elementId));

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    /// <summary>画布上一处空白。点它等于"点别处"，而不会顺带改选中。</summary>
    private static Point EmptySpot(DiagramCanvas canvas)
    {
        var model = canvas.Model ?? throw new InvalidOperationException("画布还没有数据上下文");

        for (var y = 4.0; y < canvas.Bounds.Height - 4; y += 6)
        {
            for (var x = 4.0; x < canvas.Bounds.Width - 4; x += 6)
            {
                if (model.Pick(x, y) is null)
                {
                    return new Point(x, y);
                }
            }
        }

        throw new InvalidOperationException("画布上找不到一处空白");
    }

    /// <summary>那个编辑器。</summary>
    private static RichTextEditor Editor(MainWindow window) =>
        window.GetVisualDescendants().OfType<RichTextEditor>().Single();

    /// <summary>
    /// 编辑器里某个带自动化标识的控件。
    /// </summary>
    /// <remarks>
    /// 走逻辑树而不是视觉树：编辑器平时是收起来的，而收起来的控件不参与排布，
    /// 视觉树里也就没有它的子节点。用例都先开编辑器再取，但这条口径与别处一致，
    /// 免得将来有一条在收起来的状态下取。
    /// </remarks>
    private static T Named<T>(MainWindow window, string automationId)
        where T : Control =>
        window.GetLogicalDescendants()
            .OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static TextBox Field(MainWindow window, string automationId) => Named<TextBox>(window, automationId);

    /// <summary>按一下编辑器里的某个按钮。</summary>
    /// <remarks>
    /// 按自动化标识找，不按视觉树里的次序：次序一调整就会按到另一个按钮上，
    /// 而那种错不会让测试失败，只会让它去验另一件事。
    /// </remarks>
    private static void Click(MainWindow window, string automationId) =>
        Named<Button>(window, automationId).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>开一个窗口，看一份带两个图层的文档。</summary>
    /// <remarks>
    /// <para>
    /// 示例文档没有图层，而"编辑期间写入被拒"那一条要的正是图层。
    /// </para>
    /// <para>
    /// **目录由调用方拿着。** 这份文档是有文件的，会话在它上面还压着一把跨进程的锁与
    /// 一个心跳文件；在这里就地回收目录的话，那两个东西会从会话底下消失。
    /// </para>
    /// </remarks>
    private static MainWindow OpenLayered(TempDirectory temp)
    {
        ArgumentNullException.ThrowIfNull(temp);

        var path = temp.File("layers.json");

        File.WriteAllText(path, DiagramSerializer.SerializeFull(LayeredDocument()));

        var window = new MainWindow(DocumentLaunch.File(path));

        window.Show();
        window.CaptureRenderedFrame();

        return window;
    }

    /// <summary>
    /// 两层各放一个节点，要编辑的那个在上层。
    /// </summary>
    /// <remarks>
    /// 方向是"要编辑的那个在上"。变更边栏常驻在画布右下角，而它是有底的：
    /// 落在那一块上的点击到不了画布。把目标节点放在整张图的上方，
    /// 用例点的是节点，不会顺手点到边栏上。
    /// </remarks>
    private static DiagramDocument LayeredDocument() =>
        DiagramDocument.CreateFromContent(
            "layers",
            DiagramKind.Flowchart,
            Direction.TB,
            layers:
            [
                new LayerDef { Id = "base", Name = "底层", Order = 0 },
                new LayerDef { Id = "top", Name = "上层", Order = 1 },
            ],
            nodes:
            [
                new NodeDef { Id = "a", Label = "甲", Layer = "base" },
                new NodeDef { Id = "b", Label = "乙", Layer = "top" },
            ],
            edges: [new EdgeDef { Id = "e", From = "b", To = "a" }]);

    #endregion
}
