namespace DuetDiagram.Core.Concurrency;

/// <summary>
/// 图层级访问控制：把认证主体绑到一份权限上。
/// </summary>
/// <remarks>
/// <para>
/// **绑的是主体，不是连接。** 绑连接的话，同一个令牌换一条连接就能绕开限制，
/// 而那条限制本来要说的是"这份凭据只许碰这几个图层"。
/// </para>
/// <para>
/// **认不出的主体一律按只读。** 默认放行的话，一处拼错的主体名会让限制静默失效——
/// 而那种失效不报错、不抛异常，看起来就像"本来就没有限制"，等发现时那份图已经被改过了。
/// </para>
/// <para>
/// 它只做查表，不保存"谁在什么时候用过什么权限"。要审计的话读应用日志，
/// 这里记一份的话，同一件事会有两个地方各说一遍，而两处迟早不一样。
/// </para>
/// </remarks>
public sealed class LayerAcl
{
    private readonly Dictionary<string, PermissionSet> _bySubject;

    /// <param name="subjects">主体名与它的权限。同一个名字给两次会抛。</param>
    /// <remarks>
    /// 名字重复时抛而不是后者覆盖前者：覆盖的话，一份本来要收窄的配置会静默失效，
    /// 而配置的人从启动日志里看不出任何异常。
    /// </remarks>
    public LayerAcl(IEnumerable<(string Subject, PermissionSet Permissions)> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        _bySubject = new Dictionary<string, PermissionSet>(StringComparer.Ordinal);

        foreach (var (subject, permissions) in subjects)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(subject);
            ArgumentNullException.ThrowIfNull(permissions);

            if (!_bySubject.TryAdd(subject, permissions))
            {
                throw new ArgumentException($"主体 {subject} 登记了两次。", nameof(subjects));
            }
        }
    }

    /// <summary>登记过的主体名，按登记次序。它不含权限，只用来核对配置。</summary>
    public IReadOnlyList<string> Subjects => [.. _bySubject.Keys];

    /// <summary>
    /// 一个主体拿到的权限。
    /// </summary>
    /// <remarks>
    /// 认不出的主体拿只读，而不是空引用：空引用会逼每个调用点各写一次判空，
    /// 而漏掉的那一处就是一条放行的路。
    /// </remarks>
    public PermissionSet For(string? subject) =>
        subject is not null && _bySubject.TryGetValue(subject, out var permissions)
            ? permissions
            : PermissionSet.ReadOnly;

    /// <summary>一个主体能不能碰某个图层。</summary>
    public bool AllowsLayer(string? subject, string? layerId) => For(subject).AllowsLayer(layerId);
}
