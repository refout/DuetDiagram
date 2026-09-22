using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DuetDiagram.App;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Workspace;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 多窗口与多进程：同一个进程里两个窗口看同一份文档，第二个进程打开同一份文件时只读。
/// </summary>
/// <remarks>
/// <para>
/// 这一层盯的是"两个窗口看到的是不是同一份文档"以及"只读那一份能不能改到东西"。
/// 锁文件怎么加、心跳怎么判过期由核心层的用例管；这里验的是它接到窗口上之后的行为。
/// </para>
/// <para>
/// **"另一个进程"用一个先拿住锁的 <see cref="DocumentLock"/> 冒充。** 锁是按文件句柄独占的，
/// 同一个进程里再拿一次同样拿不到，所以这条路走的是真实的那一条：拿不到独占就退成只读。
/// 真正跨进程那一半（进程被强杀之后句柄什么时候回收）在核心层量，那里才拿得到两个进程。
/// </para>
/// </remarks>
public sealed class MultiWindowTests
{
    #region 同一份文档

    [Fact]
    [Trait("Category", "MultiWindow")]
    public async Task Two_windows_on_one_document_share_it()
    {
        // 各开一份的话，两个窗口各有各的文档与历史栈：改一边另一边不动，
        // 而用户以为在看同一份文件。
        await HeadlessFixture.Run(() =>
        {
            var launch = DocumentLaunch.Sample();

            var first = Open(launch);
            var second = Open(launch.Again());

            second.Session.Document.Should().BeSameAs(first.Session.Document);
            second.Session.Workspace.Should().BeSameAs(first.Session.Workspace);
            second.Session.Bus.Should().BeSameAs(first.Session.Bus);

            // 按标识问，不问登记表里一共几条：同一个进程里还跑着别的用例，
            // 它们各自开着别的文档，总数与这一条要断言的事无关。
            WorkspaceRegistry.Shared.LeaseCount(launch.Key).Should().Be(2, "两个窗口看的是同一份文档");

            second.Close();
            first.Close();

            WorkspaceRegistry.Shared.LeaseCount(launch.Key).Should().Be(0, "两个都关掉之后这一份就该释放了");
        });
    }

    [Fact]
    [Trait("Category", "MultiWindow")]
    public async Task An_edit_in_one_window_shows_up_in_the_other()
    {
        await HeadlessFixture.Run(() =>
        {
            var launch = DocumentLaunch.Sample();

            var first = Open(launch);
            var second = Open(launch.Again());

            first.Session.Select("pass");
            first.Session.Apply("label", "已通过").IsEffectiveSuccess.Should().BeTrue();

            Settle(second);

            second.Session.Document.Version.Should()
                .Be(first.Session.Document.Version, "两个窗口看的是同一份文档，版本号不该分叉");

            Labels(second).Should()
                .Contain("已通过", "另一个窗口的画面要跟着刷新，不然它显示的还是改动之前那张图");

            second.Close();
            first.Close();
        });
    }

    [Fact]
    [Trait("Category", "MultiWindow")]
    public async Task Closing_one_window_keeps_the_other_refreshing()
    {
        // 先关掉的那个窗口不能把工作区释放掉：释放早了，剩下那个窗口的广播器已经关了，
        // 症状是"界面莫名不再刷新"，很难与别的毛病区分开。
        await HeadlessFixture.Run(() =>
        {
            var launch = DocumentLaunch.Sample();

            var first = Open(launch);
            var second = Open(launch.Again());

            first.Close();

            WorkspaceRegistry.Shared.LeaseCount(launch.Key).Should().Be(1, "关掉一个窗口之后还剩一个");

            second.Session.Select("fail");
            second.Session.Apply("label", "不通过").IsEffectiveSuccess.Should().BeTrue();

            Settle(second);

            Labels(second).Should().Contain("不通过", "剩下那个窗口自己改的东西也要能刷新");

            second.Close();

            WorkspaceRegistry.Shared.LeaseCount(launch.Key).Should().Be(0, "最后一个窗口关掉之后这一份才释放");
        });
    }

