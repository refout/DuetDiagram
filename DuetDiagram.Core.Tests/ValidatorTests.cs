using DuetDiagram.Core.Commands;
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
    public void Missing_tag_member_and_action_target_are_reported()
    {
        var document = IrFixtures.WithAction(
            IrFixtures.WithTag(IrFixtures.Base(), new TagDef { Id = "t1", Members = ["查无此物"] }),
            new ActionDef { Id = "act1", Event = "click", Kind = "open-url", Target = "也查无此物" });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == ErrorCodes.TagMemberMissing);
        issues.Should().Contain(i => i.Code == ErrorCodes.ActionTargetMissing);
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
