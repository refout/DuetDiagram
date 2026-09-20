using System.Text.Json;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Serialization;

/// <summary>
/// 序列化入口。全部走编译期生成的转换器，不使用运行时反射。
/// </summary>
/// <remarks>
/// <para>
/// 反射式序列化在裁剪和 AOT 编译下会被裁掉或直接报错，而本项目的目标之一就是
/// 能发布成无运行时依赖的原生可执行文件。走源生成之后，每个类型用到的读写代码
/// 都在编译期生成好，运行时不需要查找元数据。
/// </para>
/// <para>
/// 反序列化统一在返回空时抛异常而不是返回空引用。调用方拿到的要么是有效对象，
/// 要么是一个明确的异常；返回空引用会把"JSON 是合法的 null"和"解析失败"混在一起，
/// 错误只会在更远的地方以更难懂的形式暴露出来。
/// </para>
/// </remarks>
public static class DiagramSerializer
{
    /// <summary>
    /// 序列化整份文档。
    /// </summary>
    /// <remarks>
    /// 这个操作在大文档上不便宜。调用方在差异计算这类高频路径上应当延迟到确认需要
    /// 全量快照时再调用，不要在判断之前就序列化。
    /// </remarks>
    public static string SerializeFull(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, DiagramJsonContext.Default.DiagramDocument);
    }

    public static DiagramDocument DeserializeFull(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize(json, DiagramJsonContext.Default.DiagramDocument)
            ?? throw new JsonException("Document JSON deserialized to null.");
    }

    /// <summary>序列化逆变更快照。派生类型靠多态标签区分，所以必须按基类写出。</summary>
    public static string SerializeMemento(CommandMemento memento)
    {
        ArgumentNullException.ThrowIfNull(memento);
        return JsonSerializer.Serialize(memento, DiagramJsonContext.Default.CommandMemento);
    }

    public static CommandMemento DeserializeMemento(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize(json, DiagramJsonContext.Default.CommandMemento)
            ?? throw new JsonException("Memento JSON deserialized to null.");
    }

    public static string SerializeDiff(DiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return JsonSerializer.Serialize(diff, DiagramJsonContext.Default.DiffResult);
    }

    public static DiffResult DeserializeDiff(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize(json, DiagramJsonContext.Default.DiffResult)
            ?? throw new JsonException("Diff JSON deserialized to null.");
    }

    public static string SerializeNotification(ChangeNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return JsonSerializer.Serialize(notification, DiagramJsonContext.Default.ChangeNotification);
    }

    public static ChangeNotification DeserializeNotification(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize(json, DiagramJsonContext.Default.ChangeNotification)
            ?? throw new JsonException("ChangeNotification JSON deserialized to null.");
    }

    /// <summary>
    /// 规范化序列化。用它比较两份文档是否等价：字符串相同即内容相同。
    /// </summary>
    /// <remarks>
    /// 之所以不逐字段比较，是因为这个比较最常见的用途是判断"命令失败后文档有没有被改脏"，
    /// 而任何一处遗漏都会让这个判断失真。整体序列化再做字符串比较不会漏掉任何字段。
    /// </remarks>
    public static string Normalize(DiagramDocument document) => SerializeFull(document);
}
