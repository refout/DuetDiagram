namespace DuetDiagram.Mcp.Skills;

/// <summary>
/// 一份 Skill：按任务取用的说明正文，以及它名下的资源文件。
/// </summary>
/// <remarks>
/// <para>
/// 正文与资源文件是渐进式披露的两层。正文回答「这件事该按什么顺序做、常见错误有哪些」，
/// 是任务匹配上之后才取的；资源文件回答「精确语法长什么样、罕见形状怎么写」，
/// 是写到那一步才取的。工具描述那一层不在这里——它始终在上下文里，由工具表自己带着。
/// </para>
/// <para>
/// <see cref="Summary"/> 是「什么时候该取它」那一句话，它会出现在资源清单里。
/// 清单是调用方唯一能据以决定取不取的依据：没有它，调用方只有两个选择——全取，或者瞎猜。
/// </para>
/// </remarks>
public sealed record SkillResource
{
    /// <summary>标识。它同时是 URI 里那一段，所以只能用短横线与小写字母。</summary>
    public required string Name { get; init; }

    /// <summary>标题，给人看的那一行。</summary>
    public required string Title { get; init; }

    /// <summary>
    /// 版本。
    /// </summary>
    /// <remarks>
    /// 调用方靠它判断手上那一份还是不是当前这一份。正文本身会随版本改动，
    /// 而改动不会以别的方式暴露出来——一份过期的 Skill 正文读起来与新的没有区别。
    /// </remarks>
    public required string Version { get; init; }

    /// <summary>什么时候该取这一份。</summary>
    public required string Summary { get; init; }

    /// <summary>正文。</summary>
    public required string Body { get; init; }

    /// <summary>名下的资源文件。没有就是空表。</summary>
    public IReadOnlyList<SkillAsset> Assets { get; init; } = [];

    /// <summary>这一份的 URI。</summary>
    public string Uri => $"{SkillCatalog.Scheme}://{Name}";
}

/// <summary>
/// Skill 名下的一份资源文件。
/// </summary>
/// <remarks>
/// 它比正文深一层：正文讲怎么做，这里放的是精确到写法的那部分。
/// 放进正文的话，正文会膨胀成一份手册，而正文是「任务匹配时才取」的——
/// 取一次就为了看两行语法的代价，是让调用方下次干脆不取。
/// </remarks>
public sealed record SkillAsset
{
    /// <summary>在这份 Skill 下的相对路径。它拼在 Skill 的 URI 后面。</summary>
    public required string Path { get; init; }

    /// <summary>标题，给人看的那一行。</summary>
    public required string Title { get; init; }

    /// <summary>什么时候该取这一份。</summary>
    public required string Summary { get; init; }

    /// <summary>内容。</summary>
    public required string Text { get; init; }
}
