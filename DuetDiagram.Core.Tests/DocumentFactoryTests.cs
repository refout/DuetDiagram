using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// <see cref="DiagramDocument.CreateFromContent"/> 的契约。
/// </summary>
/// <remarks>
/// <para>
/// 这个入口是外部程序集（Mermaid 导入、DSL 映射）造文档的唯一一条路，所以它自己的
/// 契约要单独钉住：哈希算对没有、版本号从哪开始、非法内容要不要在这里拦。
/// </para>
/// <para>
/// 其余用例间接覆盖它——<see cref="IrFixtures"/> 已经改走这条入口，
/// 于是 Core 的每个夹具用例都在走它。
/// </para>
/// </remarks>
public sealed class DocumentFactoryTests
{
    [Fact]
    [Trait("Category", "IrConstruction")]
    public void Both_hashes_are_filled_in()
    {
        var document = DiagramDocument.CreateFromContent(
            "doc",
            nodes: [new NodeDef { Id = "a", Label = "甲" }],
            edges: [new EdgeDef { Id = "e1", From = "a", To = "a" }]);

        document.StructuralHash.Should().NotBeEmpty();
        document.VisualHash.Should().NotBeEmpty();

        // 必须与直接调哈希函数的结果一致。对不上说明构造路径与哈希口径分了叉，
        // 而那种分叉的表现是"对端认为内容没变"——最难查的一类不一致。
        document.StructuralHash.Should().Be(DiagramHashing.ComputeStructuralHash(document));
        document.VisualHash.Should().Be(DiagramHashing.ComputeVisualHash(document));
    }

    [Fact]
    [Trait("Category", "IrConstruction")]
    public void Version_starts_at_zero()
    {
        // 版本号是变更计数，构造不是变更。"新建时是 0、首次成功变更后是 1"不为导入破例。
        DiagramDocument.CreateFromContent("doc").Version.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "IrConstruction")]
    public void Hashes_match_what_a_round_trip_produces()
    {
        // 这条路造出来的文档与"读一份文件读出来的"必须给出同一对哈希。
        // 不然导入一次、存盘再打开，会莫名其妙地被判定为内容变了。
        var created = IrFixtures.Populated();
        var restored = DiagramSerializer.DeserializeFull(DiagramSerializer.SerializeFull(created));

        // 重新算，而不是读文件里存的那两个值——存进去的就是当初算出来的那一个，
        // 断言它们相等等于什么都没验。
        DiagramHashing.ComputeStructuralHash(restored).Should().Be(created.StructuralHash);
        DiagramHashing.ComputeVisualHash(restored).Should().Be(created.VisualHash);
    }

    [Fact]
    [Trait("Category", "IrConstruction")]
    public void Empty_content_hashes_like_an_empty_document()
    {
        var created = DiagramDocument.CreateFromContent("doc");

        created.StructuralHash.Should().Be(DiagramHashing.ComputeStructuralHash(new DiagramDocument("doc")));
    }

    [Fact]
    [Trait("Category", "IrConstruction")]
    public void Duplicate_ids_are_not_rejected_here()
    {
        // 九个集合共用一个命名空间，重复标识是可诊断的（校验器报 DUPLICATE_ID），
        // 但不是构造错误。在这里抛异常会让"造一份带重复标识的文档去测校验器"做不到。
        var document = DiagramDocument.CreateFromContent(
            "doc",
            nodes: [new NodeDef { Id = "x" }],
            composites: [new GroupDef { Id = "x" }]);

        document.Nodes.Should().HaveCount(1);
        document.Composites.Should().HaveCount(1);

        DiagramValidator.Validate(document)
            .Should().Contain(i => i.Code == ErrorCodes.DuplicateId);
    }

    [Fact]
    [Trait("Category", "IrConstruction")]
    public void Content_order_is_preserved()
    {
        // 顺序不是装饰：节点在集合里的位置是层内次序的依据，
        // 边在集合里的位置决定删除后撤销能否按原索引插回。
        var document = DiagramDocument.CreateFromContent(
            "doc",
            nodes: [new NodeDef { Id = "c" }, new NodeDef { Id = "a" }, new NodeDef { Id = "b" }]);

        document.Nodes.Select(n => n.Id).Should().Equal("c", "a", "b");
    }
}
