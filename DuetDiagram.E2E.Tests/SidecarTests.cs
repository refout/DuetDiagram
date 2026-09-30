using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using DuetDiagram.App;
using DuetDiagram.App.Resources;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Sidecar;
using DuetDiagram.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 人工产物（<c>user.json</c>）的读、写与恢复。
/// </summary>
/// <remarks>
/// <para>
/// 这一层验的是"接上了没有"：打开一份文档时固定位置回不回来、保存时写不写下去、
/// 写坏了有没有人问用户。三样人工产物各自怎么存、怎么校验由核心层的用例管。
/// </para>
/// <para>
/// **断言落在文件与重新打开的窗口上，不落在"某个方法被调过"。** 只断言"读过了"的话，
/// 一个读完就把结果丢掉、或者写了但写错地方的实现照样能过，而用户丢的是他摆过的位置。
/// 所以这里的路数一律是：拖一下 → 保存 → 关掉 → 重新打开 → 看它还在不在。
/// </para>
/// <para>
/// 每份文件都写在一个用完就删的目录里。用真实路径而不是内存里的字节，
/// 是因为这条路会碰到同名同目录的附属文件、跨进程所有权与原子替换这些东西，
/// 而它们正是真实用户手上那份文档会遇到的。
/// </para>
/// </remarks>
public sealed class SidecarTests
{
    #region 读（Category=Sidecar）

    /// <summary>打开一份文档，人工产物里的固定位置要在首帧就生效。</summary>
    /// <remarks>
    /// 晚一步的话，第一次打开看到的图与上一次保存时摆的不是一回事——
    /// 用户会以为自己拖过的位置没存下来。
    /// </remarks>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task Opening_a_document_puts_the_pinned_nodes_back()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = WriteDocument(temp, "orders.json");

            SidecarStore.SaveUser(path, new UserSidecar
            {
                DocumentId = DocumentId,
                PinnedNodes = Pins(("check", 120, 240)),
            });

            var window = HeadlessFixture.Open(DocumentLaunch.File(path));

            window.Session.PinnedNodes.Should().ContainKey("check");
            window.Session.PinnedNodes["check"].Should().Be(new Anchor(120, 240));

