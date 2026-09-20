using System.Text.Json;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Serialization;

/// <summary>
/// IR / Memento / Diff 的序列化入口。全部走源生成，无反射，AOT 安全。
/// </summary>
public static class DiagramSerializer
{
    /// <summary>全量序列化文档。P1 判据 #6 关注其 1000 节点耗时，因此调用方应延迟求值。</summary>
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
    /// 规范化 JSON。用于 P1 判据 #7「原子性用规范化 JSON 比较」——
    /// 比较两个文档是否逐字节等价。
    /// </summary>
    public static string Normalize(DiagramDocument document) => SerializeFull(document);
}
