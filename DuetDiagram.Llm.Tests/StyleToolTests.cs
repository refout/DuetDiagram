using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 外观类动作：白名单、前缀区分、画布成员、调色板增删。
/// </summary>
public sealed class StyleToolTests
{
    #region 样式令牌的白名单

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void An_unknown_token_is_rejected_and_the_available_ones_are_listed()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Style(registry, """{"action":"define-palette-entry","token":"primary"}""").IsSuccess.Should().BeTrue();

        var result = Harness.Style(registry, """{"action":"set-style","id":"a","token":"danger"}""");

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("token");
        result.Errors[0].Expected.Should().Contain("primary",
            "不列出可用令牌的话，模型只能猜着重试，而每次重试都是一轮往返");
        document.Nodes.Single().StyleToken.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void The_whitelist_follows_the_palette()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var before = Harness.Style(registry, """{"action":"set-style","id":"a","token":"primary"}""");

        before.IsSuccess.Should().BeFalse("调色板还是空的，这个令牌当然不在白名单里");

        Harness.Style(registry, """{"action":"define-palette-entry","token":"primary","field":"palette.fill","value":"#3366cc"}""")
            .IsSuccess.Should().BeTrue();

        Harness.Style(registry, """{"action":"set-style","id":"a","token":"primary"}""").IsSuccess.Should().BeTrue();

