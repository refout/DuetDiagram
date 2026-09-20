namespace DuetDiagram.Core.Model;

/// <summary>
/// 边定义（IR 语义层）。只存语义与端口，不存坐标。
/// </summary>
public sealed record EdgeDef
{
    public required string Id { get; init; }

    public required string From { get; init; }

    public required string To { get; init; }

    public string? FromPort { get; init; }

    public string? ToPort { get; init; }

    public string Label { get; init; } = string.Empty;

    public LineStyle Line { get; init; } = LineStyle.Solid;

    public ArrowStyle Arrow { get; init; } = ArrowStyle.Arrow;

    public string? StyleToken { get; init; }
}
