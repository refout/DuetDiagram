using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Sidecar;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// Sidecar 的路径约定、读写与完整性校验。
/// </summary>
/// <remarks>
/// 每个用例用一个独立的临时目录，避免相互干扰。
/// 路径相关的用例尤其需要这样：它们验的正是"文件落在哪里"。
/// </remarks>
public sealed class SidecarTests
{
    // ---- 路径约定 ----

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Three_sidecars_sit_next_to_the_document_with_the_same_stem()
    {
        var document = Path.Combine("some", "dir", "orders.dgm");

        SidecarPaths.Dsl(document).Should().Be(Path.Combine("some", "dir", "orders.dsl"));
        SidecarPaths.Layout(document).Should().Be(Path.Combine("some", "dir", "orders.layout.json"));
        SidecarPaths.User(document).Should().Be(Path.Combine("some", "dir", "orders.user.json"));
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Sidecars_ignore_the_document_extension()
    {
        // 文档扩展名是什么不该影响附属文件的名字。
        SidecarPaths.Layout(Path.Combine("some", "a.dgm"))
            .Should().Be(SidecarPaths.Layout(Path.Combine("some", "a.json")));

        SidecarPaths.Layout("orders").Should().Be("orders.layout.json");
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Backup_name_carries_a_utc_timestamp()
    {
        var at = new DateTimeOffset(2026, 9, 20, 14, 30, 5, TimeSpan.FromHours(8));

        var path = SidecarPaths.Backup("/tmp/orders.user.json", at);

        // 用世界时间而不是本地时间：夏令时切换那天本地时间会有两个时刻对应同一个名字，
        // 先写的那份会被覆盖——而备份恰恰最不该发生这种事。
        path.Should().Be("/tmp/orders.user.20260920-063005.bak");
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Backup_time_can_be_read_back()
    {
        var at = new DateTimeOffset(2026, 9, 20, 14, 30, 5, TimeSpan.Zero);
        var path = SidecarPaths.Backup("/tmp/orders.user.json", at);

        SidecarPaths.ParseBackupTime(path).Should().Be(at);
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void A_non_backup_name_has_no_time()
    {
        SidecarPaths.ParseBackupTime("/tmp/orders.user.json").Should().BeNull();
        SidecarPaths.ParseBackupTime("/tmp/orders.随便.bak").Should().BeNull();
    }

    // ---- 布局缓存 ----

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Layout_round_trips()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        var original = new LayoutSidecar
        {
            StructuralHash = document.StructuralHash,
            DocumentId = document.Id,
            Version = 7,
            Nodes = new Dictionary<string, NodePlacement>(StringComparer.Ordinal)
            {
                ["a"] = new NodePlacement(10, 20, 80, 40),
            },
            Edges = [new EdgePath("e1", [new Anchor(0, 0), new Anchor(5, 5)])],
            Width = 200,
            Height = 100,
        };

        SidecarStore.SaveLayout(path, original);

        var loaded = SidecarStore.LoadLayout(path, document);

        loaded.Status.Should().Be(SidecarStatus.Loaded);
        loaded.Value.Should().Be(original);
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Missing_layout_is_not_an_error()
    {
        using var temp = new TempDirectory();

        var loaded = SidecarStore.LoadLayout(temp.File("doc.dgm"), IrFixtures.Base());

        // 首次打开文档就是这样，属于正常情况。
        loaded.Status.Should().Be(SidecarStatus.Missing);
        loaded.Value.Should().BeNull();
        loaded.IsUsable.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void A_changed_document_makes_the_layout_cache_stale()
    {
        using var temp = new TempDirectory();
        var path = temp.File("doc.dgm");

        var before = IrFixtures.Base();
        SidecarStore.SaveLayout(path, CacheFor(before));

        // 改动要让**结构**哈希变化才算得上缓存过期。改标签不行——
        // 标签属于外观，结构哈希不变，坐标仍然有效。
        var after = IrFixtures.WithNode(
            IrFixtures.Base(),
            new NodeDef { Id = "a", Parent = "新分组" });

        after.StructuralHash.Should().NotBe(before.StructuralHash, "这个用例要的正是结构变化");

        var loaded = SidecarStore.LoadLayout(path, after);

        // 缓存过期不是错误——用户没有做错任何事，处置是静默重算而不是弹窗。
        loaded.Status.Should().Be(SidecarStatus.Stale);
        loaded.Value.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void A_corrupt_layout_file_is_unusable_rather_than_fatal()
    {
        using var temp = new TempDirectory();
        var path = temp.File("doc.dgm");

        File.WriteAllText(SidecarPaths.Layout(path), "{ 这不是合法的 JSON");

        var loaded = SidecarStore.LoadLayout(path, IrFixtures.Base());

        // 一个附属文件格式坏掉不该让整个文档打不开。
        loaded.Status.Should().Be(SidecarStatus.Unusable);
        loaded.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void A_layout_from_another_document_is_unusable_not_stale()
    {
        using var temp = new TempDirectory();
        var path = temp.File("doc.dgm");

        // 换一个文档标识。只换内容不行——那份内容仍属于同一份文档，
        // 被当成"缓存过期"才是对的。
        var other = IrFixtures.Construct(
            "另一个文档",
            0,
            null, null,
            [new NodeDef { Id = "a" }],
            null, null, null, null, null, null, null, null, null);

        other.Id.Should().NotBe(IrFixtures.Base().Id);
        SidecarStore.SaveLayout(path, CacheFor(other));

        var loaded = SidecarStore.LoadLayout(path, IrFixtures.Base());

        // 与"缓存过期"要分开：过期是正常的，文档对不上说明文件被错放了。
        loaded.Status.Should().Be(SidecarStatus.Unusable);
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Layout_orphans_are_reported()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        SidecarStore.SaveLayout(path, CacheFor(document) with
        {
            Nodes = new Dictionary<string, NodePlacement>(StringComparer.Ordinal)
            {
                ["a"] = new NodePlacement(0, 0, 10, 10),
                ["早就删掉的节点"] = new NodePlacement(0, 0, 10, 10),
            },
        });

        var loaded = SidecarStore.LoadLayout(path, document);

        loaded.Orphans.Should().Equal("早就删掉的节点");
    }

    // ---- 人工产物 ----

    [Fact]
    [Trait("Category", "Sidecar")]
    public void User_sidecar_round_trips()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        var original = new UserSidecar
        {
            DocumentId = document.Id,
            PinnedNodes = new Dictionary<string, Anchor>(StringComparer.Ordinal)
            {
                ["a"] = new Anchor(100, 200),
            },
            PinnedEdges = new Dictionary<string, IReadOnlyList<Anchor>>(StringComparer.Ordinal)
            {
                ["e1"] = [new Anchor(1, 2), new Anchor(3, 4)],
            },
            CustomPorts = new Dictionary<string, IReadOnlyList<PortDef>>(StringComparer.Ordinal)
            {
                ["a"] = [new PortDef { Name = "out", Side = PortSide.Bottom, IsCustom = true }],
            },
        };

        SidecarStore.SaveUser(path, original);

        var loaded = SidecarStore.LoadUser(path, document);

        loaded.Status.Should().Be(SidecarStatus.Loaded);
        loaded.Value!.PinnedNodes["a"].Should().Be(new Anchor(100, 200));
        loaded.Value.PinnedEdges["e1"].Should().HaveCount(2);
        loaded.Value.CustomPorts["a"].Single().Name.Should().Be("out");
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Missing_user_sidecar_is_not_an_error()
    {
        using var temp = new TempDirectory();

        SidecarStore.LoadUser(temp.File("doc.dgm"), IrFixtures.Base())
            .Status.Should().Be(SidecarStatus.Missing);
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void User_orphans_cover_nodes_edges_and_ports()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Populated();
        var path = temp.File("doc.dgm");

        SidecarStore.SaveUser(path, new UserSidecar
        {
            DocumentId = document.Id,
            PinnedNodes = new Dictionary<string, Anchor>(StringComparer.Ordinal)
            {
                ["a"] = new Anchor(0, 0),
                ["已删除的节点"] = new Anchor(0, 0),
            },
            PinnedEdges = new Dictionary<string, IReadOnlyList<Anchor>>(StringComparer.Ordinal)
            {
                ["e1"] = [new Anchor(0, 0)],
                ["已删除的边"] = [new Anchor(0, 0)],
            },
            CustomPorts = new Dictionary<string, IReadOnlyList<PortDef>>(StringComparer.Ordinal)
            {
                ["也删掉了"] = [new PortDef { Name = "p" }],
            },
        });

        var loaded = SidecarStore.LoadUser(path, document);

        loaded.Status.Should().Be(SidecarStatus.Loaded);
        loaded.Orphans.Should().BeEquivalentTo("已删除的节点", "已删除的边", "也删掉了");
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Orphans_are_reported_rather_than_dropped()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        SidecarStore.SaveUser(path, new UserSidecar
        {
            DocumentId = document.Id,
            PinnedNodes = new Dictionary<string, Anchor>(StringComparer.Ordinal) { ["临时删掉的"] = new Anchor(9, 9) },
        });

        var loaded = SidecarStore.LoadUser(path, document);

        // 条目指向的节点可能只是被临时删掉了，撤销回来之后这条固定位置仍然有意义。
        // 直接丢掉等于让一次误删顺手毁掉用户的手工调整。
        loaded.Value!.PinnedNodes.Should().ContainKey("临时删掉的");
        loaded.Orphans.Should().Contain("临时删掉的");
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Pruning_drops_only_the_orphans()
    {
        var user = new UserSidecar
        {
            PinnedNodes = new Dictionary<string, Anchor>(StringComparer.Ordinal)
            {
                ["留下的"] = new Anchor(1, 1),
                ["丢掉的"] = new Anchor(2, 2),
            },
        };

        var pruned = SidecarStore.PruneOrphans(user, ["丢掉的"]);

        pruned.PinnedNodes.Should().ContainKey("留下的");
        pruned.PinnedNodes.Should().NotContainKey("丢掉的");
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Pruning_nothing_returns_the_same_instance()
    {
        var user = new UserSidecar();

        // 没有孤儿时不该白造一份副本。
        SidecarStore.PruneOrphans(user, []).Should().BeSameAs(user);
    }

    // ---- 写入的原子性 ----

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Saving_leaves_no_temporary_file_behind()
    {
        using var temp = new TempDirectory();
        var path = temp.File("doc.dgm");

        SidecarStore.SaveUser(path, new UserSidecar { DocumentId = "ir-test" });

        // 先写临时文件再改名：直接覆写时写到一半断电，原文件会变成半截内容，
        // 而人工产物正是那份丢不起的。
        Directory.GetFiles(temp.Path).Should().NotContain(f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Saving_creates_the_directory()
    {
        using var temp = new TempDirectory();
        var nested = System.IO.Path.Combine(temp.Path, "深层", "目录", "doc.dgm");

        SidecarStore.SaveUser(nested, new UserSidecar { DocumentId = "ir-test" });

        File.Exists(SidecarPaths.User(nested)).Should().BeTrue();
    }

    // ---- 标识的大小写 ----

    [Fact]
    [Trait("Category", "Sidecar")]
    public void Identifiers_are_case_sensitive()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Build(nodes: [new NodeDef { Id = "Node1" }]);
        var path = temp.File("doc.dgm");

        SidecarStore.SaveUser(path, new UserSidecar
        {
            DocumentId = document.Id,
            PinnedNodes = new Dictionary<string, Anchor>(StringComparer.Ordinal)
            {
                ["node1"] = new Anchor(0, 0),
            },
        });

        var loaded = SidecarStore.LoadUser(path, document);

        // IR 里 Node1 与 node1 是两个不同的节点。不区分大小写的话，
        // 一个的固定位置会跑到另一个身上，而这个错位不会有任何提示。
        loaded.Orphans.Should().Equal("node1");
    }

    private static LayoutSidecar CacheFor(DiagramDocument document) => new()
    {
        StructuralHash = document.StructuralHash,
        DocumentId = document.Id,
        Version = document.Version,
    };

}
