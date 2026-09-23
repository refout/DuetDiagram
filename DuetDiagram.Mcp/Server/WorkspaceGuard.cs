using DuetDiagram.Core.Commands;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 一个被限定在工作区里的路径。
/// </summary>
/// <remarks>
/// <para>
/// 它管的是「该不该碰这份文件」，与「碰得到碰不到」是两件事：文件不存在、
/// 读不出来、格式不对，都由后面那一步去报，各自有各自的说法。混在一起的话，
/// 一个路径写错与一次越权会给出同一句话，而两者的处置完全不同。
/// </para>
/// <para>
/// **判据是解析之后的路径，不是字符串。** 拿候选路径与工作区根做前缀比较的话，
/// `..` 与符号链接这两种最普通的写法都能绕过去，而它们看起来完全正常——
/// 于是那道关形同虚设，且不会有任何东西报错。
/// </para>
/// </remarks>
public sealed class WorkspaceGuard
{
    private readonly string _root;

    /// <summary>把工作区根固定下来。根不存在时抛异常。</summary>
    /// <remarks>
    /// 根不存在就不起：让它先过，之后每一次解析都会落到「不在里面」上，
    /// 而那时报出来的是"路径越权"，与真正的原因（工作区压根没建）对不上。
    /// </remarks>
    public WorkspaceGuard(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var full = Path.GetFullPath(root);

        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"工作区目录不存在：{full}");
        }

        // 根自己也可能是一条链接。不解析的话，从链接那一侧看进来的路径全都不在根下面。
        _root = RealDirectory(full);
    }

    /// <summary>工作区根的绝对路径。</summary>
    public string Root => _root;

    /// <summary>
    /// 把候选路径解析成工作区里的一个绝对路径。
    /// </summary>
    /// <param name="candidate">候选路径。相对路径按工作区根解析。</param>
    /// <returns>解析之后的绝对路径，可以直接拿去打开。</returns>
    /// <exception cref="WorkspaceEscapeException">解析之后落在工作区之外。</exception>
    public string Resolve(string candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);

        var full = Path.GetFullPath(candidate, _root);
        var real = RealFile(full);

        return IsInside(_root, real)
            ? real
            : throw new WorkspaceEscapeException(candidate, real, _root);
    }

    /// <summary>
    /// 候选路径解析之后还在不在根下面。
    /// </summary>
    /// <remarks>
    /// 用相对路径算，不用字符串前缀：前缀比较会把 `/work-other` 当成 `/work` 里的东西，
    /// 而那是两个不同的目录。相对路径那一步由运行时按当前平台的大小写规则处理，
    /// 自己拼一个比较函数只会漏掉某一档。
    /// </remarks>
    private static bool IsInside(string root, string full)
    {
        var relative = Path.GetRelativePath(root, full);

        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    /// <summary>解析一条路径上的目录链接。</summary>
    private static string RealDirectory(string directory) =>
        new DirectoryInfo(directory).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory;

    /// <summary>
    /// 解析一条路径上的文件链接。
    /// </summary>
    /// <remarks>
    /// 文件本身还不是链接时，还要看它的父目录：链接一个目录、再从那个目录里取文件，
    /// 是比直接链接文件更常见的写法。只解一层的话，那一种就漏了。
    /// </remarks>
    private static string RealFile(string full)
    {
        if (File.Exists(full))
        {
            return new FileInfo(full).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;
        }

        var parent = Path.GetDirectoryName(full);

        return parent is not null && Directory.Exists(parent)
            ? Path.Combine(RealDirectory(parent), Path.GetFileName(full))
            : full;
    }
}

/// <summary>
/// 一个路径解析之后落在了工作区之外。
/// </summary>
/// <remarks>
/// 单独一个异常类型而不是复用别的：调用方要能一眼看出这一次失败是"越权"，
/// 而不是"文件不存在"或"读不出来"——那三种的处置完全不同。
/// </remarks>
public sealed class WorkspaceEscapeException : Exception
{
    /// <summary>这个拒绝对应的错误码。</summary>
    public const string Code = ErrorCodes.McpPathEscaped;

    public WorkspaceEscapeException(string candidate, string resolved, string root)
        : base($"路径 {candidate} 解析之后落在工作区之外。")
    {
        Candidate = candidate;
        Resolved = resolved;
        Root = root;
    }

    /// <summary>调用方给的那个路径。</summary>
    public string Candidate { get; }

    /// <summary>解析之后的绝对路径。</summary>
    public string Resolved { get; }

    /// <summary>工作区根。</summary>
    public string Root { get; }
}
