using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Logging;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 错误码到界面呈现的对照表，以及那张表接在真实窗口上是什么样子。
/// </summary>
/// <remarks>
/// <para>
/// **呈现方式由表决定，不在各处就地判断。** 就地判断的结果是同一个错误码在两个入口
/// 给出两种呈现，而用户以为遇到的是两个问题。这一层盯的是表本身完不完整、
/// 以及它有没有真的接到状态栏上。
/// </para>
/// <para>
/// 表必须覆盖全部错误码：漏一个就是一次没有反馈的失败——用户点了一下，
/// 什么都没发生，也没有一句话。逐个查一遍是最省事的挡法。
/// </para>
/// <para>
/// 文档里的那张表也要对得上。两张表分头维护的话，迟早有一边多出一个码，
/// 而外部代理是按文档写代码的——它照着文档处理，实际行为却是另一套。
/// </para>
/// </remarks>
public sealed class ErrorPresentationTests
{
    #region 表本身

    /// <summary>每个错误码都查得到呈现方式。</summary>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public void Every_error_code_has_a_presentation()
    {
        var missing = Codes()
            .Where(code => !ErrorPresenterTable.All.ContainsKey(code))
            .ToList();

        missing.Should().BeEmpty(
            "查不到呈现方式就是一次没有反馈的失败：用户点了一下，什么都没发生，也没有一句话");
    }

    /// <summary>表里没有多余的行。</summary>
    /// <remarks>
    /// 多出来的一行多半是拼错了码。拼错的码永远不会被查到，而它看起来"已经处理过了"，
    /// 于是真正那个码仍然漏着，却没人再去看。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public void The_table_carries_no_code_that_is_not_an_error_code()
    {
        var known = Codes().ToHashSet(StringComparer.Ordinal);
        var extra = ErrorPresenterTable.All.Keys.Where(code => !known.Contains(code)).ToList();

        extra.Should().BeEmpty("表里的键必须都是错误码常量，多出来的多半是拼错了");
    }

    /// <summary>文档里的那张表与代码里的表逐行一致。</summary>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public void The_documented_presentations_match_the_table()
    {
        var documented = Documented();

        documented.Keys.Should().BeEquivalentTo(
            ErrorPresenterTable.All.Keys,
            "文档与代码两张表必须覆盖同一批错误码");

        foreach (var (code, kind) in documented)
        {
            ErrorPresenterTable.All[code].Kind.ToString().Should().Be(
                kind,
                $"{code} 在文档里写的是 {kind}，代码里必须是同一个");
        }
    }

    #endregion

    #region 一次结果的呈现

    /// <summary>无操作算成功，但要在状态栏灰显一句，不弹窗。</summary>
    /// <remarks>
    /// 弹窗打断的是整条操作链，而这类失败本来就没有需要用户决策的事。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public void A_no_op_is_muted_and_does_not_pop_a_dialog()
    {
        var presentations = ErrorPresenter.Present(CommandResult.NoOp("无可撤销操作"));

        var only = presentations.Should().ContainSingle().Which;

        only.Kind.Should().Be(ErrorPresentationKind.StatusBarMuted);
        only.Message.Should().Be("无可撤销操作");

        var status = new StatusBarViewModel(new CanvasViewModel());

        status.Show(only);

        status.HasMessage.Should().BeTrue();
        status.Severity.Should().Be(StatusSeverity.Muted, "无操作是灰的，不是红的");
        status.Message.Should().Contain("无可撤销操作");
    }

    /// <summary>版本冲突弹差异对话框，并且把差异带着。</summary>
    /// <remarks>
    /// 只给一句"版本冲突"的话，用户无从知道冲突在哪，只能放弃自己的改动重来。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public void A_version_conflict_asks_for_the_diff()
    {
        var result = CommandResult.Conflict(new EmptyDiff());

        var only = ErrorPresenter.Present(result).Should().ContainSingle().Which;

        only.Kind.Should().Be(ErrorPresentationKind.VersionDiffDialog);
        only.IsRetryable.Should().BeTrue("先同步再重试就能成功");
    }

    /// <summary>内部错误只说一句笼统的话，不把异常类型名摆给用户。</summary>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public void An_internal_error_says_nothing_about_the_exception()
    {
        var error = CommandError.Of(ErrorCodes.InternalError, "System.InvalidOperationException");
        var presentation = ErrorPresenter.Present(error);

        presentation.Kind.Should().Be(ErrorPresentationKind.StatusBar);
        presentation.Message.Should().NotContain("Exception", "类型名只写应用日志，不摆给用户");
    }

    /// <summary>一个认不出的码退回"内部错误"，既不抛也不静默。</summary>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public void An_unknown_code_falls_back_instead_of_throwing()
    {
        var presentation = ErrorPresenterTable.For("SOMETHING_NEW");

        presentation.Kind.Should().Be(ErrorPresentationKind.StatusBar);
        presentation.Message.Should().NotBeEmpty("至少要让用户知道出事了");
    }

