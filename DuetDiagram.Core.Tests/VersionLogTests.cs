using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Logging;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 差异计算的全部分支。
/// </summary>
/// <remarks>
/// 每条用例对应判断链上的一个分支，构造出来的日志状态都刻意贴边：
/// 空日志、只有两条记录、索引恰好越界一个等。
/// 这些分支的代价差别很大（给全量快照要序列化整份文档，给空差异什么也不用做），
/// 走错了不会报错，只是变得很慢或者传了很多没用的数据，所以必须逐条钉住。
/// </remarks>
public sealed class VersionLogTests
{
    private const string CurrentHash = "HASH-CURRENT";

    private static string SerializeFull() => "{\"snapshot\":true}";

    private static VersionEntry Entry(int version, int changeCount = 1, int affectedCount = 1, bool bulk = false) => new()
    {
        Version = version,
        CommandId = "test-command",
        Source = ChangeSource.Human,
        Timestamp = DateTimeOffset.UnixEpoch,
        AffectedIds = [.. Enumerable.Range(0, affectedCount).Select(i => $"a{version}-{i}")],
        Changes =
        [
            .. Enumerable.Range(0, changeCount).Select(i => new FieldChange
            {
                ElementId = $"a{version}",
                Field = $"f{i}",
            }),
        ],
        IsBulkChange = bulk,
        OriginalChangeCount = bulk ? changeCount : 0,
    };

