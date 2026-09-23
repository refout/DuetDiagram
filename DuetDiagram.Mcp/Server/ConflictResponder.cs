using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 冲突时回哪一种内容。
/// </summary>
/// <remarks>
/// <para>
/// 四种情形各一种回法，分界是**能不能只回差异**：调用方什么都没声明，就只能给全量；
/// 版本一样，什么都不用给；落在版本日志范围内、而且它声明的结构哈希与当前一致，
/// 就只给一份受影响元素清单；其余情况给全量。
/// </para>
/// <para>
/// **第四种刻意退成全量，尽管逐条增量算得出来。** 走到那一步说明调用方声明的结构哈希
/// 与服务端对不上，而它的本地副本是否还与它自己声明的那个版本对得上，从这边无从验证。
/// 把字段级的增量按在一份来路不明的副本上，正是产生"静默改错"的方式：
/// 改完之后两边都不报错，而那份图已经与谁都对不上了。全量快照整体替换，没有这个风险。
/// 逐条增量仍然留给界面那条通路——它手里的副本是本地的，不存在这个问题。
/// </para>
/// <para>
/// 它只决定"回什么形状"，不决定"用哪个状态码回"。状态码是传输那一层的事，
/// 标准输入输出那条通路根本没有状态码可用。
/// </para>
/// </remarks>
public static class ConflictResponder
{
    /// <summary>冲突用的错误码。</summary>
    public const string Code = ErrorCodes.VersionConflict;

    /// <summary>
    /// 算这一次冲突该回什么。
    /// </summary>
    /// <param name="declared">调用方声明的版本与结构哈希。没声明时为空。</param>
    /// <param name="document">服务端这一刻的文档。</param>
    /// <param name="log">版本日志，用来判断调用方落没落在可补的区间里。</param>
    /// <param name="serializeFull">按需生成全量快照。只在确实要回全量时才会被调用。</param>
    /// <remarks>
    /// 版本相等时给的是空差异、版本更靠前时给的是"参数错误"——两者都不是冲突，
    /// 调用方按错误码各自处置。把它们也当成冲突处理的话，一个参数写错的调用方
    /// 会一遍遍去同步，而同步多少次都改不掉那个写错的数。
    /// </remarks>
    public static DiffResult Decide(
        VersionCheckRequest? declared,
        DiagramDocument document,
        VersionLog log,
        Func<string> serializeFull)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(serializeFull);

        // 未声明：调用方没说自己停在哪一版，也就无从算出它缺什么，只能给全量。
        if (declared is null)
        {
            return Snapshot(document.Version, serializeFull);
        }

        var diff = log.BuildDiff(
            declared.ClientVersion,
            document.Version,
            declared.ClientStructuralHash,
            document.StructuralHash,
            serializeFull);

        return diff switch
        {
            // 版本相等、版本更靠前、以及"结构没变过只给清单"这三种原样带出去。
            // 第一种什么都不用给，第二种是参数错误，第三种正是最省的那一份。
            EmptyDiff or InvalidDiff or ReferenceDiff => diff,

            // 超出日志范围时日志自己已经把全量算好了，不重复序列化一次。
            FullSnapshotDiff snapshot => snapshot,

            // 剩下的就是"在范围内、但结构哈希对不上"，也就是逐条增量。按上面那条口径退成全量。
            _ => Snapshot(document.Version, serializeFull),
        };
    }

    private static FullSnapshotDiff Snapshot(int version, Func<string> serializeFull) => new()
    {
        Version = version,
        FullJson = serializeFull(),
    };
}
