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
    public static ChatOptions Create(
        ToolRegistry registry,
        ChatOptions? basis = null,
        string? instructions = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var options = basis?.Clone() ?? new ChatOptions();

        options.Instructions ??= instructions;

        if (options.Tools is not { Count: > 0 })
        {
            options.Tools = [.. registry.ToMeaiFunctions()];
        }

        return options;
    }
}
