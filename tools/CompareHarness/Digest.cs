using System.Security.Cryptography;
using System.Text;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 内容的指纹。
/// </summary>
/// <remarks>
/// <para>
/// 用途是"对不上就报错"，不是防篡改。人工评分是对着某一版检查项做的：
/// 检查项改过之后，评分文件里的"第 3 项"指的已经是另一句话，
/// 把两边算到一起会得出一个没有意义的一致率——而它看起来和有意义的那个一模一样。
/// </para>
/// <para>
/// 行序由调用方给定，必须稳定：同一批内容换个顺序就是另一个指纹，这是刻意的，
/// 因为顺序变了，"第几项"也就变了。
/// </para>
/// <para>
/// 叫 <c>Digest</c> 而不是 <c>Fingerprint</c>，是因为记录上有个同名的属性；
/// 同名的话在记录内部写 <c>Fingerprint.Of(…)</c> 会被解析成那个属性，编译器报"需要对象引用"。
/// </para>
/// </remarks>
internal static class Digest
{
    /// <summary>把若干行按给定顺序拼起来，取 SHA-256 的前 8 字节。</summary>
    public static string Of(IEnumerable<string> lines) =>
        OfText(string.Join('\n', lines));

    public static string OfText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();

    /// <summary>文件的指纹。用来把报告与"当时读的那份文件"绑在一起。</summary>
    public static string OfFile(string path) =>
        OfText(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal));

    /// <summary>
    /// 检查项清单的指纹。
    /// </summary>
    /// <remarks>
    /// 逐条记下"提示词标识 + 序号 + 原文"。原文改了、条数改了、顺序改了，
    /// 指纹都会变——这三样都会让评分文件里的序号失去意义。
    /// </remarks>
    public static string OfChecks(PromptSet prompts) =>
        Of(prompts.SelectMany(prompt => prompt.Checks.Select(
            (check, i) => $"{prompt.Id}|{i}|{check.Text}|{(check.MachineCheckable ? "machine" : "human")}")));
}
