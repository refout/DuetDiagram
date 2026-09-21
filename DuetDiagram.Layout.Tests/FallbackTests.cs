using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Layout.Tests;

/// <summary>
/// 四级降级、路径预算、级别唯一性与尝试记录。
/// </summary>
/// <remarks>
/// 超时用"故意很慢的引擎 + 很小的预算"来测。用一个可以手动推进的假时钟来测超时，
/// 验的是"我们能不能算对时间"，而不是"超时真的会发生"——后者才是这段代码存在的理由。
/// </remarks>
public sealed class FallbackTests
{

    #region 计划的合法性

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void Plan_rejects_a_duplicate_level()
    {
        // 重复的级别要么白花时间（两次都超时），要么预算形同虚设（第一次就成功）。
        // 两种情况都说明计划写错了，构造时就该拒绝，而不是等它表现成"偶尔特别慢"。
        var act = () => new LayoutPlan(
            new LayoutPlanEntry(LayoutFallbackLevel.Full, TimeSpan.FromMilliseconds(100)),
            new LayoutPlanEntry(LayoutFallbackLevel.Full, TimeSpan.FromMilliseconds(200)));

        act.Should().Throw<ArgumentException>().WithMessage("*不止一次*");
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void Plan_rejects_an_empty_or_non_positive_budget()
    {
        var empty = () => new LayoutPlan();
        empty.Should().Throw<ArgumentException>();

        var zero = () => new LayoutPlan(new LayoutPlanEntry(LayoutFallbackLevel.Full, TimeSpan.Zero));
        zero.Should().Throw<ArgumentException>().WithMessage("*必须为正*");
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void Default_plan_is_the_four_documented_levels()
    {
        var levels = LayoutPlan.Default.Entries.Select(e => e.Level).ToArray();

        levels.Should().Equal(
            LayoutFallbackLevel.Full,
            LayoutFallbackLevel.DropLlm,
            LayoutFallbackLevel.DropAll,
            LayoutFallbackLevel.PureAuto);
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void Path_budgets_have_the_documented_values()
    {
        LayoutBudgets.DragInProgress.Should().Be(TimeSpan.Zero, "拖动过程中不调用布局");
        LayoutBudgets.DragReleased.Should().Be(TimeSpan.FromMilliseconds(200));
        LayoutBudgets.StructuralChange.Should().Be(TimeSpan.FromMilliseconds(800));
        LayoutBudgets.ManualRelayout.Should().Be(TimeSpan.FromSeconds(2));
    }

    #endregion

    #region 顺利路径

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void A_fast_engine_succeeds_at_the_first_level()
    {
        var engine = new ScriptedEngine((_, _) => Empty());

        var result = new LayoutCoordinator(engine).Compute(Job(), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        result.AppliedLevel.Should().Be(LayoutFallbackLevel.Full);
        result.WasDowngraded.Should().BeFalse();
        result.Attempts.Should().ContainSingle();
        result.Attempts[0].TimedOut.Should().BeFalse();
        engine.Calls.Should().Be(1, "第一级就成功了，不该再试");
    }

    #endregion

    #region 降级

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void A_slow_engine_downgrades_to_the_next_level()
    {
        var engine = new ScriptedEngine((call, _) =>
        {
            if (call == 1)
            {
                Thread.Sleep(200);
            }

            return Empty();
        });

        var plan = new LayoutPlan(
            new LayoutPlanEntry(LayoutFallbackLevel.Full, TimeSpan.FromMilliseconds(30)),
            new LayoutPlanEntry(LayoutFallbackLevel.DropAll, TimeSpan.FromMilliseconds(500)));

        var result = new LayoutCoordinator(engine, plan).Compute(Job(), TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        result.AppliedLevel.Should().Be(LayoutFallbackLevel.DropAll);
        result.WasDowngraded.Should().BeTrue();

        result.Attempts.Should().HaveCount(2);
        result.Attempts[0].Level.Should().Be(LayoutFallbackLevel.Full);
        result.Attempts[0].TimedOut.Should().BeTrue();
        result.Attempts[1].Level.Should().Be(LayoutFallbackLevel.DropAll);
        result.Attempts[1].TimedOut.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void All_levels_failing_throws_with_every_attempt_recorded()
    {
        var engine = new ScriptedEngine((_, _) =>
        {
            Thread.Sleep(200);
            return Empty();
        });

        var plan = new LayoutPlan(
            new LayoutPlanEntry(LayoutFallbackLevel.Full, TimeSpan.FromMilliseconds(20)),
            new LayoutPlanEntry(LayoutFallbackLevel.DropAll, TimeSpan.FromMilliseconds(20)));

        var act = () => new LayoutCoordinator(engine, plan).Compute(Job(), TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var exception = act.Should().Throw<LayoutFailedException>().Which;

        exception.Error.Code.Should().Be(ErrorCodes.LayoutAllLevelsTimeout);
        exception.Payload.Attempts.Should().HaveCount(2);
        exception.Payload.Attempts.Should().OnlyContain(a => a.TimedOut);
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void An_engine_exception_moves_to_the_next_level()
    {
        // 下一级的输入更简单，有可能就过去了。所以引擎出错不该直接失败。
        var engine = new ScriptedEngine((call, _) =>
            call == 1 ? throw new InvalidOperationException("引擎内部出错") : Empty());

        var plan = new LayoutPlan(
            new LayoutPlanEntry(LayoutFallbackLevel.Full, TimeSpan.FromMilliseconds(300)),
            new LayoutPlanEntry(LayoutFallbackLevel.DropAll, TimeSpan.FromMilliseconds(300)));

        var result = new LayoutCoordinator(engine, plan).Compute(Job(), TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        result.AppliedLevel.Should().Be(LayoutFallbackLevel.DropAll);
        result.Attempts[0].TimedOut.Should().BeFalse("它不是超时，是出错");
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void DropLlm_is_skipped_when_there_is_nothing_from_the_model()
    {
        // 没有模型提出的约束时，这一级的输入与上一级完全相同，试它只是白花时间。
        var engine = new ScriptedEngine((_, _) => Empty());

        var result = new LayoutCoordinator(engine)
            .Compute(Job(llmConstraint: false), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        result.Attempts.Should().ContainSingle();
        engine.Calls.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void A_zero_budget_reports_no_attempts()
    {
        // 拖动过程中不该调布局。给一个会被立刻判定为"没时间"的预算，
        // 让它显式失败，而不是悄悄用很短的预算草草算一个结果。
        var engine = new ScriptedEngine((_, _) => Empty());

        var act = () => new LayoutCoordinator(engine).Compute(Job(), LayoutBudgets.DragInProgress, TestContext.Current.CancellationToken);

        act.Should().Throw<LayoutFailedException>()
            .Which.Payload.Attempts.Should().BeEmpty("一级都没试过");

        engine.Calls.Should().Be(0);
    }

    #endregion

    #region 保留项矩阵

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void PureAuto_drops_pins()
    {
        var engine = new ScriptedEngine((_, _) => Empty());

        var plan = new LayoutPlan(
            new LayoutPlanEntry(LayoutFallbackLevel.Full, TimeSpan.FromMilliseconds(10)),
            new LayoutPlanEntry(LayoutFallbackLevel.PureAuto, TimeSpan.FromMilliseconds(300)));

        // 第一级先超时，把矩阵逼到最后一级。
        var slow = new ScriptedEngine((call, _) =>
        {
            if (call == 1)
            {
                Thread.Sleep(100);
            }

            return Empty();
        });

        _ = engine;
        _ = new LayoutCoordinator(slow, plan).Compute(PinnedJob(), TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        slow.Requests.Should().HaveCount(2);
        slow.Requests[0].Nodes.Should().OnlyContain(n => n.Pinned != null, "前面的级别保留固定位置");
        slow.Requests[1].Nodes.Should().OnlyContain(n => n.Pinned == null, "最后一级丢掉固定位置");
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void DropAll_keeps_pins_but_drops_constraints()
    {
        var engine = new ScriptedEngine((_, _) => Empty());

        var plan = new LayoutPlan(new LayoutPlanEntry(LayoutFallbackLevel.DropAll, TimeSpan.FromMilliseconds(300)));

        _ = new LayoutCoordinator(engine, plan).Compute(PinnedJob(), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var request = engine.Requests.Single();

        request.Nodes.Should().OnlyContain(n => n.Pinned != null, "固定位置一直保留到最后一级之前");
        request.Options.SameRankGroups.Should().BeEmpty("这一级丢掉全部约束");
        request.Options.NodeSpacing.Should().Be(Job().Hints.NodeSpacing, "间距仍然保留");
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void DropLlm_keeps_human_and_auto_constraints()
    {
        var engine = new ScriptedEngine((_, _) => Empty());

        var plan = new LayoutPlan(new LayoutPlanEntry(LayoutFallbackLevel.DropLlm, TimeSpan.FromMilliseconds(300)));

        _ = new LayoutCoordinator(engine, plan).Compute(Job(llmConstraint: true), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var groups = engine.Requests.Single().Options.SameRankGroups!;

        // 三条约束：人定的、模型提的、自动的。丢掉模型那条，其余两条留下。
        groups.Should().HaveCount(2);
        groups.Should().NotContain(g => g.Contains("llm-node"));
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void PureAuto_uses_the_engine_default_spacing()
    {
        var engine = new ScriptedEngine((_, _) => Empty());

        var plan = new LayoutPlan(new LayoutPlanEntry(LayoutFallbackLevel.PureAuto, TimeSpan.FromMilliseconds(300)));

        _ = new LayoutCoordinator(engine, plan).Compute(Job(), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var options = engine.Requests.Single().Options;
        var defaults = new LayoutOptions();

        options.NodeSpacing.Should().Be(defaults.NodeSpacing);
        options.LayerSpacing.Should().Be(defaults.LayerSpacing);
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void Order_constraints_reach_the_engine_as_node_identifiers()
    {
        // 层内次序在 IR 里存的是**出边标识**——平行边只能靠边标识区分。求解器比的却是节点坐标。
        // 少这一次转换的表现是"约束看着进了流水线却毫无效果"：求解器在节点里找不到那几个标识，
        // 一声不吭地跳过，而调用方拿到的是"我设了但没生效"。
        var engine = new ScriptedEngine((_, _) => Empty());

        var plan = new LayoutPlan(new LayoutPlanEntry(LayoutFallbackLevel.Full, TimeSpan.FromMilliseconds(300)));

        _ = new LayoutCoordinator(engine, plan).Compute(OrderJob(), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var groups = engine.Requests.Single().Options.OrderGroups!;

        groups.Should().HaveCount(1);
        groups[0].Should().Equal("b", "c");
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void Align_constraints_reach_the_engine_unchanged()
    {
        // 对齐存的就是节点标识，不需要转换。它仍然要验：转换那一层一旦顺手也去动它，
        // 结果同样是"约束静默失效"，而两条路径的表现一模一样，查起来分不清是哪一条出的问题。
        var engine = new ScriptedEngine((_, _) => Empty());

        var plan = new LayoutPlan(new LayoutPlanEntry(LayoutFallbackLevel.Full, TimeSpan.FromMilliseconds(300)));

        _ = new LayoutCoordinator(engine, plan).Compute(OrderJob(), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var groups = engine.Requests.Single().Options.AlignGroups!;

        groups.Should().HaveCount(1);
        groups[0].Should().Equal("a", "b");
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void DropAll_drops_order_and_align_along_with_the_rest()
    {
        var engine = new ScriptedEngine((_, _) => Empty());

        var plan = new LayoutPlan(new LayoutPlanEntry(LayoutFallbackLevel.DropAll, TimeSpan.FromMilliseconds(300)));

        _ = new LayoutCoordinator(engine, plan).Compute(OrderJob(), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var options = engine.Requests.Single().Options;

        options.SameRankGroups.Should().BeEmpty();
        options.OrderGroups.Should().BeEmpty();
        options.AlignGroups.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void DropLlm_drops_the_model_constraints_of_every_kind()
    {
        // 四类约束走的是同一段归属方过滤。分开写四遍的话，迟早有一类的归属方判错，
        // 而那种错表现为"某一类约束的降级行为不一样"，极难与其它原因区分开。
        var engine = new ScriptedEngine((_, _) => Empty());

        var plan = new LayoutPlan(new LayoutPlanEntry(LayoutFallbackLevel.DropLlm, TimeSpan.FromMilliseconds(300)));

        _ = new LayoutCoordinator(engine, plan).Compute(OrderJob(withLlm: true), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var options = engine.Requests.Single().Options;

        options.SameRankGroups.Should().HaveCount(2, "人定的与自动推导的同层约束都留下");
        options.OrderGroups.Should().HaveCount(1, "模型提的那条丢掉");
        options.AlignGroups.Should().HaveCount(1);
        options.OrderGroups[0].Should().Equal("b", "c");
    }

    #endregion

    #region 冲突日志

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void Downgrades_are_logged_with_what_was_dropped()
    {
        // 用户看到的是"我设的同层约束没生效"。没有日志的话，
        // 排查时无从知道是约束没进布局，还是进了但被降级丢掉了。
        var log = new RecordingConflictLog();
        var engine = new ScriptedEngine((_, _) => Empty());

        var plan = new LayoutPlan(new LayoutPlanEntry(LayoutFallbackLevel.DropAll, TimeSpan.FromMilliseconds(300)));

        _ = new LayoutCoordinator(engine, plan, log).Compute(Job(), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        log.Entries.Should().ContainSingle();
        log.Entries[0].Level.Should().Be(LayoutFallbackLevel.DropAll);
        log.Entries[0].Dropped.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "LayoutFallback")]
    public void The_top_level_logs_nothing()
    {
        var log = new RecordingConflictLog();
        var engine = new ScriptedEngine((_, _) => Empty());

        _ = new LayoutCoordinator(engine, conflicts: log).Compute(Job(), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        log.Entries.Should().BeEmpty("最高级别什么都没丢，不该产生记录");
    }

    #endregion

    #region 辅助

    private static EngineLayoutResult Empty() => new(
        [],
        [],
        0,
        0,
        new LayoutDiagnostics(0, 0, 0, 0, 0, 0, 0, 0, 0, default, default, default, default, default));

    private static LayoutJob Job(bool llmConstraint = false)
    {
        var constraints = new List<Constraint<SameRankConstraint>>
        {
            new(new SameRankConstraint(["human-a", "human-b"]), ConstraintOwner.Human, DateTimeOffset.UnixEpoch),
            new(new SameRankConstraint(["auto-a", "auto-b"]), ConstraintOwner.Auto, DateTimeOffset.UnixEpoch),
        };

        if (llmConstraint)
        {
            constraints.Add(new Constraint<SameRankConstraint>(
                new SameRankConstraint(["llm-node", "other"]),
                ConstraintOwner.Llm,
                DateTimeOffset.UnixEpoch));
        }

        return new LayoutJob(
            [new LayoutNode("a", 80, 40), new LayoutNode("b", 80, 40)],
            [new LayoutEdge("e1", "a", "b")],
            Direction.TB,
            new LayoutHints { NodeSpacing = 55, LayerSpacing = 99, SameRank = constraints });
    }

    private static LayoutJob PinnedJob()
    {
        var job = Job();

        return job with
        {
            Nodes = [.. job.Nodes.Select(n => n with { Pinned = new LayoutPoint(100, 200) })],
        };
    }

    /// <summary>
    /// 带层内次序与对齐的任务。
    /// </summary>
    /// <remarks>
    /// 次序约束按 IR 的存法给的是**出边标识**（e1、e2），期望落进引擎的是它们指向的节点（b、c）。
    /// 这里刻意不给成节点标识，否则"转换有没有做"这件事就验不出来了。
    /// </remarks>
    /// <param name="withLlm">再加一组模型提出的次序与对齐，用来验降级只丢模型那一份。</param>
    private static LayoutJob OrderJob(bool withLlm = false)
    {
        var job = Job();

        var order = new List<Constraint<OrderConstraint>>
        {
            new(new OrderConstraint("a", ["e1", "e2"]), ConstraintOwner.Human, DateTimeOffset.UnixEpoch),
        };

        var align = new List<Constraint<AlignConstraint>>
        {
            new(new AlignConstraint(["a", "b"]), ConstraintOwner.Human, DateTimeOffset.UnixEpoch),
        };

        if (withLlm)
        {
            order.Add(new Constraint<OrderConstraint>(
                new OrderConstraint("a", ["e2", "e1"]),
                ConstraintOwner.Llm,
                DateTimeOffset.UnixEpoch));
            align.Add(new Constraint<AlignConstraint>(
                new AlignConstraint(["b", "c"]),
                ConstraintOwner.Llm,
                DateTimeOffset.UnixEpoch));
        }

        return job with
        {
            Nodes = [.. job.Nodes, new LayoutNode("c", 80, 40)],
            Edges = [.. job.Edges, new LayoutEdge("e2", "a", "c")],
            Hints = job.Hints with { Order = order, Align = align },
        };
    }

    /// <summary>按调用次数决定行为的假引擎。同时记下每次收到的输入。</summary>
    private sealed class ScriptedEngine(Func<int, LayoutRequest, EngineLayoutResult> behaviour) : ILayoutEngine
    {
        private int _calls;

        public int Calls => _calls;

        public List<LayoutRequest> Requests { get; } = [];

        public EngineLayoutResult Layout(LayoutRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return behaviour(Interlocked.Increment(ref _calls), request);
        }
    }

    private sealed class RecordingConflictLog : ILayoutConflictLog
    {
        public List<(LayoutFallbackLevel Level, IReadOnlyList<string> Dropped)> Entries { get; } = [];

        public void RecordDowngrade(LayoutFallbackLevel level, IReadOnlyList<string> dropped) =>
            Entries.Add((level, dropped));
    }

    #endregion
}
