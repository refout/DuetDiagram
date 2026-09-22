using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;

namespace DuetDiagram.App.Services;

/// <summary>
/// 一份文档文件：读进来、写回去。
/// </summary>
/// <remarks>
/// <para>
/// **读进来之后一律整体校验。** 命令层在写入前挡掉了大部分非法状态，但文件是外部输入，
/// 不经过命令层；而上一个进程还可能是写到一半被强杀的，留下的既不是旧内容也不是新内容。
/// 校验只报告不修改，处置交给调用方——拒绝打开、丢掉有问题的那部分、还是照常打开再提示，
/// 这三种在不同场景下都说得通，读文件这一层不该替调用方选。
/// </para>
/// <para>
/// **写盘先写临时文件再替换。** 直接往目标文件上写的话，写到一半断电或被杀，
/// 留下的是一份截断的内容；而用户手上没有第三份可以退回去。替换走系统的改名操作，
/// 它在同一卷内是一次原子替换：目标文件要么是完整的旧内容，要么是完整的新内容。
/// </para>
/// </remarks>
internal static class DocumentFile
{
    /// <summary>写临时文件时用的后缀。它与目标文件同目录，替换才是同一卷内的改名。</summary>
    private const string TempSuffix = ".tmp";

    /// <summary>
    /// 读一份文档。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="issues">整体校验发现的问题，按发现次序追加。</param>
    /// <exception cref="InvalidDataException">文件读不出来，或者内容不是一份文档。</exception>
    /// <remarks>
    /// 内容不是合法 JSON 时抛异常而不是返回空：返回空会把"文件是坏的"与"文件里写着一个空文档"
    /// 混成一件事，而这两者该给用户的处置完全不同——前者要去看备份，后者只是这份文档还没有元素。
    /// </remarks>
    public static DiagramDocument Read(string path, IList<ValidationIssue> issues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(issues);

        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"读不出这份文档：{exception.Message}", exception);
        }

        DiagramDocument document;

        try
        {
            document = DiagramSerializer.DeserializeFull(text);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException)
        {
            throw new InvalidDataException($"这份文档不是合法的文档格式：{exception.Message}", exception);
        }

        foreach (var issue in DiagramValidator.Validate(document))
        {
            issues.Add(issue);
        }

        return document;
    }

    /// <summary>把一份文档写回去。</summary>
    /// <param name="path">目标文件路径。</param>
    /// <param name="document">要写的内容。</param>
    public static void Write(string path, DiagramDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        var temp = path + TempSuffix;

        File.WriteAllText(temp, DiagramSerializer.SerializeFull(document));

        // 覆盖式改名。写成"先删目标再改名"的话，删掉之后、改名之前的那一瞬间，
        // 磁盘上两份都不在——而那一刻正好断电就是彻底的丢失。
        File.Move(temp, path, overwrite: true);
    }
}
