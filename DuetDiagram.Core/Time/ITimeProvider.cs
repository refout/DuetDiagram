namespace DuetDiagram.Core.Time;

/// <summary>
/// 时间提供者。测试注入固定时钟，保证 memento / 版本日志的断言可复现。
/// </summary>
public interface ITimeProvider
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemTimeProvider : ITimeProvider
{
    public static SystemTimeProvider Instance { get; } = new();

    private SystemTimeProvider()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>测试用：手工推进的时钟。</summary>
public sealed class ManualTimeProvider : ITimeProvider
{
    public ManualTimeProvider(DateTimeOffset start)
    {
        UtcNow = start;
    }

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
}
