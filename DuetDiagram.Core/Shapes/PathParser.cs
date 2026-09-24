using System.Globalization;
using System.Text;

namespace DuetDiagram.Core.Shapes;

/// <summary>
/// 路径数据的语法错误。
/// </summary>
/// <remarks>
/// <para>
/// 带上出错那一处的**行号**与**那一行的原文**。只给行号的话，看的人要回去数行；
/// 只给原文的话，路径长起来之后同样找不到是哪一行——两者缺一不可。
/// </para>
/// <para>
/// 做成异常而不是返回值，是因为它跨了好几层调用：字段写入、整体校验都要把同一份
/// 出错信息原样往上报。异常本身带着行号与原文，中间那几层不必再拆包重装。
/// </para>
/// </remarks>
public sealed class PathSyntaxException : Exception
{
    /// <summary>建一条路径语法错误。</summary>
    /// <param name="line">出错的行号，从 1 开始。</param>
    /// <param name="source">那一行的原文。</param>
    /// <param name="detail">错在哪。</param>
    public PathSyntaxException(int line, string source, string detail)
        : base($"第 {line} 行「{source}」：{detail}")
    {
        Line = line;
        LineText = source;
        Detail = detail;
    }

    /// <summary>出错的行号，从 1 开始。</summary>
    public int Line { get; }

    /// <summary>出错那一行的原文。</summary>
    public string LineText { get; }

    /// <summary>错在哪。</summary>
    public string Detail { get; }
}

/// <summary>
/// 把一段路径文本解析成单位框里的轮廓。
/// </summary>
/// <remarks>
/// <para>
/// **只认单位框坐标（0 到 1）**，不认绝对像素。像素是相对某个节点尺寸的，
/// 同一个形状画在另一个尺寸的节点上就会走形，而那种走形看起来像"形状没做对"，
/// 排查时不会想到是坐标系的问题。
/// </para>
/// <para>
/// **认不出的指令一律拒绝，不跳过。** 跳过的话画出来的形状缺一块，
/// 而用户以为自己写错了——所以错误必须说清是第几行、哪一段原文。
/// </para>
/// <para>
/// 认得的语法是 SVG 路径的一个子集，够画出内置的那八个形状：
/// </para>
/// <list type="bullet">
/// <item><c>M x y</c>：起点。必须是第一条指令，且只有一条。</item>
/// <item><c>L x y</c>：直线走到这一点。</item>
/// <item><c>A rx ry rot laf sweep x y</c>：椭圆弧走到这一点。<c>rot</c> 与 <c>laf</c>
/// 只认 0——几何模型里没有这两样，收下它们只能假装没看见。</item>
/// <item><c>Z</c>：闭合。可省，省了也按闭合画；给了就必须在最后。</item>
/// </list>
/// <para>
/// 指令之间用空白或逗号分隔，两种都认——手写的人两种都会用。指令可以挤在一行里，
/// 也可以一行一条；出错信息给的是**出问题那一处的行号与原文**，所以两种写法都能定位。
/// </para>
/// <para>
/// 闭合的 <c>z</c> 与 <c>Z</c> 是同一个意思。相对坐标的小写（<c>m</c> / <c>l</c> / <c>a</c>）
/// 一律拒绝：本仓的路径只有单位框一套坐标，"相对谁"没有答案。
/// </para>
/// </remarks>
public static class PathParser
{
    /// <summary>
    /// 解析一段路径文本。认不出时抛出带行号与原文的异常。
    /// </summary>
    /// <param name="pathData">路径文本。</param>
    /// <exception cref="PathSyntaxException">语法不合法。</exception>
    public static PathOutline Parse(string pathData)
    {
        ArgumentNullException.ThrowIfNull(pathData);

        if (!TryParse(pathData, out var outline, out var error))
        {
            throw error!;
        }

        return outline!;
    }

