using System.Text;
using BenchmarkDotNet.Attributes;
using DuetDiagram.Core.Model;
using DuetDiagram.Mermaid.Export;
using DuetDiagram.Mermaid.Import;
using DuetDiagram.Mermaid.Lexing;
using DuetDiagram.Mermaid.Parsing;

namespace DuetDiagram.Benchmarks;

/// <summary>
/// Mermaid 导入的耗时基线。
/// </summary>
/// <remarks>
/// <para>
/// 三段分开测，因为它们的开销差别很大，而合成一个数之后不知道该优化哪一段。
/// 用户感知到的是第三段，前两段是它的组成部分。
/// </para>
/// <para>
/// 规模是 1000 节点、950 条连线、20 个子图、带样式——按验收口径
/// "1000 节点导入不超过 500 毫秒"来定。导出用同一份文档，两边的数字才可比。
/// </para>
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
public class MermaidBenchmarks
{
    private string _source = string.Empty;
    private DiagramDocument _document = new("bench");

    [GlobalSetup]
    public void Setup()
    {
        _source = Layered(depth: 20, breadth: 50);
        _document = MermaidImporter
            .Import(MermaidParser.Parse(_source), new ImportOptions { DocumentId = "bench" })
            .Document;
    }

    /// <summary>词法：源文本切成记号。</summary>
    [Benchmark]
    public int Lex_1000_Nodes() => MermaidLexer.Tokenize(_source).Count;

    /// <summary>词法加语法：切记号再装成语法树。</summary>
    [Benchmark]
    public int Parse_1000_Nodes() => MermaidParser.Parse(_source).Nodes.Count;

    /// <summary>词法加语法加映射：这是用户感知到的那一步。</summary>
    [Benchmark]
    public int Import_1000_Nodes() =>
        MermaidImporter
            .Import(MermaidParser.Parse(_source), new ImportOptions { DocumentId = "bench" })
            .Document.Nodes.Count;

    /// <summary>导出：IR 写回 Mermaid 文本。</summary>
    /// <remarks>
    /// 测的是导出本身，不含导入——导入的耗时在上一条里，两者相加才是往返的开销。
    /// </remarks>
    [Benchmark]
    public int Export_1000_Nodes() =>
        MermaidExporter.Export(_document, new ExportOptions()).Text.Length;

    /// <summary>
    /// 造一份分层的流程图。
    /// </summary>
    /// <remarks>
    /// 形状照着真实语料里最常见的那种：若干子图各自装一层节点，层与层之间有带标签的连线，
    /// 每个子图配一条样式。纯链条测不出子图与标签的开销，而那两样正是导入里最重的部分。
    /// </remarks>
    private static string Layered(int depth, int breadth)
    {
        var builder = new StringBuilder();
        builder.AppendLine("flowchart TD");

        for (var layer = 0; layer < depth; layer++)
        {
            builder.AppendLine($"subgraph 第{layer}层");

            for (var index = 0; index < breadth; index++)
            {
                builder.AppendLine($"n{layer}_{index}[\"节点 {layer}-{index}\"]");
            }

            builder.AppendLine("end");
        }

        for (var layer = 0; layer + 1 < depth; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                builder.AppendLine($"n{layer}_{index} -->|第 {layer} 层流向| n{layer + 1}_{(index + 1) % breadth}");
            }
        }

        for (var index = 0; index < breadth; index++)
        {
            builder.AppendLine($"style n0_{index} fill:#eef,stroke:#333");
        }

        builder.AppendLine("classDef 强调 fill:#FFF3CD,stroke:#B8860B");
        builder.AppendLine("class n1_0,n1_1,n1_2 强调");

        return builder.ToString();
    }
}
