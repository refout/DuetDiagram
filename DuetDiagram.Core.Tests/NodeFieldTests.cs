using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 改节点字段的那条命令。
/// </summary>
/// <remarks>
/// <para>
/// 这一组测试的重心不在"某个字段改得对不对"，而在**字段表、读写实现与命令三者
/// 有没有对上**。三者各在一处，任意一处加了字段而另外两处没跟上，
/// 表现都是"面板上那个编辑器改不动文档"，而界面上看不出任何异常。
/// </para>
/// <para>
/// 所以有一条测试直接拿字段表当输入，逐个字段走一遍读、写、还原。
/// 加字段的人不需要记得来改这里，字段表变了它自己就会覆盖到。
/// </para>
/// </remarks>
public sealed class NodeFieldTests
{
    [Fact]
    [Trait("Category", "NodeField")]
    public void Writable_fields_are_exactly_the_registered_node_fields()
    {
        var registered = FieldRegistry.All
            .Where(f => f.Owner == "节点")
            .Select(f => f.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        NodeFieldValue.Writable.Order(StringComparer.Ordinal).Should().Equal(registered);
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void Element_level_names_are_not_writable()
    {
        // 带 @ 前缀的名字表达的是"整个元素被增删"，不是某个字段。
        // 它们混进可写集合之后，一次"改字段"就会被写成一次元素级变更。
        NodeFieldValue.IsWritable(FieldNames.NodeElement).Should().BeFalse();
        NodeFieldValue.IsWritable(FieldNames.EdgeElement).Should().BeFalse();
        NodeFieldValue.IsWritable(null).Should().BeFalse();
        NodeFieldValue.IsWritable("no-such-field").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void Every_writable_field_can_be_read()
    {
        var node = Sample();

        foreach (var field in NodeFieldValue.Writable)
        {
            NodeFieldValue.Read(node, field).Should().NotBeNull($"字段 {field} 没有读法");
        }
    }

    [Theory]
    [MemberData(nameof(WritableFields))]
    [Trait("Category", "NodeField")]
    public void Reading_then_writing_the_same_field_changes_nothing(string field)
    {
        var node = Sample();
        var text = NodeFieldValue.Read(node, field);

        NodeFieldValue.TryWrite(node, field, text, out var updated, out var error).Should().BeTrue(error);
        updated.Should().Be(node, $"字段 {field} 读出来再写回去应当得到同一份定义");
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void An_unknown_field_is_rejected_before_touching_the_document()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        var before = harness.Snapshot();

        var result = harness.SetField("a", "no-such-field", "x");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.FieldUnknown);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void An_element_level_name_is_rejected_as_an_unknown_field()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        var result = harness.SetField("a", FieldNames.NodeElement, "x");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.FieldUnknown);
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void A_missing_node_is_rejected()
    {
        using var harness = new Harness();

        var result = harness.SetField("ghost", FieldNames.Label, "x");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.NodeMissing);
    }

    [Theory]
    [InlineData(FieldNames.Shape, "Triangle")]
    [InlineData(FieldNames.Shape, "7")]
    [InlineData(FieldNames.MathMode, "Sometimes")]
    [InlineData(FieldNames.RichText, "yes")]
    [InlineData(FieldNames.Style, "{ not json")]
    [InlineData(FieldNames.Text, "[1,2,3]")]
    [InlineData(FieldNames.Ports, "{\"name\":\"p\"}")]
    [InlineData(FieldNames.Meta, "not-json")]
    [Trait("Category", "NodeField")]
    public void A_value_of_the_wrong_shape_is_rejected_with_a_reason(string field, string value)
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        var before = harness.Snapshot();
        var versionBefore = harness.Document.Version;

        var result = harness.SetField("a", field, value);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.FieldValueInvalid);

        // 错误里要带上是哪个字段、收到了什么，否则界面上只能显示一句"值不合法"。
        result.Errors[0].Payload.Should().NotBeNullOrWhiteSpace();

