using System.Text.Json.Nodes;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 谓词求值器的自检。
/// </summary>
/// <remarks>
/// <para>
/// 求值器里有三处不能靠读代码确信的地方：多个匹配器要指派到**互不相同**的节点上（带回溯）、
/// 「按顺序串联」要区分相邻与可达、分组归属要逐层往上算。这三处写错了不会崩，
/// 只会让判定悄悄偏向某一种画法——而结果看起来和正确的一模一样。
/// </para>
/// <para>
/// 所以这里用手写的结构清单把每条规则钉一遍。清单是直接在代码里搭的，不走解析器：
/// 这条自检要验的是求值，不是解析，混进解析失败会看不出是哪一个坏了。
/// </para>
/// </remarks>
internal static class Verify
{
    public static int Run(string promptsPath)
    {
        var failures = 0;

        Console.WriteLine("谓词求值器自检");
        Console.WriteLine();

        foreach (var fixture in Fixtures())
        {
            if (Check(fixture, ref failures))
            {
                Console.WriteLine($"  通过  {fixture.Name}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("写坏的谓词必须当场抛异常");

        foreach (var (name, json) in Malformed())
        {
            try
            {
                _ = Predicate.Parse(JsonNode.Parse(json), name);
                Console.WriteLine($"  失败  {name}：没有抛异常，它会静默判错");
                failures++;
            }
            catch (InvalidDataException)
            {
                Console.WriteLine($"  通过  {name}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"真实提示词文件里的谓词");

        try
        {
            var prompts = PromptSet.Load(promptsPath);
            var checks = prompts.SelectMany(prompt => prompt.Checks).ToArray();
            var machine = checks.Count(check => check.MachineCheckable);

            Console.WriteLine($"  通过  {prompts.Count} 条提示词的 {checks.Length} 个检查项里，{machine} 个谓词全部读得进来");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  失败  {promptsPath}：{ex.GetType().Name}：{ex.Message}");
            failures++;
        }

        Console.WriteLine();

        if (failures == 0)
        {
            Console.WriteLine("全部通过。");
            return 0;
        }

        Console.WriteLine($"{failures} 项不通过。");
        return 1;
    }

    private static bool Check(Fixture fixture, ref int failures)
    {
        Verdict verdict;

        try
        {
            verdict = Predicate.Parse(JsonNode.Parse(fixture.Predicate), fixture.Name)
                .Evaluate(new StructureIndex(fixture.Listing));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  失败  {fixture.Name}：求值抛了 {ex.GetType().Name}：{ex.Message}");
            failures++;
            return false;
        }

        if (verdict.Pass == fixture.Expected)
        {
            return true;
        }

        Console.WriteLine(
            $"  失败  {fixture.Name}：期望{(fixture.Expected ? "通过" : "不通过")}，"
            + $"实际{(verdict.Pass ? "通过" : "不通过")}（{verdict.Reason ?? "没有原因"}）");
        failures++;
        return false;
    }

    #region 搭清单

    private static StructureListing Listing(
        ListedNode[] nodes,
        ListedGroup[]? groups = null,
        ListedEdge[]? edges = null) =>
        new(ParseOutcome.Clean, "TB", nodes, groups ?? [], edges ?? [], [], []);

    private static ListedNode N(string id, string? label = null, NodeShape shape = NodeShape.Rect) =>
        new(id, label ?? id, shape);

    private static ListedGroup G(string id, string label, params string[] members) =>
        new(id, label, members, null);

    private static ListedEdge E(string from, string to, string? label = null) => new(from, to, label);

    #endregion

    #region 用例

    private sealed record Fixture(string Name, StructureListing Listing, string Predicate, bool Expected);

    /// <summary>一条边都没有、只有一个节点的清单。给不需要边的用例当底。</summary>
    private static StructureListing Plain(params ListedNode[] nodes) => Listing(nodes);

    private static Fixture[] Fixtures() =>
    [
        new("标签按子串比：短词对上长标签",
            Plain(N("n1", "连接邮件服务器")),
            """{"kind":"node","each":[{"label":["连接"]}]}""",
            true),

        new("标签按子串比：长词对不上短标签",
            Plain(N("n1", "连接邮件服务器")),
            """{"kind":"node","each":[{"label":["连接服务器"]}]}""",
            false),

        new("形状要对得上",
            Plain(N("n1", "校验"), N("n2", "校验？", NodeShape.Diamond)),
            """{"kind":"node","each":[{"label":["校验"],"shape":"Diamond"}]}""",
            true),

        new("两个匹配器不能落到同一个节点上",
            Plain(N("n1", "合并")),
            """{"kind":"node","each":[{"label":["合并"]},{"label":["合并"]}]}""",
            false),

        new("指派要会回溯：先来先占会把后面的匹配器挤掉",
            Plain(N("n1", "甲"), N("n2", "乙")),
            """{"kind":"node","each":[{"label":["甲","乙"]},{"label":["甲"]}]}""",
            true),

        new("分组归属按包含算：节点在嵌套分组里也算在外层分组里",
            Listing(
                [new ListedNode("n1", "下单", NodeShape.Rect)],
                [new ListedGroup("g1", "业务层", ["g2"], null),
                 new ListedGroup("g2", "订单域", ["n1"], "g1")]),
            """{"kind":"node","each":[{"label":["下单"],"inGroup":["业务层"]}]}""",
            true),

        new("分组归属按包含算：外层分组不算在里层",
            Listing(
                [new ListedNode("n1", "下单", NodeShape.Rect)],
                [new ListedGroup("g1", "业务层", ["g2"], null),
                 new ListedGroup("g2", "订单域", ["n1"], "g1")]),
            """{"kind":"node","each":[{"label":["下单"],"inGroup":["订单域"]}]}""",
            true),

        new("标签里必须全部出现的词，少一个就不算",
            Plain(N("n1", "拉取渠道账单与本地账单")),
            """{"kind":"node","each":[{"label":["账单"],"labelAll":["渠道","本地"]}]}""",
            true),

        new("标签里必须全部出现的词，只覆盖一半不算",
            Plain(N("n1", "拉取渠道账单")),
            """{"kind":"node","each":[{"label":["账单"],"labelAll":["渠道","本地"]}]}""",
            false),

        new("边的端点落在分组上",
            Listing(
                [new ListedNode("n1", "清洗", NodeShape.Rect)],
                [new ListedGroup("g1", "原始层", [], null), new ListedGroup("g2", "明细层", [], null)],
                [new ListedEdge("g1", "g2", null)]),
            """{"kind":"edge","from":{"inGroup":["原始层"]},"to":{"inGroup":["明细层"]}}""",
            true),

        new("边的标签按同义词命中",
            Listing([N("n1"), N("n2")], null, [E("n1", "n2", "失败或超时")]),
            """{"kind":"edge","from":{"label":["n1"]},"to":{"label":["n2"]},"label":["超时"]}""",
            true),

        new("边的标签对不上就是没有这条边",
            Listing([N("n1"), N("n2")], null, [E("n1", "n2", "成功")]),
            """{"kind":"edge","from":{"label":["n1"]},"to":{"label":["n2"]},"label":["超时"]}""",
            false),

        new("chain 要求相邻",
            Listing([N("n1"), N("n2"), N("n3")], null, [E("n1", "n2"), E("n1", "n3")]),
            """{"kind":"chain","steps":[{"label":["n1"]},{"label":["n2"]},{"label":["n3"]}]}""",
            false),

        new("order 对断开的链不通过",
            Listing([N("n1"), N("n2"), N("n3")], null, [E("n1", "n2"), E("n1", "n3")]),
            """{"kind":"order","steps":[{"label":["n1"]},{"label":["n2"]},{"label":["n3"]}]}""",
            false),

        new("order 允许中间隔着别的节点",
            Listing([N("n1"), N("n2"), N("n3")], null, [E("n1", "n3"), E("n3", "n2")]),
            """{"kind":"order","steps":[{"label":["n1"]},{"label":["n2"]}]}""",
            true),

        new("reach 能顺着环走回来",
            Listing([N("n1"), N("n2")], null, [E("n1", "n2"), E("n2", "n1")]),
            """{"kind":"reach","from":{"label":["n1"]},"to":{"label":["n1"]}}""",
            true),

        new("reach 至少要走一条边，原地不算",
            Listing([N("n1")], null, []),
            """{"kind":"reach","from":{"label":["n1"]},"to":{"label":["n1"]}}""",
            false),

        new("terminal 认叶子节点",
            Listing([N("n1"), N("n2")], null, [E("n1", "n2")]),
            """{"kind":"terminal","node":{"label":["n2"]}}""",
            true),

        new("terminal 不认还有出边的节点",
            Listing([N("n1"), N("n2")], null, [E("n1", "n2")]),
            """{"kind":"terminal","node":{"label":["n1"]}}""",
            false),

        new("noShape 在没有任何该形状节点时通过",
            Plain(N("n1", "开始"), N("n2", "结束")),
            """{"kind":"noShape","shape":"Diamond"}""",
            true),

        new("noShape 有该形状节点就不通过",
            Plain(N("n1", "设置成功？", NodeShape.Diamond)),
            """{"kind":"noShape","shape":"Diamond"}""",
            false),

        new("count 的下界",
            Plain(N("n1", "副本一"), N("n2", "副本二"), N("n3", "副本三")),
            """{"kind":"count","match":{"label":["副本"]},"atLeast":3}""",
            true),

        new("count 的等值",
            Plain(N("n1", "副本一"), N("n2", "副本二"), N("n3", "副本三")),
            """{"kind":"count","match":{"label":["副本"]},"equals":2}""",
            false),

        new("fanout 数的是互不相同的出边目标",
            Listing([N("n1"), N("n2"), N("n3")], null, [E("n1", "n2"), E("n1", "n2"), E("n1", "n3")]),
            """{"kind":"fanout","node":{"label":["n1"]},"atLeast":3}""",
            false),

        new("fanout 达到下界就通过",
            Listing([N("n1"), N("n2"), N("n3")], null, [E("n1", "n2"), E("n1", "n3")]),
            """{"kind":"fanout","node":{"label":["n1"]},"atLeast":2}""",
            true),

        new("any 里有一条成立就算通过",
            Plain(N("n1", "甲")),
            """{"kind":"any","of":[{"kind":"node","each":[{"label":["乙"]}]},{"kind":"node","each":[{"label":["甲"]}]}]}""",
            true),

        new("all 里有一条不成立就不通过",
            Plain(N("n1", "甲")),
            """{"kind":"all","of":[{"kind":"node","each":[{"label":["甲"]}]},{"kind":"node","each":[{"label":["乙"]}]}]}""",
            false),

        new("分组里的成员可以是嵌套分组",
            Listing(
                [],
                [new ListedGroup("g1", "第一波", ["g2"], null),
                 new ListedGroup("g2", "用户域", [], "g1")]),
            """{"kind":"groupMembers","group":{"label":["第一波"]},"memberGroups":[{"label":["用户"]}]}""",
            true),

        new("分组要互不相同",
            Listing([], [G("g1", "甲")]),
            """{"kind":"group","each":[{"label":["甲"]},{"label":["甲"]}]}""",
            false),
    ];

    /// <summary>
    /// 写坏的谓词。每一条都必须当场抛异常——静默判错的谓词比崩掉难查得多，
    /// 因为它会一直按一个错的口径给分，而报告上看不出任何异常。
    /// </summary>
    private static (string Name, string Json)[] Malformed() =>
    [
        ("不是对象", "[]"),
        ("认不出的种类", """{"kind":"大概是这样"}"""),
        ("node 缺 each", """{"kind":"node"}"""),
        ("each 是空数组", """{"kind":"node","each":[]}"""),
        ("edge 缺 to", """{"kind":"edge","from":{"label":["甲"]}}"""),
        ("shape 认不出", """{"kind":"node","each":[{"label":["甲"],"shape":"三角形"}]}"""),
        ("count 没给范围", """{"kind":"count","match":{"label":["甲"]}}"""),
        ("label 里混了非字符串", """{"kind":"node","each":[{"label":[1]}]}"""),
        ("of 是空数组", """{"kind":"all","of":[]}"""),
    ];

    #endregion
}
