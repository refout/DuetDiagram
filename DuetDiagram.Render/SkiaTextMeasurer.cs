using DuetDiagram.Core.Model;
using SkiaSharp;

namespace DuetDiagram.Render;

/// <summary>
/// 用绘图后端量文本。
/// </summary>
/// <remarks>
/// <para>
/// 这是 <see cref="ITextMeasurer"/> 在生产路径上的实现。它**不进绘制列表**，
/// 只在构建列表之前参与"节点该多大"这件事——列表本身仍然只是一堆数，
/// 换一台机器照样逐字节可比。
/// </para>
/// <para>
/// 字体按家族、字号、字重缓存。每量一行就新建一个字体对象会让千节点图在测量上
/// 花掉比布局还多的时间，而字体对象的创建恰恰是其中最贵的一步。
/// </para>
/// <para>
/// **字体缺失时回退到默认字体，不报错。** 一个文档指定了本机没有的字体，
/// 报错会让它打不开；回退之后图还能看，只是排版与预期不同。
/// 这种"能看但不一样"必须能查出来，所以 <see cref="ResolveFamily"/> 把实际用上的
/// 家族名暴露出来，界面与自检可以用它对比。
/// </para>
/// <para>
/// **量的时候要按回退后的字体量。** 点名的家族画不出某一段字时（一份中文标签配上
/// 西文家族就是），绘制方会换一个认得的字体来画，换出来的那个字比认不出的那个宽得多。
/// 按点名的家族量的话，量出来的宽度会明显小于画出来的宽度，而框是按量出来的宽度定的——
/// 于是字溢出框、并且在框里偏向一边。哪一段字用哪个家族由 <see cref="FontFallback"/> 定，
/// 与三个绘制方同一份规则。
/// </para>
/// </remarks>
public sealed class SkiaTextMeasurer : ITextMeasurer, IDisposable
{
    private readonly Dictionary<TypefaceKey, SKTypeface> _typefaces = new();
    private readonly Dictionary<FontKey, SKFont> _fonts = [];
    private readonly Dictionary<GlyphKey, string> _fallbacks = [];
    private bool _disposed;

    /// <summary>实际用上的字体家族名。请求的字体不存在时它会是回退的那个。</summary>
    public string ResolveFamily(string fontFamily)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Typeface(fontFamily, FontWeight.Normal, italic: false).FamilyName;
    }

    /// <inheritdoc/>
    public Size Measure(string text, string fontFamily, double fontSize, FontWeight weight, bool italic = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(text))
        {
            return new Size(0, 0);
        }

        var family = string.IsNullOrWhiteSpace(fontFamily) ? FontFallback.DefaultFamily : fontFamily;
        var primary = Font(family, fontSize, weight, italic);
        var width = 0.0;

        foreach (var run in Runs(primary, family, text))
        {
            width += Font(run.Family, fontSize, weight, italic).MeasureText(run.Text);
        }

        var metrics = primary.Metrics;

        return new Size(width, metrics.Descent - metrics.Ascent + metrics.Leading);
    }

    /// <summary>
    /// 把一行按"用哪个家族画"切成连续的几段。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 切段而不是整行换一个字体：一段文字里拉丁与中文混排时，两边的字形宽度差一倍，
    /// 整行按其中一边量出来的宽度对另一边就不对。相邻同家族的字合成一段，
    /// 是为了让后续的度量少几次字体查找——同一段里每个字的家族是一样的。
    /// </para>
    /// <para>
    /// **查过一次的字记住。** 问字体管理器要一次答案要遍历本机装的字体，
    /// 而一份上千节点的图里同一个汉字会出现在几百个标签里，每次重问一遍会让
    /// 度量这一步比布局还慢。缓存按家族与码位分，与字号无关——挑字体只看这两样。
    /// </para>
    /// </remarks>
    private IEnumerable<(string Family, string Text)> Runs(SKFont primary, string family, string text)
    {
        var start = 0;
        var index = 0;
        var current = string.Empty;

        while (index < text.Length)
        {
            var codePoint = char.ConvertToUtf32(text, index);
            var key = new GlyphKey(family, codePoint);

            if (!_fallbacks.TryGetValue(key, out var resolved))
            {
                resolved = FontFallback.FamilyFor(primary, family, codePoint);
                _fallbacks[key] = resolved;
            }

            if (current.Length == 0)
            {
                current = resolved;
            }
            else if (!string.Equals(current, resolved, StringComparison.Ordinal))
            {
                yield return (current, text[start..index]);
                start = index;
                current = resolved;
            }

            index += char.IsSurrogatePair(text, index) ? 2 : 1;
        }

        if (current.Length > 0)
        {
            yield return (current, text[start..]);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var font in _fonts.Values)
        {
            font.Dispose();
        }

        foreach (var typeface in _typefaces.Values)
        {
            typeface.Dispose();
        }

        _fonts.Clear();
        _typefaces.Clear();
        _fallbacks.Clear();
        _disposed = true;
    }

    private SKFont Font(string fontFamily, double fontSize, FontWeight weight, bool italic)
    {
        var key = new FontKey(fontFamily, fontSize, weight, italic);

        if (_fonts.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var font = new SKFont(Typeface(fontFamily, weight, italic), (float)fontSize)
        {
            // 提示关闭：开与不开量出来的宽度差一点点，而这一点点会让同一份文档
            // 在不同后端上得到不同的节点尺寸。测量要的是一致，不是像素级贴合。
            Hinting = SKFontHinting.None,
        };

        _fonts[key] = font;

        return font;
    }

    private SKTypeface Typeface(string fontFamily, FontWeight weight, bool italic)
    {
        var name = string.IsNullOrWhiteSpace(fontFamily) ? FontFallback.DefaultFamily : fontFamily;
        var key = new TypefaceKey(name, weight, italic);

        if (_typefaces.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // 字重与倾斜要一起交给字体匹配，而不是拿到常规体再让绘制方自己变：
        // 量出来的宽度必须与画出来的那一个字面一致，否则加粗的那一段会溢出。
        var typeface = FontFallback.FromFamily(name, weight, italic);

        _typefaces[key] = typeface;

        return typeface;
    }

    private readonly record struct FontKey(string Family, double Size, FontWeight Weight, bool Italic);

    private readonly record struct TypefaceKey(string Family, FontWeight Weight, bool Italic);

    private readonly record struct GlyphKey(string Family, int CodePoint);
}