    [Fact]
    [Trait("Category", "MultiWindow")]
    public async Task The_shortcut_opens_another_window_on_the_same_document()
    {
        await HeadlessFixture.Run(() =>
        {
            var launch = DocumentLaunch.Sample();
            var window = Open(launch);

            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Control);

            WorkspaceRegistry.Shared.LeaseCount(launch.Key).Should().Be(2, "快捷键要真的开出第二个窗口");

            window.Close();
        });
    }

    #endregion

    #region 只读

    [Fact]
    [Trait("Category", "MultiWindow")]
    public async Task A_second_process_gets_a_read_only_window()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var path = temp.File("doc.json");

            File.WriteAllText(path, DiagramSerializer.SerializeFull(SampleDiagram.Document()));

            // 冒充另一个进程：先拿住这份文档的独占所有权。
            using var other = DocumentLock.Acquire(path);
            other.CanWrite.Should().BeTrue("第一个进程应当是能写的");

            var window = Open(DocumentLaunch.File(path));

            window.Session.IsReadOnly.Should().BeTrue("另一个进程正在编辑这份文档");
            window.Status.HasReadOnlyNote.Should().BeTrue("状态栏要给出提示，不然用户不知道为什么改不动");
            window.Status.ReadOnlyNote.Should().Contain("只读");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "MultiWindow")]
    public async Task A_read_only_window_disables_every_write_entry()
    {
        // 留一个能点的按钮就是一个能造成损坏的入口，所以这一条逐个入口验。
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var path = temp.File("doc.json");

            File.WriteAllText(path, DiagramSerializer.SerializeFull(SampleDiagram.Document()));

            using var other = DocumentLock.Acquire(path);
            var window = Open(DocumentLaunch.File(path));
            var canvas = HeadlessFixture.Canvas(window);

            window.Session.Select("pass");

            // 字段：文本框进只读档，不是禁用档——禁用的框连选都选不中，用户没法把内容复制出去。
            HeadlessFixture.Editor<TextBox>(window, "label").IsReadOnly.Should().BeTrue();

            // 约束：选中两个节点之后那两颗按钮本该亮着，只读时仍然要灰着。
            window.Session.Toggle("fail");

            NamedButton(window, "加同层").IsEnabled.Should().BeFalse();
            NamedButton(window, "加对齐").IsEnabled.Should().BeFalse();

            // 画布：拖一下什么也不该落定。
            var center = HeadlessFixture.CenterOf(canvas, "pass");
            var start = HeadlessFixture.ToWindow(canvas, window, center);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(40, 20));
            window.MouseUp(start + new Vector(40, 20), MouseButton.Left);

            window.Session.PinnedNodes.Should().BeEmpty("只读时拖动不该落定任何固定位置");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "MultiWindow")]
    public async Task A_read_only_window_refuses_writes_and_leaves_the_file_alone()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var path = temp.File("doc.json");

            var original = DiagramSerializer.SerializeFull(SampleDiagram.Document());

            File.WriteAllText(path, original);

            using var other = DocumentLock.Acquire(path);
            var window = Open(DocumentLaunch.File(path));

            window.Session.Select("pass");

            var refused = window.Session.Apply("label", "改不动");

            refused.IsSuccess.Should().BeFalse();
            refused.Errors.Should()
                .Contain(error => error.Code == ErrorCodes.DocumentReadOnly, "挡住写入的是会话那一层，界面上的入口不止一处");

            // 存盘那一条路也要挡：只读的进程把改动写回去，就等于两个进程同时在写同一个文件。
            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);

            File.ReadAllText(path).Should().Be(original, "只读的那一份一个字节都不该写回去");

            window.Close();
        });
    }

    #endregion

    #region 抢占之后重新校验

    [Fact]
    [Trait("Category", "MultiWindow")]
    public async Task An_unclean_handover_refuses_a_document_that_does_not_validate()
    {
        // 心跳文件还在说明上一个进程没好好退出，而它可能正好写到一半。
        // 照常打开的话，用户会在一份截断的图上继续编辑，然后把它存回去。
        using var temp = new TempDirectory();
        var path = temp.File("doc.json");

        var broken = DiagramDocument.CreateFromContent(
            "broken",
            nodes: [new NodeDef { Id = "a", Label = "甲" }],
            edges: [new EdgeDef { Id = "e1", From = "a", To = "missing" }]);

        File.WriteAllText(path, DiagramSerializer.SerializeFull(broken));
        File.WriteAllText(DocumentLock.HeartbeatPath(path), DateTimeOffset.UtcNow.ToString("O"));

        var open = () => DocumentLaunch.File(path);

        open.Should().Throw<InvalidDataException>();
    }

    [Fact]
    [Trait("Category", "MultiWindow")]
    public async Task An_unclean_handover_still_opens_a_document_that_validates()
    {
        // 抢占本身不是错：上一个进程可能是被强杀的，而文档是好的。
        // 一并拒掉的话，用户会以为自己的文件坏了。
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var path = temp.File("doc.json");

            File.WriteAllText(path, DiagramSerializer.SerializeFull(SampleDiagram.Document()));
            File.WriteAllText(DocumentLock.HeartbeatPath(path), DateTimeOffset.UtcNow.ToString("O"));

            var window = Open(DocumentLaunch.File(path));

            window.Session.IsReadOnly.Should().BeFalse("抢到了独占，这一份是能写的");
            window.DocumentPath.Should().Be(Path.GetFullPath(path));

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "MultiWindow")]
    public async Task A_writable_window_writes_the_document_back()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var path = temp.File("doc.json");

            File.WriteAllText(path, DiagramSerializer.SerializeFull(SampleDiagram.Document()));

            var window = Open(DocumentLaunch.File(path));

            window.Session.Select("pass");
            window.Session.Apply("label", "已通过").IsEffectiveSuccess.Should().BeTrue();

            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);

            var written = DiagramSerializer.DeserializeFull(File.ReadAllText(path));

            written.Nodes.Single(node => node.Id == "pass").Label.Should()
                .Be("已通过", "能写的这一份按了存盘就要真的写回去");

            window.Close();
        });
    }

    #endregion

    #region 助手

    /// <summary>按说明开一个窗口，并把它排布好——不排布的话画布尺寸还是零。</summary>
    private static MainWindow Open(DocumentLaunch launch)
    {
        var window = new MainWindow(launch);

        window.Show();
        window.CaptureRenderedFrame();

        return window;
    }

    /// <summary>画面上那些标签的字。</summary>
    private static IReadOnlyList<string> Labels(MainWindow window) =>
        [.. window.Session.Scene.DrawList.Commands.OfType<DrawText>().Select(text => text.Text)];

    /// <summary>
    /// 等后台那条通知投递到，并把排队等界面线程的那次重算跑掉。
    /// </summary>
    /// <remarks>
    /// 重算是排队等界面线程的，而测试体本身就在那条线程上。光阻塞着等，那一份永远轮不到——
    /// 所以一边等一边把排着的活跑掉。等的是"绘制列表追上文档版本"这件事，
    /// 不是某一条通知，因此不会因为两条通知的先后而漏掉。
    /// </remarks>
    private static void Settle(MainWindow window)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (window.Session.SceneVersion < window.Session.Document.Version && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Dispatcher.UIThread.RunJobs();

        window.Session.SceneVersion.Should()
            .Be(window.Session.Document.Version, "别的窗口改完之后，这一份的绘制列表要跟上");
    }

    /// <summary>按按钮上那行字取按钮。</summary>
    private static Button NamedButton(MainWindow window, string label) =>
        window.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content as string, label, StringComparison.Ordinal));

    #endregion
}

/// <summary>
/// 用例专用的临时目录。
/// </summary>
/// <remarks>
/// 核心层的那个助手在另一个程序集里，拿不到。这一份只管一件事：用完把整个目录删掉，
/// 免得每跑一次就多留一批锁文件与心跳文件在系统临时目录里。
/// </remarks>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"duet-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响用例结论，留给系统临时目录自己收拾。
        }
    }
}
