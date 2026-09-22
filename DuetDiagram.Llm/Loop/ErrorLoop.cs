using System.Text;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Llm.Tools;

namespace DuetDiagram.Llm.Loop;

/// <summary>
/// 错误回环：把一次失败翻成给模型看的东西，并盯住这一轮试了几次。
/// </summary>
/// <remarks>
/// <para>
/// **它是跨调用的状态，所以由宿主每轮对话各持一个。** 「同一个错误连着出现两次」与
/// 「这一轮已经试了几次」都不是一次调用内部的事；挂在共享的注册表上的话，
/// 一个窗口的错误会算到另一个窗口的账上，表现是模型莫名其妙被判定「又错了」。
/// </para>
/// <para>
/// **回环要有上限。** 不设上限的话，一次参数错误会变成无限次调用——账单上看得出来，
/// 日志里却看不出来。到上限时停下，并把整段往返记录写进应用日志，
/// 让「模型撞了多少次、撞在什么上」这件事在日志里看得见。
/// </para>
/// <para>
/// 上限数的是**回灌次数**，不是调用次数。设为 3 时，模型最多被叫回来 3 次；
/// 第 4 次失败落在这个对象上，它就停下来。
/// </para>
/// </remarks>
public sealed class ErrorLoop
{
    /// <summary>缺省的回灌次数上限。</summary>
    /// <remarks>
    /// 三次够模型改两轮：第一轮多半是参数写错，第二轮是照着建议改但没改对。
    /// 到第三轮还没成，通常说明它对这件事的理解就是错的，再喂它只会烧掉更多的往返。
    /// </remarks>
    public const int DefaultMaxAttempts = 3;

    private readonly int _maxAttempts;
    private readonly IDiagnosticsSink _diagnostics;
    private readonly List<ErrorEnvelope> _history = [];

    private HashSet<string> _previousCodes = new(StringComparer.Ordinal);
    private int _attempts;

    /// <param name="maxAttempts">最多回灌几次。至少要一次，否则模型没有改的机会。</param>
    /// <param name="diagnostics">应用日志出口。到上限时那一段写到这里。</param>
    public ErrorLoop(int maxAttempts = DefaultMaxAttempts, IDiagnosticsSink? diagnostics = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        _maxAttempts = maxAttempts;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    /// <summary>最多回灌几次。</summary>
    public int MaxAttempts => _maxAttempts;

    /// <summary>这一轮已经失败了几次。</summary>
    public int Attempts => _attempts;

    /// <summary>这一轮已经回灌过的全部信封，按发生次序。</summary>
    public IReadOnlyList<ErrorEnvelope> History => _history;

    /// <summary>上限已经用完了。宿主看到它为真就该放弃这一轮，别再调模型。</summary>
    public bool Exhausted => _attempts > _maxAttempts;

    /// <summary>
    /// 收下一次失败，给出要回灌给模型的信封。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 返回空列表表示**到上限了，别再试**：这时整段往返记录已经写进应用日志，
    /// 宿主该做的是把这件事报给人，而不是把模型再叫一遍。
    /// </para>
    /// <para>
    /// 传进来的必须是失败的结果。成功的那一次直接进下一步，不需要回灌——
    /// 走错路的话在这里就报出来，而不是悄悄返回一个空列表让宿主以为到了上限。
    /// </para>
    /// </remarks>
    /// <param name="failure">失败的那一次调用结果。</param>
    public IReadOnlyList<ErrorEnvelope> Feed(ToolResult failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (failure.IsSuccess)
        {
            throw new ArgumentException(
                "只有失败的调用才需要回灌；成功的那一次直接进下一步。",
                nameof(failure));
        }

        _attempts++;

        var envelopes = ErrorEnvelope.AllOf(failure, _attempts, _previousCodes);

        _history.AddRange(envelopes);

        // 记下这一轮的错误码，供下一轮判「同一个错误又来了」。只比上一轮，
        // 不比整段历史：中间插过一次别的错误之后，再撞回原来那个是另一件事。
        _previousCodes = [.. envelopes.Select(envelope => envelope.Code)];

        if (_attempts <= _maxAttempts)
        {
            return envelopes;
        }

        _diagnostics.Warn(
            $"错误回环到了上限（最多回灌 {_maxAttempts} 次），停下。整段往返：{Record()}");

        return [];
    }

    /// <summary>
    /// 换一轮。清掉次数与「上一次错的是什么」。
    /// </summary>
    /// <remarks>
    /// 宿主在模型终于改对之后、或者用户说了下一句话时调用。不调用的话，
    /// 上一轮的错误码会让下一轮第一次失败就被标成「上一次这么改也不行」，
    /// 而模型其实还没试过那一次。
    /// </remarks>
    public void Reset()
    {
        _attempts = 0;
        _previousCodes = new HashSet<string>(StringComparer.Ordinal);
        _history.Clear();
    }

    /// <summary>整段往返记录，按次分句。</summary>
    /// <remarks>
    /// 带上参数名：只说错误码的话，同一个码在几个参数上都可能出现，
    /// 读日志的人看不出它到底撞在哪一项上。
    /// </remarks>
    private string Record()
    {
        var text = new StringBuilder();

        foreach (var group in _history.GroupBy(envelope => envelope.Attempt))
        {
            if (text.Length > 0)
            {
                text.Append(" → ");
            }

            text.Append($"第 {group.Key} 次 ");

            text.AppendJoin(
                '、',
                group.Select(envelope => envelope.Parameter is { Length: > 0 } parameter
                    ? $"{envelope.Code}（{parameter}）"
                    : envelope.Code));
        }

        return text.ToString();
    }
}
