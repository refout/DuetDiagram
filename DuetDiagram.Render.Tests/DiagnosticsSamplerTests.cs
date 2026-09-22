using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 帧时采样器：环形窗口、分位数、开关。
/// </summary>
/// <remarks>
/// 这一层测的是"窗口怎么转、分位怎么取"。它退化时的表现是面板上的数字
/// 看起来合理但其实是错的——错的分位数不会报警，只会让人去查一个不存在的问题。
/// </remarks>
public sealed class DiagnosticsSamplerTests
{
    #region 开关

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void Nothing_is_recorded_while_it_is_off()
    {
        var sampler = new DiagnosticsSampler();

        sampler.Enabled.Should().BeFalse("默认关着。诊断工具不该在没人看的时候花钱");

        sampler.Record(Frame(1));

        sampler.Count.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void Turning_it_on_starts_recording()
    {
        var sampler = new DiagnosticsSampler { Enabled = true };

        sampler.Record(Frame(1));

        sampler.Count.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void Turning_it_off_keeps_what_was_already_recorded()
    {
        // 关掉之后旧样本要留着：面板重新打开时先显示上一次的读数，
        // 比显示一片空白有用。清空是 Clear 的事，两件事分开。
        var sampler = new DiagnosticsSampler { Enabled = true };

        sampler.Record(Frame(1));
        sampler.Enabled = false;
        sampler.Record(Frame(2));

        sampler.Count.Should().Be(1);
        sampler.Summary().Latest.Total.Should().Be(1);
    }

    #endregion

    #region 环形窗口

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void The_window_never_grows_past_its_capacity()
    {
        var sampler = new DiagnosticsSampler(capacity: 4) { Enabled = true };

        for (var index = 0; index < 100; index++)
        {
            sampler.Record(Frame(index));
        }

        sampler.Count.Should().Be(4, "窗口长度固定，与运行时长无关");
        sampler.Capacity.Should().Be(4);
    }

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void The_oldest_frame_is_the_one_that_gets_overwritten()
    {
        var sampler = new DiagnosticsSampler(capacity: 4) { Enabled = true };

        for (var index = 0; index < 4; index++)
        {
            sampler.Record(Frame(index));
        }

        // 再写一帧，最早那帧（0）被挤出去，窗口里应当是 1 到 4。
        sampler.Record(Frame(4));

        var summary = sampler.Summary();

        summary.FrameCount.Should().Be(4);
        summary.Total.Max.Should().Be(4);

        // 窗口里是 1、2、3、4，最近秩法给出的中位是靠下的那个真实样本 2，
        // 不是插出来的 2.5。插值看着更"对"，但插出来的数不是任何一帧的实测值。
        summary.Total.Median.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void The_latest_frame_is_the_last_one_written()
    {
        var sampler = new DiagnosticsSampler(capacity: 3) { Enabled = true };

        for (var index = 0; index < 7; index++)
        {
            sampler.Record(Frame(index));
        }

        sampler.Summary().Latest.Total.Should().Be(6, "最后写进去的是第 6 帧");
    }

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void The_latest_frame_survives_a_full_wrap()
    {
        // 写满一圈之后写指针回到 0，此时"最后一帧"落在数组末尾而不是指针前面。
        // 这一格算错的话，面板上的明细永远是窗口里最旧的那一帧，而它看起来同样合理。
        var sampler = new DiagnosticsSampler(capacity: 2) { Enabled = true };

        sampler.Record(Frame(1));
        sampler.Record(Frame(2));
        sampler.Record(Frame(3));

        sampler.Summary().Latest.Total.Should().Be(3);
    }

    #endregion

    #region 分位数

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void Percentiles_come_from_the_samples_themselves()
    {
        // 最近秩法，不插值：插出来的数不是任何一个真实样本，
        // 而面板要回答的是"最坏能坏到哪儿"，那种问题只有真实样本答得了。
        var sampler = new DiagnosticsSampler(capacity: 100) { Enabled = true };

        for (var index = 1; index <= 100; index++)
        {
            sampler.Record(Frame(index));
        }

        var total = sampler.Summary().Total;

        total.Median.Should().Be(50);
        total.P95.Should().Be(95);
        total.Max.Should().Be(100);
    }

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void A_single_sample_is_its_own_median_and_its_own_maximum()
    {
        var sampler = new DiagnosticsSampler { Enabled = true };

        sampler.Record(new DiagnosticsFrame(7, 1, 5, 10, 20, RenderMode.Immediate));

        var summary = sampler.Summary();

        summary.Total.Median.Should().Be(7);
        summary.Total.P95.Should().Be(7);
        summary.Total.Max.Should().Be(7);
        summary.Cull.Median.Should().Be(1);
        summary.Raster.Median.Should().Be(5);
    }

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void The_three_stages_are_read_from_their_own_columns()
    {
        // 三列混掉的话，面板上的"剔除"会显示成光栅化的数——两个数都像模像样，
        // 只有对着看才发现不对。
        var sampler = new DiagnosticsSampler { Enabled = true };

        sampler.Record(new DiagnosticsFrame(Total: 30, Cull: 2, Raster: 25, Drawn: 1, Culled: 9, RenderMode.Virtualized));
        sampler.Record(new DiagnosticsFrame(Total: 40, Cull: 4, Raster: 33, Drawn: 1, Culled: 9, RenderMode.Virtualized));

        var summary = sampler.Summary();

        // 两个样本时最近秩法取靠下的那个，所以中位是 30 而不是 35。
        summary.Total.Median.Should().Be(30);
        summary.Cull.Median.Should().Be(2);
        summary.Raster.Median.Should().Be(25);
    }

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void The_last_frame_carries_its_counts_and_its_mode()
    {
        var sampler = new DiagnosticsSampler { Enabled = true };

        sampler.Record(new DiagnosticsFrame(1, 2, 3, Drawn: 406, Culled: 2562, RenderMode.Virtualized));

        var latest = sampler.Summary().Latest;

        latest.Drawn.Should().Be(406);
        latest.Culled.Should().Be(2562);
        latest.Mode.Should().Be(RenderMode.Virtualized);
    }

    #endregion

    #region 空窗口

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void An_empty_window_reports_zero_frames_rather_than_zeros()
    {
        // 空窗口报一堆零的话，面板会显示"这一帧 0.00 毫秒"——
        // 那看起来像一切正常，而真相是还没量过。
        var summary = new DiagnosticsSampler().Summary();

        summary.FrameCount.Should().Be(0);
        summary.Total.Should().Be(DiagnosticsStatistics.Empty);
        summary.Cull.Should().Be(DiagnosticsStatistics.Empty);
        summary.Raster.Should().Be(DiagnosticsStatistics.Empty);
    }

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void Clearing_empties_the_window_but_not_the_stage_records()
    {
        // 两件事分开：换文档要清帧窗口（旧文档的帧时对新文档没有意义），
        // 而重活的耗时正是刚量出来的那一次，清掉就没有了。
        var sampler = new DiagnosticsSampler { Enabled = true };

        sampler.Record(Frame(1));
        sampler.RecordStage(DiagnosticsStage.Layout, 12.5);

        sampler.Clear();

        sampler.Count.Should().Be(0);
        sampler.Stage(DiagnosticsStage.Layout).Should().Be(12.5);
    }

    #endregion

    #region 重活的记录

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void A_stage_nobody_reported_reads_as_missing()
    {
        // 报零的话面板会显示"哈希 0.00 毫秒"，那读起来像"算过了而且很快"。
        var sampler = new DiagnosticsSampler();

        sampler.HasStage(DiagnosticsStage.Hash).Should().BeFalse();
        sampler.Stage(DiagnosticsStage.Hash).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void Reporting_a_stage_twice_keeps_the_later_one()
    {
        var sampler = new DiagnosticsSampler();

        sampler.RecordStage(DiagnosticsStage.DrawList, 3);
        sampler.RecordStage(DiagnosticsStage.DrawList, 8);

        sampler.Stage(DiagnosticsStage.DrawList).Should().Be(8);
        sampler.Stage(DiagnosticsStage.Layout).Should().BeNull("同一次里只报了绘制列表那一段");
    }

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void Stages_are_recorded_even_while_the_window_is_off()
    {
        // 重活与帧无关，而它恰恰是"打开面板之前就已经发生过"的那一段——
        // 按开关一起拦掉的话，面板一打开就是空的，而这正是最需要它的时候。
        var sampler = new DiagnosticsSampler();

        sampler.RecordStage(DiagnosticsStage.Layout, 12.5);

        sampler.Stage(DiagnosticsStage.Layout).Should().Be(12.5);
    }

    #endregion

    [Fact]
    [Trait("Category", "DiagnosticsSampler")]
    public void A_capacity_below_one_is_rejected()
    {
        var act = () => new DiagnosticsSampler(capacity: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static DiagnosticsFrame Frame(double total) =>
        new(total, 0, 0, 0, 0, RenderMode.Immediate);
}
