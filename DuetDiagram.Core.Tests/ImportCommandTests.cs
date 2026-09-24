using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Templates;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 导入一份片段：一条命令整体落地，标识冲突与放入模板同一条口径。
/// </summary>
/// <remarks>
/// <para>
/// 片段是直接构造的，不走文件：导入那条路的上游是格式解析，产物本来就是一组节点、边与组合，
/// 没有一份文件经过手。要验"文件里那些写不出来的集合"那条界线的用例在模板那一组里。
/// </para>
/// <para>
/// 标识冲突消解本身在模板那一组已经验过——这里不重复那几条规则，
/// 只验**导入这条路真的走到了同一份实现上**：重名的元素改了名、指向它的边跟着改。
/// 两处各写一套的话，这一条会红。
/// </para>
/// </remarks>
public sealed class ImportCommandTests
{
    #region 落地

    /// <summary>导进来的是节点、边与组合，一样都不少。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public void The_imported_elements_land_in_the_document()
    {
        using var harness = new Harness();

        var result = Import(harness, Flow("flow.mmd"));

        result.IsEffectiveSuccess.Should().BeTrue();

        harness.Document.Nodes.Select(node => node.Id).Should().Equal("a", "b");
        harness.Document.Edges.Should().ContainSingle().Which.From.Should().Be("a");
        harness.Document.Composites.Should().ContainSingle().Which.Members.Should().Equal("a");
    }

    /// <summary>
    /// 整份导入算一条命令，撤销一次全回去。
    /// </summary>
    /// <remarks>
    /// 按元素发命令的话撤销要按四次，而用户在界面上做的是同一次「导入了这个文件」——
    /// 两者对不上。这也是导入五百个节点时唯一可行的形状。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public void The_whole_import_is_one_command_and_one_undo_takes_it_back()
    {
        using var harness = new Harness();
        harness.AddNode("kept");

        var nodes = harness.Document.Nodes.Count;
        var history = harness.Bus.Context.History.UndoEntries().Count;

        Import(harness, Flow("flow.mmd")).IsEffectiveSuccess.Should().BeTrue();

        harness.Bus.Context.History.UndoEntries().Count.Should().Be(history + 1, "四条元素只该进一条历史");

        harness.Bus.Undo().IsEffectiveSuccess.Should().BeTrue();

        harness.Document.Nodes.Select(node => node.Id).Should().Equal(["kept"], "撤销一次整份退回");
        harness.Document.Nodes.Count.Should().Be(nodes);
        harness.Document.Edges.Should().BeEmpty();
        harness.Document.Composites.Should().BeEmpty();
        harness.Bus.Context.History.UndoEntries().Count.Should().Be(history, "撤销本身不进历史");
    }

    /// <summary>重做把它整份放回来。</summary>
    /// <remarks>
    /// 重做走的是同一份 memento。只把撤销栈换回去而不管文档的话，
    /// 界面上按了重做会什么也不发生，而历史里那一条已经回到撤销栈上了。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public void Redo_puts_the_import_back()
    {
        using var harness = new Harness();

        Import(harness, Flow("flow.mmd")).IsEffectiveSuccess.Should().BeTrue();

        harness.Bus.Undo().IsEffectiveSuccess.Should().BeTrue();
        harness.Bus.Redo().IsEffectiveSuccess.Should().BeTrue();

        harness.Document.Nodes.Select(node => node.Id).Should().Equal("a", "b");
        harness.Document.Edges.Should().ContainSingle().Which.From.Should().Be("a");
        harness.Document.Composites.Should().ContainSingle().Which.Id.Should().Be("g1");
    }