        harness.Snapshot().Should().Be(before);
        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void A_numeric_enum_value_is_rejected()
    {
        // 枚举的解析器接受数字文本，于是 "7" 会被当成一个合法取值收下，
        // 而那个值在渲染时分发不到任何分支——表现是节点忽然什么都不画。
        using var harness = new Harness();
        harness.AddNode("a", "A");

        harness.SetField("a", FieldNames.Shape, "999").IsSuccess.Should().BeFalse();
        harness.Document.Nodes[0].Shape.Should().Be(NodeShape.Rect);
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void Writing_the_same_value_is_a_no_op()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        var versionBefore = harness.Document.Version;

        var result = harness.SetField("a", FieldNames.Label, "A");

        result.IsSuccess.Should().BeTrue();
        result.IsNoOp.Should().BeTrue();
        harness.Document.Version.Should().Be(versionBefore);
        harness.Context.History.UndoCount.Should().Be(1);
        harness.Context.VersionLog.Count.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void A_successful_write_advances_the_version_by_one()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        var versionBefore = harness.Document.Version;
        var hashBefore = harness.Document.VisualHash;

        var result = harness.SetField("a", FieldNames.Label, "B");

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Version.Should().Be(versionBefore + 1);
        harness.Document.Nodes[0].Label.Should().Be("B");
        harness.Document.VisualHash.Should().NotBe(hashBefore);

        var change = result.FieldChanges.Should().ContainSingle().Subject;
        change.ElementId.Should().Be("a");
        change.Field.Should().Be(FieldNames.Label);
        change.OldValue.Should().Be("A");
        change.NewValue.Should().Be("B");
        change.Kind.Should().Be(ChangeKind.Modified);
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void Undo_puts_the_previous_definition_back_byte_for_byte()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        var original = harness.Document.Nodes[0];
        var versionBefore = harness.Document.Version;

        harness.SetField("a", FieldNames.Style, "{\"fill\":\"#ff0000\",\"weight\":2}").IsSuccess.Should().BeTrue();
        harness.Document.Nodes[0].Should().NotBe(original);

        var undone = harness.Bus.Undo();

        undone.IsSuccess.Should().BeTrue();

        // 撤销把整份旧定义放回去，所以逐字段与逐字节都应当与原来那份一致。
        // 只比"某个字段回到原值"是不够的：memento 存的是整份定义，
        // 少还原一个没注意到的字段，差异只体现在哈希上，界面上看不出来。
        harness.Document.Nodes[0].Should().Be(original);

        // 撤销本身也是一次变更，版本继续往前走——版本号是变更计数，不是内容指纹。
        harness.Document.Version.Should().Be(versionBefore + 2);

        // 重做也要能走通：撤销与重做复用同一份 memento 的实现，
        // 只恢复重做栈而漏掉文档状态是这条路径上最容易出的错。
        harness.Bus.Redo().IsSuccess.Should().BeTrue();
        harness.Document.Nodes[0].Style!.Fill.Should().Be("#ff0000");
    }

    [Theory]
    [InlineData(FieldNames.Label, false)]
    [InlineData(FieldNames.Shape, false)]
    [InlineData(FieldNames.Layer, false)]
    [InlineData(FieldNames.StyleToken, false)]
    [InlineData(FieldNames.Meta, false)]
    [InlineData(FieldNames.Parent, true)]
    [InlineData(FieldNames.Ports, true)]
    [Trait("Category", "NodeField")]
    public void The_two_changed_flags_follow_the_field_scope(string field, bool structural)
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        var value = field switch
        {
            FieldNames.Parent => "g",
            FieldNames.Ports => "[{\"name\":\"p\",\"side\":\"Top\"}]",
            FieldNames.Meta => "{\"k\":\"v\"}",
            FieldNames.Shape => "Diamond",
            FieldNames.Layer => "l1",
            FieldNames.StyleToken => "primary",
            _ => "x",
        };

        var result = harness.SetField("a", field, value);

        result.IsSuccess.Should().BeTrue();
        result.StructuralChanged.Should().Be(structural);

        // 结构类的改动同时也要重绘——坐标失效之后画面必然要重画一次。
        // 第三类（宿主数据）两者都不影响，只有版本号会动。
        var expectedVisual = FieldRegistry.Descriptor(field)!.Scope != FieldScope.Neither;
        result.VisualChanged.Should().Be(expectedVisual);
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void An_empty_text_clears_an_optional_field()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        harness.SetField("a", FieldNames.Desc, "说明").IsSuccess.Should().BeTrue();
        harness.Document.Nodes[0].Desc.Should().Be("说明");

        harness.SetField("a", FieldNames.Desc, "   ").IsSuccess.Should().BeTrue();

