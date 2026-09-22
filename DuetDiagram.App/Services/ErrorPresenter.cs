using DuetDiagram.Core.Commands;
using DuetDiagram.Layout;

namespace DuetDiagram.App.Services;

/// <summary>
/// 把命令结果与布局失败翻成界面能直接照做的呈现。
/// </summary>
/// <remarks>
/// <para>
/// 它只做翻译，不做决定：怎么呈现由 <see cref="ErrorPresenterTable"/> 查表决定，
/// 这里负责把结果里的细节（标识、是否可重试、差异）填进那份呈现里。
/// </para>
/// <para>
/// 无操作单独一档：它是"合法但没什么可做"，算成功，但要在状态栏灰显一句，
/// 不弹窗。弹窗打断的是整条操作链，而这类失败本来就没有需要用户决策的事。
/// </para>
/// </remarks>
public static class ErrorPresenter
{
    /// <summary>一次命令结果要在界面上呈现的东西。成功且非无操作时为空。</summary>
    public static IReadOnlyList<ErrorPresentation> Present(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsNoOp)
        {
            return [new ErrorPresentation(ErrorPresentationKind.StatusBarMuted, result.Message ?? "无操作")];
        }

        if (result.IsSuccess)
        {
            return [];
        }

        var presentations = new List<ErrorPresentation>(result.Errors.Length);

        foreach (var error in result.Errors)
        {
            presentations.Add(Present(error));
        }

        return presentations;
    }

    /// <summary>一个结构化错误的呈现，把载荷作为细节带上。</summary>
    public static ErrorPresentation Present(CommandError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var presentation = ErrorPresenterTable.For(error.Code);

        return string.IsNullOrEmpty(error.Payload)
            ? presentation
            : presentation with { Detail = error.Payload };
    }

    /// <summary>布局彻底失败的呈现。</summary>
    public static ErrorPresentation Present(LayoutFailedException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var presentation = ErrorPresenterTable.For(failure.Error.Code);

        return presentation with
        {
            Detail = $"{failure.Payload.Attempts.Count} 级尝试",
        };
    }
}
