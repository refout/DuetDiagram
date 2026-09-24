using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 公式排版的边界：认得的排得出来，认不出的一律给结构化错误。
/// </summary>
/// <remarks>
/// <para>
/// 这一层盯的是**边界**而不是能力。公式排版没有终点——LaTeX 的子集一层套一层，
/// 每一层都能说"再支持一个记号就好"。所以判据定成"支持的集合有清单、清单外一律结构化错误"，
/// 而不是"画得对"：后者没有可验的形式。
/// </para>
/// <para>
/// 清单本身也由用例守住：符号表、函数表与间距表里的每一个名字都排一遍。
/// 只验几个样例的话，往表里加一个拼错的名字不会有人发现，而那个名字永远排不出来，
/// 也永远不会被报错。
/// </para>
/// </remarks>
public sealed class MathTypesettingTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    private static readonly TextAppearance Text = new(
        "#000000", "test", 14, FontWeight.Normal, TextAlign.Center, VerticalAlign.Middle, 1.35);

    #region 支持的集合

    [Theory]
    [Trait("Category", "MathTypesetting")]
    [InlineData("x")]
    [InlineData("12")]
    [InlineData("\\alpha")]
    [InlineData("\\times")]
    [InlineData("\\sin x")]
    [InlineData("\\,")]
    [InlineData("\\frac{a}{b}")]
    [InlineData("\\sqrt{x}")]
    [InlineData("x^2")]
    [InlineData("a_i")]
    [InlineData("x_i^2")]
    [InlineData("{a+b}")]
    [InlineData("")]
    public void A_formula_on_the_list_typesets(string source)
    {
        var ok = MathTypesetter.TryLayout(source, Text, Measurer, out var body, out var error);

        ok.Should().BeTrue($"{source} 在清单里，不该被报成认不出来");
        error.Should().BeNull();

        if (source.Length > 0)
        {
            body.Width.Should().BeGreaterThan(0, "认得的公式要占一块地方");
        }
    }

    /// <summary>空公式排出来是空的，不是错误。</summary>
    /// <remarks>
    /// 把空串判成错误的话，一个还没填内容的节点会一直报错，而那种错没有任何处置。
    /// </remarks>
    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void An_empty_formula_is_empty_rather_than_wrong()
    {
        MathTypesetter.TryLayout(string.Empty, Text, Measurer, out var body, out var error).Should().BeTrue();

        error.Should().BeNull();
        body.Segments.Should().BeEmpty();
        body.Rules.Should().BeEmpty();
        body.Width.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void Every_symbol_on_the_list_typesets()
    {
        MathSyntax.KnownSymbols.Should().NotBeEmpty("空清单会让这一条永远通过");

        foreach (var name in MathSyntax.KnownSymbols)
        {
            MathTypesetter.TryLayout($"\\{name}", Text, Measurer, out var body, out var error)
                .Should().BeTrue($"\\{name} 在符号表里，就该排得出来；报出来的是 {error?.Detail}");

            body.Width.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void Every_function_on_the_list_typesets()
    {
        MathSyntax.KnownFunctions.Should().NotBeEmpty();

        foreach (var name in MathSyntax.KnownFunctions)
        {
            MathTypesetter.TryLayout($"\\{name} x", Text, Measurer, out var body, out var error)
                .Should().BeTrue($"\\{name} 在函数表里；报出来的是 {error?.Detail}");

            body.Width.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void Every_space_on_the_list_typesets()
    {
        MathSyntax.KnownSpaces.Should().NotBeEmpty();

        foreach (var name in MathSyntax.KnownSpaces)
        {
            // 名字后面留一个空格：命令名按字母一口气读完，`\quadb` 读出来是一个名字。
            MathTypesetter.TryLayout($"a\\{name} b", Text, Measurer, out _, out var error)
                .Should().BeTrue($"\\{name} 在间距表里；报出来的是 {error?.Detail}");
        }
    }

    /// <summary>间距命令真的拉开了距离。</summary>
    /// <remarks>
    /// 只验"排得出来"的话，一个把间距排成零宽的实现也能全绿——而那种实现
    /// 与"这个命令没实现"在画面上分不出差别。
    /// </remarks>
    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void A_space_command_widens_the_row()
    {
        var plain = Body("ab");
        var spaced = Body("a\\quad b");

        spaced.Width.Should().BeGreaterThan(plain.Width);
    }

    #endregion

    #region 清单外的输入

    [Theory]
    [Trait("Category", "MathTypesetting")]
    [InlineData("\\nope", MathErrorCodes.UnknownCommand, 0)]
    [InlineData("a\\nope", MathErrorCodes.UnknownCommand, 1)]
    [InlineData("a}", MathErrorCodes.UnbalancedBrace, 1)]
    [InlineData("{a", MathErrorCodes.UnbalancedBrace, 2)]
    [InlineData("\\frac{a}", MathErrorCodes.MissingArgument, 8)]
    [InlineData("^2", MathErrorCodes.ScriptWithoutBase, 0)]
    [InlineData("x^2^3", MathErrorCodes.DoubleScript, 3)]
    [InlineData("x_1_2", MathErrorCodes.DoubleScript, 3)]
    public void An_input_off_the_list_gives_a_structured_error(string source, string code, int position)
    {
        MathTypesetter.TryLayout(source, Text, Measurer, out _, out var error).Should().BeFalse();

        error.Should().NotBeNull();
        error!.Code.Should().Be(code);

        // 位置要指在出错那一处，而不是笼统地说"语法错了"——一段长公式里
        // 用户要自己找那一处，而找不到就会把整段删掉重写。
        error.Position.Should().Be(position);
        error.Detail.Should().NotBeEmpty();
    }

    /// <summary>空的花括号组也算缺参数。</summary>
    /// <remarks>
    /// 位置不在这里断言：报的是"读完之后"的下标，而那个下标会随实现变，
    /// 盯着它只会让这一条在每次调整读法时变红。
    /// </remarks>
    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void An_empty_group_counts_as_a_missing_argument()
    {
        MathTypesetter.TryLayout("\\frac{a}{}", Text, Measurer, out _, out var error).Should().BeFalse();

        error!.Code.Should().Be(MathErrorCodes.MissingArgument);
    }

    /// <summary>认不出的公式不画，也不占地方。</summary>
    /// <remarks>
    /// <para>
    /// 原样画出来的话，用户看到的是一串花括号，而他会以为是自己语法写错了。
    /// </para>
    /// <para>
    /// 度量也不退回按纯文本量：那样节点会突然变成一行字那么高，而画面上什么都没有——
    /// 尺寸与内容对不上的那种不一致最难查。
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void An_unrecognized_formula_is_not_drawn_at_all()
    {
        var node = new NodeDef { Id = "n1", Label = "\\nope", MathMode = MathMode.Inline };

        MathTypesetter.Measure(node.Label, Text, Measurer).Should().Be(new Size(0, 0));

        var list = Draw(node);

        list.Commands.OfType<DrawText>().Should().BeEmpty("认不出的公式一条指令都不该出");
        list.Commands.OfType<DrawPolyline>().Should().BeEmpty();
        list.Commands.OfType<DrawShape>().Should().ContainSingle(shape => shape.ElementId == "n1");
    }

    #endregion

    #region 两档模式

    /// <summary>
    /// 行内按基线对齐，独立成行按整块居中。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判据不看具体坐标，看的是两条不变量：行内那一档，两个高低差很多的公式
    /// 的基线落在同一个高度上；独立成行那一档，两个公式的中心落在同一个高度上。
    /// 写成坐标相等的话，改一处间距常数就要跟着改期望值，而那种红与"算错了"分不清。
    /// </para>
    /// <para>
    /// 两档用同一套处理的话，行内公式会把整行文字顶起来：带分数的公式比一行字高得多，
    /// 按框居中之后它的基线会掉到文字基线下面。
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void The_two_modes_align_differently()
    {
        var container = new SpatialRect(0, 0, 240, 120);
        var shortBody = Body("x");
        var tallBody = Body("\\frac{a}{b}");

        tallBody.Height.Should().BeGreaterThan(shortBody.Height, "这条用例要的是一个高矮差明显的公式");

        var shortInline = MathTypesetter.Place(shortBody, MathMode.Inline, Text, container);
        var tallInline = MathTypesetter.Place(tallBody, MathMode.Inline, Text, container);

        (shortInline.Y + shortBody.Baseline).Should().BeApproximately(tallInline.Y + tallBody.Baseline, 1e-9);

        var shortBlock = MathTypesetter.Place(shortBody, MathMode.Block, Text, container);
        var tallBlock = MathTypesetter.Place(tallBody, MathMode.Block, Text, container);

        (shortBlock.Y + (shortBody.Height / 2)).Should().BeApproximately(container.CenterY, 1e-9);
        (tallBlock.Y + (tallBody.Height / 2)).Should().BeApproximately(container.CenterY, 1e-9);

        // 同一个公式在两档里落点不同，否则"分两档"这件事在画面上看不出来。
        tallInline.Y.Should().NotBeApproximately(tallBlock.Y, 1e-9);
    }

    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void Both_modes_center_the_formula_horizontally()
    {
        var container = new SpatialRect(0, 0, 240, 120);
        var body = Body("\\frac{a}{b}");

        foreach (var mode in new[] { MathMode.Inline, MathMode.Block })
        {
            var origin = MathTypesetter.Place(body, mode, Text, container);

            origin.X.Should().BeApproximately(container.X + ((container.Width - body.Width) / 2), 1e-9);
        }
    }

    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void A_node_is_a_math_node_only_when_its_mode_says_so()
    {
        MathTypesetter.HasMath(new NodeDef { Id = "n1" }).Should().BeFalse();
        MathTypesetter.HasMath(new NodeDef { Id = "n1", MathMode = MathMode.Inline }).Should().BeTrue();
        MathTypesetter.HasMath(new NodeDef { Id = "n1", MathMode = MathMode.Block }).Should().BeTrue();
    }

    #endregion

    #region 度量与确定性

    /// <summary>尺寸一律由注入的度量器给，不另起一套字体。</summary>
    /// <remarks>
    /// 判据取的是"度量器说什么就是什么"：换一个每个字都报同样宽度的度量器，
    /// 排出来的宽度就该是那个宽度乘字数。另起一套字体栈的话，
    /// 表现是公式里的字与旁边的字不是同一个字体，而这一条会先红。
    /// </remarks>
    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void The_width_comes_from_the_injected_measurer()
    {
        var three = MathTypesetter.Measure("abc", Text, new FixedMeasurer(100, 20));
        var alsoThree = MathTypesetter.Measure("abc", Text, new FixedMeasurer(50, 20));

        three.Width.Should().Be(300, "三个原子，每个报 100 宽");
        alsoThree.Width.Should().Be(150);
        three.Height.Should().Be(20);
    }

    /// <summary>上下标按小一号的字号去量。</summary>
    /// <remarks>
    /// 量的时候不换字号的话，上下标会与底一样大，而"上标"这件事在画面上就没了。
    /// </remarks>
    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void A_script_is_measured_at_a_smaller_size()
    {
        var recorder = new RecordingMeasurer();
        var body = MathTypesetter.Measure("x^2", Text, recorder);

        recorder.Sizes.Should().Contain(size => size < Text.FontSize);
        body.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void The_same_input_typesets_the_same_way_every_time()
    {
        var first = Body("\\frac{a}{b} + \\sqrt{x^2+y^2}");
        var second = Body("\\frac{a}{b} + \\sqrt{x^2+y^2}");

        second.Width.Should().Be(first.Width);
        second.Height.Should().Be(first.Height);
        second.Baseline.Should().Be(first.Baseline);
        second.Segments.Should().Equal(first.Segments);
        second.Rules.Should().Equal(first.Rules);
    }

    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void The_same_document_draws_the_same_list_every_time()
    {
        var node = new NodeDef { Id = "n1", Label = "\\frac{a}{b}", MathMode = MathMode.Inline };

        Draw(node).ToText().Should().Be(Draw(node).ToText());
    }

    #endregion

    #region 画进列表

    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void A_formula_reaches_the_list_as_plain_text_and_polyline()
    {
        // 公式拆成普通的文本段与折线出指令，不另立一种指令——这样画布、剔除、
        // 高亮、命中与导出都照原样认识它，一处都不用改。
        var list = Draw(new NodeDef { Id = "n1", Label = "\\sqrt{x}", MathMode = MathMode.Inline });

        list.Commands.OfType<DrawText>().Should().ContainSingle(text => text.Text == "x");
        list.Commands.OfType<DrawPolyline>().Should().NotBeEmpty("根号的上横线与钩子要画出来");
    }

    [Fact]
    [Trait("Category", "MathTypesetting")]
    public void A_variable_is_italic_and_a_digit_is_not()
    {
        var list = Draw(new NodeDef { Id = "n1", Label = "x2", MathMode = MathMode.Inline });

        var texts = list.Commands.OfType<DrawText>().ToArray();

        texts[0].Italic.Should().BeTrue("字母是变量");
        texts[1].Italic.Should().BeFalse("数字是正体");
    }

    #endregion

    #region 夹具

    private static MathBody Body(string source)
    {
        MathTypesetter.TryLayout(source, Text, Measurer, out var body, out var error).Should().BeTrue(error?.Detail);
        return body;
    }

    private static DrawList Draw(NodeDef node)
    {
        var theme = Theme.Default;
        var size = SceneBuilder.MeasureNode(node, theme, Measurer);
        var placed = new[] { new PlacedNode(node.Id, 40, 40, size.Width, size.Height) };
        var document = DiagramDocument.CreateFromContent("math", nodes: [node]);

        return SceneBuilder.Build(document, Layouts.Result(placed, [], 400, 300), theme, Measurer);
    }

    /// <summary>每个字都报同一个宽高的度量器。</summary>
    private sealed class FixedMeasurer(double width, double height) : ITextMeasurer
    {
        public Size Measure(string text, string fontFamily, double fontSize, FontWeight weight, bool italic = false) =>
            new(width, height);
    }

    /// <summary>记下每次量用到的字号。</summary>
    private sealed class RecordingMeasurer : ITextMeasurer
    {
        private readonly FakeTextMeasurer _inner = new();

        public List<double> Sizes { get; } = [];

        public Size Measure(string text, string fontFamily, double fontSize, FontWeight weight, bool italic = false)
        {
            Sizes.Add(fontSize);
            return _inner.Measure(text, fontFamily, fontSize, weight, italic);
        }
    }

    #endregion
}
