using DuetDiagram.Core.Model;
using SkiaSharp;

namespace DuetDiagram.Render;

/// <summary>
/// 认不出的字该换哪个字体。
/// </summary>
/// <remarks>
/// <para>
/// **度量与绘制共用这一份。** 两边各挑一次的话，量出来的宽度与画出来的那一段就可能出自
/// 两个不同的字体，而表现是"框比字窄一点"或"字在框里偏一边"——两种看起来都像对齐算错了，
/// 真正的原因却在字体挑得不一样。
/// </para>
/// <para>
/// **整段换还是逐字换，由调用方决定。** 这里只回答"这个字用哪个家族画得出来"，
/// 不规定调用方拿这个答案去换一整段还是只换一段。位图与 PDF 那条路整段换，
/// 度量那条路按连续同家族切段，两条路因此都从这里取同一个答案。
/// </para>
/// </remarks>
internal static class FontFallback
{
    /// <summary>文档没点名家族时用的那个。度量的兜底与绘制的兜底必须是同一个。</summary>
    internal const string DefaultFamily = "Segoe UI";

    /// <summary>
    /// 这段文字里第一个这个字体画不出来的字。都在就返回零以下。
    /// </summary>
    /// <remarks>
    /// 逐字问一遍而不是只看第一个：混排的标签里第一个字常常是拉丁字母，
    /// 而画不出来的是后面那个中文字。
    /// </remarks>
    internal static int FirstMissing(SKFont font, string text)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);

        for (var index = 0; index < text.Length;)
        {
            var codePoint = char.ConvertToUtf32(text, index);

            if (font.GetGlyph(codePoint) == 0)
            {
                return codePoint;
            }

            index += char.IsSurrogatePair(text, index) ? 2 : 1;
        }

        return -1;
    }

    /// <summary>
    /// 这个字要用哪个家族来画。
    /// </summary>
    /// <remarks>
    /// 点名的家族画得出来就用它，画不出来才去问字体管理器。先问字体对象而不是直接问管理器，
    /// 是因为前者只是一次字形查表，而后者要遍历本机装的字体。
    /// </remarks>
    /// <param name="primary">按点名的家族取到的字体。</param>
    /// <param name="family">点名的家族名。</param>
    /// <param name="codePoint">这个字的码位。</param>
    /// <returns>家族名。字体管理器也认不出时返回点名的那个，让调用方按原样画。</returns>
    internal static string FamilyFor(SKFont primary, string family, int codePoint)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(family);

        if (primary.GetGlyph(codePoint) != 0)
        {
            return family;
        }

        var matched = SKFontManager.Default.MatchCharacter(family, codePoint);

        if (matched is null)
        {
            return family;
        }

        using (matched)
        {
            return matched.FamilyName;
        }
    }

    /// <summary>
    /// 换一个认得出这个字的字体。
    /// </summary>
    /// <remarks>
    /// 拿点名的家族当线索去问字体管理器：它认得这个字就把点名的家族还回来，
    /// 认不得就还一个认得的（例如西文家族遇上汉字，还的是本机的中文字体）。
    /// 问不到时返回空，由调用方决定是照原样画还是另想办法。
    /// </remarks>
    internal static SKTypeface? Substitute(string family, int codePoint, FontWeight weight, bool italic)
    {
        ArgumentNullException.ThrowIfNull(family);

        var matched = SKFontManager.Default.MatchCharacter(family, codePoint);

        if (matched is null)
        {
            return null;
        }

        using (matched)
        {
            return FromFamily(matched.FamilyName, weight, italic);
        }
    }

    /// <summary>
    /// 按家族、字重与倾斜取一个字体。
    /// </summary>
    /// <remarks>
    /// 字重与倾斜要一起交给字体匹配，而不是拿到常规体再让绘制方自己变：
    /// 量出来的宽度必须与画出来的那一个字面一致，否则加粗的那一段会溢出。
    /// 家族不存在时退回系统默认字体，与度量器同一条口径。
    /// </remarks>
    internal static SKTypeface FromFamily(string family, FontWeight weight, bool italic)
    {
        ArgumentNullException.ThrowIfNull(family);

        return SKTypeface.FromFamilyName(
            family,
            weight == FontWeight.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;
    }
}
