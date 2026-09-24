using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 整体一致性校验。
/// </summary>
/// <remarks>
/// 命令层在写入前挡住大部分非法状态；这里验的是加载外部文件、接收同步结果时会走到的那条路。
/// 那些入口不经过命令层，只靠命令层的防线是不够的。
/// </remarks>
public sealed class ValidatorTests
{
    [Fact]
    [Trait("Category", "IrValidator")]
    public void Valid_document_has_no_issues()
    {
        DiagramValidator.Validate(IrFixtures.Populated()).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Duplicate_id_across_collections_is_reported()
    {
        // 九个集合共用一个命名空间，成员列表里的标识不区分它是节点还是组合。
        var document = IrFixtures.WithTag(
            IrFixtures.WithComposite(IrFixtures.Base(), new GroupDef { Id = "a", Members = ["node1"] }),
            new TagDef { Id = "t1", Members = ["a"] });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == ErrorCodes.DuplicateId);
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Edge_endpoint_missing_is_reported_for_both_ends()
    {
        var document = IrFixtures.WithEdge(
            IrFixtures.Base(),
            new EdgeDef { Id = "e1", From = "无源", To = "无终" });

        var issues = DiagramValidator.Validate(document);

        // 两端各报一条。只报一条的话，修完一头才会发现另一头也有问题。
        issues.Should().Contain(i => i.Code == ErrorCodes.EdgeSourceMissing);
        issues.Should().Contain(i => i.Code == ErrorCodes.EdgeTargetMissing);
    }

    /// <summary>
    /// 端点是组合的边是合法的。
    /// </summary>
    /// <remarks>
    /// 分层架构图里 <c>ODS --&gt; DWD</c> 是拿分组当端点用的，说的是"这一层流向那一层"。
    /// 这是外部格式里很常见、也很自然的写法，冻结语料里真的出现了。
    /// 字段类型不用改：标识本来就是字符串，九个集合也共用同一个命名空间。
    /// </remarks>
    [Fact]
    [Trait("Category", "IrValidator")]
    public void Edge_between_two_composites_is_accepted()
    {
        var document = IrFixtures.WithEdge(
            IrFixtures.WithComposites(
                IrFixtures.Base(),
                [new GroupDef { Id = "ods" }, new GroupDef { Id = "dwd" }]),
            new EdgeDef { Id = "e1", From = "ods", To = "dwd", Label = "清洗" });

        DiagramValidator.Validate(document).Should().BeEmpty();
    }

    /// <summary>一端是节点、一端是组合，同样合法。</summary>
    [Fact]
    [Trait("Category", "IrValidator")]
    public void Edge_from_a_node_to_a_composite_is_accepted()
    {
        var document = IrFixtures.WithEdge(
            IrFixtures.WithComposite(IrFixtures.Base(), new GroupDef { Id = "g1" }),
            new EdgeDef { Id = "e1", From = "a", To = "g1" });

        DiagramValidator.Validate(document).Should().BeEmpty();
    }

    /// <summary>
    /// 端点不存在时仍然要报，放宽端点类型不能把这条一起放过。
    /// </summary>
    [Fact]
    [Trait("Category", "IrValidator")]
    public void Unknown_endpoint_is_still_reported_after_relaxing()
    {
        var document = IrFixtures.WithEdge(
            IrFixtures.WithComposite(IrFixtures.Base(), new GroupDef { Id = "g1" }),
            new EdgeDef { Id = "e1", From = "g1", To = "查无此物" });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == ErrorCodes.EdgeTargetMissing);
        issues.Should().NotContain(i => i.Code == ErrorCodes.EdgeSourceMissing);
    }

    /// <summary>
    /// 组合端点上指定端口要单独报一种码。
    /// </summary>
    /// <remarks>
    /// 组合没有端口，所以这是"写错了"而不是"端口名对不上"。两种说法给用户的下一步动作不同：
    /// 前者要把端口去掉或把端点改成节点，后者要去补端口。
    /// 用两个码而不是共用一个，是因为共用之后调用方分不出该提示哪一句。
    /// </remarks>
    [Fact]
    [Trait("Category", "IrValidator")]
    public void Port_on_a_composite_endpoint_is_reported_separately()
    {
        var document = IrFixtures.WithEdge(
            IrFixtures.WithComposite(IrFixtures.Base(), new GroupDef { Id = "g1" }),
            new EdgeDef { Id = "e1", From = "a", To = "g1", ToPort = "out" });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == ErrorCodes.EdgePortOnComposite);

