namespace DuetDiagram.Dsl.Lexing;

/// <summary>
/// DSL 的词法分析。
/// </summary>
/// <remarks>
/// <para>
/// 行式语法：换行是有意义的记号。缩进只为了可读，不参与判断。
/// </para>
/// <para>
/// 与 Mermaid 侧的口径一致：不认识的字符产出记号而不抛异常，
/// 由语法层决定是跳过还是拒绝。两份解析器对坏输入的处理必须一致，
/// 否则对比测试量到的是口径差异而不是格式差异。
/// </para>
/// </remarks>
public static class DslLexer
{
    /// <summary>把源码切成记号。</summary>
    public static IReadOnlyList<DslToken> Tokenize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tokens = new List<DslToken>();
        var index = 0;
        var line = 1;
        var column = 1;

        while (index < source.Length)
        {
            var current = source[index];

            switch (current)
            {
                case '\r':
                    // \r\n 与孤立的 \r 都当一次换行，不产出两个。
                    index++;
                    if (index < source.Length && source[index] == '\n')
                    {
                        index++;
                    }

                    tokens.Add(new DslToken(DslTokenKind.NewLine, "\n", line, column));
                    line++;
                    column = 1;
                    continue;

                case '\n':
                    index++;
                    tokens.Add(new DslToken(DslTokenKind.NewLine, "\n", line, column));
                    line++;
                    column = 1;
                    continue;

                case ' ' or '\t':
                    index++;
                    column++;
                    continue;

                case '#':
                    // 注释吃掉整行剩下的部分，含引号——注释里不该再认字符串。
                    var commentStart = index;
                    var commentColumn = column;

                    while (index < source.Length && source[index] is not ('\n' or '\r'))
                    {
                        index++;
                        column++;
                    }

                    tokens.Add(new DslToken(
                        DslTokenKind.Comment,
                        source[commentStart..index],
                        line,
                        commentColumn));
                    continue;

                case '"':
                    // 起始列在扫描前取。扫描会推进 column，事后用长度倒推既难读也容易错。
                    var quoteColumn = column;
                    var (text, terminated) = ReadQuoted(source, ref index, ref column);

                    // 没有收尾引号时产出 Unknown 而不是半个字符串：
                    // 静默接受会让一行坏掉的声明看起来完全正常。
                    // 原文补回开引号，这样语法层能认出这是"字符串没收尾"
                    // 而不是"有个不认识的字符"——两者给用户的提示完全不同。
                    tokens.Add(terminated
                        ? new DslToken(DslTokenKind.QuotedText, text, line, quoteColumn)
                        : new DslToken(DslTokenKind.Unknown, "\"" + text, line, quoteColumn));
                    continue;

                case ':':
                    tokens.Add(new DslToken(DslTokenKind.Colon, ":", line, column));
                    index++;
                    column++;
                    continue;

                case ',':
                    tokens.Add(new DslToken(DslTokenKind.Comma, ",", line, column));
                    index++;
                    column++;
                    continue;

                case '=':
                    tokens.Add(new DslToken(DslTokenKind.Equals, "=", line, column));
                    index++;
                    column++;
                    continue;

                case '.':
                    tokens.Add(new DslToken(DslTokenKind.Dot, ".", line, column));
                    index++;
                    column++;
                    continue;

                case '-':
                    // 连字符有四种可能，必须先看后一个字符。
                    // 这与 Mermaid 侧「连字符既是连线的一部分又是标识的合法字符」
                    // 是同一类问题，只是这里能在词法层一次判清。
                    tokens.Add(ReadDash(source, ref index, ref column, line));
                    continue;
            }

            if (char.IsAsciiLetter(current) || current == '_')
            {
                tokens.Add(ReadWord(source, ref index, ref column, line));
                continue;
            }

            if (char.IsAsciiDigit(current))
            {
                var start = index;
                var startColumn = column;

                tokens.Add(ReadNumber(source, ref index, ref column, line, start, startColumn));
                continue;
            }

            tokens.Add(new DslToken(DslTokenKind.Unknown, current.ToString(), line, column));
            index++;
            column++;
        }

