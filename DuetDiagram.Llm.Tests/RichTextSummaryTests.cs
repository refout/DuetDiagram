using System.Text.Json;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Context;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 富文本在归一化摘要里的投影，以及改标签那条工具路对两种标签的处理。
/// </summary>
/// <remarks>
/// <para>
/// 摘要是模型每一轮都要读的东西，所以它里面的每一个字都必须**每次一样**：
/// 投影抖一下，模型就会以为"文档变了"，而去重做一遍已经做完的事。
/// </para>
/// <para>
/// 工具那一路要验的是"两条路给出同样形状的结果"：模型不必先知道这个节点是富文本
/// 还是纯文本，才知道该用哪种写法。形状不一样的话，它得先读一次图再决定，
/// 而那一次读本身就要一轮往返。
/// </para>
/// </remarks>
public sealed class RichTextSummaryTests
{
    #region 摘要里的投影

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void A_rich_node_carries_a_plain_text_projection()
    {
        var summary = Build(Rich("新的文字", "第二段"));

        var node = summary.Nodes.Should().ContainSingle().Subject;

        node.RichText.Should().Be("新的文字\n第二段");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void The_projection_is_the_same_every_time()
    {
        var document = Document(Rich("新的文字", "第二段"));

        var first = SummaryBuilder.Build(new SummaryInput { Document = document });
        var second = SummaryBuilder.Build(new SummaryInput { Document = document });

        first.Nodes[0].RichText.Should().Be(second.Nodes[0].RichText);

        SummaryFormatter.Format(first, Harness.Now)
            .Should().Be(SummaryFormatter.Format(second, Harness.Now), "同一份文档两次渲染要逐字相同");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void The_projection_comes_from_the_content_not_from_the_label()
    {
        // 内容在的时候它是权威。这份文档的两份表达对不上，而摘要是给模型读的，
        // 它该读到画布上真有的那段文字，而不是一份过期的标签。
        var summary = Build(Rich("新的文字", label: "旧的投影"));

        var text = SummaryFormatter.Format(summary, Harness.Now);

        text.Should().Contain("新的文字");
        text.Should().NotContain("旧的投影");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void A_node_without_rich_text_carries_no_projection()
    {
        var summary = Build(Plain("纯文本"));

        summary.Nodes[0].RichText.Should().BeNull("没有内容就没有投影，摘要里也不该多一个字段");
        SummaryFormatter.Format(summary, Harness.Now).Should().Contain("a(纯文本)");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void The_projection_carries_no_styling_marks()
    {
        // 投影是纯文本：标记语法得另外教给模型，而它要的是那段文字本身。
        var summary = Build(Rich("加粗的字", styled: true));

        summary.Nodes[0].RichText.Should().Be("加粗的字");
    }

    #endregion

    #region 工具那条路

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Writing_a_rich_label_has_the_same_shape_as_writing_a_plain_one()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a","label":"旧"}""");

        var plain = Harness.Edit(registry, """{"action":"set-node-field","id":"a","field":"label","value":"新"}""");
        var rich = Harness.Edit(registry, RichLabelArguments("a", """{"paragraphs":[{"runs":[{"text":"新"}]}]}"""));

        plain.IsSuccess.Should().BeTrue();
        rich.IsSuccess.Should().BeTrue();

        // 形状一致：两条路都回一个带版本与两个变更标志的结果，
        // 模型不必先知道这个节点是不是富文本，才知道该按哪种读法读结果。
        Shape(plain).Should().Be(Shape(rich));
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Writing_rich_content_through_the_tool_synchronises_the_label()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a","label":"旧"}""");

        var result = Harness.Edit(
            registry,
            RichLabelArguments("a", """{"paragraphs":[{"runs":[{"text":"新"},{"text":"字"}]}]}"""));

        result.IsSuccess.Should().BeTrue();
        document.Nodes.Single().Label.Should().Be("新字");
        document.Nodes.Single().RichText.Should().BeTrue();
        document.Nodes.Single().RichLabel.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void An_unknown_inline_style_name_is_refused_through_the_tool()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a","label":"旧"}""");

        var result = Harness.Edit(
            registry,
            RichLabelArguments("a", """{"paragraphs":[{"runs":[{"text":"新","style":{"shadow":true}}]}]}"""));

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ErrorCodes.FieldValueInvalid);
        result.Errors[0].Message.Should().Contain("shadow");
        document.Nodes.Single().RichLabel.Should().BeNull();
    }

    #endregion

    #region 夹具

    /// <summary>工具参数里那个值本身是一段 JSON，所以要再转义一层。</summary>
    private static string RichLabelArguments(string id, string content) =>
        $$"""{"action":"set-node-field","id":{{JsonSerializer.Serialize(id)}},"field":"richLabel","value":{{JsonSerializer.Serialize(content)}}}""";

    /// <summary>一次成功答复的形状：版本之外的那几项。</summary>
    private static string Shape(ToolResult result)
    {
        var data = result.Data!.Value;

        return string.Join(
            '|',
            data.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    private static DiagramSummary Build(NodeDef node) =>
        SummaryBuilder.Build(new SummaryInput { Document = Document(node) });

    private static DiagramDocument Document(params NodeDef[] nodes) =>
        DiagramDocument.CreateFromContent("rich-doc", nodes: nodes);

    private static NodeDef Plain(string label) => new() { Id = "a", Label = label };

    /// <summary>
    /// 一个带内容的节点。
    /// </summary>
    /// <param name="first">第一段的文字。</param>
    /// <param name="second">第二段的文字。留空表示只有一个段落。</param>
    /// <param name="styled">第一段是否带行内样式。</param>
    /// <param name="label">
    /// 标签。留空时取内容的投影——那才是一份合法的文档。给了别的值就造出一份
    /// 两份表达对不上的文档，用来验摘要读的是哪一份。
    /// </param>
    private static NodeDef Rich(string first, string second = "", bool styled = false, string? label = null)
    {
        var paragraphs = new List<RichParagraph>
        {
            new()
            {
                Runs =
                [
                    new RichRun
                    {
                        Text = first,
                        Style = styled ? new RichRunStyle { Bold = true } : null,
                    },
                ],
            },
        };

        if (second.Length > 0)
        {
            paragraphs.Add(new RichParagraph { Runs = [new RichRun { Text = second }] });
        }

        return new NodeDef
        {
            Id = "a",
            Label = label ?? string.Join('\n', paragraphs.Select(paragraph => paragraph.PlainText)),
            RichText = true,
            RichLabel = new RichTextContent { Paragraphs = paragraphs },
        };
    }

    #endregion
}
