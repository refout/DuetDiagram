using System.Text.Json;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 富文本内容模型：两级的形状、往返序列化、与纯文本标签的关系，以及两个哈希里的归属。
/// </summary>
/// <remarks>
/// <para>
/// 这一组盯的是**模型本身**：内容怎么写进文件、认不出的样式名怎么被挡住、
/// 哪一份是权威、改动落在哪个哈希上。排版与编辑界面不在这一层，
/// 它们依赖的就是这里定下来的形状。
/// </para>
/// <para>
/// 与纯文本标签的关系是这一组里最要紧的一块：两份都存而主次不清的话，
/// 渲染读一份、导出读另一份，两边都不会报错，而用户看到的是"画布上是新的、导出去是旧的"。
/// </para>
/// </remarks>
public sealed class RichTextTests
{
    #region 形状与序列化

    [Fact]
    [Trait("Category", "RichText")]
    public void Paragraphs_and_inline_styles_survive_a_serialization_round_trip()
    {
        var node = Node(
            "粗体与斜体\n第二段",
            Paragraph(
                Run("粗体", new RichRunStyle { Bold = true }),
                Run("与"),
                Run("斜体", new RichRunStyle { Italic = true })),
            Paragraph(Run("第二段", new RichRunStyle { FontSize = 18, Color = "danger" })));

        var restored = RoundTrip(IrFixtures.Build(nodes: [node]));

        restored.Nodes.Single().Should().Be(node, "段落与行内样式要逐段逐项还原");
        restored.Nodes.Single().RichLabel!.Paragraphs.Should().HaveCount(2);
        restored.Nodes.Single().RichLabel!.Paragraphs[0].Runs.Should().HaveCount(3);
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void A_run_carries_all_six_inline_styles()
    {
        var style = new RichRunStyle
        {
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            FontSize = 12,
            Color = "primary",
        };

        var restored = RoundTrip(IrFixtures.Build(nodes: [Node("x", Paragraph(Run("x", style)))]));

        restored.Nodes.Single().RichLabel!.Paragraphs[0].Runs[0].Style.Should().Be(style);
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void A_paragraph_carries_its_own_alignment()
    {
        var content = new RichTextContent
        {
            Paragraphs = [new RichParagraph { Runs = [Run("中")], Align = TextAlign.Center }],
        };

        var restored = RoundTrip(IrFixtures.Build(nodes: [Node("中", content.Paragraphs[0])]));

        restored.Nodes.Single().RichLabel!.Paragraphs[0].Align.Should().Be(TextAlign.Center);
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void The_serialized_shape_carries_no_computed_members()
    {
        var document = IrFixtures.Build(
            nodes: [Node("字", Paragraph(Run("字", new RichRunStyle { Bold = true })))]);

        var json = DiagramSerializer.SerializeFull(document);

        // 算出来的成员（投影、空判断）不是 IR 的一部分。写进去的话，
        // 读回来时那个键没有任何成员接得住——一份自己写出去的内容自己读不回来。
        json.Should().Contain("\"bold\":true");
        json.Should().NotContain("plainText");
        json.Should().NotContain("isEmpty");
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void The_projection_joins_paragraphs_with_a_line_feed()
    {
        var content = Content(
            Paragraph(Run("第一段")),
            Paragraph(Run("第二"), Run("段")),
            Paragraph(Run("第三段")));

        content.PlainText.Should().Be("第一段\n第二段\n第三段");
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void Adjacent_runs_with_the_same_style_are_merged()
    {
        var content = Content(
            Paragraph(
                Run("前", new RichRunStyle { Bold = true }),
                Run("后", new RichRunStyle { Bold = true }),
                Run("平")));

        var normalized = content.Normalize();

        normalized.Paragraphs[0].Runs.Should().HaveCount(2);
        normalized.Paragraphs[0].Runs[0].Text.Should().Be("前后");
        normalized.PlainText.Should().Be(content.PlainText, "折形态不该改变文字");
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void Empty_runs_and_all_empty_styles_are_folded_away()
    {
        var content = Content(Paragraph(Run(string.Empty), Run("字", new RichRunStyle())));

        var normalized = content.Normalize();

        normalized.Paragraphs[0].Runs.Should().HaveCount(1);
        normalized.Paragraphs[0].Runs[0].Text.Should().Be("字");
        normalized.Paragraphs[0].Runs[0].Style.Should().BeNull("成员全空的样式与没有样式是同一件事");
    }

    #endregion

    #region 认不出的样式名

    [Fact]
    [Trait("Category", "RichText")]
    public void An_unknown_inline_style_field_is_refused_before_it_reaches_the_document()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        var result = harness.SetField(
            "a",
            FieldNames.RichLabel,
            """{"paragraphs":[{"runs":[{"text":"a","style":{"shadow":true}}]}]}""");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.FieldValueInvalid);

        // 错误里要点出是哪一个名字认不出，否则写的人只能一个字段一个字段地猜。
        result.Errors[0].Payload.Should().Contain("shadow");
        harness.Node("a").RichLabel.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void An_unknown_inline_style_field_in_a_file_is_refused()
    {
        var document = IrFixtures.Build(
            nodes: [Node("字", Paragraph(Run("字", new RichRunStyle { Bold = true })))]);

        var json = DiagramSerializer.SerializeFull(document);
        var broken = json.Replace("\"bold\":true", "\"bold\":true,\"shadow\":true", StringComparison.Ordinal);

        broken.Should().NotBe(json, "这份文件要真的多出一个认不出的样式名，否则下面那条断言什么都没验");

        // 直接读文件那条路不经过字段读写，挡住它的是序列化层自己的拒绝。
        // 少了这一道，一份键名写错的文件会被照常读进来，而那些样式一个都没生效。
        var load = () => DiagramSerializer.DeserializeFull(broken);

        load.Should().Throw<JsonException>();
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void Every_registered_inline_style_name_is_accepted()
    {
        foreach (var field in RichStyleFields.All)
        {
            RichStyleFields.IsKnown(field).Should().BeTrue($"{field} 是登记过的名字");
        }

        RichStyleFields.IsKnown("Bold").Should().BeFalse("大小写不敏感会让同一个键在往返之后换一个写法");
        RichStyleFields.IsKnown(null).Should().BeFalse();
        RichStyleFields.IsKnown("no-such-style").Should().BeFalse();
    }

    #endregion

    #region 两个哈希

    [Fact]
    [Trait("Category", "RichText")]
    public void Rich_text_is_visual_but_not_structural()
    {
        var plain = IrFixtures.Base();
        var rich = IrFixtures.WithNode(plain, Node("甲", Paragraph(Run("甲", new RichRunStyle { Bold = true }))));

        DiagramHashing.ComputeStructuralHash(rich)
            .Should().Be(DiagramHashing.ComputeStructuralHash(plain), "换分段样式不改节点坐标");

        DiagramHashing.ComputeVisualHash(rich)
            .Should().NotBe(DiagramHashing.ComputeVisualHash(plain), "换了样式画出来就不一样");
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void Changing_one_run_style_changes_the_visual_hash()
    {
        var bold = IrFixtures.WithNode(
            IrFixtures.Base(),
            Node("甲", Paragraph(Run("甲", new RichRunStyle { Bold = true }))));

        var italic = IrFixtures.WithNode(
            IrFixtures.Base(),
            Node("甲", Paragraph(Run("甲", new RichRunStyle { Italic = true }))));

        DiagramHashing.ComputeVisualHash(bold).Should().NotBe(DiagramHashing.ComputeVisualHash(italic));
    }

    #endregion

    #region 与纯文本标签的关系

    [Fact]
    [Trait("Category", "RichText")]
    public void Writing_the_content_synchronises_the_label()
    {
        using var harness = new Harness();
        harness.AddNode("a", "旧");

        var result = harness.SetRichLabel("a", Content(Paragraph(Run("新"), Run("字"))));

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Node("a").Label.Should().Be("新字");
        harness.Node("a").RichText.Should().BeTrue("写内容这个动作本身就声明了按富文本渲染");

        // 相邻的同样式片段会被折成一段，所以两段文字落盘之后是一段。
        harness.Node("a").RichLabel!.Paragraphs[0].Runs.Should().ContainSingle()
            .Which.Text.Should().Be("新字");
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void Writing_a_plain_label_drops_content_that_no_longer_matches()
    {
        using var harness = new Harness();
        harness.AddNode("a", "旧");
        harness.SetRichLabel("a", Content(Paragraph(Run("旧", new RichRunStyle { Bold = true }))));

        harness.SetField("a", FieldNames.Label, "换了一段").IsSuccess.Should().BeTrue();

        // 分段样式是按位置切出来的，文字一换就没有依据。留着一份对不上的内容，
        // 只有整体校验器会报，而画布上看到的是一段谁也没写过的文字。
        harness.Node("a").RichLabel.Should().BeNull();
        harness.Node("a").Label.Should().Be("换了一段");
        harness.Node("a").RichText.Should().BeTrue("开关说的是排版路径，不该被一次纯文本编辑顺手关掉");
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void Writing_a_plain_label_keeps_content_that_still_matches()
    {
        using var harness = new Harness();
        harness.AddNode("a", "旧");
        harness.SetRichLabel("a", Content(Paragraph(Run("旧", new RichRunStyle { Bold = true }))));

        harness.SetField("a", FieldNames.Label, "旧").IsSuccess.Should().BeTrue();

        harness.Node("a").RichLabel.Should().NotBeNull("文字没变，分段样式仍然有效");
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void Turning_the_rich_text_switch_off_drops_the_content()
    {
        using var harness = new Harness();
        harness.AddNode("a", "字");
        harness.SetRichLabel("a", Content(Paragraph(Run("字", new RichRunStyle { Bold = true }))));

        harness.SetField("a", FieldNames.RichText, "false").IsSuccess.Should().BeTrue();

        harness.Node("a").RichText.Should().BeFalse();
        harness.Node("a").RichLabel.Should().BeNull("关掉开关等于退回纯文本，分段样式不再有意义");
        harness.Node("a").Label.Should().Be("字", "文字本身留着");
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void Clearing_the_content_leaves_the_label_and_the_switch_alone()
    {
        using var harness = new Harness();
        harness.AddNode("a", "字");
        harness.SetRichLabel("a", Content(Paragraph(Run("字", new RichRunStyle { Bold = true }))));

        var result = harness.SetRichLabel("a", null);

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Node("a").RichLabel.Should().BeNull();
        harness.Node("a").Label.Should().Be("字");
        harness.Node("a").RichText.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void An_empty_content_counts_as_no_content_at_all()
    {
        using var harness = new Harness();
        harness.AddNode("a", "字");

        // 空文本、空段落表都表达"没有内容"。两者在渲染上没差别，
        // 但在序列化与哈希上不一样：留着会让文档多出一个字段，而它什么都没表达。
        harness.SetField("a", FieldNames.RichLabel, "  ").IsNoOp.Should().BeTrue();
        harness.SetField("a", FieldNames.RichLabel, """{"paragraphs":[]}""").IsNoOp.Should().BeTrue();

        harness.Node("a").RichLabel.Should().BeNull();
        harness.Node("a").Label.Should().Be("字");
    }

    #endregion

    #region 整体校验

    [Fact]
    [Trait("Category", "RichText")]
    public void A_content_that_does_not_match_the_label_is_reported()
    {
        var document = IrFixtures.Build(nodes: [Node("画布上这段", Paragraph(Run("导出去那段")))]);

        var issue = DiagramValidator.Validate(document).Should().ContainSingle().Subject;

        issue.Code.Should().Be(ErrorCodes.RichTextMismatch);
        issue.RelatedId.Should().Be("a");
        issue.Message.Should().Contain("导出去那段").And.Contain("画布上这段");
        issue.Suggestion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void Content_with_the_switch_off_is_reported()
    {
        var node = Node("字", Paragraph(Run("字"))) with { RichText = false };
        var document = IrFixtures.Build(nodes: [node]);

        var issue = DiagramValidator.Validate(document).Should().ContainSingle().Subject;

        issue.Code.Should().Be(ErrorCodes.RichTextMismatch);
        issue.Message.Should().Contain("开关是关的");
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void The_switch_on_without_content_is_legal()
    {
        // 开着开关却没有内容，意思是"按富文本那条路画一段纯文字"。
        // 画出来的样子与纯文本一样，所以它不是矛盾，只是一个还没分段的状态。
        var document = IrFixtures.Build(nodes: [new NodeDef { Id = "a", Label = "字", RichText = true }]);

        DiagramValidator.Validate(document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void A_matching_content_has_no_issues()
    {
        var document = IrFixtures.Build(nodes: [Node("字", Paragraph(Run("字", new RichRunStyle { Bold = true })))]);

        DiagramValidator.Validate(document).Should().BeEmpty();
    }

    #endregion

    #region 命令

    [Fact]
    [Trait("Category", "RichText")]
    public void The_command_reports_a_visual_change_and_advances_the_version()
    {
        using var harness = new Harness();
        harness.AddNode("a", "旧");

        var versionBefore = harness.Document.Version;
        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        var result = harness.SetRichLabel("a", Content(Paragraph(Run("新", new RichRunStyle { Bold = true }))));

        result.IsEffectiveSuccess.Should().BeTrue();
        result.StructuralChanged.Should().BeFalse();
        result.VisualChanged.Should().BeTrue();
        harness.Document.Version.Should().Be(versionBefore + 1);
        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().NotBe(visualBefore);

        var change = result.FieldChanges.Should().ContainSingle().Subject;
        change.ElementId.Should().Be("a");
        change.Field.Should().Be(FieldNames.RichLabel);
        change.Kind.Should().Be(ChangeKind.Modified);
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void Writing_the_same_content_is_a_no_op()
    {
        using var harness = new Harness();
        harness.AddNode("a", "字");
        harness.SetRichLabel("a", Content(Paragraph(Run("字", new RichRunStyle { Bold = true }))));

        var versionBefore = harness.Document.Version;
        var result = harness.SetRichLabel("a", Content(Paragraph(Run("字", new RichRunStyle { Bold = true }))));

        result.IsSuccess.Should().BeTrue();
        result.IsNoOp.Should().BeTrue();
        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void A_reshaped_but_equivalent_content_is_also_a_no_op()
    {
        using var harness = new Harness();
        harness.AddNode("a", "字");
        harness.SetRichLabel("a", Content(Paragraph(Run("字", new RichRunStyle { Bold = true }))));

        var versionBefore = harness.Document.Version;

        // 折形态在写入时做，所以"同样式分成两段"与"合成一段"落到文档上是同一份内容。
        var result = harness.SetRichLabel(
            "a",
            Content(Paragraph(
                Run("字", new RichRunStyle { Bold = true }),
                Run(string.Empty))));

        result.IsNoOp.Should().BeTrue();
        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void Undo_puts_the_previous_definition_back()
    {
        using var harness = new Harness();
        harness.AddNode("a", "旧");

        var original = harness.Node("a");
        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        harness.SetRichLabel("a", Content(Paragraph(Run("新", new RichRunStyle { Bold = true }))));
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Node("a").Should().Be(original);

        // 撤销本身也是一次变更，版本继续往前走，所以比的是内容哈希而不是快照。
        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);

        harness.Bus.Redo().IsSuccess.Should().BeTrue();
        harness.Node("a").RichLabel!.Paragraphs[0].Runs[0].Style!.Bold.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void The_memento_survives_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.AddNode("a", "旧");

        var memento = new SetRichLabelCommand("a", Content(Paragraph(Run("新"))))
            .CaptureMemento(harness.Document);

        var restored = DiagramSerializer.DeserializeMemento(DiagramSerializer.SerializeMemento(memento));

        restored.Should().BeOfType<SetRichLabelMemento>();
        restored.AffectedIds.Should().Equal("a");

        harness.SetRichLabel("a", Content(Paragraph(Run("新"))));
        new SetRichLabelCommand("a", Content(Paragraph(Run("新"))))
            .RestoreMemento(harness.Document, restored);

        harness.Node("a").Label.Should().Be("旧");
        harness.Node("a").RichLabel.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void A_missing_node_is_refused()
    {
        using var harness = new Harness();

        var result = harness.SetRichLabel("ghost", Content(Paragraph(Run("字"))));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.NodeMissing);
    }

    [Fact]
    [Trait("Category", "RichText")]
    public void An_empty_node_id_is_refused()
    {
        using var harness = new Harness();

        var result = harness.SetRichLabel("  ", Content(Paragraph(Run("字"))));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.InvalidId);
    }

    #endregion

    #region 夹具

    private static NodeDef Node(string label, params RichParagraph[] paragraphs) => new()
    {
        Id = "a",
        Label = label,
        RichText = true,
        RichLabel = Content(paragraphs),
    };

    private static RichTextContent Content(params RichParagraph[] paragraphs) =>
        new() { Paragraphs = paragraphs };

    private static RichParagraph Paragraph(params RichRun[] runs) => new() { Runs = runs };

    private static RichRun Run(string text, RichRunStyle? style = null) => new() { Text = text, Style = style };

    private static DiagramDocument RoundTrip(DiagramDocument document) =>
        DiagramSerializer.DeserializeFull(DiagramSerializer.SerializeFull(document));

    #endregion
}
