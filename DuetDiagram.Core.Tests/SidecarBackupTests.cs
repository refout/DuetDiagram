using DuetDiagram.Core.Model;
using DuetDiagram.Core.Sidecar;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 人工产物的备份、保留策略与恢复。
/// </summary>
/// <remarks>
/// 时间一律显式传入。备份的整个行为都围绕时间展开（命名、年龄上限、清理），
/// 靠系统时钟会让"最老的那份有没有被删掉"变成一条时快时慢的断言。
/// </remarks>
public sealed class SidecarBackupTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int days, int hours = 0) => Start.AddDays(days).AddHours(hours);

    // ---- 产生备份 ----

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void First_save_produces_no_backup()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        // 头一次保存时没有旧内容可备份。
        SidecarBackup.Save(path, new UserSidecar { DocumentId = document.Id }, now: At(0))
            .BackupPath.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Saving_again_backs_up_the_previous_content()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        SidecarBackup.Save(path, User(document, 1), now: At(0));
        var outcome = SidecarBackup.Save(path, User(document, 2), now: At(1));

        outcome.BackupPath.Should().NotBeNull();

        // 备份里必须是**旧**内容。顺序反了的话备份下来的就是刚写进去的新内容，等于没有备份。
        var restored = SidecarBackup.Restore(outcome.BackupPath!, document);
        restored.Value!.PinnedNodes["a"].Should().Be(new Anchor(1, 1));
    }

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void A_corrupt_current_file_produces_no_backup()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        File.WriteAllText(SidecarPaths.User(path), "{ 这不是合法的 JSON");

        // 解析不了的内容没有可恢复的东西。放进备份列表只会让用户在恢复对话框里
        // 选到一个同样打不开的文件。
        SidecarBackup.Save(path, User(document, 1), now: At(0))
            .BackupPath.Should().BeNull();

        SidecarBackup.List(path).Should().BeEmpty();
    }

    // ---- 列出 ----

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Backups_are_listed_newest_first()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        SidecarBackup.Save(path, User(document, 1), now: At(0));
        SidecarBackup.Save(path, User(document, 2), now: At(1));
        SidecarBackup.Save(path, User(document, 3), now: At(2));

        var list = SidecarBackup.List(path);

        list.Should().HaveCount(2, "三次保存产生两份备份，最后一份是当前内容");
        list[0].CreatedAt.Should().BeAfter(list[1].CreatedAt);

        // 时间戳是**做备份的时刻**，不是被备份内容的写入时刻：
        // 第三次保存在第三天做，它备份的是第二次写入的内容，但文件按第三天命名。
        list[0].CreatedAt.Should().Be(At(2));
        list[1].CreatedAt.Should().Be(At(1));
    }

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Listing_an_empty_directory_is_not_an_error()
    {
        using var temp = new TempDirectory();

        SidecarBackup.List(temp.File("doc.dgm")).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Backups_of_other_documents_are_not_listed()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();

        SidecarBackup.Save(temp.File("甲.dgm"), User(document, 1), now: At(0));
        SidecarBackup.Save(temp.File("甲.dgm"), User(document, 2), now: At(1));
        SidecarBackup.Save(temp.File("乙.dgm"), User(document, 1), now: At(0));
        SidecarBackup.Save(temp.File("乙.dgm"), User(document, 2), now: At(1));

        // 同一目录下两份文档各自的备份不能混在一起。
        SidecarBackup.List(temp.File("甲.dgm")).Should().HaveCount(1);
        SidecarBackup.List(temp.File("乙.dgm")).Should().HaveCount(1);
    }

    // ---- 保留策略 ----

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Retention_keeps_only_the_most_recent_ones()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        // 十二份备份，都发生在三十天以内。
        for (var day = 0; day < 12; day++)
        {
            SidecarBackup.Save(path, User(document, day), policy: BackupPolicy.Keep(10, 30), now: At(day));
        }

        var list = SidecarBackup.List(path);

        // 两头上限各自生效，取交集。数量上限在这里起作用。
        list.Should().HaveCount(10);
        list[0].CreatedAt.Should().Be(At(11), "留下的应当是最新的一批");
    }

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Retention_drops_backups_past_the_age_limit()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        // 三份备份，只有一份在三十天以内。
        SidecarBackup.Save(path, User(document, 1), now: At(0));
        SidecarBackup.Save(path, User(document, 2), policy: BackupPolicy.Keep(10, 30), now: At(40));
        SidecarBackup.Save(path, User(document, 3), policy: BackupPolicy.Keep(10, 30), now: At(45));

        var list = SidecarBackup.List(path);

        // 数量上限在这一例里没起作用（远不到十份），年龄上限才是决定因素。
        list.Should().HaveCount(2);
        list.Should().OnlyContain(b => b.CreatedAt >= At(45) - TimeSpan.FromDays(30));
    }

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Two_limits_apply_together()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        // 五份备份：最老的两份超过年龄，剩下三份里只留最新的两份。
        for (var day = 0; day < 5; day++)
        {
            SidecarBackup.Save(path, User(document, day), now: At(day));
        }

        // 五天内保存五次，产生四份备份（第一天那次没有旧内容可备）。
        SidecarBackup.List(path).Should().HaveCount(4);

        var removed = SidecarBackup.Prune(path, BackupPolicy.Keep(2, 3), now: At(4));

        // 交集：既要在最新两份之内，又不能超过年龄。
        // 四份里留最新两份，删掉另两份。
        removed.Should().HaveCount(2);
        SidecarBackup.List(path).Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Pruning_leaves_the_current_file_alone()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        SidecarBackup.Save(path, User(document, 1), now: At(0));
        SidecarBackup.Save(path, User(document, 2), now: At(1));

        SidecarBackup.Prune(path, BackupPolicy.Keep(1, 1), now: At(100));

        // 清理只动备份，当前内容不受影响——它不是备份列表里的一员。
        File.Exists(SidecarPaths.User(path)).Should().BeTrue();
        SidecarStore.LoadUser(path, document).Value!.PinnedNodes["a"].Should().Be(new Anchor(2, 2));
    }

    // ---- 恢复 ----

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Restore_reads_the_chosen_backup()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        SidecarBackup.Save(path, User(document, 1), now: At(0));
        SidecarBackup.Save(path, User(document, 2), now: At(1));

        var oldest = SidecarBackup.List(path)[^1];
        var restored = SidecarBackup.Restore(oldest.Path, document);

        restored.Status.Should().Be(SidecarStatus.Loaded);
        restored.Value!.PinnedNodes["a"].Should().Be(new Anchor(1, 1));
    }

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Restore_reports_orphans_against_the_current_document()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        SidecarBackup.Save(path, new UserSidecar
        {
            DocumentId = document.Id,
            PinnedNodes = new Dictionary<string, Anchor>(StringComparer.Ordinal)
            {
                ["早就删掉的"] = new Anchor(0, 0),
            },
        }, now: At(0));

        SidecarBackup.Save(path, User(document, 1), now: At(1));

        var restored = SidecarBackup.Restore(SidecarBackup.List(path)[^1].Path, document);

        // 备份是旧快照，里面的节点可能早就删了。这些条目以孤儿形式报出来，
        // 而不是悄悄指向一个不存在的元素。
        restored.Orphans.Should().Contain("早就删掉的");
    }

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Restore_rejects_a_missing_file()
    {
        using var temp = new TempDirectory();

        SidecarBackup.Restore(temp.File("根本没有这个文件.bak"), IrFixtures.Base())
            .Status.Should().Be(SidecarStatus.Unusable);
    }

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Restore_rejects_a_corrupt_backup()
    {
        using var temp = new TempDirectory();
        var broken = temp.File("doc.user.20260901-000000.bak");
        File.WriteAllText(broken, "{ 坏掉了");

        SidecarBackup.Restore(broken, IrFixtures.Base())
            .Status.Should().Be(SidecarStatus.Unusable);
    }

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Restore_rejects_a_backup_from_another_document()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        SidecarBackup.Save(path, new UserSidecar { DocumentId = "另一个文档" }, now: At(0));
        SidecarBackup.Save(path, User(document, 1), now: At(1));

        SidecarBackup.Restore(SidecarBackup.List(path)[^1].Path, document)
            .Status.Should().Be(SidecarStatus.Unusable);
    }

    // ---- 命名与清理的边界 ----

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Backup_names_keep_the_user_marker()
    {
        using var temp = new TempDirectory();
        var path = temp.File("orders.dgm");

        SidecarBackup.Save(path, new UserSidecar { DocumentId = "ir-test" }, now: At(0));
        SidecarBackup.Save(path, new UserSidecar { DocumentId = "ir-test" }, now: At(1));

        // 光看名字要能认出这是人工产物的备份：.user 那一段必须留住。
        Path.GetFileName(SidecarBackup.List(path)[0].Path).Should().Be("orders.user.20260902-000000.bak");    }

    [Fact]
    [Trait("Category", "SidecarBackup")]
    public void Non_backup_files_are_ignored_when_listing()
    {
        using var temp = new TempDirectory();
        var document = IrFixtures.Base();
        var path = temp.File("doc.dgm");

        SidecarBackup.Save(path, User(document, 1), now: At(0));
        SidecarBackup.Save(path, User(document, 2), now: At(1));

        // 同目录下的无关文件不该被当成备份，更不该被清理掉。
        File.WriteAllText(temp.File("doc.user.不是时间戳.bak"), "x");
        File.WriteAllText(temp.File("随便一个文件.txt"), "x");

        SidecarBackup.List(path).Should().HaveCount(1);

        SidecarBackup.Prune(path, new BackupPolicy(0, TimeSpan.Zero), now: At(100));

        File.Exists(temp.File("随便一个文件.txt")).Should().BeTrue();
        File.Exists(temp.File("doc.user.不是时间戳.bak")).Should().BeTrue();
    }

    private static UserSidecar User(DiagramDocument document, int seed) => new()
    {
        DocumentId = document.Id,
        PinnedNodes = new Dictionary<string, Anchor>(StringComparer.Ordinal)
        {
            ["a"] = new Anchor(seed, seed),
        },
    };
}
