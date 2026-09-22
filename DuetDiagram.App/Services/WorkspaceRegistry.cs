using DuetDiagram.Core.Workspace;

namespace DuetDiagram.App.Services;

/// <summary>
/// 进程内的文档工作区表：同一份文档只留一个工作区，多个窗口共用它。
/// </summary>
/// <remarks>
/// <para>
/// **各开一份的话，两个窗口各有各的文档与历史栈。** 改一边另一边不动，
/// 而用户以为在看同一份文件——等他发现时已经分不清哪一边才是自己要的版本了。
/// 共用一份之后，版本号天然一致，也不存在版本冲突：冲突是两个副本之间的事，
/// 而这里根本没有第二个副本。
/// </para>
/// <para>
/// **引用计数是必需的：先关掉的那个窗口不能把工作区释放掉。** 释放早了，
/// 剩下那个窗口的广播器已经关了，症状是"界面莫名不再刷新"，很难与别的毛病区分开。
/// </para>
/// <para>
/// **跨进程的那把锁也挂在这张表上，不挂在窗口上。** 锁说的是"这个进程在编辑这份文件"，
/// 而进程里可能开着好几个窗口看它。每个窗口各去加一次锁的话，第二个窗口会撞上
/// 第一个窗口自己加的那把锁，于是同一个进程的第二个窗口变成只读——
/// 而它本该和第一个窗口看到完全一样的东西。
/// </para>
/// </remarks>
public sealed class WorkspaceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>进程内唯一的那一份。</summary>
    public static WorkspaceRegistry Shared { get; } = new();

    /// <summary>还开着几个工作区。测试用来断言最后一个窗口关掉之后确实释放了。</summary>
    public int OpenCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// 按标识取一个工作区。已经有了就复用，没有就用给定的工厂造一个。
    /// </summary>
    /// <param name="key">文档标识。同一份文档必须给出同一个标识。</param>
    /// <param name="create">第一次打开时怎么建这份工作区。</param>
    public WorkspaceLease Acquire(string key, Func<WorkspaceSetup> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(create);

        Entry entry;

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var existing))
            {
                existing = new Entry(create());
                _entries[key] = existing;
            }

            existing.Leases++;
            entry = existing;
        }

        return new WorkspaceLease(this, key, entry.Setup);
    }

    /// <summary>某个工作区还挂着几个窗口。</summary>
    public int LeaseCount(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_gate)
        {
            return _entries.TryGetValue(key, out var entry) ? entry.Leases : 0;
        }
    }

    /// <summary>
    /// 还回一个租约。最后一个还回来时才真的释放工作区与那份跨进程所有权。
    /// </summary>
    /// <remarks>
    /// 释放放在锁外面做：释放要等广播器的投递任务收尾（最多两秒），
    /// 握着锁等的话，这期间别的窗口连打开都打不开。
    /// </remarks>
    internal void Release(string key)
    {
        WorkspaceSetup? closing = null;

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                return;
            }

            if (--entry.Leases > 0)
            {
                return;
            }

            _entries.Remove(key);
            closing = entry.Setup;
        }

        closing.Workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();

        // 锁最后放。先放锁的话，另一个进程能立刻抢进来开始写，而这边的工作区
        // 还在收尾——两边会同时对着同一个文件动手，而那时两边都以为自己是唯一的那个。
        closing.Lock?.Dispose();
    }

    private sealed class Entry
    {
        public Entry(WorkspaceSetup setup) => Setup = setup;

        public WorkspaceSetup Setup { get; }

        public int Leases { get; set; }
    }
}

/// <summary>
/// 一个窗口对工作区的占用。窗口关掉时还回去。
/// </summary>
/// <remarks>
/// 做成一个对象而不是"取一次、还一次"两个方法，是为了让还回去这件事没法被漏掉：
/// 漏掉一次，那份工作区就永远留在表里，而下一个打开同一份文档的窗口会拿到一个
/// 已经没有窗口在用的旧工作区——它看起来一切正常，只是历史栈里还留着上一次的东西。
/// </remarks>
public sealed class WorkspaceLease : IDisposable
{
    private readonly WorkspaceRegistry _registry;
    private readonly string _key;
    private bool _released;

    internal WorkspaceLease(WorkspaceRegistry registry, string key, WorkspaceSetup setup)
    {
        _registry = registry;
        _key = key;
        Setup = setup;
    }

    /// <summary>这份文档的工作区。</summary>
    public DiagramWorkspace Workspace => Setup.Workspace;

    /// <summary>文档文件的路径。没有文件时为空。</summary>
    public string? Path => Setup.Path;

    /// <summary>这一份是不是只读的：另一个进程正拿着这份文件。</summary>
    public bool ReadOnly => Setup.ReadOnly;

    /// <summary>只读的原因，一句话。可写时为空。</summary>
    public string? Reason => Setup.Reason;

    private WorkspaceSetup Setup { get; }

    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _registry.Release(_key);
    }
}
