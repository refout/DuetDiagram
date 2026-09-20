using System.Text.Json.Serialization;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Sidecar;

/// <summary>
/// Sidecar 的序列化上下文。与主文档分开：两者的读写时机与失败处置完全不同，
/// 混在一个上下文里会让"哪些类型属于哪种文件"变得看不出来。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LayoutSidecar))]
[JsonSerializable(typeof(UserSidecar))]
[JsonSerializable(typeof(NodePlacement))]
[JsonSerializable(typeof(EdgePath))]
[JsonSerializable(typeof(Anchor))]
[JsonSerializable(typeof(PortDef))]
internal sealed partial class SidecarJsonContext : JsonSerializerContext;

/// <summary>
/// Sidecar 的路径约定。
/// </summary>
/// <remarks>
/// <para>
/// 三个文件与文档同名同目录。放在一起的理由是"跟着文档走"：
/// 复制、移动、删除文档时不会漏掉附属内容。放到统一的缓存目录里虽然好清理，
/// 但用户把文档发给别人时会丢掉自己所有的固定位置，而那个损失是无声的。
/// </para>
/// <para>
/// 后缀取 <c>.layout.json</c> 而不是 <c>.json</c>，是为了让文件管理器里
/// 一眼能看出它属于哪份文档的哪一类内容，而不是三个同名文件挤在一起。
/// </para>
/// </remarks>
public static class SidecarPaths
{
    /// <summary>语义文本。LLM 纯文本模式的入口。</summary>
    public const string DslExtension = ".dsl";

    /// <summary>自动布局结果。可丢弃、可重算。</summary>
    public const string LayoutExtension = ".layout.json";

    /// <summary>人工产物。不可丢。</summary>
    public const string UserExtension = ".user.json";

    /// <summary>备份后缀。中间带时间戳，见 <see cref="Backup"/>。</summary>
    public const string BackupExtension = ".bak";

    /// <summary>去掉文档的扩展名，作为三个 sidecar 的共同前缀。</summary>
    private static string Stem(string documentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        var directory = Path.GetDirectoryName(documentPath);
        var stem = Path.GetFileNameWithoutExtension(documentPath);

        // 文档本身可能是 .layout.json 结尾的怪名字，去掉一次扩展名即可，
        // 多余的尾部原样保留——路径约定要可预测，不能猜用户想表达什么。
        return string.IsNullOrEmpty(directory) ? stem : Path.Combine(directory, stem);
    }

    public static string Dsl(string documentPath) => Stem(documentPath) + DslExtension;

    public static string Layout(string documentPath) => Stem(documentPath) + LayoutExtension;

    public static string User(string documentPath) => Stem(documentPath) + UserExtension;

    /// <summary>
    /// 备份路径：<c>文档名.user.yyyyMMdd-HHmmss.bak</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 时间戳用世界时间而不是本地时间：本地时间在夏令时切换那天会出现两个不同的时刻
    /// 对应同一个名字，先写的那份会被后写的覆盖掉——而备份恰恰最不该发生这种事。
    /// 名字里保留到秒是因为同一秒内的两次备份几乎不可能发生，真发生了覆盖一份也影响有限。
    /// </para>
    /// <para>
    /// 只剥掉 <c>.json</c>，保留 <c>.user</c>。整个后缀都剥掉的话，
    /// 备份会变成 <c>文档名.时间戳.bak</c>，光看名字分不出它是人工产物的备份
    /// 还是别的东西的备份。
    /// </para>
    /// </remarks>
    public static string Backup(string userPath, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPath);

        var stem = userPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? userPath[..^".json".Length]
            : userPath;

        return $"{stem}.{at.UtcDateTime:yyyyMMdd-HHmmss}{BackupExtension}";
    }

    /// <summary>从一个备份文件名里读回时间戳。不是备份时返回空。</summary>
    public static DateTimeOffset? ParseBackupTime(string backupPath)
    {
        var name = Path.GetFileNameWithoutExtension(backupPath);

        // 形如 文档名.user.20260920-143000，取最后一段。
        var parts = name.Split('.');
        var stamp = parts[^1];

        return stamp.Length == 15 && stamp[8] == '-'
            && DateTimeOffset.TryParseExact(
                stamp,
                "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed)
            ? parsed
            : null;
    }
}
