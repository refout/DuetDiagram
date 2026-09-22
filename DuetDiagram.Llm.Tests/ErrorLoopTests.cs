using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Llm.Loop;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 错误回环：一次失败翻成什么给模型看，以及这一轮试几次就停。
/// </summary>
/// <remarks>
/// <para>
/// 回灌的内容要能让人照着改：只给一句"参数非法"的话，模型只能靠猜重试，
/// 而每一次重试都是一轮往返。
/// </para>
/// <para>
/// 同一个错误连着来第二次时，回灌的内容必须**不一样**——不变的话，模型会原样重试，
/// 表现是它卡在一个错误上反复撞，而日志上只是同一条码重复了很多次。
/// </para>
/// <para>
/// 失败真的走的是工具层那两条路径：参数被 schema 拦下、命令被前置检查拒掉。
/// 拿手搓的 <see cref="ToolResult"/> 当输入的话，验的是回环自己的算术，
/// 而不是"真实失败长什么样"。
/// </para>
/// </remarks>
public sealed class ErrorLoopTests
{
    /// <summary>一个命令层会拒掉的调用：要删的节点不存在。</summary>
    private const string MissingNode = """{"action":"remove-node","id":"ghost"}""";

    #region 一次失败翻成什么

    /// <summary>参数写错时，四样东西都在：码、参数名、可用形式、一句能照着改的话。</summary>
    /// <remarks>
    /// 这一条盯的是回环有没有把失败现场的信息丢掉。丢掉参数名或可用形式之后，
    /// 模型看到的只剩一句笼统的话，而它照着改的成功率就落回"猜"。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorLoop")]
    public void A_bad_parameter_carries_the_code_the_parameter_the_form_and_a_suggestion()
    {
        var failure = Harness.Edit(Harness.Registry(), """{"action":"add-node","id":"Node 1"}""");

        var only = new ErrorLoop().Feed(failure).Should().ContainSingle().Which;

        only.Code.Should().Be(ToolErrorCodes.ArgumentInvalid);
        only.Parameter.Should().Be("id");
        only.Expected.Should().Be(Patterns.DiagramId);
        only.Message.Should().Contain("Node 1", "说明里要带上出错的那个值，模型才知道自己给了什么");
        only.Suggestion.Should().NotBeEmpty();
        only.Attempt.Should().Be(1);
        only.Repeated.Should().BeFalse();
        only.Note.Should().BeNull();
    }

    /// <summary>动作名不认识时，可用取值来自失败现场，不是表里写死的那一份。</summary>
    /// <remarks>
    /// 可用动作随工具表变，静态表里写不出来。这一条盯的是回环有没有让失败现场的那一份
    /// 盖过表里的——反过来的话，模型会照着一份过期的动作名去改。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorLoop")]
    public void An_unknown_action_lists_the_ones_that_exist()
    {
        var failure = Harness.Edit(Harness.Registry(), """{"action":"fly-to-mars"}""");

        var only = new ErrorLoop().Feed(failure).Should().ContainSingle().Which;

        only.Code.Should().Be(ToolErrorCodes.ArgumentInvalid);
        only.Parameter.Should().Be("action");
        only.Expected.Should().Contain("add-node").And.Contain("connect-edge");
        only.Suggestion.Should().NotBeEmpty();
    }

    /// <summary>命令被拒时，参数名由修复线索补上。</summary>
    /// <remarks>
    /// 命令层只知道那个标识不存在，不知道它是从哪个参数传进来的——载荷里是标识的值。
    /// 补不上参数名的话，模型只收到一句"节点不存在"，而它得自己猜是哪一个参数写错了。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorLoop")]
    public void A_rejected_command_gets_its_parameter_from_the_hint_table()
    {
        var failure = Harness.Edit(Harness.Registry(), MissingNode);

        var only = new ErrorLoop().Feed(failure).Should().ContainSingle().Which;

        only.Code.Should().Be(ErrorCodes.NodeMissing);
        only.Parameter.Should().Be("id");
        only.Suggestion.Should().NotBeEmpty();
    }

    /// <summary>一条码都没给的失败也要翻成能行动的东西。</summary>
    /// <remarks>
    /// 命令被拒却没有错误码时，回环这边要按内部错误兜一次。兜不住的话，
    /// 模型收到的是一份没有码、没有参数、没有建议的空信封，而它会把这当成"再试一次"。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorLoop")]
    public void A_failure_without_a_code_is_still_something_the_model_can_act_on()
    {
        var only = new ErrorLoop().Feed(ToolResult.Fail()).Should().ContainSingle().Which;

        only.Code.Should().Be(ErrorCodes.InternalError);
        only.Message.Should().NotBeEmpty();
        only.Suggestion.Should().NotBeEmpty();
    }

    /// <summary>一次调用报出好几个错时，每条各自一个信封。</summary>
    /// <remarks>
    /// 摊平成一个信封的话，模型只改得动其中一条，下一轮再收到另一条，来回好几轮。
    /// 参数校验本来就是一次报全的，摊平把那个优点丢了。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorLoop")]
    public void Several_errors_in_one_call_each_get_their_own_envelope()
    {
        var failure = Harness.Edit(Harness.Registry(), """{"action":"add-node","bogus":1,"alsoBogus":2}""");

        var envelopes = new ErrorLoop().Feed(failure);

        envelopes.Should().HaveCount(2);
        envelopes.Should().OnlyContain(envelope => envelope.Code == ToolErrorCodes.ArgumentUnknown);
        envelopes.Select(envelope => envelope.Parameter).Should().Equal("bogus", "alsoBogus");
    }

