using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DuetDiagram.App;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 导入入口：选一份文件，把它导进当前文档。
/// </summary>
/// <remarks>
/// <para>
/// 选文件那一步在无头模式下打不开，所以用例走的是主窗口那个公开的导入入口
/// （<c>MainWindow.Import</c>），它与点菜单那条路是同一段代码——只有"文件从哪儿来"不同。
/// </para>
/// <para>
/// 每份内容都写在一个用完就删的目录里。用真实路径而不是内存里的字符串，
/// 是因为"读一份文件"这条路上会碰到编码、换行与路径这些东西，
/// 而它们正是真实用户手上那份文件会有的样子。
/// </para>
/// </remarks>
public sealed class ImportTests
{
    #region 落地（Category=Import）

    /// <summary>导进来之后画布上多出那些元素，撤销一次全部消失。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public async Task Importing_a_file_puts_its_elements_in_and_one_undo_takes_them_all_back()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("flow.mmd", """
                flowchart LR
                  A[下单] --> B[付款]
                  B --> C[发货]
                """);

            var window = HeadlessFixture.Open();

            var nodes = window.Session.Document.Nodes.Count;
            var history = HistoryOf(window);

            window.Import(path);

            window.Session.Document.Nodes.Count.Should().Be(nodes + 3);
            window.Session.Document.Nodes.Select(node => node.Label)
                .Should().Contain(["下单", "付款", "发货"]);
            window.Session.Document.Edges.Count.Should().BeGreaterThan(5, "样例图本来有五条边");

            HistoryOf(window).Should().Be(history + 1, "三条节点两条边只该进一条历史");

            window.Session.Undo();

            window.Session.Document.Nodes.Count.Should().Be(nodes);
            window.Session.Document.Nodes.Should().NotContain(node => node.Label == "下单");
            HistoryOf(window).Should().Be(history, "撤销本身不进历史");

            window.Close();
        });
    }

    /// <summary>
    /// 一份五百个节点的内容导进来也只算一条操作。
    /// </summary>
    /// <remarks>
    /// 这是"导入是一条命令"这条口径真正吃劲的地方：按元素发命令的话撤销要按一千次，
    /// 而用户在界面上做的是同一次「导入了这个文件」。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public async Task A_five_hundred_node_file_is_one_operation()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("big.mmd", Big());

            var window = HeadlessFixture.Open();

            var nodes = window.Session.Document.Nodes.Count;
            var history = HistoryOf(window);

            window.Import(path);

            window.Session.Document.Nodes.Count.Should().Be(nodes + 500);
            HistoryOf(window).Should().Be(history + 1);

            window.Session.Undo();

            window.Session.Document.Nodes.Count.Should().Be(nodes);

            window.Close();
        });
    }

    /// <summary>
    /// 导进来的标识与文档里已有的撞上时改过名的那一份仍然落得进去。
    /// </summary>
    /// <remarks>
    /// 样例图里已经有 e1 到 e5 五条边，而 Mermaid 的连线没有标识、导入时按 e1、e2……
    /// 依次生成，所以这条路上的重名是必然的，不是碰巧。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public async Task Edge_ids_that_collide_with_the_document_are_renamed()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("flow.mmd", """
                flowchart TB
                  A --> B
                """);

            var window = HeadlessFixture.Open();

            window.Import(path);

            // 原来那五条边一条都没被动过，导进来的那条按"接一个序号"改名。
            window.Session.Document.Edges.Select(edge => edge.Id)
                .Should().Contain(["e1", "e2", "e3", "e4", "e5"]);

            window.Session.Document.Edges.Should().Contain(edge => edge.Id == "e1-2");
            window.Session.Document.Edges.Single(edge => edge.Id == "e1-2").From.Should()
                .NotBe("start", "它连的是导进来的那两个节点，不是文档里原来那个 e1");

            window.Close();
        });
    }

    /// <summary>代码围栏里的内容也认得出来——真实回答大多整段贴在围栏里。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public async Task A_fenced_block_is_imported()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("answer.txt", """
                下面是一张图：