    /// <summary>
    /// 片段里写着的页面归属一律丢掉，导进来的元素落在缺省页上。
    /// </summary>
    /// <remarks>
    /// 导进来的是「片段」，不是整份文档：放着来源那份的页号不管的话，
    /// 一份从第二页导出来的内容会把元素塞回第二页，而用户刚打开的是第一页。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public void The_fragment_lands_on_the_default_page_even_when_it_carries_one()
    {
        using var harness = new Harness();

        var fragment = new TemplateDocument(
            "flow.mmd",
            [new NodeDef { Id = "a", Page = "p9" }],
            [new EdgeDef { Id = "e1", From = "a", To = "a", Page = "p9" }],
            []);

        Import(harness, fragment).IsEffectiveSuccess.Should().BeTrue();

        harness.Node("a").Page.Should().BeNull();
        harness.Document.Edges.Single().Page.Should().BeNull();
    }

    #endregion

    #region 标识冲突

    /// <summary>
    /// 与文档里已有的标识重名时改过名的那一份仍然放得进去，指向它的引用一起改。
    /// </summary>
    /// <remarks>
    /// 不消解的话拼出来是一份有两套同名标识的文档，而校验器报出来的位置
    /// 离"导入了哪个文件"很远——用户看到的是"某个我根本没碰过的元素标识重复"。
    /// 引用不跟着改的话，边会指向文档里原来那个同名元素，图上看起来少了一条线。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public void A_colliding_id_is_renamed_and_the_references_follow()
    {
        using var harness = new Harness();
        harness.AddNode("a", "文档里本来就有的");

        var result = Import(harness, Flow("flow.mmd"));

        result.IsEffectiveSuccess.Should().BeTrue();

        harness.Document.Nodes.Select(node => node.Id).Should().Equal("a", "a-2", "b");
        harness.Node("a-2").Label.Should().Be("甲", "导进来的那一个才是被改名的");

        harness.Document.Edges.Should().ContainSingle()
            .Which.From.Should().Be("a-2", "边要跟着改名走，否则它指向的是文档里原来那个 a");
    }

    /// <summary>同一份片段导两次，第二次拿到的是另一套名字，两次都能放进去。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public void Two_imports_of_the_same_fragment_get_different_names()
    {
        using var harness = new Harness();

        Import(harness, Flow("flow.mmd")).IsEffectiveSuccess.Should().BeTrue();
        Import(harness, Flow("flow.mmd")).IsEffectiveSuccess.Should().BeTrue();

        harness.Document.Nodes.Select(node => node.Id).Should().Equal("a", "b", "a-2", "b-2");
        harness.Document.Edges.Select(edge => edge.From).Should().Equal("a", "a-2");
    }

    #endregion

    #region 失败整体回滚

    /// <summary>片段里的边指向片段里没有的东西时，一条元素都不落。</summary>
    /// <remarks>
    /// 落一半的话文档里是一份悬空引用的东西：画布上少一条线，
    /// 而校验器报的是那个不存在的标识——用户不知道那是"导进来的时候就不全"。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public void An_edge_pointing_outside_the_fragment_is_refused()
    {
        using var harness = new Harness();

        var fragment = new TemplateDocument(
            "flow.mmd",
            [new NodeDef { Id = "a" }],
            [new EdgeDef { Id = "e1", From = "a", To = "missing" }],
            []);

        var command = new ImportFragmentCommand(fragment);

        command.Validate(harness.Document).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCodes.EdgeTargetMissing);