    #endregion

    #region 接在真实窗口上

    /// <summary>一次没有写进去的改动，状态栏上看得见那句话。</summary>
    /// <remarks>
    /// 这条盯的是"表有没有接到窗口上"。只验表本身的话，一个算了半天却没人用的呈现
    /// 也能全绿，而用户那边仍然是点了一下什么反馈都没有。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public async Task A_rejected_change_shows_up_on_the_status_bar()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.Select("start");

            // 把标签写成它已经是的那个值：命令合法，但没什么可做。
            var result = window.Session.Apply("label", "开始");

            result.IsNoOp.Should().BeTrue("值没变，命令应当报无操作");

            window.Status.HasMessage.Should().BeTrue("状态栏上要有一句话");
            window.Status.Severity.Should().Be(StatusSeverity.Muted);

            // 那句话要真的画出来。只看视图模型的话，一个算好了却没接到控件上的状态
            // 也能全绿，而用户那边仍然是点了一下什么反馈都没有。
            window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Should()
                .Contain(
                    block => block.IsVisible && block.Text == window.Status.Message,
                    "状态栏上要看得到那句话");

            window.Close();
        });
    }

    /// <summary>窗口开出来的时候状态栏是空的，没有一句残留的提示。</summary>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public async Task A_fresh_window_has_nothing_to_report()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Status.HasMessage.Should().BeFalse();

            window.Close();
        });
    }

    #endregion

    #region 人工产物恢复提示

    /// <summary>
    /// 恢复提示的两条路各自把自己那件事报出来。
    /// </summary>
    /// <remarks>
    /// 它只负责问，不负责办：恢复与放弃都由宿主去执行。没有这一条的话，
    /// 一个把两个按钮接成同一件事的实现也能"看起来能用"，
    /// 而用户点哪边都是同一个结果。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public async Task The_recovery_dialog_reports_which_way_the_user_picked()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var dialog = window.GetVisualDescendants().OfType<SidecarRecoveryDialog>().Single();
            var picked = new List<string>();

            dialog.RestoreRequested += () => picked.Add("restore");
            dialog.DiscardRequested += () => picked.Add("discard");

            dialog.IsVisible.Should().BeFalse("没出事的时候不该挡着图");

            window.ShowSidecarRecovery("user.json 读不出来");

            dialog.IsVisible.Should().BeTrue();

            Click(dialog, "从备份恢复");
            Click(dialog, "放弃人工调整");

            picked.Should().Equal("restore", "discard");

            window.Close();
        });
    }

    #endregion

    #region 取数

    /// <summary>按下按钮，按它上面那行字找。</summary>
    /// <remarks>
    /// <para>
    /// 按文字找而不是按视觉树里的次序：次序一调整就会按到另一个按钮上，
    /// 而那种错不会让测试失败，只会让它去验另一件事。
    /// </para>
    /// <para>
    /// 走逻辑树而不是视觉树：这两条提示平时是收起来的，而收起来的控件不参与排布，
    /// 视觉树里也就没有它们的子节点——按视觉树找会一个都找不到。
    /// </para>
    /// </remarks>
    private static void Click(UserControl dialog, string label)
    {
        var button = dialog.GetLogicalDescendants()
            .OfType<Button>()
            .Single(candidate => string.Equals(candidate.Content as string, label, StringComparison.Ordinal));

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    /// <summary>错误码清单。反射一遍，加一个常量就自动进这一批。</summary>
    private static IEnumerable<string> Codes() =>
        typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    /// <summary>
    /// 从错误码文档里读回那张呈现对照表。
    /// </summary>
    /// <remarks>
    /// 只认两格都是反引号包起来的短标识的行，并且第一格必须是一个真错误码。
    /// 这样同一节里那张"呈现方式是什么含义"的表不会被误读成码到呈现的映射。
    /// </remarks>
    private static Dictionary<string, string> Documented()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "Error-Codes.md");
        var lines = File.ReadAllLines(path);
        var known = Codes().ToHashSet(StringComparer.Ordinal);
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            var cells = line.Split('|', StringSplitOptions.TrimEntries);

            if (cells.Length != 4 || !Unwrap(cells[1], out var code) || !Unwrap(cells[2], out var kind))
            {
                continue;
            }

            if (known.Contains(code))
            {
                found[code] = kind;
            }
        }

        return found;
    }

    private static bool Unwrap(string cell, out string value)
    {
        value = string.Empty;

        if (cell.Length < 3 || cell[0] != '`' || cell[^1] != '`')
        {
            return false;
        }

        var inner = cell[1..^1];

        if (inner.Length == 0 || !inner.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            return false;
        }

        value = inner;
        return true;
    }

    /// <summary>从测试程序集的位置逐级上溯，找到含解决方案文件的目录。</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DuetDiagram.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("从测试程序集的位置找不到仓库根。");
    }

    #endregion
}
