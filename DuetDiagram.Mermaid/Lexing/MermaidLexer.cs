namespace DuetDiagram.Mermaid.Lexing;

/// <summary>
/// Mermaid 的词法分析。
/// </summary>
/// <remarks>
/// <para>
/// **行是有意义的。** Mermaid 的语句以行为单位，因此换行输出成记号而不是当作空白吞掉。
/// 吞掉之后语句边界只能靠猜，而 <c>a --&gt; b c --&gt; d</c> 这类合法的紧凑写法
/// 与真正的错误写法将无法区分。
/// </para>
/// <para>
/// **定界符之内是自由文本。** 这是本实现与"把每个词都按标识切分"最大的不同。
/// 标签里可以出现几乎任何字符——<c>&lt;br/&gt;</c>、问号、逗号、十六进制颜色里的井号——
/// 而真实语料里七成以上的文件都含这类字符。按标识去切的话它们全会变成"不认识的东西"。
/// 所以形状定界符与竖线之间的一整段被当作一个记号收下，一个字都不切。
/// </para>
/// <para>
/// **不认识的字符输出成记号，不抛异常。** 宽松模式要能在有坏字符的输入上继续走完，
/// 把错误就地抛出去会让它连"哪里坏了"都报不出来。
/// </para>
/// <para>
/// 最麻烦的一处是连字符。它既是连线的一部分（<c>--&gt;</c>），
/// 又是节点标识的合法字符（<c>my-node-1</c>）。做法是在扫描标识的过程中
/// 每到一个位置先试着匹配连线，匹配上就切断——这样 <c>my-node--&gt;b</c>
/// 会切成标识 <c>my-node</c> 与连线，而 <c>a--&gt;b</c> 切成 <c>a</c> 与连线。
/// 按"遇到连字符就切"来做会把 <c>my-node</c> 切碎。
/// </para>
/// </remarks>
public static class MermaidLexer
{
    private static readonly (string Text, MermaidArrowKind Kind)[] Arrows =
    [
        ("~~~", MermaidArrowKind.Open),
        ("-.->", MermaidArrowKind.Dotted),
        ("-.-", MermaidArrowKind.DottedOpen),
        ("==>", MermaidArrowKind.Thick),
        ("===", MermaidArrowKind.ThickOpen),
        ("-->", MermaidArrowKind.Arrow),
        ("---", MermaidArrowKind.Open),
        ("->", MermaidArrowKind.Arrow),
        ("--", MermaidArrowKind.Open),
        ("==", MermaidArrowKind.ThickOpen),
    ];

    /// <summary>形状定界符：开符号到它对应的闭符号。</summary>
    /// <remarks>
    /// 长的排在前面，否则 <c>([</c> 会被当成 <c>(</c>，而闭符号就配不上了。
    /// </remarks>
    private static readonly (string Open, string Close)[] Shapes =
    [
        ("([", "])"),
        ("[[", "]]"),
        ("[(", ")]"),
        ("((", "))"),
        ("{{", "}}"),
        ("[", "]"),
        ("(", ")"),
        ("{", "}"),
    ];

    /// <summary>
    /// 尾部是自由文本的指令。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>style A fill:#f9f,stroke:#333</c> 里的井号、逗号与连字符都是普通字符，
    /// 整条尾巴是一段属性列表而不是若干标识。把它按标识切的话，
    /// 井号和逗号会变成"不认识的东西"，而真实输出里六成以上的文件都有 style 行。
    /// </para>
    /// <para>
    /// 这就是 Mermaid 语法的上下文相关之处：同一个字符在标签里是普通字符，
    /// 在标识位置是分隔符。词法层必须知道自己在哪一行的什么位置。
    /// </para>
    /// </remarks>
    private static readonly string[] FreeTailKeywords =
    [
        "style", "classDef", "class", "linkStyle", "click",
    ];

    /// <summary>把文本切成记号。</summary>
    public static IReadOnlyList<MermaidToken> Tokenize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tokens = new List<MermaidToken>();
        var line = 1;
        var column = 1;
        var index = 0;
        var atLineStart = true;

