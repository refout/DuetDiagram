namespace DuetDiagram.Render;

/// <summary>
/// 渲染模式的切换器：判定、预备、生效分在三帧上。
/// </summary>
/// <remarks>
/// <para>
/// **切换那一帧不做额外的事。** 需要为新模式准备的东西（大图上是那份空间索引）
/// 在**判定**那一帧就做完，下一帧只是换一份要画的东西。
/// 切换与建索引挤在同一帧的话，那一帧必然超时，表现是"转一下视图就卡一下"。
/// </para>
/// <para>
/// 状态只有两个：当前模式与目标模式。两者不同就说明切换在路上——
/// 预备已经做过、只等下一次生效。
/// </para>
/// </remarks>
public sealed class ModeSwitch
{
    private RenderMode _current;
    private RenderMode _target;

    public ModeSwitch(CullingPolicy policy, RenderMode initial = RenderMode.Immediate)
    {
        ArgumentNullException.ThrowIfNull(policy);

        Policy = policy;
        _current = initial;
        _target = initial;
    }

    /// <summary>判定用的策略。</summary>
    public CullingPolicy Policy { get; }

    /// <summary>这一帧实际在用的模式。</summary>
    public RenderMode Current => _current;

    /// <summary>下一次生效时该用的模式。</summary>
    public RenderMode Target => _target;

    /// <summary>
    /// 切换在路上：预备做过了，但还没生效。
    /// </summary>
    /// <remarks>
    /// 它只在一帧之内为真——判定那一帧为真，下一帧生效时变回假。
    /// </remarks>
    public bool IsSwitching => _current != _target;

    /// <summary>已经切换过多少次。诊断面板用它确认切换真的发生过。</summary>
    public int SwitchCount { get; private set; }

    /// <summary>
    /// 从头开始：当前与目标都设成给定模式。
    /// </summary>
    /// <remarks>
    /// 换文档时用。新文档的元素数可能差得很远，沿用旧状态会带着一份为旧列表准备的索引，
    /// 而那份索引对新列表毫无用处。
    /// </remarks>
    public void Reset(RenderMode mode)
    {
        _current = mode;
        _target = mode;
        SwitchCount = 0;
    }

    /// <summary>
    /// 按元素数判定目标模式。
    /// </summary>
    /// <remarks>
    /// 返回真表示目标变了，调用方应当**在这一次调用里**把新模式需要的东西备好。
    /// 已经对准目标时返回假，于是预备那一份工作不会每帧重做一遍。
    /// </remarks>
    public bool Request(int elementCount)
    {
        var wanted = Policy.ModeFor(elementCount);

        if (wanted == _target)
        {
            return false;
        }

        _target = wanted;

        return true;
    }

    /// <summary>
    /// 让目标模式生效。返回是否真的换了一档。
    /// </summary>
    /// <remarks>
    /// 在每一帧的开始调用。放帧末也可以，但那样"这一帧用的是哪一档"要跨过帧边界才说得清，
    /// 而那种含糊在排查画面差异时很费劲。
    /// </remarks>
    public bool Commit()
    {
        if (_current == _target)
        {
            return false;
        }

        _current = _target;
        SwitchCount++;

        return true;
    }
}
