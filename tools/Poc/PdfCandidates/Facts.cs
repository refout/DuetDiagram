using System.Text;

namespace DuetDiagram.Poc.PdfCandidates;

/// <summary>
/// 把一份 PDF 自己的属性读出来。
/// </summary>
/// <remarks>
/// <para>
/// **判据尽量落在文件上，不落在候选的代码上。** 字节数、有没有位图对象、字体嵌没嵌、
/// 两次导出一不一样——这些都是文件的属性，换一个写入器它们照样成立。
/// 只读内容流的地方才需要解压，那是唯一一处不得不看内容的。
/// </para>
/// <para>
/// **这一份不认识候选。** 给它字节，它给事实。所以加一个候选不用改这里，
/// 而报告里那张对照表的每一格都能追到这里的某一行输出。
/// </para>
/// </remarks>
internal static class Facts
{
    public static void Print(string name, byte[]? bytes, string? failure, Func<byte[]?> again)
    {
        Console.WriteLine($"=== {name} ===");
        Console.WriteLine($"license      {Candidates.License(name)}");

        if (bytes is null)
        {
            Console.WriteLine($"result       画不出来：{failure}");
            Console.WriteLine();

            return;
        }

        var text = Encoding.Latin1.GetString(bytes);
        var second = again();

        Console.WriteLine($"bytes        {bytes.Length}");
        Console.WriteLine($"head         {Encoding.ASCII.GetString(bytes, 0, Math.Min(8, bytes.Length)).Replace("\n", "\\n")}");
        Console.WriteLine($"deterministic {second is not null && bytes.AsSpan().SequenceEqual(second)}");
        Console.WriteLine($"vector       {!text.Contains("/Subtype /Image", StringComparison.Ordinal)}");
        Console.WriteLine($"font embedded {text.Contains("/FontFile", StringComparison.Ordinal)}");
        Console.WriteLine($"cid font     {text.Contains("/Type0", StringComparison.Ordinal) && text.Contains("Identity-H", StringComparison.Ordinal)}");
        Console.WriteLine($"pages        {Pages(text)}");
        Console.WriteLine($"timestamp    {text.Contains("/CreationDate", StringComparison.Ordinal)}");
        Console.WriteLine($"whole font   {string.Join(", ", Lengths(text))}");

        if (failure is not null)
        {
            Console.WriteLine($"note         {failure}");
        }

        Console.WriteLine();
    }

    /// <summary>嵌进去的字体程序有多大。等于源字体文件大小就说明整份嵌了，没做子集。</summary>
    private static IEnumerable<string> Lengths(string text)
    {
        var at = text.IndexOf("/Length1", StringComparison.Ordinal);

        while (at >= 0)
        {
            var from = at + "/Length1".Length;

            while (from < text.Length && char.IsWhiteSpace(text[from]))
            {
                from++;
            }

            var end = from;

            while (end < text.Length && char.IsAsciiDigit(text[end]))
            {
                end++;
            }

            if (end > from)
            {
                yield return text[from..end];
            }

            at = text.IndexOf("/Length1", from, StringComparison.Ordinal);
        }
    }

    /// <summary>几页。数的是页对象，把页树那一个 <c>/Type /Pages</c> 排除掉。</summary>
    private static int Pages(string text)
    {
        var count = 0;
        var at = 0;

        while ((at = text.IndexOf("/Type /Page", at, StringComparison.Ordinal)) >= 0)
        {
            if (!text.AsSpan(at).StartsWith("/Type /Pages"))
            {
                count++;
            }

            at += "/Type /Page".Length;
        }

        return count;
    }
}
