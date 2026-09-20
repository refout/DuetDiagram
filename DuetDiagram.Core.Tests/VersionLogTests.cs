using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Logging;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>P1 判据 #5：BuildDiff 的每一条边界。</summary>
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

        log.BuildDiff(1, 7, null, CurrentHash, SerializeFull).Should().BeOfType<FullSnapshotDiff>();
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Range_without_entries_returns_empty()
    {
        var log = Log(Entry(1), Entry(5));

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
        // 每条 100 个变更（未触发单条批量阈值），累计超过 500 即降级。
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
        reference.AffectedIds.Should().Equal("a2-0", "a3-0");
    }

    [Fact]
    [Trait("Category", "DiffBoundary")]
    public void Missing_client_hash_returns_entries()
    {
        var log = Log(Entry(1), Entry(2), Entry(3));

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

        // 哈希一致时走 Reference，不应付出序列化成本。
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
