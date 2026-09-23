using DuetDiagram.Core.Commands;

namespace DuetDiagram.Llm.Loop;

/// <summary>
/// 一个错误码的修复线索：通常错在哪个参数，以及下一步该怎么改。
/// </summary>
/// <remarks>
/// <para>
/// 只有两样东西，因为另外两样是运行时的：**可用的取值**（可用的动作名、样式令牌、
/// 字段名、已登记的工具名）随文档与工具表变，静态表里写不出来，写死了就是错的。
/// 它们由调用点填进错误自己的期望字段，那里的值才是当下真的可用的那些。
/// </para>
/// <para>
/// <see cref="Parameter"/> 允许为空：内部错误、版本冲突、只读这类失败不是某个参数写错了，
/// 硬凑一个参数名会让模型去改一个根本不相干的字段。
/// </para>
/// </remarks>
/// <param name="Code">错误码。</param>
/// <param name="Parameter">通常出错的参数名。没有就为空。</param>
/// <param name="Suggestion">一句可执行的修复建议。必填。</param>
public sealed record RepairHint(string Code, string? Parameter, string Suggestion);

/// <summary>
/// 错误码到修复建议的映射表。
/// </summary>
/// <remarks>
/// <para>
/// **映射表放在一处。** 散在各处的话，同一个错误码会在两个入口给模型两种说法，
/// 而调用方按哪一条都可能是错的。所有失败路径都从这里取建议。
/// </para>
/// <para>
/// 表覆盖**两套**错误码：命令层的前置检查（<see cref="ErrorCodes"/>）与工具层的参数校验
/// （<see cref="Tools.ToolErrorCodes"/>）。回灌给模型的失败两条来源都会出现，所以两边都得有。
/// </para>
/// <para>
/// 表必须覆盖全部错误码，漏一个就是一次模型只能靠猜的失败。漏项由一条测试挡住：
/// 拿两套错误码逐个查表，查不到就红。
/// </para>
/// </remarks>
public static class RepairHints
{
    /// <summary>
    /// 认不出的错误码退回的那句话。
    /// </summary>
    /// <remarks>
    /// 认不出时不抛也不静默：退回一句让模型别乱试的话，至少不会让它把同一个错误原样撞一遍。
    /// 真正的兜底是那条覆盖全表的门禁——走到这里说明码是别处新加的。
    /// </remarks>
    private const string UnknownSuggestion =
        "这个错误码没有登记过修复建议。原样重试之前先确认参数与文档现状，或者把这一次失败报给人。";

