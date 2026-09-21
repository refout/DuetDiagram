using DuetDiagram.Core.Model;
using DuetDiagram.Core.Sidecar;
using DuetDiagram.Core.Time;
using DuetDiagram.Dsl.Mapping;
using DuetDiagram.Dsl.Parsing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Dsl.Tests;

/// <summary>
/// 五类布局意图落到 IR 的四类约束上。
/// </summary>
/// <remarks>
/// <para>
/// 这五条是 DSL 存在的主要理由——Mermaid 一条都表达不了——所以每一条都要有一条用例，
/// 逐条钉住它落到哪里、带什么归属方。
/// </para>
/// <para>
/// <c>pin</c> 不落 IR（绝对坐标在 <see cref="LayoutHints"/> 里没有位置），
/// 它走 sidecar，用例在 <c>SidecarTests</c>。
/// </para>
/// </remarks>
public sealed class LayoutIntentTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    #region 四类约束逐条

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Same_rank_lands_on_the_same_rank_list()
    {
        var hints = Hints("same-rank pass, fail");

        hints.SameRank.Should().ContainSingle();
        hints.SameRank.Single().Value.Nodes.Should().Equal("pass", "fail");
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Align_lands_on_the_align_list()
    {
        var hints = Hints("align pass, fail");

        hints.Align.Should().ContainSingle();
        hints.Align.Single().Value.Nodes.Should().Equal("pass", "fail");
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Place_lands_on_the_place_list()
    {
        var hints = Hints("place fail right-of pass");

        hints.Place.Should().ContainSingle();
        hints.Place.Single().Value.NodeId.Should().Be("fail");
        hints.Place.Single().Value.RelativeTo.Should().Be("pass");
        hints.Place.Single().Value.Relation.Should().Be(PlaceRelation.RightOf);
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Order_lands_on_the_order_list_as_edge_ids()
    {
        // DSL 写的是节点名（`order check: pass, fail`），而 IR 存的是出边标识。
        // 两处对同一个概念的定义不一致，转换在映射层做。
        var hints = Hints("""
            check "判定"
            check -> pass "是"
            check -> fail "否"
            order check: fail, pass
            """);

        hints.Order.Should().ContainSingle();

        var value = hints.Order.Single().Value;

        value.NodeId.Should().Be("check");

        // 原文说 fail 在前，所以 e2（check → fail）排在 e1（check → pass）前面。
        value.Order.Should().Equal("e2", "e1");
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Order_keeps_every_parallel_edge_to_the_same_target()
    {
        // 同一个终点可以有多条边。收一条、丢几条会让约束的表达与文本不符，而且不报错。
        var hints = Hints("""
            check "判定"
            check -> pass "甲"
            check -> pass "乙"
            check -> fail "否"
            order check: pass, fail
            """);

        hints.Order.Single().Value.Order.Should().Equal("e1", "e2", "e3");
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Pin_produces_no_ir_constraint()
    {
        // LayoutHints 的四个列表都是相对约束，没有绝对坐标。
        // 布局输入本来就从 sidecar 收固定坐标，所以 pin 走那条路。
        var hints = Hints("pin fail at 640, 320");

        hints.SameRank.Should().BeEmpty();
        hints.Order.Should().BeEmpty();
        hints.Align.Should().BeEmpty();
        hints.Place.Should().BeEmpty();
    }

    #endregion

    #region 归属方与时间

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Constraints_carry_the_declared_owner_and_time()
    {
        var hints = Hints(
            """
            a "甲"
            b "乙"
            a -> b
            same-rank a, b
            order a: b
            align a, b
            place a right-of b
            """,
            new MappingOptions { DocumentId = "dsl", Owner = ConstraintOwner.Human, Time = new ManualTimeProvider(At) });

        hints.SameRank.Single().Owner.Should().Be(ConstraintOwner.Human);
        hints.Order.Single().Owner.Should().Be(ConstraintOwner.Human);
        hints.Align.Single().Owner.Should().Be(ConstraintOwner.Human);
        hints.Place.Single().Owner.Should().Be(ConstraintOwner.Human);
        hints.SameRank.Single().CreatedAt.Should().Be(At);
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void The_default_owner_is_the_model()
    {
        // 归属方由调用方声明，不按指令类型推断：人手工写的 same-rank 也会被标成 Llm
        // 那种"省事"的推断，代价是人工意图被下一次自动重排冲掉。
        Hints("same-rank a, b").SameRank.Single().Owner.Should().Be(ConstraintOwner.Llm);
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Mapping_twice_with_the_same_clock_gives_the_same_product()
    {
        var options = new MappingOptions { DocumentId = "dsl", Time = new ManualTimeProvider(At) };
        const string Source = "a -> b\nsame-rank a, b\npin a at 10, 20";

        var first = DslMapper.Map(DslParser.Parse(Source), options).Document;
        var second = DslMapper.Map(DslParser.Parse(Source), options).Document;

        first.Layout.Should().Be(second.Layout);
        first.StructuralHash.Should().Be(second.StructuralHash);
    }

    #endregion

    #region 引用不落地

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void An_order_entry_without_a_matching_edge_is_reported()
    {
        // 约束少了一项之后图还是画得出来，只是层内次序不是你写的那个——
        // 而那种偏差在图上完全看不出来，所以必须留记录。
        var result = Map("""
            check "判定"
            check -> pass "是"
            order check: pass, missing
            """);

        var hints = result.Document.Layout;

        hints.Order.Single().Value.Order.Should().Equal("e1");
        result.Report.UnresolvedIntents.Should().ContainSingle();

        var unresolved = result.Report.UnresolvedIntents.Single();

        unresolved.Reference.Should().Be("missing");
        unresolved.Intent.Should().Contain("order check");
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void An_order_that_resolves_to_nothing_produces_no_constraint()
    {
        // 一条空次序的约束没有意义，而下游分不清"没有约束"与"约束是空的"。
        var result = Map("""
            check "判定"
            order check: missing
            """);

        result.Document.Layout.Order.Should().BeEmpty();
        result.Report.UnresolvedIntents.Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Order_does_not_create_nodes_for_its_entries()
    {
        // order 的次序项指的是出边的终点，不是新节点。补出来只会多一个谁也不连的方块。
        var result = Map("""
            check "判定"
            check -> pass "是"
            order check: pass, missing
            """);

        result.Document.Nodes.Select(n => n.Id).Should().Equal("check", "pass");
    }

    #endregion

    #region 补节点

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void An_intent_reference_creates_the_node_it_names()
    {
        // 规格的设计原则是"未声明即隐式创建：节点在首次被引用时自动出现"，
        // 而 `same-rank a, b` 里的 b 就是一次引用。不补的话那条约束会指向一个
        // 不存在的节点，而校验器目前不检查布局约束的节点引用，它会一路安静地传下去。
        var result = Map("a \"甲\"\nsame-rank a, b");

        result.Document.Nodes.Select(n => n.Id).Should().Equal("a", "b");
        result.Report.CreatedNodes.Should().ContainSingle()
            .Which.Id.Should().Be("b");
        result.Report.CreatedNodes.Single().Reason.Should().Contain("same-rank");
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void A_pinned_node_that_was_never_declared_is_created()
    {
        var result = Map("pin lonely at 10, 20");

        result.Document.Nodes.Select(n => n.Id).Should().Equal("lonely");
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Edges_are_referenced_before_intents_when_deciding_node_order()
    {
        // 两类引用都没有行号可比，所以定死一个顺序：边在前、意图在后。
        // 层内次序会受影响，所以这是规则不是实现细节。
        var result = Map("""
            a "甲"
            a -> x
            same-rank a, y
            """);

        result.Document.Nodes.Select(n => n.Id).Should().Equal("a", "x", "y");
    }

    #endregion

    #region 产物

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void The_product_passes_the_validator()
    {
        var result = Map("""
            start "开始" shape=stadium
            check "校验" shape=diamond
            start -> check
            check -> ok "是"
            check -> bad "否"
            same-rank ok, bad
            order check: ok, bad
            align ok, bad
            place bad right-of ok
            pin check at 420, 200
            node-spacing 30
            """);

        DiagramValidator.Validate(result.Document).Should().BeEmpty();
        result.Document.Layout.SameRank.Should().HaveCount(1);
        result.Document.Layout.Order.Should().HaveCount(1);
        result.Document.Layout.Align.Should().HaveCount(1);
        result.Document.Layout.Place.Should().HaveCount(1);
        result.Document.Layout.NodeSpacing.Should().Be(30);
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void No_intent_gives_the_defaults_with_empty_lists()
    {
        var hints = Hints("a -> b");

        hints.SameRank.Should().BeEmpty();
        hints.Order.Should().BeEmpty();
        hints.Align.Should().BeEmpty();
        hints.Place.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Every_corpus_constraint_refers_to_something_that_exists()
    {
        // 校验器目前不检查布局约束的节点引用，
        // 所以约束指向不存在的节点或边时不会有任何报错，只会一路安静地传下去。
        // 这条用例替校验器把这件事守住：语料里的每条约束都必须指向真实存在的元素。
        var broken = new List<string>();

        foreach (var entry in Corpus.Load())
        {
            var result = DslMapper.Map(DslParser.Parse(entry.Content), new MappingOptions { DocumentId = entry.PromptId });
            var document = result.Document;

            var nodes = document.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            var outgoing = document.Edges.ToLookup(e => e.From, e => e.Id, StringComparer.Ordinal);

            void Check(string what, IEnumerable<string> ids)
            {
                foreach (var id in ids.Where(id => !nodes.Contains(id)))
                {
                    broken.Add($"{entry.PromptId}: {what} 指向不存在的节点 {id}");
                }
            }

            foreach (var constraint in document.Layout.SameRank)
            {
                Check("same-rank", constraint.Value.Nodes);
            }

            foreach (var constraint in document.Layout.Align)
            {
                Check("align", constraint.Value.Nodes);
            }

            foreach (var constraint in document.Layout.Place)
            {
                Check("place", [constraint.Value.NodeId, constraint.Value.RelativeTo]);
            }

            foreach (var constraint in document.Layout.Order)
            {
                Check("order 的主语", [constraint.Value.NodeId]);

                // 次序项必须是该节点自己的出边标识。落错东西的话层内次序会按别的边排，
                // 而图上完全看不出来。
                var mine = outgoing[constraint.Value.NodeId].ToHashSet(StringComparer.Ordinal);

                foreach (var id in constraint.Value.Order.Where(id => !mine.Contains(id)))
                {
                    broken.Add($"{entry.PromptId}: order 的次序项 {id} 不是节点 {constraint.Value.NodeId} 的出边");
                }
            }
        }

        broken.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Every_corpus_intent_is_either_landed_or_reported()
    {
        // 三条路必须覆盖全部意图：落进 IR、是 pin（走 sidecar）、进了报告。
        // 都不占的说明有意图被静默吃掉了——那是最难发现的一种。
        var silent = new List<string>();

        foreach (var entry in Corpus.Load())
        {
            var parsed = DslParser.Parse(entry.Content);
            var result = DslMapper.Map(parsed, new MappingOptions { DocumentId = entry.PromptId });

            var nonPin = parsed.Layout.Count(i => i.Kind != DslLayoutIntentKind.Pin);

            var landed = result.Document.Layout.SameRank.Count
                + result.Document.Layout.Order.Count
                + result.Document.Layout.Align.Count
                + result.Document.Layout.Place.Count;

            if (landed + result.Report.UnresolvedIntents.Count < nonPin)
            {
                silent.Add($"{entry.Arm}/{entry.PromptId}: 非 pin 意图 {nonPin} 条，只交代了 {landed + result.Report.UnresolvedIntents.Count} 条");
            }
        }

        silent.Should().BeEmpty();
    }

    #endregion

    #region pin 与 sidecar

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Pin_lands_in_the_sidecar()
    {
        var result = Map("""
            a "甲"
            b "乙"
            pin a at 640, 320
            pin b at -10, 12.5
            """);

        result.Sidecar.PinnedNodes.Should().HaveCount(2);
        result.Sidecar.PinnedNodes["a"].Should().Be(new Anchor(640, 320));
        result.Sidecar.PinnedNodes["b"].Should().Be(new Anchor(-10, 12.5));
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void The_sidecar_carries_the_document_id()
    {
        // 固定位置是按文档归属的：换了文档标识，那份记录就该被当成"文件放错了地方"
        // 而不是"缓存过期"。两个处置完全不同。
        var result = DslMapper.Map(
            DslParser.Parse("pin a at 1, 2"),
            new MappingOptions { DocumentId = "订单流程" });

        result.Sidecar.DocumentId.Should().Be("订单流程");
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void No_pin_gives_an_empty_but_identified_sidecar()
    {
        var result = Map("a -> b");

        result.Sidecar.PinnedNodes.Should().BeEmpty();
        result.Sidecar.DocumentId.Should().Be("dsl");
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void The_last_pin_of_a_node_wins()
    {
        // 逐行读下来的直觉就是后面的覆盖前面的，而且这里不存在歧义——
        // 不像撞名那样两边的身份分不清。
        var result = Map("pin a at 1, 2\npin a at 3, 4");

        result.Sidecar.PinnedNodes["a"].Should().Be(new Anchor(3, 4));
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Merging_does_not_overwrite_an_existing_pin()
    {
        // 人工产物不该被文本里的值覆盖：模型重生成一次文本是常事，
        // 而用户每次拖动都会白做。这是决策 C 的那条规则。
        var existing = new UserSidecar
        {
            DocumentId = "dsl",
            PinnedNodes = new Dictionary<string, Anchor>(StringComparer.Ordinal)
            {
                ["a"] = new Anchor(100, 100),
            },
        };

        var fromText = Map("pin a at 1, 2\npin b at 3, 4").Sidecar;

        var merged = SidecarMerge.Fill(existing, fromText);

        merged.PinnedNodes["a"].Should().Be(new Anchor(100, 100), "人工拖过的位置不该被文本盖掉");
        merged.PinnedNodes["b"].Should().Be(new Anchor(3, 4), "文本补上 sidecar 里没有的那部分");
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Merging_keeps_the_existing_document_id()
    {
        var existing = new UserSidecar { DocumentId = "原来的" };
        var fromText = Map("pin a at 1, 2").Sidecar;

        SidecarMerge.Fill(existing, fromText).DocumentId.Should().Be("原来的");
    }

    [Fact]
    [Trait("Category", "DslLayoutIntent")]
    public void Merging_into_a_missing_sidecar_takes_the_text_id()
    {
        var merged = SidecarMerge.Fill(new UserSidecar(), Map("pin a at 1, 2").Sidecar);

        merged.DocumentId.Should().Be("dsl");
        merged.PinnedNodes["a"].Should().Be(new Anchor(1, 2));
    }

    private static MappingResult Map(string source) =>
        DslMapper.Map(DslParser.Parse(source), new MappingOptions { DocumentId = "dsl" });

    private static LayoutHints Hints(string source) => Map(source).Document.Layout;

    private static LayoutHints Hints(string source, MappingOptions options) =>
        DslMapper.Map(DslParser.Parse(source), options).Document.Layout;

    #endregion
}
