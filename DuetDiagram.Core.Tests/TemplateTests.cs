using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Templates;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 模板：从文件读一份文档片段，拼进当前文档。
/// </summary>
/// <remarks>
/// <para>
/// 模板文件就是一份少了几个集合的文档，所以这里的输入用的是手写的 JSON——
/// 直接构造对象的话，验不到"文件里那些写不出来的集合"这条界线。
/// </para>
/// <para>
/// 装配分两步：先核片段自己立不立得住，再消解标识冲突。两条都只用文档做输入，
/// 所以可以绕开总线单独验；经总线那几条验的是"校验、算逆变更、落地读的是同一份计划"。
/// </para>
/// </remarks>
public sealed class TemplateTests
{
    #region 装载

    /// <summary>一份最小的片段：两个节点一条边，外加一个把它们框起来的组合。</summary>
    private const string Fragment = """
        {
          "id": "flow",
          "nodes": [ { "id": "a", "label": "甲" }, { "id": "b", "label": "乙" } ],
          "edges": [ { "id": "e1", "from": "a", "to": "b" } ],
          "composites": [ { "$composite": "group", "id": "g1", "label": "一组", "members": ["a"] } ]
        }
        """;

    [Fact]
    [Trait("Category", "Template")]
    public void A_fragment_loads_as_nodes_edges_and_composites()
    {
        var template = TemplateDocument.Load(Fragment);

        template.Name.Should().Be("flow");
        template.Nodes.Select(node => node.Id).Should().Equal("a", "b");
        template.Edges.Should().ContainSingle().Which.From.Should().Be("a");
        template.Composites.Should().ContainSingle().Which.Members.Should().Equal("a");
        template.ElementCount.Should().Be(4, "节点、边与组合都算一条");
    }

    /// <summary>模板带不了的那几样，拒绝时要按名字点出来。</summary>
    /// <remarks>
    /// 静默丢掉的表现是"模板里的颜色不见了"，而用户无从知道是模板没写对还是程序没做。
    /// 报出是哪几样，他才知道该删掉哪一段还是该换个做法。
    /// </remarks>
    [Fact]
    [Trait("Category", "Template")]
    public void A_collection_a_template_cannot_carry_is_named_in_the_refusal()
    {
        var exception = FluentActions.Invoking(() => TemplateDocument.Load("""
            {
              "id": "flow",
              "pages": [ { "id": "p1", "name": "主页" } ],
              "layers": [ { "id": "l1", "name": "前景", "order": 1 } ],
              "nodes": [ { "id": "a" } ]
            }
            """)).Should().Throw<TemplateFormatException>().Which;

        exception.Unsupported.Should().Equal("页面", "图层");
        exception.Message.Should().Contain("页面").And.Contain("图层");
    }