        while (index < source.Length)
        {
            var startLine = line;
            var startColumn = column;
            var c = source[index];

            // 换行：\r\n、\n、\r 都算一次。
            if (c is '\r' or '\n')
            {
                var width = c == '\r' && index + 1 < source.Length && source[index + 1] == '\n' ? 2 : 1;

                tokens.Add(new MermaidToken(MermaidTokenKind.NewLine, "\n", line, column));
                index += width;
                line++;
                column = 1;
                atLineStart = true;
                continue;
            }

            if (c is ' ' or '\t')
            {
                index++;
                column++;
                continue;
            }

            // 注释：%% 到行尾。保留成记号而不是丢掉，原文因此完整可还原。
            if (c == '%' && index + 1 < source.Length && source[index + 1] == '%')
            {
                var comment = ReadToLineEnd(source, index);

                tokens.Add(new MermaidToken(MermaidTokenKind.Comment, comment, startLine, startColumn));

                column += comment.Length;
                index += comment.Length;
                continue;
            }

            if (TryReadArrow(source, index) is { } arrow)
            {
                tokens.Add(new MermaidToken(MermaidTokenKind.Arrow, arrow.Text, startLine, startColumn, arrow.Kind));
                index += arrow.Text.Length;
                column += arrow.Text.Length;
                atLineStart = false;
                continue;
            }

            // 左向连线：`<-`、`<- .->`、`<==>` 这些只是在连线前面多一个尖括号。
            // 统一按前缀处理，比把每一种左向写法都列进连线表可靠——
            // 后者漏掉一个就变成"不认识的字符"。
            if (c == '<' && TryReadArrow(source, index + 1) is { } leftArrow)
            {
                var text = "<" + leftArrow.Text;

                tokens.Add(new MermaidToken(MermaidTokenKind.Arrow, text, startLine, startColumn, leftArrow.Kind));
                index += text.Length;
                column += text.Length;
                atLineStart = false;
                continue;
            }

            if (TryReadShape(source, index) is { } shape)
            {
                EmitShape(source, tokens, shape, ref index, ref column, startLine, startColumn);
                atLineStart = false;
                continue;
            }

            // 竖线定界：边标签。与形状同理，之内是自由文本。
            if (c == '|')
            {
                EmitPipeLabel(source, tokens, ref index, ref column, line, startColumn);
                atLineStart = false;
                continue;
            }

            if (c == '"')
            {
                var end = source.IndexOf('"', index + 1);
                var text = end < 0 ? source[(index + 1)..] : source[(index + 1)..end];
                var width = end < 0 ? source.Length - index : end + 1 - index;

                tokens.Add(new MermaidToken(MermaidTokenKind.QuotedText, text, startLine, startColumn));
                index += width;
                column += width;
                atLineStart = false;
                continue;
            }

            var single = c switch
            {
                ':' => MermaidTokenKind.Colon,
                '&' => MermaidTokenKind.Ampersand,
                ';' => MermaidTokenKind.Semicolon,
                _ => (MermaidTokenKind?)null,
            };

            if (single is not null)
            {
                tokens.Add(new MermaidToken(single.Value, c.ToString(), startLine, startColumn));
                index++;
                column++;
                atLineStart = false;
                continue;
            }

            if (!IsWordChar(c))
            {
                tokens.Add(new MermaidToken(MermaidTokenKind.Unknown, c.ToString(), startLine, startColumn));
                index++;
                column++;
                atLineStart = false;
                continue;
            }

            var word = ReadWord(source, ref index, ref column);

            tokens.Add(new MermaidToken(MermaidTokenKind.Word, word, startLine, startColumn));

            // 尾部自由文本只在这类关键字**真的是指令**时收。
            // 同一个词也可能是节点名——真实语料里有 `click --> setpwd[设置新密码]`，
            // 那里的 click 是个节点。判据是紧跟其后的字符：连线与形状定界符只可能
            // 出现在节点或连线里，指令后面接的是普通标识或属性。
            // 不这样判的话整行会被收成一个文本记号，节点与它引出的连线一起消失。
            if (atLineStart
                && FreeTailKeywords.Contains(word, StringComparer.Ordinal)
                && !StartsNodeOrLink(source, index))
            {
                EmitFreeTail(source, tokens, ref index, ref column, line);
            }

            atLineStart = false;
        }