                ```mermaid
                flowchart TB
                  A[甲] --> B[乙]
                ```
                """);

            var window = HeadlessFixture.Open();

            window.Import(path);

            window.Session.Document.Nodes.Should().Contain(node => node.Label == "甲");
            HeadlessFixture.ImportReport(window).IsVisible.Should().BeTrue();

            window.Close();
        });
    }

    #endregion

    #region 报告（Category=Import）

    /// <summary>报告摆出来，点名了文件与导进去多少。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public async Task The_report_names_the_file_and_what_came_in()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("flow.mmd", """
                flowchart TB
                  A[甲] --> B[乙]
                """);

            var window = HeadlessFixture.Open();

            HeadlessFixture.ImportReport(window).IsVisible.Should().BeFalse("还没导入时不该挂着");

            window.Import(path);

            var report = HeadlessFixture.ImportReport(window);

            report.IsVisible.Should().BeTrue();
            report.File.Should().Be("flow.mmd");
            report.Notes.Should().BeEmpty("一份干净的内容没有要交代的");

            window.Close();
        });
    }

    /// <summary>
    /// 认不出的内容记成要摆在界面上的一句话，能认的那部分照常进来。
    /// </summary>
    /// <remarks>
    /// 一句话都不说的话，用户拿到的是一张少了一条线的图，
    /// 而他无从知道是原文里就没有、还是程序没做。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public async Task A_line_that_was_not_understood_shows_up_in_the_report()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            // click 是 Mermaid 的指令，IR 里没有对应位置，解析器会记一条诊断跳过去。
            var path = scratch.Write("flow.mmd", """
                flowchart TB
                  A[甲] --> B[乙]
                  click A "https://example.com"
                """);

            var window = HeadlessFixture.Open();

            window.Import(path);

            window.Session.Document.Nodes.Should().Contain(node => node.Label == "甲");

            var report = HeadlessFixture.ImportReport(window);

            report.Notes.Should().NotBeEmpty("认不出的那一行要说出来");
            report.Notes.Should().Contain(note => note.Contains("第 3 行"));

            window.Close();
        });
    }

    /// <summary>
    /// 没映射进来的样式属性逐条列出来。
    /// </summary>
    /// <remarks>
    /// 丢样式比丢节点隐蔽得多：图还是画得出来，只是少了一处颜色，
    /// 而"少了一处"在图上几乎看不出来。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public async Task A_style_property_that_was_dropped_shows_up_in_the_report()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("flow.mmd", """
                flowchart TB
                  A[甲] --> B[乙]
                  style A stroke-dasharray:5 5
                """);

            var window = HeadlessFixture.Open();

            window.Import(path);

            HeadlessFixture.ImportReport(window).Notes.Should()
                .Contain(note => note.Contains("stroke-dasharray"));

            window.Close();
        });
    }

    /// <summary>
    /// 内容写的方向与文档现在的方向不一致时留一句话。
    /// </summary>
    /// <remarks>
    /// 导进来的是「片段」，不是整份文档，片段里没有方向这个字段，所以方向跟着文档走。
    /// 不说的话，用户看到的是"我明明写的是从左到右，导进来怎么是竖的"。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public async Task The_report_says_when_the_content_points_another_way()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("flow.mmd", """
                flowchart LR
                  A[甲] --> B[乙]
                """);

            var window = HeadlessFixture.Open();

            window.Session.Document.Direction.Should().Be(Direction.TB, "样例图是竖的");

            window.Import(path);

            HeadlessFixture.ImportReport(window).Notes.Should()
                .Contain(note => note.Contains("从左到右") && note.Contains("从上到下"));

            // 方向确实没被改掉：改的话整张图会重排，而用户只是导进来一段内容。
            window.Session.Document.Direction.Should().Be(Direction.TB);

            window.Close();
        });
    }

    /// <summary>方向本来就一致时不留那句话。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public async Task Nothing_is_said_when_the_directions_agree()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("flow.mmd", """
                flowchart TB
                  A[甲] --> B[乙]
                """);

            var window = HeadlessFixture.Open();

            window.Import(path);

            HeadlessFixture.ImportReport(window).Notes.Should()
                .NotContain(note => note.Contains("方向"));

            window.Close();
        });
    }

    /// <summary>关掉之后报告清干净，下一次导入不会看到上一次留下的那几句。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public async Task Closing_the_report_clears_it()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("flow.mmd", """
                flowchart TB
                  A[甲] --> B[乙]
                  style A stroke-dasharray:5 5
                """);

            var window = HeadlessFixture.Open();

            window.Import(path);

            var report = HeadlessFixture.ImportReport(window);

            report.Notes.Should().NotBeEmpty();

            HeadlessFixture.Button(report, "关闭")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            report.IsVisible.Should().BeFalse();
            report.Notes.Should().BeEmpty("关掉之后单子要清干净");
            report.File.Should().BeNull();

            window.Close();
        });
    }

    #endregion

    #region 拒绝（Category=Import）

    /// <summary>
    /// 一份不是 Mermaid 的文件被拒，文档一个字都不动。
    /// </summary>
    /// <remarks>
    /// 按扩展名判的话，一份 .txt 里的 Mermaid 导不进来；按内容判的话，
    /// 一份 .txt 里的购物清单也导不进来，而这两件事要说的话不一样。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public async Task A_file_that_is_not_mermaid_is_refused_without_touching_the_document()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("notes.txt", "买牛奶\n买鸡蛋\n");

            var window = HeadlessFixture.Open();

            var before = window.Session.Document.Nodes.Count;
            var history = HistoryOf(window);

            window.Import(path);

            var report = HeadlessFixture.ImportReport(window);

            report.IsVisible.Should().BeTrue("被拒了也要说一句，否则用户以为是自己点错了");
            report.File.Should().Be("notes.txt");

            window.Session.Document.Nodes.Count.Should().Be(before);
            HistoryOf(window).Should().Be(history, "一条元素都没落进去，就不该进历史");

            window.Close();
        });
    }

    /// <summary>
    /// 认得出来但我们表达不了的图类型被拒，说的理由是解析器自己那一句。
    /// </summary>
    /// <remarks>
    /// 硬解析出来的是垃圾，而垃圾比空结果更糟——它看起来像是导入成功了。
    /// </remarks>
    [Fact]
    [Trait("Category", "Import")]
    public async Task A_diagram_kind_we_cannot_express_is_refused_in_the_parsers_own_words()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("seq.mmd", """
                sequenceDiagram
                  甲->>乙: 你好
                """);

            var window = HeadlessFixture.Open();

            var before = window.Session.Document.Nodes.Count;

            window.Import(path);

            var report = HeadlessFixture.ImportReport(window);

            report.IsVisible.Should().BeTrue();
            report.Notes.Should().Contain(note => note.Contains("Sequence"), "要说明是哪种图被拒了");

            window.Session.Document.Nodes.Count.Should().Be(before);

            window.Close();
        });
    }

    /// <summary>解析得通但里面一条元素都没有：拒绝，且不留下空片段。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public async Task A_file_with_nothing_to_import_is_refused()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("empty.mmd", "flowchart LR\n");

            var window = HeadlessFixture.Open();

            var history = HistoryOf(window);

            window.Import(path);

            var report = HeadlessFixture.ImportReport(window);

            report.IsVisible.Should().BeTrue();
            report.Notes.Should().BeEmpty("一条元素都没导进来，就不谈方向对不对");

            HistoryOf(window).Should().Be(history);

            window.Close();
        });
    }

    /// <summary>只读的那一份不许导入：另一个进程正拿着这份文件。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public async Task A_read_only_document_refuses_the_import()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var source = scratch.Write("flow.mmd", """
                flowchart TB
                  A[甲] --> B[乙]
                """);

            // 另一个进程正拿着这份文档，于是这一份退成只读。
            var document = scratch.Write("doc.json", DiagramSerializer.SerializeFull(SampleDiagram.Document()));

            using var other = DocumentLock.Acquire(document);

            var window = HeadlessFixture.Open(DocumentLaunch.File(document));

            window.Session.IsReadOnly.Should().BeTrue("另一个进程正在编辑这份文档");

            var before = window.Session.Document.Nodes.Count;
            var history = HistoryOf(window);

            window.Import(source);

            window.Session.Document.Nodes.Count.Should().Be(before);
            HistoryOf(window).Should().Be(history, "只读时一条命令都不发");

            HeadlessFixture.ImportReport(window).IsVisible.Should().BeTrue("挡下来要说一句");

            window.Close();
        });
    }

    /// <summary>只读时菜单上那一条点不动，理由与命令层拒绝写入时说的是同一句。</summary>
    [Fact]
    [Trait("Category", "Import")]
    public async Task The_menu_entry_is_disabled_when_the_document_is_read_only()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var document = scratch.Write("doc.json", DiagramSerializer.SerializeFull(SampleDiagram.Document()));

            using var other = DocumentLock.Acquire(document);

            var window = HeadlessFixture.Open(DocumentLaunch.File(document));

            var entry = HeadlessFixture.MenuBar(window).Find("file.import");

            entry.Should().NotBeNull("导入要有一个入口，否则人在界面上够不着它");
            entry!.IsEnabled.Should().BeFalse();

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>一份用完就删的目录，装这个用例自己的文件。</summary>
    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"duet-import-{Guid.NewGuid():N}");

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        /// <summary>往目录里放一个文件，返回它的全路径。</summary>
        public string Write(string name, string content)
        {
            var path = System.IO.Path.Combine(Path, name);

            // 显式写 UTF-8 且不带字节序标记：中文标签走的是默认编码的话，
            // 在别的机器上会读成乱码，而那种失败看起来像"导入丢了字"。
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            return path;
        }

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

    /// <summary>一条链式的内容：<c>n1 --&gt; n2 --&gt; …</c>，一共 500 个节点。</summary>
    private static string Big()
    {
        var text = new StringBuilder("flowchart TB\n");

        for (var i = 1; i <= 500; i++)
        {
            text.Append("  n").Append(i).Append("[节点").Append(i).Append(']').Append('\n');
        }

        for (var i = 1; i < 500; i++)
        {
            text.Append("  n").Append(i).Append(" --> n").Append(i + 1).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>历史里现在有几条。</summary>
    private static int HistoryOf(MainWindow window) =>
        window.Session.Bus.Context.History.UndoEntries().Count;

    #endregion
}
