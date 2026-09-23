using DuetDiagram.Llm.Tools;
using Microsoft.Extensions.AI;

namespace DuetDiagram.Llm.Chat;

/// <summary>
/// 建一次对话要用的 <see cref="ChatOptions"/>。
/// </summary>
/// <remarks>
/// <para>
/// 它只管一件事：**这一轮广告哪几个工具**。工具表在注册表里，而模型要看见它们
/// 还得有人把那一份放进请求——放进来的地方只该有一处，两处各加一批的话，
/// 同一批工具会在请求里出现两遍，而重复的工具名会让对端的行为变得不确定。
/// </para>
/// <para>
/// 调用方自己给了一份工具表时不再填：那一份得自己保证都在注册表里，
/// 否则执行体回的是「工具名不在表里」，而那句话与调用方看到的参数对不上。
/// 要换工具就换注册表，不要在这一层另开一份。
/// </para>
/// <para>
/// 除了工具，它还负责把**这一轮匹配上的 Skill 正文**接到系统提示后面。选哪一份不在这里：
/// 只有调用方知道这一轮的任务是什么，而它手上那份正文的来源是代理那一侧的目录——
/// 依赖方向是反的，所以这一层收的是一段纯文本。
/// </para>
/// </remarks>
public static class ChatOptionsFactory
{
    /// <summary>
    /// 按注册表补齐一份对话选项。
    /// </summary>
    /// <remarks>
    /// 传进来的那份会先克隆再改，不就地改：它是调用方的东西，
    /// 而一次请求改了之后复用同一个对象是很自然的写法，就地改会让第二次请求带上第一次的痕迹。
    /// </remarks>
    /// <param name="registry">工具表。</param>
    /// <param name="basis">调用方已经建好的那一份。为空时新建。</param>
    /// <param name="instructions">系统提示。调用方自己写了就不覆盖。</param>
    /// <param name="skill">
    /// 这一轮匹配上的 Skill 正文。为空表示这一轮不需要 Skill，缺省就是这一档。
    /// </param>
    public static ChatOptions Create(
        ToolRegistry registry,
        ChatOptions? basis = null,
        string? instructions = null,
        string? skill = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var options = basis?.Clone() ?? new ChatOptions();

        options.Instructions ??= instructions;

        // 接在后面而不是替掉：系统提示说的是这份图是什么、有什么规矩，
        // Skill 说的是这一轮这类任务该怎么做，两者不是一回事。
        if (!string.IsNullOrWhiteSpace(skill))
        {
            options.Instructions = string.IsNullOrWhiteSpace(options.Instructions)
                ? skill
                : $"{options.Instructions}{Environment.NewLine}{Environment.NewLine}{skill}";
        }

        if (options.Tools is not { Count: > 0 })
        {
            options.Tools = [.. registry.ToMeaiFunctions()];
        }

        return options;
    }
}