    private static readonly IReadOnlyDictionary<string, RepairHint> Table =
        new Dictionary<string, RepairHint>(StringComparer.Ordinal)
        {
            #region 命令的前置检查

            [ErrorCodes.DuplicateId] = new(
                ErrorCodes.DuplicateId,
                "id",
                "换一个没被占用的标识；要保留同名元素的话，先读一次图看现在有哪些标识。"),

            [ErrorCodes.EdgeTargetMissing] = new(
                ErrorCodes.EdgeTargetMissing,
                "to",
                "终点不存在。先读一次图确认现有标识，或者先把终点建出来再连这条线。"),

            [ErrorCodes.EdgeSourceMissing] = new(
                ErrorCodes.EdgeSourceMissing,
                "from",
                "起点不存在。先读一次图确认现有标识，或者先把起点建出来再连这条线。"),

            [ErrorCodes.GroupMemberMissing] = new(
                ErrorCodes.GroupMemberMissing,
                "memberIds",
                "成员标识写错了或已经没了。读一次图核对之后重发，成员列表里只留确实存在的那些。"),

            [ErrorCodes.CompositeMissing] = new(
                ErrorCodes.CompositeMissing,
                "id",
                "这个组合已经不在了。读一次图看现有组合，或者先建一个再操作。"),

            [ErrorCodes.GroupCycle] = new(
                ErrorCodes.GroupCycle,
                "targetId",
                "目标组合在这个元素的子树里，移进去会成环。换一个不在它下面的组合，或者先把中间那一层解散。"),

            [ErrorCodes.CompositeTooDeep] = new(
                ErrorCodes.CompositeTooDeep,
                "targetId",
                "嵌套已经到上限了。别再往里套，或者先把中间那几层解散再套。"),

            [ErrorCodes.LayerMissing] = new(
                ErrorCodes.LayerMissing,
                "id",
                "图层标识写错了或已经没了。读一次图看现有图层，或者先建一个。"),

            [ErrorCodes.PageMissing] = new(
                ErrorCodes.PageMissing,
                "id",
                "页面标识写错了或已经没了。读一次图看现有页面，或者先建一页。"),

            [ErrorCodes.PageRequired] = new(
                ErrorCodes.PageRequired,
                "id",
                "这是最后一页，删了文档就没有页了。先建一页，再删这一页。"),

            [ErrorCodes.TagMissing] = new(
                ErrorCodes.TagMissing,
                "id",
                "标签标识写错了或已经没了。读一次图看现有标签，或者先建一个。"),

            [ErrorCodes.ActionMissing] = new(
                ErrorCodes.ActionMissing,
                "id",
                "动作标识写错了或已经没了。读一次图看现有动作，或者先建一个。"),

            [ErrorCodes.NodeMissing] = new(
                ErrorCodes.NodeMissing,
                "id",
                "节点标识写错了或已经没了。先读一次图拿到现有节点标识，再照着重发。"),

            [ErrorCodes.EdgeMissing] = new(
                ErrorCodes.EdgeMissing,
                "id",
                "边标识写错了或已经没了。先读一次图拿到现有边标识，再照着重发。"),

            [ErrorCodes.InvalidId] = new(
                ErrorCodes.InvalidId,
                "id",
                "标识不能是空的。给它一个小写字母、数字与连字符组成的名字。"),

            [ErrorCodes.FieldUnknown] = new(
                ErrorCodes.FieldUnknown,
                "field",
                "这个字段没登记过。先读一次图看这份文档认得的字段名，或者换一个字段。"),

            [ErrorCodes.FieldValueInvalid] = new(
                ErrorCodes.FieldValueInvalid,
                "value",
                "这个值的写法不对。按这个字段要的类型重写一次，字段名本身是对的、不用换。"),

            [ErrorCodes.EdgePortMissing] = new(
                ErrorCodes.EdgePortMissing,
                "from",
                "这个节点上没有这个端口。去掉端点后面的端口名，或者换一个这个节点真有的端口。"),

            [ErrorCodes.EdgePortOnComposite] = new(
                ErrorCodes.EdgePortOnComposite,
                "from",
                "组合没有端口。端点写成组合标识本身，不要在后面带端口名。"),

            [ErrorCodes.LayoutNodeMissing] = new(
                ErrorCodes.LayoutNodeMissing,
                "memberIds",
                "这条约束引用的节点不存在。先读一次图核对成员标识，再重发这条约束。"),

            [ErrorCodes.LayoutOrderEdgeMissing] = new(
                ErrorCodes.LayoutOrderEdgeMissing,
                "memberIds",
                "次序引用的边不存在，或者不是主语节点的出边。换一条主语节点真的有的出边。"),

            [ErrorCodes.LayoutConstraintInvalid] = new(
                ErrorCodes.LayoutConstraintInvalid,
                "kind",
                "这条约束本身不成立。核对种类与成员：同层与对齐要两个以上节点，层内次序要一个主语加它的出边。"),

            [ErrorCodes.LayoutConstraintMissing] = new(
                ErrorCodes.LayoutConstraintMissing,
                "kind",
                "要删的约束已经不在了。先读一次图看现在有哪些约束，别重复删同一条。"),

            [ErrorCodes.MembershipMismatch] = new(
                ErrorCodes.MembershipMismatch,
                "memberIds",
                "成员列表与父级对不上。把两边改成一致：要么把成员从列表里去掉，要么把它的父级改成这个组合。"),

            [ErrorCodes.ParentMissing] = new(
                ErrorCodes.ParentMissing,
                "targetId",
                "父级指向的组合不存在。先把那个组合建出来，或者把父级换成一个存在的组合。"),

            [ErrorCodes.TagMemberMissing] = new(
                ErrorCodes.TagMemberMissing,
                "memberIds",
                "标签里的成员不存在。读一次图核对成员标识之后重发。"),

            [ErrorCodes.ActionTargetMissing] = new(
                ErrorCodes.ActionTargetMissing,
                "targetId",
                "动作的目标不存在。先把目标建出来，或者把目标换成一个存在的元素。"),

            [ErrorCodes.PaletteEntryMissing] = new(
                ErrorCodes.PaletteEntryMissing,
                "token",
                "这个样式令牌不在调色板里。先读一次图看现有令牌，或者先建一个。"),

            [ErrorCodes.PaletteEntryInUse] = new(
                ErrorCodes.PaletteEntryInUse,
                "token",
                "还有元素在用这个令牌。先把引用它的节点、边与标签改成别的令牌，再删它。"),

            [ErrorCodes.VersionConflict] = new(
                ErrorCodes.VersionConflict,
                null,
                "手上的副本旧了。先读一次图拿到当前版本号，再照着重发这一次改动。"),

            [ErrorCodes.InvalidExpectedVersion] = new(
                ErrorCodes.InvalidExpectedVersion,
                null,
                "报的版本号比服务端还新，是参数写错了。重新读一次图拿真实版本号，不要照着上一次的数报。"),

            [ErrorCodes.ExpectedVersionRequired] = new(
                ErrorCodes.ExpectedVersionRequired,
                null,
                "这条通路要求带上版本号。先读一次图，把读到的版本号带上来再发。"),

            [ErrorCodes.LayoutAllLevelsTimeout] = new(
                ErrorCodes.LayoutAllLevelsTimeout,
                null,
                "布局算不出来。减少一些约束或者把图拆小；改布局参数重试之前，先想清楚是哪一条约束让它算不动。"),

            [ErrorCodes.McpUnauthorized] = new(
                ErrorCodes.McpUnauthorized,
                null,
                "凭据没通过。这不是改参数能解决的，去换一份凭据或者找配置这件事的人。"),

            [ErrorCodes.McpRateLimited] = new(
                ErrorCodes.McpRateLimited,
                null,
                "请求太密了。等一会儿再发，别立刻重试——立刻重试只会再撞一次。"),

            [ErrorCodes.McpForbidden] = new(
                ErrorCodes.McpForbidden,
                null,
                "凭据的权限档不够做这件事。换成能改这份图的凭据，或者把要做的事改成只读的那些。"),

            [ErrorCodes.McpPathEscaped] = new(
                ErrorCodes.McpPathEscaped,
                null,
                "这个路径落在被限定的工作区之外，换凭据也打不开。把文件放进工作区，或者换一个在里面的路径。"),

            [ErrorCodes.McpTimeout] = new(
                ErrorCodes.McpTimeout,
                null,
                "这一次调用超时了，而文档一个字节都没改。把这一次的活拆小一点再发，或者过一会儿重试。"),

            [ErrorCodes.InternalError] = new(
                ErrorCodes.InternalError,
                null,
                "服务端内部出错。原样重试一次；再失败就别再试，把这一次调用报给人。"),

            [ErrorCodes.DocumentReadOnly] = new(
                ErrorCodes.DocumentReadOnly,
                null,
                "这份文档正被另一个进程编辑着，这一份只读。去改那一份，或者等对方放开再来。"),

            #endregion

            #region 工具层的参数校验

            [Tools.ToolErrorCodes.UnknownTool] = new(
                Tools.ToolErrorCodes.UnknownTool,
                null,
                "工具名不在表里。换成已登记的那几个工具名，别自己拼一个。"),

            [Tools.ToolErrorCodes.ArgumentMissing] = new(
                Tools.ToolErrorCodes.ArgumentMissing,
                null,
                "必填参数没给。按报出来的那个参数名把它补上再发，其余参数不用动。"),

            [Tools.ToolErrorCodes.ArgumentInvalid] = new(
                Tools.ToolErrorCodes.ArgumentInvalid,
                null,
                "参数值不满足它的约束。按期望那一栏给的形式重写这个参数；形式对不上时这一次调用什么都没改。"),

            [Tools.ToolErrorCodes.ArgumentUnknown] = new(
                Tools.ToolErrorCodes.ArgumentUnknown,
                null,
                "参数名不认得。把它删掉，或者换成期望那一栏列出的可用参数名。"),

            [Tools.ToolErrorCodes.NotSupported] = new(
                Tools.ToolErrorCodes.NotSupported,
                null,
                "这条路还没接上，重试多少次都一样。换成现在支持的做法，或者把缺的那一样报给人。"),

            [Tools.ToolErrorCodes.RetryExhausted] = new(
                Tools.ToolErrorCodes.RetryExhausted,
                null,
                "同一个调用已经连着试到上限了，别再原样重试。换一个做法，或者把这件事报给人。"),

            #endregion
        };

    /// <summary>全部登记过的修复线索，供门禁逐个核对。</summary>
    public static IReadOnlyDictionary<string, RepairHint> All => Table;

    /// <summary>
    /// 查一个错误码的修复线索。认不出的码退回一句通用的。
    /// </summary>
    /// <remarks>
    /// 认不出时仍然给出一句建议而不是空字符串：模型拿到一个空建议，多半会把同一个调用原样再发一遍。
    /// </remarks>
    public static RepairHint For(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return Table.TryGetValue(code, out var hint)
            ? hint
            : new RepairHint(code, null, UnknownSuggestion);
    }
}