        // 不能再报"端口不存在"：同一处错因引出两条问题，用户会以为有两件事要修。
        issues.Should().NotContain(i => i.Code == ErrorCodes.EdgePortMissing);
    }

    /// <summary>
    /// 命令层与校验器对"什么算端点"必须给出同一个答案。
    /// </summary>
    /// <remarks>
    /// 两层各写一份判定必然分叉，症状是"能画出来的边，存下来再打开就报错"——
    /// 而那时候用户已经看不出是哪一步不对了。
    /// </remarks>
    [Fact]
    [Trait("Category", "IrValidator")]
    public void Command_layer_and_validator_agree_on_what_an_endpoint_is()
    {
        var document = IrFixtures.WithComposite(IrFixtures.Base(), new GroupDef { Id = "g1" });
        var edge = new EdgeDef { Id = "e1", From = "a", To = "g1" };

        new ConnectEdgeCommand(edge).Validate(document).IsValid.Should().BeTrue();
        DiagramValidator.Validate(IrFixtures.WithEdge(document, edge)).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Missing_port_is_reported()
    {
        var document = IrFixtures.WithEdge(
            IrFixtures.Base(),
            new EdgeDef { Id = "e1", From = "a", To = "a", FromPort = "不存在" });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == ErrorCodes.EdgePortMissing);
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Membership_mismatch_is_reported()
    {
        // 组合的成员列表与成员的父级说的是同一件事。约定以成员列表为准，
        // 不一致时必须报出来——放任不管的话，布局按一处算、渲染按另一处画。
        var document = IrFixtures.WithComposite(
            IrFixtures.Base(),
            new GroupDef { Id = "g1", Members = ["a"] });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == ErrorCodes.MembershipMismatch);
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Parent_missing_is_reported()
    {
        var document = IrFixtures.WithComposite(
            IrFixtures.Base(),
            new GroupDef { Id = "g1", Parent = "不存在的组合" });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == ErrorCodes.ParentMissing);
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Missing_member_is_reported()
    {
        var document = IrFixtures.WithComposite(
            IrFixtures.Base(),
            new GroupDef { Id = "g1", Members = ["查无此物"] });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == ErrorCodes.GroupMemberMissing);
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Composite_cycle_is_reported()
    {
        // 成环会让"向上找容器"这类遍历永远走不到头。这类问题在写入时不容易发现——
        // 单独看每一步都是合法的父子关系，只有连起来才成环。
        var document = IrFixtures.WithComposites(
            IrFixtures.Base(),
            [
                new GroupDef { Id = "g1", Parent = "g2" },
                new GroupDef { Id = "g2", Parent = "g1" },
            ]);

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == ErrorCodes.GroupCycle);
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Every_cycle_member_is_reported()
    {
        // 环上的每一个都报一条，界面按标识高亮时才能把整个环标出来。
        // 只报一个的话，用户还得自己顺着找下一个。
        var document = IrFixtures.WithComposites(
            IrFixtures.Base(),
            [
                new GroupDef { Id = "g1", Parent = "g2" },
                new GroupDef { Id = "g2", Parent = "g3" },
                new GroupDef { Id = "g3", Parent = "g1" },
            ]);

        var cycleIds = DiagramValidator.Validate(document)
            .Where(i => i.Code == ErrorCodes.GroupCycle)
            .Select(i => i.RelatedId)
            .ToArray();

        cycleIds.Should().BeEquivalentTo("g1", "g2", "g3");
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Self_parenting_composite_is_reported_as_cycle()
    {
        var document = IrFixtures.WithComposite(
            IrFixtures.Base(),
            new GroupDef { Id = "g1", Parent = "g1" });

        DiagramValidator.Validate(document).Should().Contain(i => i.Code == ErrorCodes.GroupCycle);
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void A_composite_chain_exactly_at_the_limit_is_clean()
    {
        var issues = DiagramValidator.Validate(IrFixtures.WithComposites(IrFixtures.Base(), Chain(CompositeLimits.MaxDepth)));

        // 上限是"允许到第几层"，不是"从第几层开始报"。差一位的错会让最后一个合法层级被拒，
        // 而那条命令看起来只是"没生效"。
        issues.Should().NotContain(i => i.Code == ErrorCodes.CompositeTooDeep);
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void A_composite_chain_deeper_than_the_limit_is_reported()
    {
        var issues = DiagramValidator.Validate(
            IrFixtures.WithComposites(IrFixtures.Base(), Chain(CompositeLimits.MaxDepth + 1)));

        // 命令层在建组合的时候已经查过一遍，这里再查一次是因为**文件是从外面进来的**：
        // 命令层的检查管不到别人手写或另一个工具生成的文档。
        issues.Should().Contain(i => i.Code == ErrorCodes.CompositeTooDeep);
    }

    /// <summary>一条逐级嵌套的组合链，第 1 个在最外层。</summary>
    private static List<CompositeDef> Chain(int depth)
    {
        var composites = new List<CompositeDef>(depth);

        for (var level = 1; level <= depth; level++)
        {
            composites.Add(new GroupDef
            {
                Id = $"g{level}",
                Parent = level == 1 ? null : $"g{level - 1}",
            });
        }

        return composites;
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Missing_tag_member_and_action_target_are_reported()
    {
        var document = IrFixtures.WithAction(
            IrFixtures.WithTag(IrFixtures.Base(), new TagDef { Id = "t1", Members = ["查无此物"] }),
            new ActionDef { Id = "act1", Event = "click", Kind = "open-url", Target = "也查无此物" });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == ErrorCodes.TagMemberMissing);
        issues.Should().Contain(i => i.Code == ErrorCodes.ActionTargetMissing);
    }

    /// <summary>
    /// 自定义形状的路径写错了：报出结构化错误，带行号与那一行的原文。
    /// </summary>
    /// <remarks>
    /// 不报的话，渲染层会一路抛到界面上；而报成"值不合法"这种泛泛的说法，
    /// 用户得自己在那段路径里逐行找。行号与原文两样都要有。
    /// </remarks>
    [Fact]
    [Trait("Category", "IrValidator")]
    public void A_broken_custom_path_is_reported_with_its_line()
    {
        var document = IrFixtures.WithNode(
            IrFixtures.Base(),
            new NodeDef { Id = "a", Label = "甲", ShapePath = "M 0.5 0\nQ 1 1" });

        var issue = DiagramValidator.Validate(document)
            .Should().ContainSingle(i => i.Code == ErrorCodes.ShapePathInvalid).Subject;

        issue.RelatedId.Should().Be("a");
        issue.Message.Should().Contain("第 2 行").And.Contain("Q 1 1");
        issue.Suggestion.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// 节点引用的形状名没有对应的几何：报的是另一种码。
    /// </summary>
    /// <remarks>
    /// 与路径写错分开：那一条的处置是按行号改路径，这一条是换个名字。
    /// 合成一个码的话，用户会去改值的写法，而值本来就是合法的形状名。
    /// </remarks>
    [Fact]
    [Trait("Category", "IrValidator")]
    public void An_unknown_shape_name_is_reported()
    {
        // 枚举里没有的取值只可能来自外部输入（另一个工具写错了名字）。
        var document = IrFixtures.WithNode(
            IrFixtures.Base(),
            new NodeDef { Id = "a", Label = "甲", Shape = (NodeShape)9999 });

        var issue = DiagramValidator.Validate(document)
            .Should().ContainSingle(i => i.Code == ErrorCodes.ShapeUnknown).Subject;

        issue.RelatedId.Should().Be("a");
        issue.Suggestion.Should().Contain("自定义路径", "换成已有的形状名或者写一段路径，两条路都指出来");
    }

    /// <summary>路径合法、形状名也认得时，两种码都不该出现。</summary>
    [Fact]
    [Trait("Category", "IrValidator")]
    public void A_valid_custom_path_raises_no_shape_issue()
    {
        // 没有这一条的话，上面两条可以被"永远报错"满足。
        var document = IrFixtures.WithNode(
            IrFixtures.Base(),
            new NodeDef { Id = "a", Label = "甲", ShapePath = "M 0.5 0 L 1 0.5 L 0.5 1 L 0 0.5 Z" });

        var codes = DiagramValidator.Validate(document).Select(i => i.Code).ToArray();

        codes.Should().NotContain(ErrorCodes.ShapePathInvalid);
        codes.Should().NotContain(ErrorCodes.ShapeUnknown);
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Every_issue_carries_a_suggestion()
    {
        // 只说哪里错、不说怎么改，等于把问题原样丢回给用户。
        // 这一点对模型同样重要：一个能听懂"该动哪里"的模型可以自己修好，
        // 而只收到"标识重复"的模型只能猜。
        var document = IrFixtures.WithAction(
            IrFixtures.WithTag(
                IrFixtures.WithEdge(
                    IrFixtures.WithComposites(
                        IrFixtures.Base(),
                        [
                            new GroupDef { Id = "a", Members = ["查无此物"], Parent = "也不存在" },
                            new GroupDef { Id = "g2", Parent = "g3" },
                            new GroupDef { Id = "g3", Parent = "g2" },
                        ]),
                    new EdgeDef { Id = "e1", From = "无源", To = "无终", FromPort = "无此端口" }),
                new TagDef { Id = "t1", Members = ["查无此物"] }),
            new ActionDef { Id = "act1", Event = "click", Kind = "open-url", Target = "同样查无此物" });

        var issues = DiagramValidator.Validate(document);

        // 先确认这些用例确实造出了多种问题，否则"每条都有建议"可能只是因为问题太少。
        issues.Select(i => i.Code).Distinct().Should().HaveCountGreaterThanOrEqualTo(5);

        issues.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Suggestion));
        issues.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Message));
        issues.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.RelatedId));
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Issue_converts_to_a_command_error()
    {
        // 校验结果是给人看、给界面高亮的宽形状；传给外部代理的线上形状只有码与载荷。
        var issue = new ValidationIssue
        {
            Code = ErrorCodes.GroupCycle,
            Message = "说明",
            RelatedId = "g1",
            Suggestion = "建议",
        };

        var error = issue.ToCommandError();

        error.Code.Should().Be(ErrorCodes.GroupCycle);
        error.Payload.Should().Be("g1");

        // 建议不进线上形状：它是给人看的，外部代理拿到码与标识就能自己决定怎么处理。
        error.Payload.Should().NotContain("建议");
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void A_layout_constraint_pointing_at_a_missing_node_is_reported()
    {
        // 四类约束此前完全没校验过。约束指向不存在的节点时不报错，
        // 布局层把它当"没有这条约束"忽略掉，症状是"布局没按我写的来"——
        // 而用户无从知道是那个名字写错了。外部输入里这种错很容易出现：
        // 名字是模型写出来的，没有任何编译期检查。
        var document = IrFixtures.WithLayout(
            IrFixtures.Base(),
            new LayoutHints
            {
                SameRank = [Constraint(new SameRankConstraint(["a", "查无此节点"]), ConstraintOwner.Llm)],
            });

        var issues = DiagramValidator.Validate(document);

        issues.Should().ContainSingle(i => i.Code == ErrorCodes.LayoutNodeMissing)
            .Which.RelatedId.Should().Be("查无此节点");
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void An_order_entry_that_is_not_an_outgoing_edge_is_reported()
    {
        // 两层判据：边得存在，而且得是主语节点的出边。
        // 指向别的边时约束照样生效，只是排的不是它想排的那些——那比"引用不存在"更难看出来。
        var document = IrFixtures.WithLayout(
            IrFixtures.WithEdge(
                IrFixtures.Base(),
                new EdgeDef { Id = "e1", From = "b", To = "a" }),
            new LayoutHints
            {
                Order =
                [
                    Constraint(new OrderConstraint("a", ["e1"]), ConstraintOwner.Llm),
                    Constraint(new OrderConstraint("a", ["查无此边"]), ConstraintOwner.Llm),
                ],
            });

        var issues = DiagramValidator.Validate(document).Where(i => i.Code == ErrorCodes.LayoutOrderEdgeMissing).ToArray();

        issues.Should().HaveCount(2);
        issues.Select(i => i.Message).Should().Contain(m => m.Contains("查无此边"));
        issues.Select(i => i.Message).Should().Contain(m => m.Contains("起点是 b"));
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void A_composite_local_layout_names_the_composite_it_belongs_to()
    {
        // 组合内部的提示用同一套判据，但文案要带上组合标识——
        // 否则用户看到一条约束报错，不知道是文档级那条还是某个分组里那条。
        var document = IrFixtures.WithComposite(
            IrFixtures.Base(),
            new GroupDef
            {
                Id = "g1",
                Members = ["a"],
                LocalLayout = new LayoutHints
                {
                    Align = [Constraint(new AlignConstraint(["查无此节点"]), ConstraintOwner.Human)],
                },
            });

        DiagramValidator.Validate(document)
            .Should().ContainSingle(i => i.Code == ErrorCodes.LayoutNodeMissing)
            .Which.Message.Should().Contain("组合 g1");
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Layout_hints_that_refer_to_real_things_are_accepted()
    {
        // 夹具里那份提示指向的节点与出边都真实存在，所以它不该产出任何布局相关问题。
        // 没有这一条的话，上面前几条可以被"永远报错"满足。
        var document = IrFixtures.Populated();

        var codes = DiagramValidator.Validate(document).Select(i => i.Code).ToArray();

        codes.Should().NotContain(ErrorCodes.LayoutNodeMissing);
        codes.Should().NotContain(ErrorCodes.LayoutOrderEdgeMissing);
    }

    private static Constraint<T> Constraint<T>(T value, ConstraintOwner owner) where T : notnull =>
        new(value, owner, DateTimeOffset.UnixEpoch);

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Validator_does_not_modify_the_document()
    {
        // 只报告不修改。发现问题时由调用方决定是拒绝加载、丢弃问题部分，还是照常打开并提示——
        // 这三种处置在不同场景下都合理，校验器不该替调用方做这个决定。
        var document = IrFixtures.WithComposite(
            IrFixtures.Base(),
            new GroupDef { Id = "g1", Members = ["查无此物"] });

        var before = DuetDiagram.Core.Serialization.DiagramSerializer.Normalize(document);

        DiagramValidator.Validate(document).Should().NotBeEmpty();

        DuetDiagram.Core.Serialization.DiagramSerializer.Normalize(document).Should().Be(before);
    }
}
