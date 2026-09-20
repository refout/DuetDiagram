using System.Text.Json.Serialization;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Serialization;

/// <summary>
/// AOT 源生成上下文。所有需要序列化的类型都必须在此登记，否则 AOT 发布会失败。
/// </summary>
/// <remarks>
/// 多项式类型（<see cref="CommandMemento"/>、<see cref="DiffResult"/>）通过基类上的
/// <c>[JsonDerivedType]</c> 注册派生记录；登记缺失由 MementoRegistration 测试拦截。
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DiagramDocument))]
[JsonSerializable(typeof(NodeDef))]
[JsonSerializable(typeof(EdgeDef))]
[JsonSerializable(typeof(CommandMemento))]
[JsonSerializable(typeof(EdgePlacement))]
[JsonSerializable(typeof(FieldChange))]
[JsonSerializable(typeof(DiffResult))]
[JsonSerializable(typeof(VersionEntry))]
[JsonSerializable(typeof(ChangeNotification))]
[JsonSerializable(typeof(CommandError))]
[JsonSerializable(typeof(ValidationResult))]
[JsonSerializable(typeof(CommandResult))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(FieldChange[]))]
[JsonSerializable(typeof(VersionEntry[]))]
[JsonSerializable(typeof(EdgePlacement[]))]
internal sealed partial class DiagramJsonContext : JsonSerializerContext;
