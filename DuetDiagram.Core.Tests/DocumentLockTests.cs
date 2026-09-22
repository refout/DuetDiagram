using DuetDiagram.Core.Time;
using DuetDiagram.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 跨进程的文档所有权：锁文件、心跳文件、超时抢占。
/// </summary>
/// <remarks>
/// <para>
/// 这一组用例里最要紧的是"心跳过期但持有者还活着"那一条。抢占判错的后果不是界面难看，
/// 而是把一个正在写的进程的文档撕掉——而那种损坏看不出来，等用户发现时已经过了很久。
/// </para>
/// <para>
/// 时钟用可推进的那个，不靠真的等十五秒：等出来的用例跑得慢，而且超时值一改就全红。
/// </para>
/// </remarks>
public sealed class DocumentLockTests
{
    #region 占用

    [Fact]
    [Trait("Category", "DocumentLock")]
    public void The_first_acquire_is_exclusive()
    {
        using var temp = new TempDirectory();
        var document = temp.File("doc.dg");

        using var held = DocumentLock.Acquire(document);

        held.Mode.Should().Be(DocumentLockMode.Exclusive);
        held.CanWrite.Should().BeTrue();
        held.TookOver.Should().BeFalse("没人占着，不需要抢");
        held.Reason.Should().BeNull("拿得到独占就没什么要解释的");
    }

    [Fact]
    [Trait("Category", "DocumentLock")]
    public void The_second_acquire_is_read_only()
    {
        // "别人正在编辑"是正常情形，不是失败：用户要看到的是这份文档加一句说明，
        // 而不是一个打不开的窗口。
        using var temp = new TempDirectory();
        var document = temp.File("doc.dg");

        using var first = DocumentLock.Acquire(document);
        using var second = DocumentLock.Acquire(document);

        second.Mode.Should().Be(DocumentLockMode.ReadOnly);
        second.CanWrite.Should().BeFalse();
        second.Reason.Should().NotBeNullOrWhiteSpace("界面要拿这句话告诉用户为什么改不了");
    }

    [Fact]
    [Trait("Category", "DocumentLock")]
    public void The_holder_can_be_read_while_the_lock_is_held()
    {
        // 锁与心跳必须是两个文件。合成一个的话，心跳的写入会撞上那把独占锁，
        // 表现是"第一个进程一写心跳就把自己锁死"。
        using var temp = new TempDirectory();
        var document = temp.File("doc.dg");
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-22T10:00:00Z"));

        using var held = DocumentLock.Acquire(document, clock);

        temp.Files().Should().Contain(DocumentLock.LockPath(document));
        temp.Files().Should().Contain(DocumentLock.HeartbeatPath(document));

        Heartbeat.Read(DocumentLock.HeartbeatPath(document)).Should().Be(
            clock.UtcNow,
            "心跳文件里写着持有者最后一次报活的时刻，而它在锁被占着的时候读得出来");
    }

    [Fact]
    [Trait("Category", "DocumentLock")]
    public void Releasing_lets_the_next_acquire_take_the_lock()
    {
        using var temp = new TempDirectory();
        var document = temp.File("doc.dg");

        var first = DocumentLock.Acquire(document);
        first.Dispose();

        File.Exists(DocumentLock.HeartbeatPath(document)).Should().BeFalse(
            "正常退出会把自己的心跳清掉，那正是「上一次是好好退出的」这个标记");

        using var second = DocumentLock.Acquire(document);

        second.Mode.Should().Be(DocumentLockMode.Exclusive);
        second.TookOver.Should().BeFalse("前一个进程是正常退出的，不是崩掉的");
    }

    #endregion

    #region 上一次是不是异常退出的

    [Fact]
    [Trait("Category", "DocumentLock")]
    public void A_heartbeat_left_behind_marks_the_handover_as_unclean()
    {
        // 被强杀的进程不会去清自己的心跳，于是那个文件留了下来。
        // 这是"上一次没写完就被杀掉了"的唯一线索，而拿到它的一方必须重新校验文档。
        using var temp = new TempDirectory();
        var document = temp.File("doc.dg");
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-22T10:00:00Z"));

        File.WriteAllText(DocumentLock.LockPath(document), string.Empty);
        File.WriteAllText(
            DocumentLock.HeartbeatPath(document),
            clock.UtcNow.Subtract(TimeSpan.FromMinutes(1)).ToString("O"));

        using var taken = DocumentLock.Acquire(document, clock);

        taken.Mode.Should().Be(DocumentLockMode.Exclusive);
        taken.TookOver.Should().BeTrue("上一次没清心跳，得把文档重新校验一遍再写");
    }

