using DuetDiagram.Core.Model;
using DuetDiagram.Dsl.Export;
using DuetDiagram.Dsl.Mapping;
using DuetDiagram.Dsl.Parsing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Dsl.Tests;

/// <summary>
/// 导出再导入的无损性。
/// </summary>
/// <remarks>
/// <para>
/// **往返无损是导出方向唯一的判据形式。** "导出正确"没法验——那要看的是文本像不像
/// 人写的东西；"导出再导入得到同一份文档"可以验，而且它恰好是使用这条通路的人
/// 关心的那件事：把图发出去，对方打开之后还是同一张图。
/// </para>
/// <para>
/// 比的是**结构哈希**而不是逐字段：它就是这个仓库里"这两份文档是不是同一张图"
/// 的既有口径，自己再写一套比较只会多一份会漂移的定义。
/// </para>
/// </remarks>
public sealed class DslRoundTripTests
{
    #region 无损的图

    [Theory]
    [InlineData("a \"甲\"\nb \"乙\"\na -> b \"是\"")]
    [InlineData("api \"API\" shape=hexagon style=primary layer=fore ports=req:left\nweb -> api.req \"请求\"")]
    [InlineData("check \"校验\" shape=diamond\npass \"通过\" style=success\nfail \"失败\" style=danger\ncheck -> pass \"是\"\ncheck -> fail \"否\"\norder check: pass, fail")]
    [InlineData("group backend \"后端\"\n  api \"API\"\n  db \"库\" shape=cylinder\n  api -> db\nend")]
    [InlineData("lane pay \"支付\"\n  payStart \"发起\"\nend\npay \"完成\"\npayStart -> pay")]
    [InlineData("a\nb\nc\nsame-rank a, b\nalign b, c\nplace c right-of a")]
    [InlineData("a\nb\nnode-spacing 25\nlayer-spacing 90\na -> b line=dotted arrow=cross")]
    [InlineData("a -> b\nb -- c")]
    [InlineData("start -> check\ncheck -> fail")]
    [Trait("Category", "DslRoundTrip")]
    public void Export_then_import_keeps_the_structural_hash(string source)
    {
        var options = new MappingOptions { DocumentId = "dsl" };

        var first = DslMapper.Map(DslParser.Parse(source), options);
        var exported = DslExporter.Export(first.Document, first.Sidecar);
        var second = DslMapper.Map(DslParser.Parse(exported.Text), options);

        second.Document.StructuralHash.Should().Be(
            first.Document.StructuralHash,
            "导出再导入要得到同一张图，否则发出去的那份文件打开之后就不是原来的图了");

        second.Document.VisualHash.Should().Be(first.Document.VisualHash, "外观也要一样");
    }

    [Fact]
    [Trait("Category", "DslRoundTrip")]
    public void Round_trip_keeps_the_pins()
    {
        var options = new MappingOptions { DocumentId = "dsl" };

        var first = DslMapper.Map(DslParser.Parse("a\nb\npin b at 640, 320"), options);

        first.Sidecar.PinnedNodes.Should().ContainKey("b", "pin 落 sidecar，不进 IR");

        var exported = DslExporter.Export(first.Document, first.Sidecar);
        var second = DslMapper.Map(DslParser.Parse(exported.Text), options);

        second.Sidecar.PinnedNodes.Should().BeEquivalentTo(first.Sidecar.PinnedNodes);
    }

    [Fact]
    [Trait("Category", "DslRoundTrip")]
    public void A_second_round_trip_changes_nothing()
    {
        // 导出是幂等的：过了第一轮之后，文本已经是最规整的写法，
        // 再导一遍应当逐字节相同。不相同说明某一轮里还有东西在悄悄变。
        var options = new MappingOptions { DocumentId = "dsl" };

        var source = """
            group g "组"
              b "乙" shape=stadium
              a "甲" ports=out:right
            end
            a.out -> b "是"
            same-rank a, b
            order a: b
            pin a at 10, 20
            """;

        var first = DslMapper.Map(DslParser.Parse(source), options);
        var once = DslExporter.Export(first.Document, first.Sidecar);

        var second = DslMapper.Map(DslParser.Parse(once.Text), options);
        var twice = DslExporter.Export(second.Document, second.Sidecar);

        twice.Text.Should().Be(once.Text);
    }

