using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>P1 判据 #29：SessionId 只有一个来源，冲突必须告警。</summary>
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

        harness.Session.CurrentSessionId = SessionIds.Llm("conv-1");
        harness.Bus.Undo();

        harness.Context.VersionLog.Snapshot()[^1].SessionId.Should().Be("llm:conv-1");
    }

    [Fact]
    [Trait("Category", "SessionIdResolution")]
    public void SessionIds_follow_the_documented_format()
    {
        SessionIds.Gui("w1").Should().Be("gui:w1");
        SessionIds.Mcp("t", "s").Should().Be("mcp:t:s");
        SessionIds.Llm("c").Should().Be("llm:c");
        SessionIds.Import("a.json").Should().Be("import:a.json");
    }
}