            window.Close();
        });
    }

    /// <summary>折点与自定义端口同样要装回来。</summary>
    /// <remarks>
    /// 三样人工产物各走各的字段，只验固定位置的话，另外两样漏掉一个都看不出来——
    /// 而它们同样是重算不出来的内容。
    /// </remarks>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task Opening_a_document_puts_the_edge_bends_and_custom_ports_back()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = WriteDocument(temp, "orders.json");

            SidecarStore.SaveUser(path, new UserSidecar
            {
                DocumentId = DocumentId,
                PinnedEdges = new Dictionary<string, IReadOnlyList<Anchor>>(StringComparer.Ordinal)
                {
                    ["e1"] = [new Anchor(10, 20), new Anchor(30, 40)],
                },
                CustomPorts = new Dictionary<string, IReadOnlyList<PortDef>>(StringComparer.Ordinal)
                {
                    ["check"] = [new PortDef { Name = "left", Side = PortSide.Left, IsCustom = true }],
                },
            });

            var window = HeadlessFixture.Open(DocumentLaunch.File(path));

            window.Session.PinnedEdges.Should().ContainKey("e1");
            window.Session.PinnedEdges["e1"].Should().Equal(new Anchor(10, 20), new Anchor(30, 40));

            window.Session.CustomPorts.Should().ContainKey("check");
            window.Session.CustomPorts["check"].Should().ContainSingle()
                .Which.Name.Should().Be("left");

            window.Close();
        });
    }

    /// <summary>人工产物读不出来时弹恢复提示，而文档照常打开。</summary>
    /// <remarks>
    /// 一份附属文件格式坏掉不该让整份文档打不开：文档那一份是好的，
    /// 用户要看的是它。要问的是"他手工摆过的那些怎么办"。
    /// </remarks>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task A_corrupt_sidecar_shows_the_recovery_dialog()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = WriteDocument(temp, "orders.json");

            File.WriteAllText(SidecarPaths.User(path), "{ 这不是 JSON");

            var window = HeadlessFixture.Open(DocumentLaunch.File(path));

            window.Session.Document.Nodes.Should().NotBeEmpty("文档本身是好的，要照常打开");
            HeadlessFixture.SidecarRecovery(window).IsVisible.Should().BeTrue("读不出来就要问用户怎么办");

            window.Close();
        });
    }

    #endregion

    #region 写（Category=Sidecar）

    /// <summary>拖一下再保存，固定位置落在文档旁边那份文件里。</summary>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task Saving_writes_the_pins_next_to_the_document()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = WriteDocument(temp, "orders.json");
            var window = HeadlessFixture.Open(DocumentLaunch.File(path));

            Drag(window, "check", 60, 40);
            window.Session.PinnedNodes.Should().ContainKey("check", "这一拖要真的把它钉住，否则下面验不到东西");

            window.Save();

            var user = SidecarPaths.User(path);

            File.Exists(user).Should().BeTrue("人工产物要落在文档旁边");
            SidecarStore.LoadUser(path, window.Session.Document)
                .IsUsable.Should().BeTrue("刚写下去的那一份要读得回来");

            window.Close();
        });
    }

    /// <summary>
    /// 保存之后再打开，拖过的位置还在。
    /// </summary>
    /// <remarks>
    /// 这一条是整块的目的：前面几条各验一段，这一条验"用户看不到的那次丢失"。
    /// 位置写进文件却没读回来、或者读回来却没装进会话，都只在这一条上露出来。
    /// </remarks>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task A_pinned_position_survives_a_save_and_a_reopen()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = WriteDocument(temp, "orders.json");

            var first = HeadlessFixture.Open(DocumentLaunch.File(path));

            Drag(first, "check", 60, 40);
            first.Save();

            var pinned = first.Session.PinnedNodes["check"];

            first.Close();

            var second = HeadlessFixture.Open(DocumentLaunch.File(path));

            second.Session.PinnedNodes.Should().ContainKey("check");
            second.Session.PinnedNodes["check"].Should().Be(pinned, "存下去的就是再打开时装回来的");

            second.Close();
        });
    }

    /// <summary>没拖过、没改过折点的文档旁边不该多出一个空文件。</summary>
    /// <remarks>
    /// 一份文档旁边凭空多一个 <c>user.json</c> 对用户没有意义，
    /// 而它会被拷来拷去、被版本管理盯着——那是白占地方。
    /// </remarks>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task A_document_with_nothing_manual_gets_no_sidecar()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = WriteDocument(temp, "orders.json");
            var window = HeadlessFixture.Open(DocumentLaunch.File(path));

            window.Save();

            File.Exists(SidecarPaths.User(path)).Should().BeFalse("没有人工产物就不该建这个文件");

            window.Close();
        });
    }

    /// <summary>
    /// 第二次保存要给第一次那一份留一份备份。
    /// </summary>
    /// <remarks>
    /// 备份是人工产物唯一的退路。不留的话，写入过程中断电或者内容被改坏，
    /// 用户手上就什么都没有了——而那份内容重算不出来。
    /// </remarks>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task Saving_a_second_time_keeps_a_backup_of_the_first()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = WriteDocument(temp, "orders.json");
            var window = HeadlessFixture.Open(DocumentLaunch.File(path));

            Drag(window, "check", 60, 40);
            window.Save();

            SidecarBackup.List(path).Should().BeEmpty("第一次保存时还没有旧内容可以备份");

            Drag(window, "pass", -60, 40);
            window.Save();

            SidecarBackup.List(path).Should().ContainSingle("第二次保存要把第一次那一份留下来");

            window.Close();
        });
    }

    /// <summary>
    /// 把固定位置清掉之后再保存，文件里也不该留着它。
    /// </summary>
    /// <remarks>
    /// 反过来写（清空就不写这个文件）的话，旧内容会留在原地，
    /// 下次打开又把用户刚删掉的位置装回来——而他明明撤销过。
    /// </remarks>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task Saving_after_clearing_the_pins_clears_them_from_the_file()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = WriteDocument(temp, "orders.json");

            var first = HeadlessFixture.Open(DocumentLaunch.File(path));

            Drag(first, "check", 60, 40);
            first.Save();

            SidecarStore.LoadUser(path, first.Session.Document).Value!.PinnedNodes
                .Should().ContainKey("check", "先确认真的写下去了");

            first.Session.UndoPin();
            first.Session.PinnedNodes.Should().BeEmpty("撤销把固定位置拿回去了");
            first.Save();

            first.Close();

            var second = HeadlessFixture.Open(DocumentLaunch.File(path));

            second.Session.PinnedNodes.Should().BeEmpty("清掉之后再打开不该又冒出来");

            second.Close();
        });
    }

    /// <summary>
    /// 只读的窗口不写人工产物。
    /// </summary>
    /// <remarks>
    /// 只读说的是"另一个进程正在编辑这份文档"，所以连它旁边那份文件也不该动——
    /// 那份文件是另一个进程的会话正在写的。
    /// </remarks>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task A_read_only_window_does_not_write_the_sidecar()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = WriteDocument(temp, "orders.json");

            // 冒充另一个进程：先拿住这份文档的独占所有权。
            using var other = DocumentLock.Acquire(path);

            var window = new MainWindow(DocumentLaunch.File(path));

            window.Show();
            window.CaptureRenderedFrame();

            window.Session.IsReadOnly.Should().BeTrue("另一个进程正在编辑这份文档");

            window.Save();

            File.Exists(SidecarPaths.User(path)).Should().BeFalse("只读的窗口不该动旁边那份文件");

            window.Close();
        });
    }

    /// <summary>
    /// 保存一份 DSL 不动人工产物。
    /// </summary>
    /// <remarks>
    /// DSL 那一形态的固定位置写在文本里（<c>pin</c>），而两种形态共用同名同目录的
    /// 人工产物——那一条路要是也去写它，打开一份 <c>.dsl</c> 再保存就会改掉另一份
    /// <c>.dgm</c> 的固定位置。宁可让折点在这一形态下存不下来。
    /// </remarks>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task Saving_a_dsl_document_does_not_touch_the_sidecar()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = temp.File("flow.dsl");

            File.WriteAllText(path, """
                dsl 1
                kind flowchart
                direction TB

                start "开始" shape=stadium
                check "校验" shape=diamond

                start -> check
                """);

            var window = HeadlessFixture.Open(DocumentLaunch.FromPath(path));

            window.Session.Document.Nodes.Should().HaveCount(2, "这份文本要真的读成了一份文档");
            window.Save();

            File.Exists(SidecarPaths.User(path)).Should().BeFalse("DSL 那一形态不碰人工产物");

            window.Close();
        });
    }

    /// <summary>
    /// 打开一份文档时顺手清一次过老的备份。
    /// </summary>
    /// <remarks>
    /// 保存那一次由写入路径自己带，定时那一次由窗口排，剩下的就是打开这一下。
    /// 三处少任何一处，备份都会在某条路上无限攒下去。
    /// </remarks>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task Opening_a_document_prunes_the_backups_beyond_the_policy()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = WriteDocument(temp, "orders.json");

            SidecarStore.SaveUser(path, new UserSidecar
            {
                DocumentId = DocumentId,
                PinnedNodes = Pins(("check", 120, 240)),
            });

            // 攒够比上限多的备份。每份差一小时，名字因此不会撞上。
            for (var hours = 1; hours <= BackupPolicy.Default.MaxCount + 2; hours++)
            {
                SidecarBackup.Create(path, DateTimeOffset.UtcNow.AddHours(-hours));
            }

            SidecarBackup.List(path).Should().HaveCount(
                BackupPolicy.Default.MaxCount + 2,
                "先确认攒够了，否则下面验的是空集");

            var window = HeadlessFixture.Open(DocumentLaunch.File(path));

            SidecarBackup.List(path).Should().HaveCount(
                BackupPolicy.Default.MaxCount,
                "打开时清一次，留下策略允许的那几份");

            window.Close();
        });
    }

    #endregion

    #region 恢复（Category=Sidecar）

    /// <summary>
    /// 人工产物坏了之后从备份恢复，固定位置回来。
    /// </summary>
    /// <remarks>
    /// 这一条是恢复那条路唯一说得清的后果：坏文件还在原地，
    /// 用户点"从备份恢复"之后他摆过的位置要真的回到图上。
    /// </remarks>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task Restoring_from_a_backup_puts_the_pins_back()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = WriteDocument(temp, "orders.json");

            SidecarStore.SaveUser(path, new UserSidecar
            {
                DocumentId = DocumentId,
                PinnedNodes = Pins(("check", 120, 240)),
            });

            // 先存一次：这一次会把上面那一份备份下来，备份里因此留着这条固定位置。
            var first = HeadlessFixture.Open(DocumentLaunch.File(path));

            first.Save();
            first.Close();

            SidecarBackup.List(path).Should().NotBeEmpty("保存要先给旧的留一份");

            // 把当前那一份弄坏。打开时它会读不出来，于是弹提示。
            File.WriteAllText(SidecarPaths.User(path), "{ 这不是 JSON");

            var window = HeadlessFixture.Open(DocumentLaunch.File(path));
            var dialog = HeadlessFixture.SidecarRecovery(window);

            dialog.IsVisible.Should().BeTrue();

            HeadlessFixture.Button(dialog, Strings.SidecarRestore)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            dialog.IsVisible.Should().BeFalse("选完了要把提示收掉");
            window.Session.PinnedNodes.Should().ContainKey("check", "备份里那一份要真的装回来");
            window.Session.PinnedNodes["check"].Should().Be(new Anchor(120, 240));

            window.Close();
        });
    }

    /// <summary>
    /// 用户选"放弃人工调整"时，什么都不装，也不去删那份坏文件。
    /// </summary>
    /// <remarks>
    /// 顺手清掉的话，用户连"坏成什么样"都看不到了。下一次保存会用会话里这一份
    /// 覆盖它，而按"解析不了就不备份"的规矩，坏内容不会进备份列表。
    /// </remarks>
    [Fact]
    [Trait("Category", "Sidecar")]
    public async Task Discarding_the_recovery_leaves_the_document_alone()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            var path = WriteDocument(temp, "orders.json");

            File.WriteAllText(SidecarPaths.User(path), "{ 这不是 JSON");

            var window = HeadlessFixture.Open(DocumentLaunch.File(path));
            var dialog = HeadlessFixture.SidecarRecovery(window);

            dialog.IsVisible.Should().BeTrue();

            HeadlessFixture.Button(dialog, Strings.SidecarDiscard)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            dialog.IsVisible.Should().BeFalse();
            window.Session.PinnedNodes.Should().BeEmpty("放弃就是不装任何人工产物");
            File.Exists(SidecarPaths.User(path)).Should().BeTrue("坏文件留在原地，用户还能自己去看");

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>示例文档的标识。人工产物里写的标识与它不符时整份都作废。</summary>
    private const string DocumentId = "sample";

    /// <summary>往目录里放一份示例文档，返回它的全路径。</summary>
    private static string WriteDocument(TempDirectory temp, string name)
    {
        var path = temp.File(name);

        File.WriteAllText(path, DiagramSerializer.SerializeFull(SampleDiagram.Document()));

        return path;
    }

    /// <summary>拼一份固定位置表。</summary>
    private static Dictionary<string, Anchor> Pins(params (string Id, double X, double Y)[] entries) =>
        entries.ToDictionary(entry => entry.Id, entry => new Anchor(entry.X, entry.Y), StringComparer.Ordinal);

    /// <summary>在画布上拖一个节点，把它钉住。</summary>
    private static void Drag(MainWindow window, string nodeId, double dx, double dy)
    {
        var canvas = HeadlessFixture.Canvas(window);
        var from = HeadlessFixture.ToWindow(canvas, window, HeadlessFixture.CenterOf(canvas, nodeId));
        var to = new Point(from.X + dx, from.Y + dy);

        window.MouseDown(from, MouseButton.Left);

        for (var step = 1; step <= 3; step++)
        {
            var t = (double)step / 3;

            window.MouseMove(new Point(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t)));
        }

        window.MouseUp(to, MouseButton.Left);
    }

    #endregion
}