    private static VersionLog Log(params VersionEntry[] entries)
    {
        var log = new VersionLog();

        foreach (var entry in entries)
        {
            log.Record(entry);
        }

        return log;
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Identical_versions_return_empty()
    {
        var log = Log(Entry(1), Entry(2), Entry(3));

        log.BuildDiff(3, 3, null, CurrentHash, SerializeFull).Should().BeOfType<EmptyDiff>();
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Client_ahead_of_server_returns_invalid()
    {
        var log = Log(Entry(1), Entry(2));

        // 调用方声称的版本比当前还新。这是参数传错了，不是并发冲突，
        // 两者的区别在于要不要让调用方重试。
        log.BuildDiff(5, 3, null, CurrentHash, SerializeFull).Should().BeOfType<InvalidDiff>();
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Empty_log_returns_a_full_snapshot()
    {
        var log = new VersionLog();

        var diff = log.BuildDiff(0, 3, null, CurrentHash, SerializeFull);

        var snapshot = diff.Should().BeOfType<FullSnapshotDiff>().Which;
        snapshot.Version.Should().Be(3);
        snapshot.FullJson.Should().Be("{\"snapshot\":true}");
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Range_older_than_the_ring_buffer_returns_a_full_snapshot()
    {
        var log = Log(Entry(5), Entry(6), Entry(7));

        // 起点 1 早于日志保留的最早版本 5，中间那段发生了什么已经无从得知，
        // 只能给全量。这跟"区间内没有记录"是两回事。
        log.BuildDiff(1, 7, null, CurrentHash, SerializeFull).Should().BeOfType<FullSnapshotDiff>();
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Range_without_entries_returns_empty()
    {
        var log = Log(Entry(1), Entry(5));

        // 起点 3 不早于最早版本 1，但 (3, 4] 区间里确实没有记录，
        // 说明这段时间没发生变更，属于正常情况，应回空差异。
        log.BuildDiff(3, 4, null, CurrentHash, SerializeFull).Should().BeOfType<EmptyDiff>();
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Bulk_entry_in_range_returns_a_full_snapshot()
    {
        var log = Log(Entry(1), Entry(2, changeCount: 500, bulk: true));

        log.BuildDiff(1, 2, null, CurrentHash, SerializeFull).Should().BeOfType<FullSnapshotDiff>();
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Accumulated_changes_over_threshold_return_a_full_snapshot()
    {
        // 每条 100 个变更都不到单条的批量阈值，但累计到第六条就超过了总量上限。
        // 这条覆盖的是"整体划不划算"，跟单条是否超限是两套判断。
        var entries = Enumerable.Range(1, 6).Select(v => Entry(v, changeCount: 100)).ToArray();
        var log = Log(entries);

        log.BuildDiff(0, 6, null, CurrentHash, SerializeFull).Should().BeOfType<FullSnapshotDiff>();
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Matching_structural_hash_returns_a_reference()
    {
        var log = Log(Entry(1), Entry(2), Entry(3));

        var diff = log.BuildDiff(1, 3, CurrentHash, CurrentHash, SerializeFull);

        var reference = diff.Should().BeOfType<ReferenceDiff>().Which;
        reference.Version.Should().Be(3);
        reference.BaseVersion.Should().Be(1);
        reference.StructuralHash.Should().Be(CurrentHash);

        // 受影响元素取并集并去重：同一元素在这段区间里可能被改过多次。
        reference.AffectedIds.Should().Equal("a2-0", "a3-0");
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Missing_client_hash_returns_entries()
    {
        var log = Log(Entry(1), Entry(2), Entry(3));

        // 调用方没声明结构哈希，就走不了"只列受影响元素"这条捷径，只能给逐条增量。
        var diff = log.BuildDiff(1, 3, null, CurrentHash, SerializeFull);

        var entries = diff.Should().BeOfType<EntriesDiff>().Which;
        entries.Entries.Select(e => e.Version).Should().Equal(2, 3);
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Mismatching_client_hash_returns_entries()
    {
        var log = Log(Entry(1), Entry(2), Entry(3));

        log.BuildDiff(1, 3, "SOMETHING-ELSE", CurrentHash, SerializeFull)
            .Should().BeOfType<EntriesDiff>();
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Full_snapshot_is_serialized_lazily()
    {
        var log = Log(Entry(1), Entry(2));
        var calls = 0;

        var diff = log.BuildDiff(1, 2, CurrentHash, CurrentHash, () =>
        {
            calls++;
            return "{}";
        });

        // 哈希一致时落在"只列受影响元素"这条路上，不该白白序列化整份文档。
        // 差异计算会被高频调用，每次多付一次全量序列化代价很可观。
        diff.Should().BeOfType<ReferenceDiff>();
        calls.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void No_serialization_happens_when_no_entries_are_in_range()
    {
        var log = Log(Entry(1));
        var calls = 0;

        var diff = log.BuildDiff(1, 2, null, "OTHER-HASH", () =>
        {
            calls++;
            return "{}";
        });

        diff.Should().BeOfType<EmptyDiff>();
        calls.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Ring_buffer_keeps_only_the_latest_entries()
    {
        var log = Log([.. Enumerable.Range(1, 150).Select(v => Entry(v))]);

        // 容量上限让内存占用可预测，代价是"落后太多"的对端只能拿全量快照。
        log.Count.Should().Be(VersionLogLimits.MaxEntries);
        log.EarliestVersion.Should().Be(51);
        log.LatestVersion.Should().Be(150);
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Bulk_entries_are_truncated_and_keep_their_original_size()
    {
        var log = Log(Entry(1, changeCount: 150, affectedCount: 250));

        var stored = log.Snapshot()[0];
        stored.IsBulkChange.Should().BeTrue();
        stored.Changes.Should().BeEmpty();
        stored.AffectedIds.Should().BeEmpty();

        // 原始规模要留下来：累计判断需要用它，而且排查问题时也想知道那一次到底改了多少。
        stored.OriginalChangeCount.Should().Be(150);
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Small_entries_are_stored_verbatim()
    {
        var log = Log(Entry(1, changeCount: 3, affectedCount: 2));

        var stored = log.Snapshot()[0];
        stored.IsBulkChange.Should().BeFalse();
        stored.Changes.Should().HaveCount(3);
        stored.AffectedIds.Should().HaveCount(2);
    }
}
