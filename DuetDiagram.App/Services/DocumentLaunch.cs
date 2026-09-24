using DuetDiagram.Core.Model;
using DuetDiagram.Core.Workspace;
using DuetDiagram.Layout;

namespace DuetDiagram.App.Services;

/// <summary>
/// 一个窗口要用的那份工作区，外加它背后的文件与跨进程所有权。
/// </summary>
/// <param name="Workspace">这份文档的工作区，多窗口共用同一份。</param>
/// <param name="Lock">
/// 这份文档的跨进程所有权。文档不是从文件打开的（例如示例文档）时为空——
/// 那种文档没有文件，也就没有第二个进程会来抢。
/// </param>
/// <param name="Path">文档文件的路径。没有文件时为空。</param>
/// <param name="ReadOnly">这一份是不是只读的：另一个进程正拿着这份文件时为真。</param>
/// <param name="Reason">只读的原因，一句话。可写时为空。</param>
/// <remarks>
/// 这几样东西的生命周期是一致的：文件路径决定了锁，锁决定了能不能写，
/// 而工作区是那份文档本身。分开传的话，调用方迟早会把它们配错——
/// 配错的表现是"能写的那一份拿了个只读的工作区"，而那种组合不报错，只是改不动。
/// </remarks>
public sealed record WorkspaceSetup(
    DiagramWorkspace Workspace,
    DocumentLock? Lock,
    string? Path,
    bool ReadOnly,
    string? Reason);

/// <summary>
/// 开一个窗口要看哪份文档。
/// </summary>
/// <remarks>
/// <para>
/// **它只描述"看哪份"，不负责建工作区。** 工作区由进程内的登记表按标识取，
/// 第一次取的时候才调这里的工厂造一个。这样第二个窗口拿到的是第一个窗口那份，
/// 而不是自己再建一份——各建一份的话两个窗口各有各的文档与历史栈，
/// 改一边另一边不动，而用户以为在看同一份文件。
/// </para>
/// <para>
/// **标识决定"是不是同一份文档"。** 从文件打开的用文件全路径：两个窗口打开同一个文件，
/// 路径相同，于是共用。示例文档每次给一个新标识——测试里会同时开着好几个窗口，
/// 标识固定的话，第二个用例会拿到第一个用例改过的文档。
/// </para>
/// </remarks>
public sealed class DocumentLaunch
{
    private DocumentLaunch(
        string key,
        ILayoutEngine? engine,
        TemplateCatalog templates,
        Func<WorkspaceSetup> create)
    {
        Key = key;
        Engine = engine;
        Templates = templates;
        Create = create;
    }

    /// <summary>进程内登记用的标识。同一个标识就是同一份文档。</summary>
    public string Key { get; }

    /// <summary>布局引擎。注入的用途只有一个：让"布局彻底失败"这条路径能被真正走到。</summary>
    public ILayoutEngine? Engine { get; }

    /// <summary>
    /// 从哪儿找模板。
    /// </summary>
    /// <remarks>
    /// 它挂在这里而不是让面板自己去拼一个路径：装好之后运行目录未必是开发机上那一个，
    /// 写死相对路径的症状是"模板列表是空的"，且没有任何报错。
    /// 与布局引擎同一个位置，理由也一样——**这一层是窗口依赖的注入点**。
    /// </remarks>
    public TemplateCatalog Templates { get; }

    /// <summary>第一次打开这份文档时怎么建工作区。</summary>
    public Func<WorkspaceSetup> Create { get; }

    /// <summary>
    /// 开一份示例文档。
    /// </summary>
    /// <remarks>
    /// 标识每次都换一个：它没有文件，也就没有"同一份"可言。
    /// 用一个固定标识的话，同一个进程里开出来的第二个示例窗口会共用第一个的文档，
    /// 而那在用户看来是"我新开了一个窗口，里面却是刚才那张图"。
    /// </remarks>
    public static DocumentLaunch Sample(ILayoutEngine? engine = null, TemplateCatalog? templates = null) =>
        new(
            $"sample:{Guid.NewGuid():N}",
            engine,
            templates ?? new TemplateCatalog(),
            () => new WorkspaceSetup(
                DiagramSession.CreateWorkspace(SampleDiagram.Document()),
                Lock: null,
                Path: null,
                ReadOnly: false,
                Reason: null));

    /// <summary>
    /// 打开一个文件。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="engine">布局引擎。</param>
    /// <exception cref="InvalidDataException">文件读不出来、不是合法文档，或者被强杀之后没能通过校验。</exception>
    /// <remarks>
    /// <para>
    /// **拿不到独占不报错，退成只读。** "别人正在编辑"是一种正常情形，
    /// 用户要看到的是这份文档的内容加一句说明，而不是一个打不开的窗口。
    /// </para>
    /// <para>
    /// **抢占之后必须重新校验。** 心跳文件还在说明上一个进程没好好退出，
    /// 而它可能正好写到一半。校验不通过就不打开——照常打开的话，
    /// 用户会在一份截断的图上继续编辑，然后把它存回去，那时坏掉的就不只是内存里的那一份了。
    /// </para>
    /// </remarks>
    public static DocumentLaunch File(string path, ILayoutEngine? engine = null, TemplateCatalog? templates = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        var catalog = templates ?? new TemplateCatalog();

        // 文件在不在先看一眼，再动锁。反过来做的话，打开一个写错的路径会在那份文档旁边
        // 留下一个空的锁文件——而它本来就不存在，那个文件是凭空多出来的。
        if (!System.IO.File.Exists(full))
        {
            throw new InvalidDataException($"这份文档不在：{full}");
        }

        var documentLock = DocumentLock.Acquire(full);

        try
        {
            var issues = new List<ValidationIssue>();
            var document = DocumentFile.Read(full, issues);

            if (documentLock.TookOver && issues.Count > 0)
            {
                throw new InvalidDataException(
                    $"上一个进程没有正常退出，而这份文档没能通过校验：{issues[0].Message}");
            }

            return new DocumentLaunch(
                full,
                engine,
                catalog,
                () => new WorkspaceSetup(
                    DiagramSession.CreateWorkspace(document),
                    documentLock,
                    full,
                    ReadOnly: !documentLock.CanWrite,
                    Reason: documentLock.Reason));
        }
        catch
        {
            // 开不起来就把所有权还回去。留着的话，这一个进程已经不再编辑这份文档了，
            // 却还占着它，别的进程只能只读——而那个进程明明才是唯一在看它的人。
            documentLock.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 再开一个窗口看同一份文档。
    /// </summary>
    /// <remarks>
    /// 标识与工厂都沿用，所以第二次取到的是登记表里已经有的那一份工作区，
    /// 而不是新造一个。窗口各开一份工作区的话，两个窗口各有各的撤销栈，
    /// 在一边撤销不会动另一边——而它们显示的是同一份文档。
    /// </remarks>
    public DocumentLaunch Again() => new(Key, Engine, Templates, Create);
}
