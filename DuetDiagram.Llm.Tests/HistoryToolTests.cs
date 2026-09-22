using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 撤销与重做：同一条历史栈、多步、栈见底，以及「撤掉的是哪一条」。
/// </summary>
public sealed class HistoryToolTests
{
    #region 一步

    [Fact]
    [Trait("Category", "HistoryTool")]
    public void Undo_rolls_back_the_last_change_and_names_it()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"b"}""");

        var result = Harness.Invoke(registry, DiagramToolset.UndoRedo, """{"action":"undo"}""");

        result.IsSuccess.Should().BeTrue();
        document.Nodes.Should().ContainSingle().Which.Id.Should().Be("a");
        result.Message.Should().Contain("add-node",
            "模型需要知道自己刚才那一步被撤了，否则它会以为文档还停在自己写完之后的状态");
    }

    [Fact]
    [Trait("Category", "HistoryTool")]
    public void Redo_replays_what_was_undone()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Invoke(registry, DiagramToolset.UndoRedo, """{"action":"undo"}""");

        var result = Harness.Invoke(registry, DiagramToolset.UndoRedo, """{"action":"redo"}""");

        result.IsSuccess.Should().BeTrue();
        document.Nodes.Should().ContainSingle().Which.Id.Should().Be("a");
        result.Message.Should().Contain("add-node");
    }

    [Fact]
    [Trait("Category", "HistoryTool")]
    public void A_new_change_makes_the_redo_stack_go_away()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Invoke(registry, DiagramToolset.UndoRedo, """{"action":"undo"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"b"}""");

        var result = Harness.Invoke(registry, DiagramToolset.UndoRedo, """{"action":"redo"}""");

        result.IsSuccess.Should().BeTrue("重做栈空了不是失败，是没什么可做");
        result.Data!.Value.GetProperty("noOp").GetBoolean().Should().BeTrue();
        document.Nodes.Should().ContainSingle().Which.Id.Should().Be("b");
    }

    #endregion

    #region 多步

    [Fact]
    [Trait("Category", "HistoryTool")]
    public void Several_steps_are_undone_in_one_call()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"b"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"c"}""");

        var result = Harness.Invoke(registry, DiagramToolset.UndoRedo, """{"action":"undo","steps":2}""");

        result.IsSuccess.Should().BeTrue();
        document.Nodes.Should().ContainSingle().Which.Id.Should().Be("a");
        result.Message.Should().Contain("撤销了 2 步");
    }

    [Fact]
    [Trait("Category", "HistoryTool")]
    public void Asking_for_more_steps_than_there_are_stops_at_the_bottom()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"b"}""");

        var result = Harness.Invoke(registry, DiagramToolset.UndoRedo, """{"action":"undo","steps":5}""");

        result.IsSuccess.Should().BeTrue("栈见底不是失败，报成失败会让模型以为整条调用没生效");
        document.Nodes.Should().BeEmpty();
        result.Message.Should().Contain("撤销了 2 步").And.Contain("见底");
    }

    [Fact]
    [Trait("Category", "HistoryTool")]
    public void Undoing_an_empty_stack_is_a_no_op()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        var result = Harness.Invoke(registry, DiagramToolset.UndoRedo, """{"action":"undo"}""");

        result.IsSuccess.Should().BeTrue();
        result.Data!.Value.GetProperty("noOp").GetBoolean().Should().BeTrue();
        result.Message.Should().Contain("空的");
    }

    #endregion

    #region 人和模型共用一条栈

    [Fact]
    [Trait("Category", "HistoryTool")]
    public void A_change_made_by_another_session_is_undone_too()
    {
        var document = new DiagramDocument("doc");
        var session = Harness.Session();
        var registry = Harness.Registry(document, session: session);

        // 先让另一个会话改一次，再让当前会话改一次。
        session.CurrentSessionId = "human:alice";
        Harness.Edit(registry, """{"action":"add-node","id":"human-one"}""");

        session.CurrentSessionId = "llm:mine";
        Harness.Edit(registry, """{"action":"add-node","id":"llm-one"}""");

        var result = Harness.Invoke(registry, DiagramToolset.UndoRedo, """{"action":"undo","steps":2}""");

        result.IsSuccess.Should().BeTrue();
        document.Nodes.Should().BeEmpty("人和模型共用同一条历史栈，撤销会一路撤到人改的那一步");
        result.Message.Should().Contain("其中 1 步是你自己改的",
            "模型要知道哪一步是自己写的，而判据是历史条目上的会话标识");
    }

    #endregion

    #region 参数

    [Fact]
    [Trait("Category", "HistoryTool")]
    public void An_unknown_action_lists_the_available_ones()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(registry, DiagramToolset.UndoRedo, """{"action":"rollback"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("action");
        result.Errors[0].Expected.Should().Contain("undo").And.Contain("redo");
    }

    [Fact]
    [Trait("Category", "HistoryTool")]
    public void A_step_count_below_one_is_rejected()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        var result = Harness.Invoke(registry, DiagramToolset.UndoRedo, """{"action":"undo","steps":0}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("steps");
        document.Version.Should().Be(0, "参数不成立时不该动历史栈");
    }

    #endregion
}
