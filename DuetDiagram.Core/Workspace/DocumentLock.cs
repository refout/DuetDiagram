using DuetDiagram.Core.Time;

namespace DuetDiagram.Core.Workspace;

/// <summary>
/// 这个进程拿到的是哪一种所有权。
/// </summary>
public enum DocumentLockMode
{
    /// <summary>独占：可以写，别的进程只能看。</summary>
    Exclusive,

    /// <summary>只读：另一个进程正在编辑这份文档。</summary>
    ReadOnly,
}

/// <summary>
/// 一份文档的跨进程所有权。
/// </summary>
/// <remarks>
/// <para>
/// **单进程多窗口不经过这里。** 那些窗口共享同一个工作区，文档只有一份，
/// 谁也不用跟谁抢；这个锁管的是**两个进程**打开同一份文件。
/// </para>
/// <para>
/// **锁与心跳是两个文件。** 锁文件按独占方式打开并一直拿着，谁拿着谁就是持有者；
/// 心跳文件按允许读写的方式打开，持有者每隔几秒往里写一次时刻。
/// 合成一个文件的话，心跳的写入会撞上那把独占锁，表现是"第一个进程一写心跳就把自己锁死"。
/// </para>
/// <para>
/// **句柄是判据，心跳是补充。** 拿得到锁文件的独占句柄，就说明没有活着的持有者——
/// 进程无论怎么死，系统都会把它手上的句柄收回去。心跳管的是另一件事：
/// 句柄拿不到时，对方是正在干活还是已经卡住了。所以心跳过期不会去抢一个握着句柄的进程，
/// 那一下"删不掉"才是"别抢"的真正判据。
/// </para>
/// <para>
/// **正常退出会清掉心跳，异常退出会留下它。** 于是下一次打开看到心跳文件还在，
/// 就知道上一次是死掉的，而不是好好退出的——这一条由 <see cref="TookOver"/> 报给调用方，
/// 它必须据此**重新校验那份文档**：被强杀的进程可能正好写了一半，留下的是截断的文件。
/// 校验交给读文档的那一侧，只有它知道该用哪个解析器、也才知道坏文件该给用户看什么。
/// </para>
/// </remarks>
public sealed class DocumentLock : IDisposable
{
    private readonly Heartbeat? _heartbeat;
    private readonly string _heartbeatPath;
    private FileStream? _handle;

    private DocumentLock(
        string documentPath,
        DocumentLockMode mode,
        FileStream? handle,
        Heartbeat? heartbeat,
        bool tookOver,
        string? reason)
    {
        DocumentPath = documentPath;
        Mode = mode;
        _handle = handle;
        _heartbeat = heartbeat;
        TookOver = tookOver;
        Reason = reason;
        _heartbeatPath = HeartbeatPath(documentPath);
    }

    /// <summary>这份文档的路径。</summary>
    public string DocumentPath { get; }

    /// <summary>拿到了哪一种所有权。</summary>
    public DocumentLockMode Mode { get; }

    /// <summary>
    /// 上一个持有者是不是异常退出的。
    /// </summary>
    /// <remarks>
    /// 为真时调用方必须把文档重新解析并校验一遍再写：上一次可能正好写到一半就被杀掉了。
    /// </remarks>
    public bool TookOver { get; }

    /// <summary>只读的原因，一句话。独占时为空。</summary>
    public string? Reason { get; }

    /// <summary>能不能写。</summary>
    public bool CanWrite => Mode == DocumentLockMode.Exclusive;

    /// <summary>锁文件的路径。</summary>
    public static string LockPath(string documentPath) => documentPath + ".lock";

    /// <summary>心跳文件的路径。</summary>
    public static string HeartbeatPath(string documentPath) => documentPath + ".heartbeat";

    /// <summary>
    /// 给一份文档加锁。拿不到独占时退成只读，而不是失败——
    /// "别人正在编辑"是一种正常情形，用户要看到的是这份文档的内容加一句说明，
    /// 而不是一个打不开的窗口。
    /// </summary>
    /// <param name="documentPath">文档路径。</param>
    /// <param name="clock">判断心跳过期的时钟。</param>
    public static DocumentLock Acquire(string documentPath, ITimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        var lockPath = LockPath(documentPath);
        var heartbeatPath = HeartbeatPath(documentPath);

        // 拿得到句柄就说明没有活着的持有者。上一次留下心跳文件意味着它是被强杀的，
        // 而不是好好退出的——好好退出的那一次会把自己的心跳清掉。
        if (TryHold(lockPath, out var handle))
        {
            var unclean = File.Exists(heartbeatPath);

            Clear(heartbeatPath);

            return new DocumentLock(
                documentPath,
                DocumentLockMode.Exclusive,
                handle,
                new Heartbeat(heartbeatPath, clock),
                tookOver: unclean,
                reason: null);
        }

        if (!Heartbeat.IsStale(heartbeatPath, clock))
        {
            return Busy(documentPath, "另一个进程正在编辑这份文档，这一份是只读的");
        }

        // 句柄拿不到，心跳又停了：对方可能卡住了。抢一下——删得掉锁文件才抢得到，
        // 而握着它的活进程会让这一步失败。这一下失败才是判据，心跳过期只是触发去试。
        try
        {
            File.Delete(lockPath);
        }
        catch (IOException)
        {
            return Busy(documentPath, "另一个进程好像卡住了，但它还占着这份文档");
        }
        catch (UnauthorizedAccessException)
        {
            return Busy(documentPath, "另一个进程好像卡住了，但它还占着这份文档");
        }

        if (!TryHold(lockPath, out handle))
        {
            return Busy(documentPath, "另一个进程正在编辑这份文档，这一份是只读的");
        }

        Clear(heartbeatPath);

        return new DocumentLock(
            documentPath,
            DocumentLockMode.Exclusive,
            handle,
            new Heartbeat(heartbeatPath, clock),
            tookOver: true,
            reason: null);
    }

    public void Dispose()
    {
        // 只读那一份什么都不清：那个心跳文件是持有者的，清掉之后它下一次重排
        // 就会被别的进程当成"上一次是异常退出的"。
        if (_heartbeat is null)
        {
            return;
        }

        // 先停心跳，再清掉它，最后才松开锁。顺序反过来的话，松开锁之后到心跳停掉之间
        // 还可能有一下写入，而那时别的进程已经能抢进来了——它会在自己的抢占校验里
        // 读到那一下心跳，于是把已经空出来的锁又当成"有人拿着"。
        _heartbeat.Dispose();
        Clear(_heartbeatPath);

        _handle?.Dispose();
        _handle = null;
    }

    private static DocumentLock Busy(string documentPath, string reason) =>
        new(
            documentPath,
            DocumentLockMode.ReadOnly,
            handle: null,
            heartbeat: null,
            tookOver: false,
            reason: reason);

    /// <summary>按独占方式打开锁文件。拿不到时返回假。</summary>
    private static bool TryHold(string lockPath, out FileStream handle)
    {
        try
        {
            handle = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            return true;
        }
        catch (IOException)
        {
            handle = null!;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            handle = null!;
            return false;
        }
    }

    /// <summary>清掉一个残留的文件。删不掉时留着，不影响结论。</summary>
    private static void Clear(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
