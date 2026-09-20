namespace DuetDiagram.Core.Time;

/// <summary>
/// 时间来源。之所以要抽象一层，是因为命令总线会用当前时间填充上下文，
/// 而测试需要断言"审计日志里的时间戳等于注入的那个时刻"。
/// 直接读系统时钟的话这类断言只能写成范围判断，既不稳定也说明不了问题。
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

/// <summary>
/// 手动推进的时钟。只在时间确实参与逻辑的测试里使用：
/// 它让"同一个时刻执行多条命令"和"跨越一段时间执行"这两类场景都能精确构造。
/// </summary>
public sealed class ManualTimeProvider : ITimeProvider
{
    public ManualTimeProvider(DateTimeOffset start)
    {
        UtcNow = start;
    }

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
}