    #endregion

    #region 同一个错误连着来

    /// <summary>同一个错误连着来第二次时，回灌的内容里多一句「上一次这么改也不行」。</summary>
    [Fact]
    [Trait("Category", "ErrorLoop")]
    public void The_second_time_the_same_error_comes_back_it_says_so()
    {
        var registry = Harness.Registry();
        var loop = new ErrorLoop();

        var first = loop.Feed(Harness.Edit(registry, MissingNode)).Should().ContainSingle().Which;
        var second = loop.Feed(Harness.Edit(registry, MissingNode)).Should().ContainSingle().Which;

        first.Repeated.Should().BeFalse();
        first.Note.Should().BeNull();

        second.Repeated.Should().BeTrue();
        second.Note.Should().Contain("上一次这么改也不行");
        second.Attempt.Should().Be(2);
        second.Suggestion.Should().Be(first.Suggestion, "变的是那句提醒，建议本身不该跟着变");
    }

    /// <summary>中间撞过另一个错误之后，再撞回原来那个不算「连着」。</summary>
    /// <remarks>
    /// 判据是"上一轮"而不是"整段历史"：模型换了个做法、在别处碰了壁、又绕回原来那一步，
    /// 那是另一件事。按整段历史判的话，它会收到一句与当下无关的提醒。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorLoop")]
    public void A_different_error_in_between_clears_the_note()
    {
        var registry = Harness.Registry();
        var loop = new ErrorLoop();

        loop.Feed(Harness.Edit(registry, MissingNode));
        loop.Feed(Harness.Edit(registry, """{"action":"disconnect-edge","id":"ghost"}"""));

        var third = loop.Feed(Harness.Edit(registry, MissingNode)).Should().ContainSingle().Which;

        third.Repeated.Should().BeFalse();
        third.Note.Should().BeNull();
    }

    #endregion

    #region 上限

    /// <summary>到上限就停下，并把整段往返记录写进应用日志。</summary>
    /// <remarks>
    /// 不设上限的话，一次参数错误会变成无限次调用——账单上看得出来、日志里看不出来。
    /// 记的是整段而不是最后一条：只看最后一条的话，读日志的人看不出模型撞了几次、撞在什么上。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorLoop")]
    public void The_loop_stops_at_the_cap_and_writes_the_whole_round_trip()
    {
        var registry = Harness.Registry();
        var diagnostics = new CollectingDiagnosticsSink();
        var loop = new ErrorLoop(maxAttempts: 2, diagnostics);

        loop.Feed(Harness.Edit(registry, MissingNode)).Should().HaveCount(1);
        loop.Feed(Harness.Edit(registry, MissingNode)).Should().HaveCount(1);
        loop.Feed(Harness.Edit(registry, MissingNode)).Should().BeEmpty("上限是两次回灌，第三次就该停下");

        loop.Exhausted.Should().BeTrue();
        loop.Attempts.Should().Be(3);

        var record = diagnostics.Warnings.Should().ContainSingle().Which;

        record.Should().Contain("NODE_MISSING");
        record.Should().Contain("第 3 次");
        record.Should().Contain("id", "记录里要带上参数名，同一个码可能在几个参数上出现");
        diagnostics.Errors.Should().BeEmpty("放弃这一轮不是内部出错");
    }

    /// <summary>换一轮之后次数与「上一次错的是什么」都清掉。</summary>
    [Fact]
    [Trait("Category", "ErrorLoop")]
    public void Reset_starts_a_new_round()
    {
        var registry = Harness.Registry();
        var loop = new ErrorLoop(maxAttempts: 1);

        loop.Feed(Harness.Edit(registry, MissingNode)).Should().HaveCount(1);
        loop.Feed(Harness.Edit(registry, MissingNode)).Should().BeEmpty();
        loop.Exhausted.Should().BeTrue();

        loop.Reset();

        loop.Attempts.Should().Be(0);
        loop.Exhausted.Should().BeFalse();
        loop.History.Should().BeEmpty();

        var again = loop.Feed(Harness.Edit(registry, MissingNode)).Should().ContainSingle().Which;

        again.Attempt.Should().Be(1);
        again.Repeated.Should().BeFalse("换了一轮，上一轮撞过什么不算数");
    }

    #endregion

    #region 用错

    /// <summary>成功的那一次不该进回环，而且报错时不该把次数算掉。</summary>
    [Fact]
    [Trait("Category", "ErrorLoop")]
    public void A_successful_result_cannot_be_fed()
    {
        var loop = new ErrorLoop();
        var act = () => loop.Feed(ToolResult.Ok("成了"));

        act.Should().Throw<ArgumentException>();
        loop.Attempts.Should().Be(0, "报错的那一次没有回灌，就不该占掉一次机会");
    }

    #endregion
}