    /// <summary>一条元素都没有的模板，读的时候就要挡住。</summary>
    /// <remarks>
    /// 放进画布时才发现"什么都没发生"的话，用户会以为是自己点错了位置。
    /// 空内容与写成 <c>{}</c> 的文件说同一句话：零字节的文件是手滑留下的，不是另一种格式错。
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("""{ "id": "empty" }""")]
    [InlineData("""{ "id": "empty", "nodes": [] }""")]
    [Trait("Category", "Template")]
    public void A_template_with_no_elements_is_refused(string json)
    {
        FluentActions.Invoking(() => TemplateDocument.Load(json))
            .Should().Throw<TemplateFormatException>()
            .Which.Message.Should().Contain("一条元素都没有");
    }

    /// <summary>
    /// 写出去再读回来，还是同一份片段。
    /// </summary>
    /// <remarks>
    /// 这一条盯的是"文档级的那几样会不会被当成超纲内容"。写出去的文件里带着画布设置、
    /// 缺省的布局提示与空的调色板，而它们都是**文件里有、模板表达不了**的东西；
    /// 判"有没有真的写了东西"要是按整份记录比，读回来就会被判成带了布局提示。
    /// </remarks>
    [Fact]
    [Trait("Category", "Template")]
    public void A_template_survives_being_written_and_read_back()
    {
        var before = TemplateDocument.Load(Fragment);

        var after = TemplateDocument.Load(TemplateDocument.Save(before));

        after.Name.Should().Be(before.Name);
        after.Nodes.Select(node => node.Id).Should().Equal(before.Nodes.Select(node => node.Id));
        after.Edges.Select(edge => edge.Id).Should().Equal(before.Edges.Select(edge => edge.Id));
        after.Composites.Select(c => c.Id).Should().Equal(before.Composites.Select(c => c.Id));
        after.Nodes.Should().BeEquivalentTo(before.Nodes);
    }

    #endregion

    #region 拼进去

    [Fact]
    [Trait("Category", "Template")]
    public void An_unconflicting_template_keeps_its_ids()
    {
        using var harness = new Harness();
        harness.AddNode("x");

        TemplateInstantiator.TryPlan(harness.Document, TemplateDocument.Load(Fragment), out var plan, out var errors)
            .Should().BeTrue();

        errors.Should().BeEmpty();
        plan!.Ids.Should().Equal("a", "b", "e1", "g1");
    }

    /// <summary>标识撞上时要改名，而且引用要跟着改。</summary>
    /// <remarks>
    /// 只改节点名不改边上的引用的话，拼进去的是一条指向旧名字的边——
    /// 画布上少一条线，而校验器报的是那个不存在的标识。
    /// </remarks>
    [Fact]
    [Trait("Category", "Template")]
    public void A_conflicting_id_is_renamed_and_the_references_follow()
    {
        using var harness = new Harness();
        harness.AddNode("a", "已经在这里了");

        TemplateInstantiator.TryPlan(harness.Document, TemplateDocument.Load(Fragment), out var plan, out _)
            .Should().BeTrue();

        plan!.Nodes.Select(node => node.Id).Should().Equal("a-2", "b");
        plan.Edges.Should().ContainSingle().Which.From.Should().Be("a-2");
        plan.Edges[0].To.Should().Be("b");
        plan.Renames["a"].Should().Be("a-2", "序号从 2 起：原名占的就是第一个");
    }

    /// <summary>同一次计划里已经分配出去的名字也要躲开。</summary>
    /// <remarks>
    /// 只查目标文档的话，片段里同时有 <c>a</c> 与 <c>a-2</c> 而文档里已有 <c>a</c> 时，
    /// 两个都会变成 <c>a-2</c>——拼出来的是一份自己跟自己撞名的文档。
    /// </remarks>
    [Fact]
    [Trait("Category", "Template")]
    public void A_rename_does_not_collide_with_a_name_used_earlier_in_the_same_template()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var template = TemplateDocument.Load("""
            { "id": "flow", "nodes": [ { "id": "a" }, { "id": "a-2" }, { "id": "a-3" } ] }
            """);

        TemplateInstantiator.TryPlan(harness.Document, template, out var plan, out _).Should().BeTrue();

        plan!.Ids.Should().OnlyHaveUniqueItems();
        plan.Renames["a"].Should().Be("a-2", "原名被文档占着，接上序号");
        plan.Renames["a-2"].Should().Be("a-2-2", "a-2 刚被这次计划里的前一条占了，序号得再往下接");
        plan.Renames["a-3"].Should().Be("a-3", "没撞上就保持原名");
    }

    /// <summary>九个集合共用一个命名空间，别的集合占了的名字也算被占了。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public void An_id_taken_by_any_other_collection_counts_as_taken()
    {
        // 目标文档里没有叫 t1 的节点，但有一个叫 t1 的标签。
        var target = IrFixtures.WithTag(IrFixtures.Base(), new TagDef { Id = "t1", Members = ["a"] });

        TemplateInstantiator.TryPlan(
            target,
            TemplateDocument.Load("""{ "id": "flow", "nodes": [ { "id": "t1" } ] }"""),
            out var plan,
            out _).Should().BeTrue();

        plan!.Nodes.Should().ContainSingle().Which.Id.Should().Be("t1-2");
    }

    /// <summary>模板不带页面归属：放进哪一页由目标文档决定。</summary>
    /// <remarks>
    /// 带着来源文档的页号的话，放进第二页的东西会被模板带回第一页。
    /// </remarks>
    [Fact]
    [Trait("Category", "Template")]
    public void The_inserted_elements_lose_the_page_they_were_saved_on()
    {
        using var harness = new Harness();

        TemplateInstantiator.TryPlan(harness.Document, TemplateDocument.Load("""
            {
              "id": "flow",
              "nodes": [ { "id": "a", "page": "p1" }, { "id": "b", "page": "p1" } ],
              "edges": [ { "id": "e1", "from": "a", "to": "b", "page": "p1" } ]
            }
            """), out var plan, out _).Should().BeTrue();

        plan!.Nodes.Should().OnlyContain(node => node.Page == null);
        plan.Edges.Should().OnlyContain(edge => edge.Page == null);
    }

    /// <summary>成员表是权威：只写了成员表，元素的父级在拼进去时补上。</summary>
    /// <remarks>
    /// 手写模板的人很自然会只写组合的成员表，而不去每个节点上再抄一遍父级。
    /// 不补的话，拼进去的是一份整体校验报 MEMBERSHIP_MISMATCH 的文档——
    /// 放的时候一切正常，报错时离"放了哪个模板"很远。
    /// </remarks>
    [Fact]
    [Trait("Category", "Template")]
    public void A_member_without_a_written_parent_gets_the_group_as_its_parent()
    {
        using var harness = new Harness();

        TemplateInstantiator.TryPlan(harness.Document, TemplateDocument.Load("""
            {
              "id": "flow",
              "nodes": [ { "id": "a" }, { "id": "b" } ],
              "composites": [ { "$composite": "group", "id": "g1", "members": ["a"] } ]
            }
            """), out var plan, out var errors).Should().BeTrue();

        errors.Should().BeEmpty();
        plan!.Nodes.Single(node => node.Id == "a").Parent.Should().Be("g1");
        plan.Nodes.Single(node => node.Id == "b").Parent.Should().BeNull("不在任何成员表里就是顶层");
    }

    /// <summary>端点可以是组合：分层图里「这一层流向那一层」就是这么写的。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public void An_edge_between_two_composites_is_accepted()
    {
        using var harness = new Harness();

        TemplateInstantiator.TryPlan(harness.Document, TemplateDocument.Load("""
            {
              "id": "flow",
              "composites": [
                { "$composite": "group", "id": "g1" },
                { "$composite": "group", "id": "g2" }
              ],
              "edges": [ { "id": "e1", "from": "g1", "to": "g2" } ]
            }
            """), out var plan, out var errors).Should().BeTrue();

        errors.Should().BeEmpty();
        plan!.Ids.Should().Equal("e1", "g1", "g2");
    }

    /// <summary>片段自己没超深，拼到一份已经很深的文档上照样会超。</summary>
    /// <remarks>
    /// 这一步不做的话，插入成功而文档立刻变成一份校验不过的东西——
    /// 用户看到的是一句"嵌套太深"，而那个深是拼进来之后才有的。
    /// </remarks>
    [Fact]
    [Trait("Category", "Template")]
    public void A_template_that_would_push_the_document_past_the_depth_limit_is_refused()
    {
        var target = IrFixtures.WithComposites(IrFixtures.Base(), Nesting(CompositeLimits.MaxDepth));

        TemplateInstantiator.TryPlan(target, TemplateDocument.Load("""
            {
              "id": "flow",
              "nodes": [ { "id": "n1" } ],
              "composites": [ { "$composite": "group", "id": "g1", "members": ["n1"] } ]
            }
            """), out _, out var errors).Should().BeFalse();

        errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.CompositeTooDeep);
    }

    #endregion

    #region 片段自己不合法

    [Fact]
    [Trait("Category", "Template")]
    public void An_edge_whose_endpoint_is_not_in_the_fragment_is_refused()
    {
        using var harness = new Harness();

        TemplateInstantiator.TryPlan(harness.Document, TemplateDocument.Load("""
            { "id": "f", "nodes": [ { "id": "a" } ], "edges": [ { "id": "e1", "from": "a", "to": "missing" } ] }
            """), out _, out var errors).Should().BeFalse();

        errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.EdgeTargetMissing);
    }

    [Fact]
    [Trait("Category", "Template")]
    public void A_group_member_outside_the_fragment_is_refused()
    {
        using var harness = new Harness();

        TemplateInstantiator.TryPlan(harness.Document, TemplateDocument.Load("""
            {
              "id": "f",
              "nodes": [ { "id": "a" } ],
              "composites": [ { "$composite": "group", "id": "g1", "members": ["missing"] } ]
            }
            """), out _, out var errors).Should().BeFalse();

        errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.GroupMemberMissing);
    }

    [Fact]
    [Trait("Category", "Template")]
    public void A_parent_outside_the_fragment_is_refused()
    {
        using var harness = new Harness();

        TemplateInstantiator.TryPlan(harness.Document, TemplateDocument.Load("""
            { "id": "f", "nodes": [ { "id": "a", "parent": "g9" } ] }
            """), out _, out var errors).Should().BeFalse();

        errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.ParentMissing);
    }

    /// <summary>写了父级、而那个组合的成员表里没有它：两处说的不是一件事。</summary>
    /// <remarks>
    /// 成员表是权威，而整体校验按它比父级。放任不管的话，拼进去的是一份
    /// 「节点说自己在 g1、g1 却不认它」的文档——放的时候一切正常，校验时才报出来。
    /// </remarks>
    [Fact]
    [Trait("Category", "Template")]
    public void A_parent_whose_group_does_not_list_the_node_is_refused()
    {
        using var harness = new Harness();

        TemplateInstantiator.TryPlan(harness.Document, TemplateDocument.Load("""
            {
              "id": "f",
              "nodes": [ { "id": "a", "parent": "g1" } ],
              "composites": [ { "$composite": "group", "id": "g1", "members": [] } ]
            }
            """), out _, out var errors).Should().BeFalse();

        errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.MembershipMismatch);
    }

    /// <summary>一个标识被两个组合同时收下：成员表只有一份能落地。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public void A_member_claimed_by_two_composites_is_refused()
    {
        using var harness = new Harness();

        TemplateInstantiator.TryPlan(harness.Document, TemplateDocument.Load("""
            {
              "id": "f",
              "nodes": [ { "id": "a" } ],
              "composites": [
                { "$composite": "group", "id": "g1", "members": ["a"] },
                { "$composite": "group", "id": "g2", "members": ["a"] }
              ]
            }
            """), out _, out var errors).Should().BeFalse();

        errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.MembershipMismatch);
    }

    [Fact]
    [Trait("Category", "Template")]
    public void A_duplicate_id_inside_the_fragment_is_refused()
    {
        using var harness = new Harness();

        TemplateInstantiator.TryPlan(harness.Document, TemplateDocument.Load("""
            { "id": "f", "nodes": [ { "id": "a" }, { "id": "a" } ] }
            """), out _, out var errors).Should().BeFalse();

        errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.DuplicateId);
    }

    /// <summary>校验与落地读的是同一份判断，所以被拒的模板一条元素都落不下去。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public void The_command_refuses_a_broken_template_before_it_touches_anything()
    {
        using var harness = new Harness();

        var command = new InsertTemplateCommand(TemplateDocument.Load("""
            { "id": "f", "nodes": [ { "id": "a" } ], "edges": [ { "id": "e1", "from": "a", "to": "missing" } ] }
            """));

        var validation = command.Validate(harness.Document);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.EdgeTargetMissing);
    }

    [Fact]
    [Trait("Category", "Template")]
    public void A_refused_template_leaves_the_document_untouched()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var before = harness.Snapshot();
        var history = harness.Bus.Context.History.UndoEntries().Count;

        var result = Insert(harness, """
            { "id": "f", "nodes": [ { "id": "n1" } ], "edges": [ { "id": "e1", "from": "n1", "to": "missing" } ] }
            """);

        result.IsSuccess.Should().BeFalse();
        harness.Snapshot().Should().Be(before, "一处不合法就整体不落，不留半个模板在文档里");
        harness.Bus.Context.History.UndoEntries().Count.Should().Be(history);
    }

    [Fact]
    [Trait("Category", "Template")]
    public void An_empty_template_is_refused_with_its_own_code()
    {
        using var harness = new Harness();

        TemplateInstantiator.TryPlan(
            harness.Document,
            new TemplateDocument("空", [], [], []),
            out _,
            out var errors).Should().BeFalse();

        errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.TemplateEmpty);
    }

    #endregion

    #region 一条命令

    /// <summary>拼进去十条元素也只有一条历史，撤销按一次整份退回。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public void Inserting_is_one_history_entry_and_undo_restores_the_document_exactly()
    {
        using var harness = new Harness();
        harness.AddNode("x");

        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;
        var history = harness.Bus.Context.History.UndoEntries().Count;

        var result = Insert(harness, Fragment);

        result.IsEffectiveSuccess.Should().BeTrue();
        result.StructuralChanged.Should().BeTrue("拼进来的是节点与边，要重新求解布局");
        result.VisualChanged.Should().BeTrue();
        result.AffectedIds.Should().Equal("a", "b", "e1", "g1");
        harness.Document.Nodes.Should().Contain(node => node.Id == "a");
        harness.Bus.Context.History.UndoEntries().Count.Should().Be(history + 1);

        harness.Bus.Undo().IsEffectiveSuccess.Should().BeTrue();

        // 退回到不到位看的是内容散列，不是整份序列化：版本号撤销时也会往前走一格，
        // 拿它比会把"退干净了"误判成没退。
        harness.Document.StructuralHash.Should().Be(structuralBefore, "撤销按一次整份退回");
        harness.Document.VisualHash.Should().Be(visualBefore);
        harness.Document.Nodes.Should().NotContain(node => node.Id == "a");
        harness.Document.Composites.Should().NotContain(composite => composite.Id == "g1");

        harness.Bus.Redo().IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Nodes.Should().Contain(node => node.Id == "a");
        harness.Document.Composites.Should().Contain(composite => composite.Id == "g1");
    }

    /// <summary>拼进一份已经有同名元素的文档：标识被改过，历史仍然只有一条。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public void Inserting_over_an_existing_id_renames_and_still_undoes_in_one_step()
    {
        using var harness = new Harness();
        harness.AddNode("a", "原来的");

        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        Insert(harness, Fragment).IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Nodes.Should().Contain(node => node.Id == "a-2");
        harness.Document.Edges.Should().Contain(edge => edge.From == "a-2" && edge.To == "b");

        harness.Bus.Undo();

        harness.Document.StructuralHash.Should().Be(structuralBefore, "改了名也一样一次退回");
        harness.Document.VisualHash.Should().Be(visualBefore);
        harness.Document.Nodes.Should().ContainSingle(node => node.Id == "a");
        harness.Document.Nodes.Should().NotContain(node => node.Id == "a-2");
    }

    #endregion

    #region 存成模板

    /// <summary>只带两端都在选中里的边：线通常不在框选范围里（框选选的是节点）。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public void Saving_a_selection_keeps_the_edges_whose_two_ends_are_both_selected()
    {
        var document = IrFixtures.Build(
            nodes: [new NodeDef { Id = "a" }, new NodeDef { Id = "b" }, new NodeDef { Id = "c" }],
            edges:
            [
                new EdgeDef { Id = "ab", From = "a", To = "b" },
                new EdgeDef { Id = "bc", From = "b", To = "c" },
            ]);

        var template = TemplateDocument.FromDocument("片段", document, ["a", "b"]);

        template.Nodes.Select(node => node.Id).Should().Equal("a", "b");
        template.Edges.Should().ContainSingle().Which.Id.Should().Be("ab");
    }

    /// <summary>选中一个组合，成员要跟着走。</summary>
    /// <remarks>
    /// 只带那个组合的话，片段里的成员表指向不存在的东西——存的时候看着没问题，
    /// 用的时候才报错。
    /// </remarks>
    [Fact]
    [Trait("Category", "Template")]
    public void Saving_a_composite_pulls_in_its_members()
    {
        var document = IrFixtures.Build(
            nodes: [new NodeDef { Id = "a" }],
            composites: [new GroupDef { Id = "g1", Members = ["a"] }]);

        var template = TemplateDocument.FromDocument("片段", document, ["g1"]);

        template.Nodes.Should().ContainSingle().Which.Id.Should().Be("a");
        template.Composites.Should().ContainSingle().Which.Id.Should().Be("g1");
    }

    /// <summary>父级链也要走一遍：选了内层而没选外层时，只带内层会让父级指向空处。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public void Saving_a_composite_also_pulls_in_the_composites_it_sits_in()
    {
        var document = IrFixtures.Build(
            nodes: [new NodeDef { Id = "a", Parent = "g2" }],
            composites:
            [
                new GroupDef { Id = "g1" },
                new GroupDef { Id = "g2", Parent = "g1", Members = ["a"] },
            ]);

        var template = TemplateDocument.FromDocument("片段", document, ["g2"]);

        // 顺序照文档里的原序，不照递归的先后：组合集合本身的顺序也有语义。
        template.Composites.Select(c => c.Id).Should().Equal("g1", "g2");
    }

    /// <summary>页面归属在截取这一步就去掉。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public void A_saved_selection_drops_the_page_it_came_from()
    {
        var document = IrFixtures.Build(
            nodes: [new NodeDef { Id = "a", Page = "p1" }],
            pages: [new PageDef { Id = "p1", Name = "主页" }]);

        var template = TemplateDocument.FromDocument("片段", document, ["a"]);

        template.Nodes.Should().ContainSingle().Which.Page.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Template")]
    public void Saving_nothing_is_refused()
    {
        FluentActions.Invoking(() => TemplateDocument.FromDocument("片段", IrFixtures.Base(), []))
            .Should().Throw<ArgumentException>();
    }

    /// <summary>截出来、写出去、读回来、再拼进另一份文档——整条路走一遍。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public void A_template_cut_out_of_a_document_can_be_read_back_and_planned()
    {
        var document = IrFixtures.Build(
            nodes: [new NodeDef { Id = "a" }, new NodeDef { Id = "b" }],
            edges: [new EdgeDef { Id = "e1", From = "a", To = "b" }]);

        var loaded = TemplateDocument.Load(
            TemplateDocument.Save(TemplateDocument.FromDocument("片段", document, ["a", "b"])));

        using var harness = new Harness();

        TemplateInstantiator.TryPlan(harness.Document, loaded, out var plan, out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        plan!.Ids.Should().Equal("a", "b", "e1");
    }

    #endregion

    #region 辅助

    /// <summary>经总线放一份模板。上下文按界面上那条路给。</summary>
    private static CommandResult Insert(Harness harness, string json) =>
        harness.Bus.Execute(new InsertTemplateCommand(TemplateDocument.Load(json))
            .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

    /// <summary>一层套一层的组合，最外层是 <c>g1</c>。</summary>
    private static IReadOnlyList<CompositeDef> Nesting(int depth) =>
    [
        .. Enumerable.Range(1, depth).Select(level => (CompositeDef)new GroupDef
        {
            Id = $"g{level}",
            Parent = level == 1 ? null : $"g{level - 1}",
        }),
    ];

    #endregion
}