        tokens.Add(new DslToken(DslTokenKind.End, string.Empty, line, column));
        return tokens;
    }

    /// <summary>读一个连字符开头的记号。</summary>
    /// <remarks>
    /// 四种情形：<c>-&gt;</c> 是箭头，<c>--</c> 是无箭头连线，
    /// <c>-数字</c> 是负坐标，其余是孤立字符。
    /// 判定顺序不能换——先认连线再认负号，否则 <c>-&gt;</c> 会被读成负号加别的。
    /// </remarks>
    private static DslToken ReadDash(string source, ref int index, ref int column, int line)
    {
        var startColumn = column;

        if (index + 1 < source.Length && source[index + 1] == '>')
        {
            index += 2;
            column += 2;
            return new DslToken(DslTokenKind.Arrow, "->", line, startColumn, HasArrow: true);
        }

        if (index + 1 < source.Length && source[index + 1] == '-')
        {
            index += 2;
            column += 2;
            return new DslToken(DslTokenKind.Arrow, "--", line, startColumn, HasArrow: false);
        }

        if (index + 1 < source.Length && char.IsAsciiDigit(source[index + 1]))
        {
            var start = index;
            return ReadNumber(source, ref index, ref column, line, start, startColumn);
        }

        index++;
        column++;
        return new DslToken(DslTokenKind.Unknown, "-", line, startColumn);
    }

    /// <summary>读一个数字，含可选的负号与小数部分。</summary>
    /// <remarks>
    /// 小数点只在后面真的跟着数字时才吃掉。无条件吃点的话，
    /// <c>1.</c> 或 <c>1.a</c> 里的点会被吞进去，而那个点本该是别的记号——
    /// 端口引用就写作 <c>节点.端口</c>，吞掉点会让 <c>a.5</c> 这种输入
    /// 从"节点 a 的端口 5"变成"节点 a5"。
    /// </remarks>
    private static DslToken ReadNumber(
        string source,
        ref int index,
        ref int column,
        int line,
        int startIndex,
        int startColumn)
    {
        // 负号。放在这里而不是调用点，是因为两处调用（普通数字与连字符开头）
        // 都要支持它，写两遍迟早有一遍漏掉。
        if (source[index] == '-')
        {
            index++;
            column++;
        }

        while (index < source.Length && char.IsAsciiDigit(source[index]))
        {
            index++;
            column++;
        }

        if (index + 1 < source.Length && source[index] == '.' && char.IsAsciiDigit(source[index + 1]))
        {
            index++;
            column++;

            while (index < source.Length && char.IsAsciiDigit(source[index]))
            {
                index++;
                column++;
            }
        }

        return new DslToken(DslTokenKind.Word, source[startIndex..index], line, startColumn);
    }

    /// <summary>读一个标识或数字。</summary>
    /// <remarks>
    /// 连字符是标识的合法字符（<c>same-rank</c>、<c>node-spacing</c>），
    /// 但它也是连线的一部分。规则是：后面跟着 <c>&gt;</c> 或 <c>-</c> 就停下来，
    /// 否则继续。这样 <c>same-rank</c> 是一个标识，而 <c>a-&gt;b</c> 是三个记号。
    /// </remarks>
    private static DslToken ReadWord(string source, ref int index, ref int column, int line)
    {
        var start = index;
        var startColumn = column;

        while (index < source.Length)
        {
            var current = source[index];

            if (char.IsAsciiLetterOrDigit(current) || current == '_')
            {
                index++;
                column++;
                continue;
            }

            if (current == '-')
            {
                var next = index + 1 < source.Length ? source[index + 1] : '\0';

                if (next is '>' or '-')
                {
                    break;
                }

                index++;
                column++;
                continue;
            }

            break;
        }

        return new DslToken(DslTokenKind.Word, source[start..index], line, startColumn);
    }

    /// <summary>
    /// 读一个引号字符串。
    /// </summary>
    /// <remarks>
    /// 只认三种转义：<c>\"</c>、<c>\\</c>、<c>\n</c>。
    /// 换行必须支持，因为标签里的换行是常见需求——Mermaid 语料里
    /// <c>&lt;br/&gt;</c> 是最常见的写法，说明模型确实需要多行标签。
    /// 其余 <c>\x</c> 原样保留反斜杠，不报错也不猜测。
    /// </remarks>
    private static (string Text, bool Terminated) ReadQuoted(string source, ref int index, ref int column)
    {
        index++;
        column++;

        var text = new System.Text.StringBuilder();

        while (index < source.Length)
        {
            var current = source[index];

            if (current == '"')
            {
                index++;
                column++;
                return (text.ToString(), true);
            }

            // 引号字符串不跨行。行式语法里跨行只会让「哪一行坏了」失去唯一答案。
            if (current is '\n' or '\r')
            {
                return (text.ToString(), false);
            }

            if (current == '\\' && index + 1 < source.Length)
            {
                var escaped = source[index + 1];

                switch (escaped)
                {
                    case '"':
                        text.Append('"');
                        index += 2;
                        column += 2;
                        continue;
                    case '\\':
                        text.Append('\\');
                        index += 2;
                        column += 2;
                        continue;
                    case 'n':
                        text.Append('\n');
                        index += 2;
                        column += 2;
                        continue;
                }
            }

            text.Append(current);
            index++;
            column++;
        }

        return (text.ToString(), false);
    }
}