        Import(harness, fragment).IsSuccess.Should().BeFalse();
    }

    /// <summary>被拒的那一次，文档与历史都逐字节没动。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public void A_refused_import_leaves_the_document_untouched()
    {
        using var harness = new Harness();
        harness.AddNode("kept");

        var before = harness.Snapshot();
        var history = harness.Bus.Context.History.UndoEntries().Count;

        var fragment = new TemplateDocument(
            "flow.mmd",
            [new NodeDef { Id = "n1" }],
            [new EdgeDef { Id = "e1", From = "n1", To = "missing" }],
            []);

        Import(harness, fragment).IsSuccess.Should().BeFalse();

        harness.Snapshot().Should().Be(before, "一处不合法就整体不落，不留半个片段在文档里");
        harness.Bus.Context.History.UndoEntries().Count.Should().Be(history);
    }

    /// <summary>
    /// 空片段报的是导入自己的码，不是"模板是空的"那一个。
    /// </summary>
    /// <remarks>
    /// 一份只有一行 <c>flowchart LR</c> 的文件解析得通，只是里面没有能变成元素的内容。
    /// 报"这份模板是空的"的话，用户会去看自己从没碰过的模板目录。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public void An_empty_import_is_refused_with_its_own_code()
    {
        using var harness = new Harness();

        var command = new ImportFragmentCommand(new TemplateDocument("flow.mmd", [], [], []));

        command.Validate(harness.Document).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCodes.ImportEmpty);

        var result = Import(harness, new TemplateDocument("flow.mmd", [], [], []));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.ImportEmpty);
    }

    #endregion

    #region 快照与说明

    /// <summary>
    /// memento 是导入自己那一种，带着这次导进来的全部元素。
    /// </summary>
    /// <remarks>
    /// 借用放入模板那一个记录的话，标签写的是"放入模板"，
    /// 读日志的人会去模板目录里找是哪一份模板——而那次改动来自一份导入的文件，
    /// 模板目录里根本没有它。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public void The_memento_is_its_own_kind_and_carries_the_whole_fragment()
    {
        using var harness = new Harness();

        var command = new ImportFragmentCommand(Flow("flow.mmd"));
        var memento = command.CaptureMemento(harness.Document);

        memento.Should().BeOfType<ImportFragmentMemento>();

        var typed = (ImportFragmentMemento)memento;

        typed.Nodes.Select(node => node.Id).Should().Equal("a", "b");
        typed.Edges.Should().ContainSingle();
        typed.Composites.Should().ContainSingle();
        typed.AffectedIds.Should().Equal("a", "b", "e1", "g1");

        typed.InverseChanges.Should().HaveCount(4)
            .And.OnlyContain(change => change.Kind == ChangeKind.Removed, "导入的逆操作是移除");
    }

    /// <summary>一次导入在变更明细里逐条看得见，说明里点名了来源与改过几个名。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public void The_message_names_the_source_and_the_renames()
    {
        using (var clean = new Harness())
        {
            Import(clean, Flow("flow.mmd")).Message.Should()
                .Be("导入 flow.mmd：4 条元素", "没有重名时不该提改名");
        }

        using (var crowded = new Harness())
        {
            crowded.AddNode("a", "占住 a");

            Import(crowded, Flow("flow.mmd")).Message.Should()
                .Be("导入 flow.mmd：4 条元素，其中 1 个标识与文档里已有的重名，已自动改名");
        }
    }

    /// <summary>每条元素一条"新增"明细——一次导入不能只报一条笼统的。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public void Every_element_shows_up_in_the_change_details()
    {
        using var harness = new Harness();

        var result = Import(harness, Flow("flow.mmd"));

        result.FieldChanges.Should().HaveCount(4)
            .And.OnlyContain(change => change.Kind == ChangeKind.Added);

        result.FieldChanges.Select(change => change.ElementId).Should().Equal("a", "b", "e1", "g1");
    }

    #endregion

    #region 夹具

    /// <summary>一份最小的片段：两个节点、一条把它们连起来的边、一个把第一个框起来的组合。</summary>
    private static TemplateDocument Flow(string name) => new(
        name,
        [new NodeDef { Id = "a", Label = "甲" }, new NodeDef { Id = "b", Label = "乙" }],
        [new EdgeDef { Id = "e1", From = "a", To = "b" }],
        [new GroupDef { Id = "g1", Label = "一组", Members = ["a"] }]);

    /// <summary>经总线导一份片段。上下文按界面上那条路给。</summary>
    private static CommandResult Import(Harness harness, TemplateDocument fragment) =>
        harness.Bus.Execute(new ImportFragmentCommand(fragment)
            .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

    #endregion
}
