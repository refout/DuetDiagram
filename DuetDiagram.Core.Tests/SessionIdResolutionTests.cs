using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 会话标识的解析规则：命令声明优先，未声明则用当前会话兜底，两者不一致要告警。
/// </summary>
public sealed class SessionIdResolutionTests
{
    [Fact]
    [Trait("Category", "SessionIdResolution")]
    public void Empty_context_session_falls_back_to_the_session_provider()
    {
        using var harness = new Harness();

        harness.AddNode("a");

        harness.Context.VersionLog.Snapshot()[0].SessionId.Should().Be("gui:w1");
        harness.Context.AuditLog.All()[0].SessionId.Should().Be("gui:w1");
        harness.Diagnostics.Warnings.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "SessionIdResolution")]
    public void Declared_session_wins_and_a_mismatch_is_reported()
    {
        using var harness = new Harness();

        // 命令声明的会话与环境默认会话不同。仍以命令声明的为准——
        // 它描述的是这次操作本身；但差异要留痕，因为频繁出现说明会话传递链有问题。
        harness.Bus.Execute(new AddNodeCommand(new NodeDef { Id = "a" })
            .WithContext(ChangeContext.For(ChangeSource.Mcp, "agent-7", SessionIds.Mcp("token", "42"))));

        harness.Context.VersionLog.Snapshot()[0].SessionId.Should().Be("mcp:token:42");
        harness.Diagnostics.Warnings.Should().ContainSingle(w => w.Contains("SessionId mismatch"));
    }

    [Fact]
    [Trait("Category", "SessionIdResolution")]
    public void Matching_session_ids_do_not_warn()
    {
        using var harness = new Harness();

        harness.Bus.Execute(new AddNodeCommand(new NodeDef { Id = "a" })
            .WithContext(ChangeContext.For(ChangeSource.Human, "tester", "gui:w1")));

        harness.Diagnostics.Warnings.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "SessionIdResolution")]
    public void Undo_records_the_current_session()
    {
        using var harness = new Harness();
        harness.AddNode("a", source: ChangeSource.Llm);

        // 撤销走的是"当前是谁在撤销"，而不是原命令的执行者。
        // 撤销是当下发生的新动作，归属当然应该是按下撤销的那个人。
        harness.Session.CurrentSessionId = SessionIds.Llm("conv-1");
        harness.Bus.Undo();

        harness.Context.VersionLog.Snapshot()[^1].SessionId.Should().Be("llm:conv-1");
    }

    [Fact]
    [Trait("Category", "SessionIdResolution")]
    public void SessionIds_follow_the_documented_format()
    {
        // 格式收敛在工厂方法里：各处自己拼字符串很容易出现大小写或段数不一致，
        // 跨进程比对时就认不出是同一个会话了。
        SessionIds.Gui("w1").Should().Be("gui:w1");
        SessionIds.Mcp("t", "s").Should().Be("mcp:t:s");
        SessionIds.Llm("c").Should().Be("llm:c");
        SessionIds.Import("a.json").Should().Be("import:a.json");
    }
}
