using DuetDiagram.App;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 变更高亮接在命令总线的通知上：一条命令之后对应元素被标记，撤销之后标记继承原来源并带撤销符号。
/// </summary>
/// <remarks>
/// <para>
/// 高亮只认变更通知，不自己比较文档。界面自己推断"哪个字段变了"的话，LLM 改的东西不会亮——
/// 那条路径根本不经过界面。这里的断言盯的正是"标记来自通知里的来源与受影响元素"。
/// </para>
/// <para>
/// 撤销重做单独看：它们的来源是 Undo / Redo，但用户想看的还是"谁改的"，
/// 所以标记要继承原命令的来源色，另加一个 ↶ / ↷ 符号。不继承的话，来源信息就丢了。
/// 这里刻意用一条来源为 LLM 的命令，好让"继承"这件事看得出来——
/// 若用默认来源，撤销前后都是一种颜色，继承与否分不出来。
/// </para>
/// </remarks>
public sealed class HighlightTests
{
    /// <summary>一条命令之后，受影响元素被标记，来源是那条命令声明的来源。</summary>
    [Fact]
    [Trait("Category", "Highlight")]
    public async Task A_change_marks_the_affected_element()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());

            ReconnectAsLlm(session);

            session.WaitForHighlights().Should().BeTrue("变更通知应当在时限内到达");

            var mark = session.HighlightSnapshot.Should().ContainSingle(m => m.ElementId == "e1").Which;

            mark.Source.Should().Be(ChangeSource.Llm);
            mark.Kinds.Should().Contain(HighlightKind.Badge);
            mark.Kinds.Should().Contain(HighlightKind.Outline);
            mark.IsUndo.Should().BeFalse();
        });
    }

    /// <summary>撤销之后，标记继承原命令的来源，并带一个撤销符号。</summary>
    [Fact]
    [Trait("Category", "Highlight")]
    public async Task Undo_inherits_the_source_and_adds_a_symbol()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());

            ReconnectAsLlm(session);
            session.WaitForHighlights().Should().BeTrue();

            session.Bus.Undo().IsSuccess.Should().BeTrue();
            session.WaitForHighlights().Should().BeTrue();

            var mark = session.HighlightSnapshot.Single(m => m.ElementId == "e1");

            mark.IsUndo.Should().BeTrue("撤销带 ↶");
            mark.Source.Should().Be(ChangeSource.Llm, "撤销继承原命令的来源，而不是把撤销本身当来源");

            // 符号是画出来的：把标记套在真实绘制列表上，应当出现一条 ↶ 指令。
            var commands = Highlight.Build([mark], session.Scene.DrawList.Commands, session.Theme, pulsePhase: -1);

            commands.Should().Contain(c => c.ElementId == Highlight.SymbolPrefix + "e1");
        });
    }

    /// <summary>撤销之后重做，符号换成 ↷，来源仍然是原命令的来源。</summary>
    [Fact]
    [Trait("Category", "Highlight")]
    public async Task Redo_shows_the_redo_symbol()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());

            ReconnectAsLlm(session);
            session.WaitForHighlights();
            session.Bus.Undo();
            session.WaitForHighlights();

            session.Bus.Redo().IsSuccess.Should().BeTrue();
            session.WaitForHighlights().Should().BeTrue();

            var mark = session.HighlightSnapshot.Single(m => m.ElementId == "e1");

            mark.IsRedo.Should().BeTrue("重做带 ↷");
            mark.IsUndo.Should().BeFalse();
            mark.Source.Should().Be(ChangeSource.Llm);
        });
    }

    /// <summary>以 LLM 为来源重连一条边。走总线而不是会话的便捷方法，因为要指定来源。</summary>
    private static void ReconnectAsLlm(DiagramSession session)
    {
        var command = new ReconnectEdgeCommand("e1", "start", null, "pass", null)
            .WithContext(ChangeContext.For(ChangeSource.Llm));

        session.Bus.Execute(command).IsEffectiveSuccess.Should().BeTrue();
    }
}