        // 空文本读成"没有值"而不是空串：两者在渲染上没差别，
        // 但写进去一个空串会让文档多出一个字段，而它表达的意思完全相同。
        harness.Document.Nodes[0].Desc.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void An_empty_text_on_a_composite_field_clears_it()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        harness.SetField("a", FieldNames.Text, "{\"fontSize\":14}").IsSuccess.Should().BeTrue();
        harness.Document.Nodes[0].Text.Should().NotBeNull();

        harness.SetField("a", FieldNames.Text, "").IsSuccess.Should().BeTrue();
        harness.Document.Nodes[0].Text.Should().BeNull();

        harness.SetField("a", FieldNames.Ports, "").IsSuccess.Should().BeTrue();
        harness.Document.Nodes[0].Ports.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void The_memento_survives_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");
        harness.SetField("a", FieldNames.Shape, "Diamond");

        var memento = new SetNodeFieldCommand("a", FieldNames.Label, "Z").CaptureMemento(harness.Document);

        var restored = DiagramSerializer.DeserializeMemento(DiagramSerializer.SerializeMemento(memento));

        restored.Should().BeOfType<SetNodeFieldMemento>();
        restored.AffectedIds.Should().Equal("a");

        // 按还原出来的那份快照改回去，应当得到改之前的样子。
        new SetNodeFieldCommand("a", FieldNames.Label, "Z")
            .RestoreMemento(harness.Document, restored);

        harness.Document.Nodes[0].Label.Should().Be("A");
        harness.Document.Nodes[0].Shape.Should().Be(NodeShape.Diamond);
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void Restoring_a_field_on_a_node_that_is_gone_does_nothing()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        var memento = new SetNodeFieldCommand("a", FieldNames.Label, "B").CaptureMemento(harness.Document);

        harness.Bus.Execute(new RemoveNodeCommand("a").WithContext(ChangeContext.For(ChangeSource.Human, "tester")));
        var after = harness.Snapshot();

        // 还原一个已经不存在的节点的字段，逆操作是"改回去"而不是"把它插回来"——
        // 插回来会让撤销把别的命令删掉的东西带回来。
        new SetNodeFieldCommand("a", FieldNames.Label, "B").RestoreMemento(harness.Document, memento);

        harness.Snapshot().Should().Be(after);
        harness.Document.Nodes.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "NodeField")]
    public void The_structural_hash_moves_only_for_structural_fields()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        var structuralBefore = harness.Document.StructuralHash;

        harness.SetField("a", FieldNames.Label, "B").IsSuccess.Should().BeTrue();
        harness.Document.StructuralHash.Should().Be(structuralBefore, "标签是外观，不该让坐标失效");

        harness.SetField("a", FieldNames.Parent, "g").IsSuccess.Should().BeTrue();
        harness.Document.StructuralHash.Should().NotBe(structuralBefore, "换了父级会改变嵌套关系");
    }

    /// <summary>
    /// 一份把每个可写字段都填上的节点，用来遍历字段表。
    /// </summary>
    /// <remarks>
    /// 每个成员都要填。空成员读出来是空，而那与"没有读法"在返回值上分不开——
    /// 少填一个，遍历那条测试就会把"这个成员没有值"误报成"这个字段没有读法"。
    /// </remarks>
    private static NodeDef Sample() => new()
    {
        Id = "a",
        Label = "A",
        Shape = NodeShape.Hexagon,
        Parent = "g",
        Layer = "l1",
        StyleToken = "primary",
        Style = new NodeStyle
        {
            Fill = "#ffffff",
            Stroke = "#000000",
            Text = "#111111",
            Border = LineStyle.Dashed,
            Weight = 2,
            Radius = 4,
            Opacity = 0.9,
            Badge = "1",
        },
        Text = new TextStyle
        {
            FontFamily = "sans",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            FontColor = "primary",
            Align = TextAlign.Center,
        },
        Ports = [new PortDef { Name = "in", Side = PortSide.Left }, new PortDef { Name = "out" }],
        RichText = true,
        MathMode = MathMode.Inline,
        Desc = "说明",
        Meta = new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" },
    };

    public static TheoryData<string> WritableFields()
    {
        var data = new TheoryData<string>();

        foreach (var field in NodeFieldValue.Writable)
        {
            data.Add(field);
        }

        return data;
    }
}