    /// <summary>
    /// 解析一段路径文本，认不出时给出结构化错误而不是抛。
    /// </summary>
    /// <remarks>
    /// 整体校验器用它：校验只报告不抛——一份带坏路径的文档要能加载进来被人看到问题，
    /// 而不是加载到一半炸掉。
    /// </remarks>
    /// <param name="pathData">路径文本。</param>
    /// <param name="outline">解析出来的轮廓。失败时为空。</param>
    /// <param name="error">出错信息。成功时为空。</param>
    public static bool TryParse(string? pathData, out PathOutline? outline, out PathSyntaxException? error)
    {
        outline = null;
        error = null;

        if (string.IsNullOrWhiteSpace(pathData))
        {
            error = new PathSyntaxException(1, string.Empty, "路径是空的。至少要有一条 M 指令。");
            return false;
        }

        var tokens = Tokenize(pathData);

        if (tokens.Count == 0)
        {
            error = new PathSyntaxException(1, string.Empty, "路径是空的。至少要有一条 M 指令。");
            return false;
        }

        ShapePoint? start = null;
        var segments = new List<PathSegment>();
        var closed = false;
        var index = 0;

        while (index < tokens.Count)
        {
            var command = tokens[index];

            if (!command.IsLetter)
            {
                error = new PathSyntaxException(
                    command.Line,
                    command.LineText,
                    $"这里要一条指令（M / L / A / Z），收到的是「{command.Text}」。");

                return false;
            }

            index++;

            if (closed)
            {
                error = new PathSyntaxException(command.Line, command.LineText, "Z 之后不能再有指令。");
                return false;
            }

            if (!Step(command, tokens, ref index, ref start, segments, out error))
            {
                return false;
            }

            closed = command.Text is "Z" or "z";
        }

        if (start is null)
        {
            var first = tokens[0];

            error = new PathSyntaxException(first.Line, first.LineText, "路径要以 M 开头，先给一个起点。");
            return false;
        }

        if (segments.Count == 0)
        {
            var first = tokens[0];

            error = new PathSyntaxException(first.Line, first.LineText, "路径至少要有一段（L 或 A），只给一个起点画不出东西。");
            return false;
        }

        outline = new PathOutline(start.Value, segments);
        return true;
    }

    /// <summary>解析一条指令与它的参数。</summary>
    private static bool Step(
        PathToken command,
        IReadOnlyList<PathToken> tokens,
        ref int index,
        ref ShapePoint? start,
        List<PathSegment> segments,
        out PathSyntaxException? error)
    {
        error = null;
        var name = command.Text;

        // 闭合的 z 与 Z 是同一个意思，SVG 里两种都有人写；相对坐标的小写（m / l / a）
        // 则一律拒绝——本仓的路径只有单位框一套坐标，"相对谁"没有答案。
        if (name is "z")
        {
            name = "Z";
        }

        if (char.IsAsciiLetterLower(name[0]))
        {
            error = new PathSyntaxException(
                command.Line,
                command.LineText,
                $"只支持大写指令（绝对坐标），收到的是「{command.Text}」。");

            return false;
        }

        switch (name)
        {
            case "M":
                if (start is not null)
                {
                    error = new PathSyntaxException(command.Line, command.LineText, "路径只能有一个 M，收到第二个。");
                    return false;
                }

                if (!Coordinates(command, tokens, ref index, 2, out var origin, out error))
                {
                    return false;
                }

                start = new ShapePoint(origin[0], origin[1]);
                return true;

            case "L":
                if (!RequireStart(command, start, out error)
                    || !Coordinates(command, tokens, ref index, 2, out var lineTo, out error))
                {
                    return false;
                }

                segments.Add(new PathLine(new ShapePoint(lineTo[0], lineTo[1])));
                return true;

            case "A":
                return Arc(command, tokens, ref index, start, segments, out error);

            case "Z":
                if (!RequireStart(command, start, out error))
                {
                    return false;
                }

                if (index < tokens.Count && !tokens[index].IsLetter)
                {
                    var extra = tokens[index];

                    error = new PathSyntaxException(extra.Line, extra.LineText, $"Z 不带参数，后面还有「{extra.Text}」。");
                    return false;
                }

                return true;

            default:
                error = new PathSyntaxException(
                    command.Line,
                    command.LineText,
                    $"认不出的路径指令「{command.Text}」。认得的只有 M / L / A / Z。");

                return false;
        }
    }

    /// <summary>解析一条 A 指令。</summary>
    private static bool Arc(
        PathToken command,
        IReadOnlyList<PathToken> tokens,
        ref int index,
        ShapePoint? start,
        List<PathSegment> segments,
        out PathSyntaxException? error)
    {
        if (!RequireStart(command, start, out error)
            || !Coordinates(command, tokens, ref index, 7, out var values, out error))
        {
            return false;
        }

        if (values[2] != 0)
        {
            error = new PathSyntaxException(command.Line, command.LineText, "弧的旋转（第三个参数）只支持 0，本仓的几何里没有这一项。");
            return false;
        }

        if (values[3] != 0)
        {
            error = new PathSyntaxException(command.Line, command.LineText, "弧只支持小弧（第四个参数为 0），大弧还没有对应的几何。");
            return false;
        }

        if (values[4] is not (0 or 1))
        {
            error = new PathSyntaxException(command.Line, command.LineText, "弧的方向（第五个参数）只能是 0 或 1。");
            return false;
        }

        segments.Add(new PathArc(
            new ShapePoint(values[5], values[6]),
            values[0],
            values[1],
            Clockwise: values[4] == 1));

        return true;
    }

