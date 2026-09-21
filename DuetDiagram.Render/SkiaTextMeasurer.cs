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
/// </remarks>
public sealed class SkiaTextMeasurer : ITextMeasurer, IDisposable
{
    private readonly Dictionary<string, SKTypeface> _typefaces = new(StringComparer.Ordinal);
    private readonly Dictionary<FontKey, SKFont> _fonts = [];
    private bool _disposed;

    /// <summary>实际用上的字体家族名。请求的字体不存在时它会是回退的那个。</summary>
    public string ResolveFamily(string fontFamily)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Typeface(fontFamily).FamilyName;
    }

    /// <inheritdoc/>
    public Size Measure(string text, string fontFamily, double fontSize, FontWeight weight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(text))
        {
            return new Size(0, 0);
        }

        var font = Font(fontFamily, fontSize, weight);
        var metrics = font.Metrics;

        return new Size(font.MeasureText(text), metrics.Descent - metrics.Ascent + metrics.Leading);
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
        _disposed = true;
    }

    private SKFont Font(string fontFamily, double fontSize, FontWeight weight)
    {
        var key = new FontKey(fontFamily, fontSize, weight);

        if (_fonts.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var font = new SKFont(Typeface(fontFamily), (float)fontSize)
        {
            // 提示关闭：开与不开量出来的宽度差一点点，而这一点点会让同一份文档
            // 在不同后端上得到不同的节点尺寸。测量要的是一致，不是像素级贴合。
            Hinting = SKFontHinting.None,
        };

        _fonts[key] = font;

        return font;
    }

    private SKTypeface Typeface(string fontFamily)
    {
        var name = string.IsNullOrWhiteSpace(fontFamily) ? DefaultFamily : fontFamily;

        if (_typefaces.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var typeface = SKTypeface.FromFamilyName(name) ?? SKTypeface.Default;
        _typefaces[name] = typeface;

        return typeface;
    }

    private const string DefaultFamily = "Segoe UI";

    private readonly record struct FontKey(string Family, double Size, FontWeight Weight);
}
