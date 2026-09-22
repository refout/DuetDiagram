using DuetDiagram.Core.Time;

namespace DuetDiagram.Core.Workspace;

/// <summary>
/// 心跳：持有文档的那个进程每隔几秒写一次时刻，让别的进程看得出它还活着。
/// </summary>
/// <remarks>
/// <para>
/// **它必须是单独一个文件，不能和锁文件合并。** 锁文件按独占方式打开，
/// 心跳要往同一个文件里写就会撞上那把独占锁，表现是"第一个进程一写心跳就把自己锁死"——
/// 而且这个症状只在第一次心跳到点时才出现，看起来像随机卡死。
/// </para>
/// <para>
/// **时刻写在文件内容里，不靠文件的修改时间。** 修改时间的精度随文件系统而变
/// （有的只有秒级，有的会延迟刷新），拿它当判据的话超时判定会在不同机器上不一致；
/// 而内容是我们自己写的，读出来就能比。
/// </para>
/// </remarks>
public sealed class Heartbeat : IDisposable
{
    /// <summary>心跳周期。</summary>
    /// <remarks>
    /// 与超时一起定义在这一处。分开写的话，改了一个忘了另一个，
    /// 表现是"偶尔把活着的进程判成崩溃"，而没有规律可循。
    /// </remarks>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>多久没有心跳就判定为疑似崩溃。</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _path;
    private readonly ITimeProvider _clock;
    private readonly Timer _timer;

    public Heartbeat(string path, ITimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _clock = clock ?? SystemTimeProvider.Instance;

        // 建好就先写一次：等到第一个周期到点才写的话，新开的进程在头五秒里
        // 会被别的进程当成"心跳过期"，而它其实刚开始干活。
        Beat();

        _timer = new Timer(_ => Beat(), null, Interval, Interval);
    }

    /// <summary>心跳文件的路径。</summary>
    public string Path => _path;

    /// <summary>写一次当前时刻。</summary>
    public void Beat()
    {
        try
        {
            // 允许读写共享：别的进程要能一边读它、一边等它被更新。
            // 按独占方式打开的话，读的一方与写的一方会互相挡住，
            // 而"读心跳"恰恰是抢占判定要做的事。
            using var stream = new FileStream(
                _path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite);

            using var writer = new StreamWriter(stream);
            writer.Write(_clock.UtcNow.ToString("O"));
        }
        catch (IOException)
        {
            // 写不进去说明这个文件被别的东西占着。心跳写失败不是致命错误——
            // 它的唯一用途是让别人判断我们还在不在，而写失败最多让别人以为我们挂了。
            // 抛出去的话，一次磁盘抖动会把正在编辑的文档一起带走。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 读一个心跳文件里的时刻。文件不在、读不动、内容不是时刻时给空。
    /// </summary>
    /// <remarks>
    /// 这三种情况一律当"读不出来"而不是当"过期"：判成过期会触发抢占，
    /// 而抢占一个活着的进程会把它正在写的文档撕掉。
    /// </remarks>
    public static DateTimeOffset? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            return DateTimeOffset.TryParse(reader.ReadToEnd(), out var stamp) ? stamp : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>这个心跳是不是已经过期。</summary>
    /// <param name="path">心跳文件路径。</param>
    /// <param name="clock">读时刻用的时钟。</param>
    /// <param name="timeout">多久算过期。不传时用 <see cref="Timeout"/>。</param>
    /// <remarks>
    /// 读不出时刻时**返回假**：读不出来与过期是两回事，
    /// 而把前者当成后者会去抢占一个可能活着的进程。
    /// </remarks>
    public static bool IsStale(string path, ITimeProvider? clock = null, TimeSpan? timeout = null)
    {
        var now = (clock ?? SystemTimeProvider.Instance).UtcNow;
        var stamp = Read(path);

        return stamp is { } value && now - value > (timeout ?? Timeout);
    }

    public void Dispose() => _timer.Dispose();
}