    [Fact]
    [Trait("Category", "DocumentLock")]
    public void A_fresh_leftover_heartbeat_also_marks_the_handover_as_unclean()
    {
        // 判据是"心跳文件在不在"，不是"它过没过期"：刚被杀掉的进程留下的心跳也是新鲜的，
        // 而它同样可能写了一半。
        using var temp = new TempDirectory();
        var document = temp.File("doc.dg");
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-22T10:00:00Z"));

        File.WriteAllText(DocumentLock.LockPath(document), string.Empty);
        File.WriteAllText(DocumentLock.HeartbeatPath(document), clock.UtcNow.ToString("O"));

        using var taken = DocumentLock.Acquire(document, clock);

        taken.Mode.Should().Be(DocumentLockMode.Exclusive);
        taken.TookOver.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "DocumentLock")]
    public void A_stale_heartbeat_does_not_let_a_live_holder_be_displaced()
    {
        // 这一条是整组里最要紧的。持有者还握着那把独占锁，只是心跳没跟上
        // （被调度卡住、磁盘卡住、时钟跳了）。这时删除锁文件会失败——
        // 那一下失败就是"别抢"的判据，心跳过期只是触发去试一下。
        using var temp = new TempDirectory();
        var document = temp.File("doc.dg");
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-22T10:00:00Z"));

        using var holder = DocumentLock.Acquire(document, clock);

        // 把心跳改成很久以前：持有者还活着，只是看起来像停了。
        File.WriteAllText(
            DocumentLock.HeartbeatPath(document),
            clock.UtcNow.Subtract(TimeSpan.FromMinutes(1)).ToString("O"));

        using var second = DocumentLock.Acquire(document, clock);

        second.Mode.Should().Be(DocumentLockMode.ReadOnly);
        second.TookOver.Should().BeFalse();
        second.Reason.Should().Contain("卡住", "要说清是对方不响应，而不是对方在正常编辑");
        holder.Mode.Should().Be(DocumentLockMode.Exclusive, "活着的持有者不该被挤掉");
    }

    [Fact]
    [Trait("Category", "DocumentLock")]
    public void A_stale_heartbeat_with_a_deletable_lock_file_can_be_taken_over()
    {
        // 抢占那一段要真的走得到。造一个"占着锁文件但允许别人删"的持有者：
        // 活着的持有者按独占方式拿着文件，删除会失败；这个持有者不设那一层，
        // 于是删除成功、锁被抢过来。这也是这一段的真实用途——
        // 锁文件被别的东西（同步盘、编辑器、复制工具）压着不放的时候。
        using var temp = new TempDirectory();
        var document = temp.File("doc.dg");
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-22T10:00:00Z"));

        using var squatter = new FileStream(
            DocumentLock.LockPath(document),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);

        File.WriteAllText(
            DocumentLock.HeartbeatPath(document),
            clock.UtcNow.Subtract(TimeSpan.FromMinutes(1)).ToString("O"));

        using var taken = DocumentLock.Acquire(document, clock);

        taken.Mode.Should().Be(DocumentLockMode.Exclusive);
        taken.TookOver.Should().BeTrue();
    }

    #endregion

    #region 心跳过期

    [Fact]
    [Trait("Category", "DocumentLock")]
    public void A_heartbeat_past_the_timeout_is_stale()
    {
        using var temp = new TempDirectory();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-22T10:00:00Z"));
        var heartbeat = temp.File("doc.dg.heartbeat");

        new Heartbeat(heartbeat, clock).Dispose();

        Heartbeat.IsStale(heartbeat, clock).Should().BeFalse("刚跳过，不算过期");

        clock.Advance(Heartbeat.Timeout + TimeSpan.FromSeconds(1));

        Heartbeat.IsStale(heartbeat, clock).Should().BeTrue("超过超时值就算过期");
    }

    [Fact]
    [Trait("Category", "DocumentLock")]
    public void A_missing_heartbeat_is_not_stale()
    {
        // 读不出来与过期是两回事。把前者当成后者会去抢一个可能活着的进程。
        using var temp = new TempDirectory();

        Heartbeat.IsStale(temp.File("nobody.dg.heartbeat")).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "DocumentLock")]
    public void A_garbage_heartbeat_is_not_stale()
    {
        using var temp = new TempDirectory();
        var heartbeat = temp.File("doc.dg.heartbeat");

        File.WriteAllText(heartbeat, "这不是一个时刻");

        Heartbeat.IsStale(heartbeat).Should().BeFalse();
    }

    #endregion

    #region 只读那一份什么都不动

    [Fact]
    [Trait("Category", "DocumentLock")]
    public void A_read_only_holder_leaves_the_files_alone()
    {
        // 只读那一份若也去写心跳，持有者就再也看不出谁在动这个文件；
        // 它若把锁文件删掉，持有者下一次重排就会以为自己的所有权没了。
        using var temp = new TempDirectory();
        var document = temp.File("doc.dg");
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-22T10:00:00Z"));

        using var holder = DocumentLock.Acquire(document, clock);

        using (var reader = DocumentLock.Acquire(document, clock))
        {
            reader.Mode.Should().Be(DocumentLockMode.ReadOnly);
        }

        Heartbeat.Read(DocumentLock.HeartbeatPath(document)).Should().Be(
            clock.UtcNow,
            "只读那一份退出时不该动心跳文件");
        File.Exists(DocumentLock.LockPath(document)).Should().BeTrue("锁文件还归持有者");
    }

    #endregion
}
