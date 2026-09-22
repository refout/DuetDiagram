using System.Text.Json.Serialization;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Serialization;

/// <summary>
/// 编译期生成的序列化上下文。所有需要进出的类型都必须在这里登记。
/// </summary>
/// <remarks>
/// <para>
/// 漏登记不会在编译时报错，而是等到运行时第一次序列化那个类型才失败——
/// 在 AOT 发布之后才暴露。所以有两道防线：一是这个清单要跟着类型走，
/// 二是测试用反射枚举程序集里的具体类型跟已登记的多态子类型做全等比较。
/// </para>
/// <para>
/// 选项里的三项各有原因：属性名用小驼峰是为了让产出的 JSON 符合常见约定，
/// 便于外部直接阅读和手写；枚举写成名字而不是序号，是为了让文件在版本演进、
/// 枚举成员顺序调整之后仍然能被正确解析；忽略空值是为了让文件体积小一些，
/// 而且缺省值本来就不需要写出来。
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DiagramDocument))]
[JsonSerializable(typeof(DiagramSnapshot))]
[JsonSerializable(typeof(NodeDef))]
[JsonSerializable(typeof(EdgeDef))]
[JsonSerializable(typeof(PageDef))]
[JsonSerializable(typeof(LayerDef))]
[JsonSerializable(typeof(CompositeDef))]
[JsonSerializable(typeof(GroupDef))]
[JsonSerializable(typeof(LaneDef))]
[JsonSerializable(typeof(SubflowDef))]
[JsonSerializable(typeof(ComboDef))]
[JsonSerializable(typeof(TagDef))]
[JsonSerializable(typeof(ActionDef))]
[JsonSerializable(typeof(FontDef))]
[JsonSerializable(typeof(TextStylePreset))]
[JsonSerializable(typeof(NodeStyle))]
[JsonSerializable(typeof(EdgeStyle))]
[JsonSerializable(typeof(TextStyle))]
[JsonSerializable(typeof(Thickness))]
[JsonSerializable(typeof(PortDef))]
[JsonSerializable(typeof(Palette))]
[JsonSerializable(typeof(PaletteEntry))]
[JsonSerializable(typeof(CanvasSettings))]
[JsonSerializable(typeof(Size))]
[JsonSerializable(typeof(LayoutHints))]
[JsonSerializable(typeof(Constraint<SameRankConstraint>))]
[JsonSerializable(typeof(Constraint<OrderConstraint>))]
[JsonSerializable(typeof(Constraint<AlignConstraint>))]
[JsonSerializable(typeof(Constraint<PlaceConstraint>))]
[JsonSerializable(typeof(SameRankConstraint))]
[JsonSerializable(typeof(OrderConstraint))]
[JsonSerializable(typeof(AlignConstraint))]
[JsonSerializable(typeof(PlaceConstraint))]
[JsonSerializable(typeof(CommandMemento))]
[JsonSerializable(typeof(EdgePlacement))]
[JsonSerializable(typeof(MemberPlacement))]
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
[JsonSerializable(typeof(MemberPlacement[]))]
[JsonSerializable(typeof(CompositeDef[]))]
[JsonSerializable(typeof(LayerDef[]))]
[JsonSerializable(typeof(PageDef[]))]
[JsonSerializable(typeof(TagDef[]))]
[JsonSerializable(typeof(ActionDef[]))]
[JsonSerializable(typeof(PortDef[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class DiagramJsonContext : JsonSerializerContext;