        tokens.Add(new MermaidToken(MermaidTokenKind.End, string.Empty, line, column));

        return tokens;
    }

    /// <summary>
    /// 把指令行剩下的部分整段收成一个文本记号。
    /// </summary>
    /// <remarks>
    /// 末尾的注释仍然单独成记号，否则注释会被并进属性列表里，
    /// 而宽松模式要靠注释判断哪些内容是模型自己标注的。
    /// </remarks>
    private static void EmitFreeTail(
        string source,
        List<MermaidToken> tokens,
        ref int index,
        ref int column,
        int line)
    {
        var end = index;

        while (end < source.Length && source[end] is not ('\r' or '\n'))
        {
            if (source[end] == '%' && end + 1 < source.Length && source[end + 1] == '%')
            {
                break;
            }

            end++;
        }

        var tail = source[index..end].Trim();

        if (tail.Length > 0)
        {
            // 行内的前导空白已经跳过，这里的位置按实际起点算。
            tokens.Add(new MermaidToken(MermaidTokenKind.Text, tail, line, column + (end - index - tail.Length)));
        }

        column += end - index;
        index = end;
    }

    /// <summary>
    /// 产出竖线定界的边标签。
    /// </summary>
    /// <remarks>
    /// 找不到配对的竖线时把本行剩下的收成标签。少一个竖线是模型输出里的常见错误，
    /// 就地放弃会让整行白解析。
    /// </remarks>
    private static void EmitPipeLabel(
        string source,
        List<MermaidToken> tokens,
        ref int index,
        ref int column,
        int line,
        int column0)
    {
        tokens.Add(new MermaidToken(MermaidTokenKind.Pipe, "|", line, column0));
        index++;
        column++;

        var end = source.IndexOf('|', index);
        var crossesLine = end >= 0 && source.AsSpan(index, end - index).ContainsAny('\n', '\r');
        var hasClose = end >= 0 && !crossesLine;

        var label = hasClose ? source[index..end] : ReadToLineEnd(source, index);

        if (label.Length > 0)
        {
            tokens.Add(new MermaidToken(MermaidTokenKind.Text, label, line, column));
        }

        index = hasClose ? end + 1 : index + label.Length;
        column += hasClose ? end - column + column : label.Length;

        if (hasClose)
        {
            tokens.Add(new MermaidToken(MermaidTokenKind.Pipe, "|", line, column - 1));
        }
    }

    /// <summary>
    /// 产出形状记号：开符号、标签文本、闭符号。
    /// </summary>
    /// <remarks>
    /// 标签整体收成一个记号，中间一个字都不切。找不到闭符号时收整行——
    /// 少一个括号是常见错误，就地放弃会让整行白解析，而它其余部分往往是好的。
    /// </remarks>
    private static void EmitShape(
        string source,
        List<MermaidToken> tokens,
        (string Open, string Close) shape,
        ref int index,
        ref int column,
        int line,
        int column0)
    {
        tokens.Add(new MermaidToken(MermaidTokenKind.ShapeOpen, shape.Open, line, column0));
        index += shape.Open.Length;
        column += shape.Open.Length;

        var labelStart = index;
        var end = source.IndexOf(shape.Close, index, StringComparison.Ordinal);

        // 闭符号必须在同一行上。跨行去找会把下一条语句的开头当成这条的结尾。
        var crossesLine = end >= 0 && source.AsSpan(index, end - index).ContainsAny('\n', '\r');
        var hasClose = end >= 0 && !crossesLine;

        var label = hasClose
            ? source[labelStart..end]
            : ReadToLineEnd(source, labelStart);

        if (label.Length > 0)
        {
            tokens.Add(new MermaidToken(MermaidTokenKind.Text, label, line, column));
        }

        index = hasClose ? end + shape.Close.Length : labelStart + label.Length;
        column += (hasClose ? end - labelStart + shape.Close.Length : label.Length);

        if (hasClose)
        {
            tokens.Add(new MermaidToken(MermaidTokenKind.ShapeClose, shape.Close, line, column));
        }
    }

    /// <summary>
    /// 读一个标识。
    /// </summary>
    /// <remarks>
    /// 每到一个位置先试连线：这样 <c>my-node--&gt;b</c> 会正确地切成
    /// 标识 <c>my-node</c> 与连线，而不是在第一个连字符处断开。
    /// </remarks>
    private static string ReadWord(string source, ref int index, ref int column)
    {
        var start = index;

        while (index < source.Length)
        {
            if (!IsWordChar(source[index]) || TryReadArrow(source, index) is not null)
            {
                break;
            }

            index++;
            column++;
        }

        return source[start..index];
    }

    /// <summary>
    /// 这个字符能不能出现在标识里。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **按"允许什么"来定，而不是按"不允许什么"。** 反过来定的话，
    /// 任何没列进终止符集的字符都会悄悄变成合法的标识字符，
    /// 于是一个乱入的 <c>@</c> 会变成一个节点名，而调用方看到的症状是
    /// "图里多了一个莫名其妙的节点"——离真正的原因很远。
    /// </para>
    /// <para>
    /// 连字符、点号与等号在这里算合法：它们是标识的常见字符，
    /// 什么时候该断开由"后面是不是一条连线"决定，见 <see cref="ReadWord"/>。
    /// </para>
    /// <para>
    /// 非 ASCII 一律放行。中文可以直接当节点标识用，而模型的输出里这种写法很常见。
    /// </para>
    /// </remarks>
    public static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '-' or '.' || c >= '\u0080';

    /// <summary>
    /// 这段文本能不能原样当作标识写出来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判据有两半，缺一不可：每个字符都得是标识字符，而且里面不能藏着连线记号。
    /// 后半条容易漏——<c>a--b</c> 每一个字符都是标识字符，可它读出来是
    /// <c>a</c>、连线、<c>b</c> 三段，而不是一个叫 <c>a--b</c> 的节点。
    /// </para>
    /// <para>
    /// 写在词法层是因为**这里才知道什么算标识**。导出那一侧要判"这个标识写得出来吗"，
    /// 自己抄一份判据必然与这里分叉，而分叉的表现是导出结果再导入时被切成两段。
    /// </para>
    /// </remarks>
    public static bool IsPlainIdentifier(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!IsWordChar(c))
            {
                return false;
            }
        }

        for (var index = 0; index < text.Length; index++)
        {
            if (TryReadArrow(text, index) is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static string ReadToLineEnd(string source, int index)
    {
        var end = index;

        while (end < source.Length && source[end] is not ('\r' or '\n'))
        {
            end++;
        }

        return source[index..end];
    }

    /// <summary>
    /// 从这个位置往后看，像不像一条节点或连线语句的开头。
    /// </summary>
    /// <remarks>
    /// 只看紧接着的那个非空白字符：形状定界符、连线记号、并列符号三者只可能出现在
    /// 节点或连线里。空白要先跳过，因为 <c>click --&gt; x</c> 与 <c>click--&gt;x</c> 都得认。
    /// </remarks>
    private static bool StartsNodeOrLink(string source, int index)
    {
        var probe = index;

        while (probe < source.Length && source[probe] is ' ' or '\t')
        {
            probe++;
        }

        return TryReadShape(source, probe) is not null
            || TryReadArrow(source, probe) is not null
            || (probe < source.Length && source[probe] == '&');
    }

    private static (string Open, string Close)? TryReadShape(string source, int index)
    {
        foreach (var shape in Shapes)
        {
            if (Matches(source, index, shape.Open))
            {
                return shape;
            }
        }

        return null;
    }

    private static (string Text, MermaidArrowKind Kind)? TryReadArrow(string source, int index)
    {
        foreach (var (text, kind) in Arrows)
        {
            if (Matches(source, index, text))
            {
                return (text, kind);
            }
        }

        return null;
    }

    private static bool Matches(string source, int index, string text) =>
        index + text.Length <= source.Length
        && string.CompareOrdinal(source, index, text, 0, text.Length) == 0;
}