    [Fact]
    [Trait("Category", "DslRoundTrip")]
    public void The_first_round_trip_normalizes_the_text()
    {
        // 第一轮不是原样回抄：缺省值被去掉、边标识被补上、缩进被规整。
        // 这一条与上一条合起来说明"原文本 → 规整文本 → 原样"这个收敛过程。
        var options = new MappingOptions { DocumentId = "dsl" };

        var mapped = DslMapper.Map(DslParser.Parse("a -> b"), options);
        var exported = DslExporter.Export(mapped.Document, mapped.Sidecar);

        exported.Text.Should().Contain("e1: a -> b", "边标识由映射层补出来，导出时要写出去");
        exported.Text.Should().NotContain("shape=rect");
    }

    #endregion

    #region 无损这件事只对 DSL 表达得了的图成立

    [Fact]
    [Trait("Category", "DslRoundTrip")]
    public void A_document_with_pages_does_not_survive_and_says_so()
    {
        // 页面是 DSL 表达不了的东西。往返之后哈希会变——那不是 bug，
        // 是这条通路的边界，而导出报告必须说出来。
        var document = DiagramDocument.CreateFromContent(
            "doc",
            pages: [new PageDef { Id = "p1", Name = "第一页", Order = 0 }],
            nodes: [new NodeDef { Id = "a", Label = "甲", Page = "p1" }]);

        var exported = DslExporter.Export(document);

        exported.Report.Dropped.Select(item => item.Feature).Should().Contain("页面").And.Contain("节点归属的页面");

        var options = new MappingOptions { DocumentId = "doc" };
        var reimported = DslMapper.Map(DslParser.Parse(exported.Text), options);

        reimported.Document.StructuralHash.Should().NotBe(
            document.StructuralHash,
            "页面写不进去，所以回来的不是同一份文档——报告里已经说了");
        reimported.Document.Nodes.Should().ContainSingle(node => node.Id == "a", "节点本身还是保住了");
    }

    [Fact]
    [Trait("Category", "DslRoundTrip")]
    public void Constraints_with_one_owner_survive_the_owner_field()
    {
        // 约束的归属方不在文本里，导入时由调用方统一声明。所以只要原文档里的约束
        // 归属方一致，往返就无损；混着不同归属方时会丢，而那种情况导出报告里会写。
        var options = new MappingOptions { DocumentId = "dsl", Owner = ConstraintOwner.Human };

        var first = DslMapper.Map(DslParser.Parse("a\nb\nsame-rank a, b"), options);
        var exported = DslExporter.Export(first.Document, first.Sidecar);
        var second = DslMapper.Map(DslParser.Parse(exported.Text), options);

        second.Document.StructuralHash.Should().Be(first.Document.StructuralHash);
        second.Document.Layout.SameRank.Should().OnlyContain(item => item.Owner == ConstraintOwner.Human);
    }

    [Fact]
    [Trait("Category", "DslRoundTrip")]
    public void Mixed_owners_are_reported_as_a_loss()
    {
        var layout = LayoutHintsDefaults.Create() with
        {
            SameRank =
            [
                new Constraint<SameRankConstraint>(new SameRankConstraint(["a", "b"]), ConstraintOwner.Human, default),
                new Constraint<SameRankConstraint>(new SameRankConstraint(["c", "d"]), ConstraintOwner.Llm, default),
            ],
        };

        var document = DiagramDocument.CreateFromContent(
            "doc",
            nodes:
            [
                new NodeDef { Id = "a" },
                new NodeDef { Id = "b" },
                new NodeDef { Id = "c" },
                new NodeDef { Id = "d" },
            ],
            layout: layout);

        DslExporter.Export(document).Report.Dropped.Select(item => item.Feature)
            .Should().Contain("约束的归属方");
    }

    #endregion

}
