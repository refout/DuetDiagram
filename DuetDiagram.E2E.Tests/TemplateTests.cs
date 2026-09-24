using Avalonia.Controls;
using Avalonia.Interactivity;
using DuetDiagram.App;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Templates;
using DuetDiagram.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 模板面板：目录里的模板列出来，点一个把它拼进画布。
/// </summary>
/// <remarks>
/// <para>
/// 操作一律经由界面上的按钮触发（点那一颗按钮），验的是"按钮把动作送到了面板"，
/// 而不是面板自己的方法。
/// </para>
/// <para>
/// 每个用例给窗口一个自己的模板目录。用程序旁边那个缺省目录的话，用例之间会通过
/// 残留的文件互相干扰——尤其是"存为模板"那一条，它真的往磁盘上写东西。
/// </para>
/// </remarks>
public sealed class TemplateTests
{
    #region 放入画布（Category=Template）

    /// <summary>点一个模板：里面的元素都进来了，而整份只算一次操作。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public async Task Clicking_a_template_puts_its_elements_in_and_one_undo_takes_them_all_back()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            scratch.Write("pair.template.json", """
                {
                  "id": "pair",
                  "nodes": [ { "id": "p1", "label": "一" }, { "id": "p2", "label": "二" } ],
                  "edges": [ { "id": "pe", "from": "p1", "to": "p2" } ]
                }
                """);

            var window = HeadlessFixture.Open(WithTemplates(scratch));

            var before = HistoryOf(window);
            var nodes = window.Session.Document.Nodes.Count;

            Press(window, "template.entry.pair").Should().Be(1, "整份模板算一次操作，撤销按一次就全回去");

            window.Session.Document.Nodes.Count.Should().Be(nodes + 2);
            window.Session.Document.Edges.Should().Contain(edge => edge.Id == "pe");

            window.Session.Undo();

            window.Session.Document.Nodes.Count.Should().Be(nodes, "撤销一次整份退回");
            window.Session.Document.Edges.Should().NotContain(edge => edge.Id == "pe");
            HistoryOf(window).Should().Be(before, "撤销本身不进历史");

            window.Close();
        });
    }

    /// <summary>模板里的标识与文档里已有的撞上时，改过名的那一份仍然放得进去。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public async Task A_template_whose_ids_collide_is_renamed_on_the_way_in()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            // 样例图里已经有一个叫 start 的节点。
            scratch.Write("reuse.template.json", """
                {
                  "id": "reuse",
                  "nodes": [ { "id": "start", "label": "另一个开始" }, { "id": "next" } ],
                  "edges": [ { "id": "se", "from": "start", "to": "next" } ]
                }
                """);

            var window = HeadlessFixture.Open(WithTemplates(scratch));

            Press(window, "template.entry.reuse").Should().Be(1);

            window.Session.Document.Nodes.Should().Contain(node => node.Id == "start-2");
            window.Session.Document.Edges.Should().Contain(edge => edge.Id == "se" && edge.From == "start-2");

            window.Close();
        });
    }

    /// <summary>一个文件读不出来，其余几个照常能用，坏的那一个带一句原因摆出来。</summary>
    /// <remarks>
    /// 整张清单一起空掉的话，其余能用的模板也跟着不见了，
    /// 而用户会以为模板功能坏了——坏掉的只是他手写的那一个。
    /// </remarks>
    [Fact]
    [Trait("Category", "Template")]
    public async Task A_broken_template_file_is_reported_without_hiding_the_good_ones()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            scratch.Write("good.template.json", """{ "id": "good", "nodes": [ { "id": "n1" } ] }""");

            // 模板带不了图层，这个文件会被拒。
            scratch.Write("broken.template.json", """
                { "id": "broken", "layers": [ { "id": "l1" } ], "nodes": [ { "id": "n1" } ] }
                """);

            var window = HeadlessFixture.Open(WithTemplates(scratch));

            window.Templates.Rows.Should().ContainSingle().Which.Name.Should().Be("good");
            window.Templates.Failures.Should().ContainSingle()
                .Which.File.Should().Be("broken.template.json");
            window.Templates.Failures[0].Reason.Should().Contain("图层");

            // 能用的那一个照常放得进去。
            Press(window, "template.entry.good").Should().Be(1);
            window.Session.Document.Nodes.Should().Contain(node => node.Id == "n1");

            window.Close();
        });
    }

    /// <summary>
    /// 程序自带的那个模板在清单里。
    /// </summary>
    /// <remarks>
    /// 这一条盯的是"模板文件有没有跟着构建输出走"。不声明的话开发机上一切正常
    /// （跑的是源码目录），而装好之后模板列表是空的——那种空看起来像"这一轮没做"。
    /// </remarks>
    [Fact]
    [Trait("Category", "Template")]
    public async Task The_template_that_ships_with_the_program_is_in_the_list()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Templates.Rows.Should().Contain(row => row.Name == "basic-flow");
            Press(window, "template.entry.basic-flow").Should().Be(1);

            window.Close();
        });
    }

    /// <summary>只读的那一份不许拼：另一个进程正拿着这份文件。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public async Task A_read_only_document_refuses_the_template()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();
            scratch.Write("pair.template.json", """{ "id": "pair", "nodes": [ { "id": "p1" } ] }""");

            // 另一个进程正拿着这份文档，于是这一份退成只读。
            var path = scratch.Write("doc.json", DiagramSerializer.SerializeFull(SampleDiagram.Document()));

            using var other = DocumentLock.Acquire(path);
            var window = HeadlessFixture.Open(
                DocumentLaunch.File(path, templates: new TemplateCatalog(scratch.Path)));

            window.Session.IsReadOnly.Should().BeTrue("另一个进程正在编辑这份文档");

            var nodes = window.Session.Document.Nodes.Count;

            Press(window, "template.entry.pair").Should().Be(0, "只读时一条命令都不发");
            window.Templates.Error.Should().NotBeNull("挡下来要说一句");
            window.Session.Document.Nodes.Count.Should().Be(nodes);

            window.Close();
        });
    }

    #endregion

    #region 存为模板（Category=Template）

    /// <summary>把选中的东西存成模板：目录里多一个文件，面板上多一行。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public async Task Saving_the_selection_writes_a_template_and_the_panel_lists_it()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var window = HeadlessFixture.Open(WithTemplates(scratch));

            window.Templates.Rows.Should().BeEmpty("目录一开始是空的");

            window.Session.SetSelection(["start", "check"]);
            window.Invoke("file.save-template");

            window.Templates.Error.Should().BeNull();
            window.Templates.Note.Should().NotBeNull();
            window.Templates.Rows.Should().ContainSingle().Which.Name.Should().Be("sample");

            var file = scratch.Files().Single();
            Path.GetFileName(file).Should().Be("sample.template.json");

            // 写出去的那一份读得回来，而且只带两端都在选中里的那条边。
            var saved = TemplateDocument.Load(File.ReadAllText(file));

            saved.Nodes.Select(node => node.Id).Should().Equal("start", "check");
            saved.Edges.Should().ContainSingle().Which.Id.Should().Be("e1");

            window.Close();
        });
    }

    /// <summary>没有选中东西时「存为模板」给一句理由，不写文件。</summary>
    [Fact]
    [Trait("Category", "Template")]
    public async Task Saving_without_a_selection_is_refused()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var window = HeadlessFixture.Open(WithTemplates(scratch));

            window.Session.SetSelection([]);
            window.Invoke("file.save-template");

            window.Status.Message.Should().Contain("先选中", "挡住它的理由要说在用户看得见的地方");
            window.Templates.Rows.Should().BeEmpty();
            scratch.Files().Should().BeEmpty("没有选中就什么都不该写出去");

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>一个用完就删的目录，装这个用例自己的模板。</summary>
    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"duet-e2e-{Guid.NewGuid():N}");

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        /// <summary>往目录里放一个文件，返回它的全路径。</summary>
        public string Write(string name, string content)
        {
            var path = System.IO.Path.Combine(Path, name);

            File.WriteAllText(path, content);

            return path;
        }

        public string[] Files() => Directory.GetFiles(Path);

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

    /// <summary>开一份示例文档，模板从这个目录里找。</summary>
    private static DocumentLaunch WithTemplates(Scratch scratch) =>
        DocumentLaunch.Sample(templates: new TemplateCatalog(scratch.Path));

    /// <summary>历史里现在有几条。</summary>
    private static int HistoryOf(MainWindow window) =>
        window.Session.Bus.Context.History.UndoEntries().Count;

    /// <summary>点一颗按钮。</summary>
    /// <returns>这一下之后历史里多了几条。</returns>
    private static int Press(MainWindow window, string id)
    {
        var before = HistoryOf(window);

        HeadlessFixture.Editor<Button>(window, id)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        return HistoryOf(window) - before;
    }

    #endregion
}
