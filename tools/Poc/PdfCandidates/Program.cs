using DuetDiagram.Poc.PdfCandidates;

// PDF 写入候选的对照取证。用法：
//   dotnet run --project tools/Poc/PdfCandidates -c Release -- all
//   dotnet run --project tools/Poc/PdfCandidates -c Release -- quest
//
// 四个候选各画同一张图，再把文件自己的属性打出来。它只出事实，不判好坏。

var wanted = args.Length == 0 || args.Contains("all", StringComparer.Ordinal) ? Candidates.Names : args;

foreach (var name in wanted)
{
    if (!Candidates.Names.Contains(name, StringComparer.Ordinal))
    {
        Console.WriteLine($"认不出的候选：{name}。可用的是：{string.Join('、', Candidates.Names)}");

        return 1;
    }

    Facts.Print(name, Candidates.Render(name, out var failure), failure, () => Candidates.Render(name, out _));
}

return 0;