    /// <summary>起点还没给就开始画线，是这一条指令的错。</summary>
    private static bool RequireStart(PathToken command, ShapePoint? start, out PathSyntaxException? error)
    {
        if (start is not null)
        {
            error = null;
            return true;
        }

        error = new PathSyntaxException(command.Line, command.LineText, "还没有起点。第一条指令要是 M。");
        return false;
    }

    /// <summary>
    /// 这个记号是不是一条认得的指令。
    /// </summary>
    /// <remarks>
    /// 读参数时用它而不是"是不是单个字母"来判断指令到此为止。差别在
    /// <c>L x 0.5</c> 这种写法上：<c>x</c> 是个字母但不是指令，按字母截断会把错报成
    /// "L 少给了参数"，让人去看 L 而真正写错的是那个 x。
    /// </remarks>
    private static bool IsKnownCommand(PathToken token) =>
        token.IsLetter && token.Text[0] is 'M' or 'L' or 'A' or 'Z' or 'z';

    /// <summary>
    /// 读若干个坐标。
    /// </summary>
    /// <remarks>
    /// 参数不够时把错报在**指令那一行**：出问题的是这条指令少写了参数，
    /// 而不是它后面那个东西。报在别处会让人去改一个没写错的地方。
    /// 数值本身解析不出、或者越出单位框，则报在**那个数值所在的行**。
    /// <para>
    /// 只有认得的指令才算"这一条读完了"。一个不认得的字母落在参数位置上，
    /// 它就是一个写错的数值，报"不是一个数"而不是报"参数不够"——后一种说法
    /// 会让人去看那条指令，而真正写错的是这个字母。
    /// </para>
    /// </remarks>
    private static bool Coordinates(
        PathToken command,
        IReadOnlyList<PathToken> tokens,
        ref int index,
        int count,
        out double[] values,
        out PathSyntaxException? error)
    {
        values = new double[count];
        error = null;
        var read = 0;

        while (read < count)
        {
            if (index >= tokens.Count || IsKnownCommand(tokens[index]))
            {
                error = new PathSyntaxException(
                    command.Line,
                    command.LineText,
                    $"{command.Text} 要 {count} 个参数，只读到 {read} 个。");

                return false;
            }

            var token = tokens[index];
            index++;

            if (!double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value))
            {
                error = new PathSyntaxException(token.Line, token.LineText, $"「{token.Text}」不是一个数。");
                return false;
            }

            if (value is < 0 or > 1)
            {
                error = new PathSyntaxException(
                    token.Line,
                    token.LineText,
                    $"数值要在 0 到 1 之间（单位框），收到的是 {ShapePoint.Format(value)}。");

                return false;
            }

            values[read] = value;
            read++;
        }

        return true;
    }

    /// <summary>把整段文本切成指令与数值，每段都带上它所在的行号与那一行的原文。</summary>
    private static List<PathToken> Tokenize(string pathData)
    {
        var lines = pathData
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var tokens = new List<PathToken>();
        var current = new StringBuilder();

        void Flush(int line, string text)
        {
            if (current.Length == 0)
            {
                return;
            }

            var piece = current.ToString();
            current.Clear();
            tokens.Add(new PathToken(piece, line, text));
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var text = lines[index].Trim();

            if (text.Length == 0)
            {
                continue;
            }

            foreach (var character in text)
            {
                if (character == ',' || char.IsWhiteSpace(character))
                {
                    Flush(index + 1, text);
                    continue;
                }

                current.Append(character);
            }

            Flush(index + 1, text);
        }

        return tokens;
    }

    /// <summary>一个记号：它的原文、在哪一行、那一行长什么样。</summary>
    /// <param name="Text">记号原文。</param>
    /// <param name="Line">行号，从 1 开始。</param>
    /// <param name="LineText">那一行去掉首尾空白之后的原文。</param>
    private readonly record struct PathToken(string Text, int Line, string LineText)
    {
        /// <summary>这个记号是不是一条指令（单个字母）。</summary>
        public bool IsLetter => Text.Length == 1 && char.IsAsciiLetter(Text[0]);
    }
}