        document.Nodes.Single().StyleToken.Should().Be("primary",
            "白名单是运行时判据：令牌表是文档里的数据，用户随时可以加一个");
    }

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void An_empty_palette_points_at_the_way_to_add_one()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var result = Harness.Style(registry, """{"action":"set-style","id":"a","token":"primary"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Expected.Should().Contain("define-palette-entry",
            "调色板是空的时候列出零个可用令牌没有意义，要说的是怎么加一个");
    }

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void A_style_token_change_is_visual_not_structural()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Style(registry, """{"action":"define-palette-entry","token":"primary"}""");

        var outcome = Harness.Style(registry, """{"action":"set-style","id":"a","token":"primary"}""").Data!.Value;

        outcome.GetProperty("visualChanged").GetBoolean().Should().BeTrue();
        outcome.GetProperty("structuralChanged").GetBoolean().Should().BeFalse(
            "换令牌只改像素，报成结构变更会让宿主白白重排一遍");
    }

    #endregion

    #region 前缀区分

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void set_style_refuses_a_text_member()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var result = Harness.Style(registry, """{"action":"set-text","id":"a","field":"style.fill","value":"#ff0000"}""");

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("field");
        result.Errors[0].Expected.Should().Contain(StyleResolver.TextPrefix);
        document.Nodes.Single().Style.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void set_text_refuses_a_style_member()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var result = Harness.Style(registry, """{"action":"set-style","id":"a","field":"text.fontSize","value":"14"}""");

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Expected.Should().Contain(StyleResolver.StylePrefix,
            "前缀检查是这两个动作各自唯一的那点语义，不查的话两个动作的说明就成了一句空话");
    }

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void set_style_without_a_token_or_a_member_is_rejected()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var result = Harness.Style(registry, """{"action":"set-style","id":"a"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("field");
        document.Version.Should().Be(1, "参数不成立时不该发命令");
    }

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void set_style_and_set_text_write_their_members()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        Harness.Style(registry, """{"action":"set-style","id":"a","field":"style.fill","value":"#ff0000"}""")
            .IsSuccess.Should().BeTrue();

        Harness.Style(registry, """{"action":"set-text","id":"a","field":"text.fontSize","value":"14"}""")
            .IsSuccess.Should().BeTrue();

        var node = document.Nodes.Single();

        node.Style!.Fill.Should().Be("#ff0000");
        node.Text!.FontSize.Should().Be(14);
    }

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void set_shape_writes_the_shape_field()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Style(registry, """{"action":"set-shape","id":"a","value":"Diamond"}""").IsSuccess.Should().BeTrue();

        document.Nodes.Single().Shape.Should().Be(NodeShape.Diamond);
    }

    #endregion

    #region 画布设置

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void set_canvas_writes_each_member()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Style(registry, """{"action":"set-canvas","field":"canvas.grid","value":"Dots"}""").IsSuccess.Should().BeTrue();
        Harness.Style(registry, """{"action":"set-canvas","field":"canvas.gridSize","value":"24"}""").IsSuccess.Should().BeTrue();
        Harness.Style(registry, """{"action":"set-canvas","field":"canvas.pageSize","value":"1123x794"}""").IsSuccess.Should().BeTrue();
        Harness.Style(registry, """{"action":"set-canvas","field":"canvas.orientation","value":"Portrait"}""").IsSuccess.Should().BeTrue();
        Harness.Style(registry, """{"action":"set-canvas","field":"canvas.background","value":"#ffffff"}""").IsSuccess.Should().BeTrue();
        Harness.Style(registry, """{"action":"set-canvas","field":"canvas.infinite","value":"false"}""").IsSuccess.Should().BeTrue();

        var canvas = document.Canvas;

        canvas.Grid.Should().Be(GridStyle.Dots);
        canvas.GridSize.Should().Be(24);
        canvas.PageSize.Should().Be(new Size(1123, 794));
        canvas.Orientation.Should().Be(CanvasOrientation.Portrait);
        canvas.Background.Should().Be("#ffffff");
        canvas.Infinite.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void set_canvas_rejects_an_unknown_member()
    {
        var registry = Harness.Registry();

        var result = Harness.Style(registry, """{"action":"set-canvas","field":"canvas.colour","value":"red"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("field");
        result.Errors[0].Expected.Should().Contain("canvas.gridSize");
    }

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void set_canvas_rejects_a_value_that_does_not_fit_its_member()
    {
        var registry = Harness.Registry();

        var orientation = Harness.Style(registry, """{"action":"set-canvas","field":"canvas.orientation","value":"Sideways"}""");
        var pageSize = Harness.Style(registry, """{"action":"set-canvas","field":"canvas.pageSize","value":"1123"}""");
        var flag = Harness.Style(registry, """{"action":"set-canvas","field":"canvas.infinite","value":"maybe"}""");

        Harness.CodeOf(orientation).Should().Be(ToolErrorCodes.ArgumentInvalid);
        orientation.Errors[0].Expected.Should().Contain("Portrait");
        Harness.CodeOf(pageSize).Should().Be(ToolErrorCodes.ArgumentInvalid);
        pageSize.Errors[0].Expected.Should().Contain("1123x794");
        Harness.CodeOf(flag).Should().Be(ToolErrorCodes.ArgumentInvalid);
        flag.Errors[0].Message.Should().Contain("true");
    }

    #endregion

    #region 调色板

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void Palette_entries_go_through_their_commands()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Style(registry, """{"action":"define-palette-entry","token":"primary","field":"palette.fill","value":"#3366cc"}""")
            .IsSuccess.Should().BeTrue();

        document.Palette.Find("primary")!.Fill.Should().Be("#3366cc");

        Harness.Style(registry, """{"action":"update-palette-entry","token":"primary","field":"palette.weight","value":"2"}""")
            .IsSuccess.Should().BeTrue();

        document.Palette.Find("primary")!.Weight.Should().Be(2);

        Harness.Style(registry, """{"action":"remove-palette-entry","token":"primary"}""").IsSuccess.Should().BeTrue();

        document.Palette.Find("primary").Should().BeNull();
    }

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void A_member_that_the_palette_cannot_write_is_rejected()
    {
        var registry = Harness.Registry();

        var result = Harness.Style(
            registry,
            """{"action":"define-palette-entry","token":"primary","field":"palette.name","value":"other"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("field");
        result.Errors[0].Expected.Should().Contain(FieldNames.PaletteFill,
            "令牌名是条目的标识而不是它的属性，改它等于换一个条目");
    }

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void Removing_a_token_that_is_still_in_use_is_blocked()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Style(registry, """{"action":"define-palette-entry","token":"primary"}""");
        Harness.Style(registry, """{"action":"set-style","id":"a","token":"primary"}""").IsSuccess.Should().BeTrue();

        var result = Harness.Style(registry, """{"action":"remove-palette-entry","token":"primary"}""");

        Harness.CodeOf(result).Should().Be(ErrorCodes.PaletteEntryInUse,
            "删掉一个被引用的令牌不会报错、不会崩，只会让一批节点悄悄换了颜色");
        result.Errors[0].Message.Should().Contain("：a", "载荷里要带上引用者，用户才知道该先改哪些元素");
        document.Palette.Find("primary").Should().NotBeNull();
    }

    #endregion

    #region 未知动作

    [Fact]
    [Trait("Category", "StyleWhitelist")]
    public void An_unknown_style_action_lists_the_available_ones()
    {
        var result = Harness.Style(Harness.Registry(), """{"action":"set-color"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("action");
        result.Errors[0].Expected.Should().Contain("set-style").And.Contain("define-palette-entry");
    }

    #endregion
}
