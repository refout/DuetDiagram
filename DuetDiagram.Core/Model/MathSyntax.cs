namespace DuetDiagram.Core.Model;

/// <summary>
/// 公式语法错误的码。
/// </summary>
/// <remarks>
/// 分开而不是共用一个"语法错误"：处置不同。不认识的命令要改名字，
/// 括号不配对要补括号，上下标重复要删一个——合成一个码之后，
/// 界面上只能给一句"语法错了"，而用户看不出该动哪里。
/// </remarks>
public static class MathErrorCodes
{
    /// <summary>反斜杠后面那个名字不在认得的表里。</summary>
    public const string UnknownCommand = "MATH_UNKNOWN_COMMAND";

    /// <summary>花括号不配对。</summary>
    public const string UnbalancedBrace = "MATH_UNBALANCED_BRACE";

    /// <summary>结构命令缺参数，例如 <c>\frac</c> 只跟了一个组。</summary>
    public const string MissingArgument = "MATH_MISSING_ARGUMENT";

    /// <summary>上下标前面没有底。</summary>
    public const string ScriptWithoutBase = "MATH_SCRIPT_WITHOUT_BASE";

    /// <summary>同一个底上写了两个上标（或两个下标）。</summary>
    public const string DoubleScript = "MATH_DOUBLE_SCRIPT";
}

/// <summary>
/// 认不出的公式：错在哪一条、错在第几个字符、具体是什么。
/// </summary>
/// <remarks>
/// <para>
/// **位置是给用户指路用的。** 只说"语法错了"的话，一段长公式里用户要自己找，
/// 而找不到就会把整段删掉重写——那正是"不如不报"。
/// </para>
/// <para>
/// 位置是从 0 数的下标，与字符串下标一致；给人看时加一。
/// </para>
/// </remarks>
/// <param name="Code">错误码，取值见 <see cref="MathErrorCodes"/>。</param>
/// <param name="Position">出错处在源串里的下标，从 0 数。</param>
/// <param name="Detail">一句话说清这一处是什么。</param>
public sealed record MathSyntaxError(string Code, int Position, string Detail);

/// <summary>
/// 公式语法树上的一个节点。
/// </summary>
/// <remarks>
/// <para>
/// **这些节点只活在一次排版里。** 它们不落盘、不进哈希、不进绘制列表——
/// 排版完就只剩位置与尺寸了。所以它们不做值相等：给带集合的节点补一套相等性
/// 只会让"两个语法树相等"这件事看起来有意义，而实际上没有一处需要它。
/// </para>
/// <para>
/// 抽象基类加派生密封类，理由与绘制指令一致：读取方要按类型分发。
/// </para>
/// </remarks>
public abstract class MathNode;

/// <summary>横向排开的一串。空的一串是合法的，表示"这里什么都没有"。</summary>
public sealed class MathRow(IReadOnlyList<MathNode> children) : MathNode
{
    /// <summary>从左到右的成员。</summary>
    public IReadOnlyList<MathNode> Children { get; } = children;
}

/// <summary>
/// 一段字。
/// </summary>
/// <param name="Text">写出来是什么。</param>
/// <param name="Italic">
/// 要不要斜体。变量斜、数字与符号正——与数学排版的老规矩一致，
/// 而"整段一个样式"会让 <c>sin</c> 看起来像一个变量名。
/// </param>
public sealed class MathAtom(string text, bool italic) : MathNode
{
    /// <summary>文本。</summary>
    public string Text { get; } = text;

    /// <summary>斜体。</summary>
    public bool Italic { get; } = italic;
}

/// <summary>一段空白。宽度按字号的比例给。</summary>
public sealed class MathSpace(double em) : MathNode
{
    /// <summary>宽度，单位是字号。负值是往回缩。</summary>
    public double Em { get; } = em;
}

/// <summary>上下摞起来的分数。</summary>
public sealed class MathFraction(MathNode numerator, MathNode denominator) : MathNode
{
    /// <summary>分子。</summary>
    public MathNode Numerator { get; } = numerator;

    /// <summary>分母。</summary>
    public MathNode Denominator { get; } = denominator;
}

/// <summary>根号。只做平方根，不做开 n 次方。</summary>
public sealed class MathRadical(MathNode radicand) : MathNode
{
    /// <summary>根号里那一段。</summary>
    public MathNode Radicand { get; } = radicand;
}

/// <summary>带上标与下标的底。两个都可以没有，但至少有一个。</summary>
public sealed class MathScript(MathNode @base, MathNode? superscript, MathNode? subscript) : MathNode
{
    /// <summary>底。</summary>
    public MathNode Base { get; } = @base;

    /// <summary>上标。没有时为空。</summary>
    public MathNode? Superscript { get; } = superscript;

    /// <summary>下标。没有时为空。</summary>
    public MathNode? Subscript { get; } = subscript;
}

/// <summary>
/// 认得的公式语法，以及那份清单之外怎么办。
/// </summary>
/// <remarks>
/// <para>
/// **这一层的产出是"边界"而不是"能力"。** LaTeX 的子集一层套一层，每一层都能说
/// "再支持一个记号就好"，所以没有终点。定下来的是一份**写死的清单**：
/// 清单里的一律排得出来，清单外的一律给结构化错误，绝不原样画出去。
/// 原样画的话，用户看到的是一串花括号，而他会以为是自己语法写错了。
/// </para>
/// <para>
/// **空白在公式里不占位置**，与 LaTeX 一致：要间距得写 <c>\,</c> 这一类的命令。
/// 不这么做的话，换行与缩进会悄悄改变公式的宽度，而那种差别没人能解释。
/// </para>
/// <para>
/// **解析不碰字体。** 它只认字符，尺寸由 <see cref="MathLayout"/> 去量。
/// 两件事混在一起的话，一个语法错误要等到量文本那一步才暴露，
/// 而那时已经画了一半。
/// </para>
/// </remarks>
public static class MathSyntax
{
    /// <summary>认得的符号名：写出来是什么。小写希腊字母之外的一律正体。</summary>
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.Ordinal)
    {
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
        ["epsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η", ["theta"] = "θ",
        ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ",
        ["nu"] = "ν", ["xi"] = "ξ", ["pi"] = "π", ["rho"] = "ρ",
        ["sigma"] = "σ", ["tau"] = "τ", ["upsilon"] = "υ", ["phi"] = "φ",
        ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ",
        ["Xi"] = "Ξ", ["Pi"] = "Π", ["Sigma"] = "Σ", ["Phi"] = "Φ",
        ["Psi"] = "Ψ", ["Omega"] = "Ω",
        ["cdot"] = "·", ["times"] = "×", ["div"] = "÷", ["pm"] = "±", ["mp"] = "∓",
        ["le"] = "≤", ["ge"] = "≥", ["ne"] = "≠", ["approx"] = "≈", ["equiv"] = "≡",
        ["to"] = "→", ["rightarrow"] = "→", ["leftarrow"] = "←", ["leftrightarrow"] = "↔",
        ["in"] = "∈", ["notin"] = "∉", ["subset"] = "⊂", ["subseteq"] = "⊆",
        ["cup"] = "∪", ["cap"] = "∩", ["forall"] = "∀", ["exists"] = "∃",
        ["partial"] = "∂", ["nabla"] = "∇", ["infty"] = "∞", ["angle"] = "∠",
        ["perp"] = "⊥", ["parallel"] = "∥", ["cdots"] = "⋯", ["ldots"] = "…",
        ["prime"] = "′", ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫",
    };

    /// <summary>认得的函数名：写出来是正体的名字，后面跟一点空隙。</summary>
    private static readonly HashSet<string> Functions = new(StringComparer.Ordinal)
    {
        "sin", "cos", "tan", "cot", "sec", "csc",
        "arcsin", "arccos", "arctan", "sinh", "cosh", "tanh",
        "log", "ln", "lg", "exp", "lim", "max", "min",
        "sup", "inf", "det", "dim", "gcd", "deg",
    };

    /// <summary>认得的间距命令：宽度按字号的比例。</summary>
    private static readonly Dictionary<string, double> Spaces = new(StringComparer.Ordinal)
    {
        [","] = 0.167,
        [";"] = 0.278,
        ["!"] = -0.167,
        [" "] = 0.333,
        ["quad"] = 1.0,
        ["qquad"] = 2.0,
    };

    /// <summary>
    /// 解析一段公式。
    /// </summary>
    /// <remarks>
    /// 空串是合法的：它排出来是空的，与空标签一样。把空串判成错误的话，
    /// 一个还没填内容的节点会一直报错，而那种错没有任何处置。
    /// </remarks>
    /// <param name="source">公式原文。</param>
    /// <param name="root">语法树。解析失败时是一棵空的树。</param>
    /// <param name="error">认不出的地方。成功时为空。</param>
    /// <returns>认不认得。</returns>
    public static bool TryParse(string? source, out MathNode root, out MathSyntaxError? error)
    {
        root = new MathRow([]);
        error = null;

        if (string.IsNullOrEmpty(source))
        {
            return true;
        }

        var parser = new Parser(source);

        if (!parser.TryRow(stopAtBrace: false, out var row))
        {
            error = parser.Error;
            return false;
        }

        root = row;
        return true;
    }

    /// <summary>认得的符号名。用例与文档对清单时读它。</summary>
    public static IReadOnlyCollection<string> KnownSymbols => Symbols.Keys;

    /// <summary>认得的函数名。</summary>
    public static IReadOnlyCollection<string> KnownFunctions => Functions;

    /// <summary>认得的间距命令。</summary>
    public static IReadOnlyCollection<string> KnownSpaces => Spaces.Keys;

    /// <summary>
    /// 递归下降的那一层。
    /// </summary>
    /// <remarks>
    /// 状态只有一个下标。做成有状态的小对象而不是一串传下标的静态方法，
    /// 是因为出错时要带回位置，而位置就是那个下标——每层各传一份的话，
    /// 报出来的位置会是各层自己算的，而它们迟早对不上。
    /// </remarks>
    private sealed class Parser(string source)
    {
        private int _index;

        public MathSyntaxError? Error { get; private set; }

        /// <summary>排一串。遇到收尾的花括号时停下，由调用方吃掉它。</summary>
        public bool TryRow(bool stopAtBrace, out MathNode node)
        {
            node = new MathRow([]);

            var children = new List<MathNode>();

            while (_index < source.Length)
            {
                var current = source[_index];

                if (current == '}')
                {
                    if (stopAtBrace)
                    {
                        break;
                    }

                    return Fail(MathErrorCodes.UnbalancedBrace, _index, "多了一个收尾的花括号");
                }

                if (current == '{')
                {
                    _index++;

                    if (!TryRow(stopAtBrace: true, out var inner) || !TryClose())
                    {
                        return false;
                    }

                    children.Add(inner);
                    continue;
                }

                if (char.IsWhiteSpace(current))
                {
                    _index++;
                    continue;
                }

                if (current is '^' or '_')
                {
                    return Fail(
                        MathErrorCodes.ScriptWithoutBase,
                        _index,
                        "上下标前面没有东西可以当底");
                }

                if (!TryElement(out var element))
                {
                    return false;
                }

                children.Add(element);
            }

            node = new MathRow(children);
            return true;
        }

        private bool TryClose()
        {
            if (_index < source.Length && source[_index] == '}')
            {
                _index++;
                return true;
            }

            return Fail(MathErrorCodes.UnbalancedBrace, _index, "少了一个收尾的花括号");
        }

        /// <summary>一个底，外加挂在它上面的上下标。</summary>
        private bool TryElement(out MathNode node)
        {
            if (!TryAtom(out var atom))
            {
                node = new MathRow([]);
                return false;
            }

            MathNode? superscript = null;
            MathNode? subscript = null;

            while (_index < source.Length && source[_index] is '^' or '_')
            {
                var isSuperscript = source[_index] == '^';
                var at = _index;
                _index++;

                if (!TryOperand(out var operand))
                {
                    node = new MathRow([]);
                    return false;
                }

                if (isSuperscript)
                {
                    if (superscript is not null)
                    {
                        node = new MathRow([]);
                        return Fail(MathErrorCodes.DoubleScript, at, "同一个底上写了两个上标");
                    }

                    superscript = operand;
                }
                else
                {
                    if (subscript is not null)
                    {
                        node = new MathRow([]);
                        return Fail(MathErrorCodes.DoubleScript, at, "同一个底上写了两个下标");
                    }

                    subscript = operand;
                }
            }

            node = superscript is null && subscript is null
                ? atom
                : new MathScript(atom, superscript, subscript);

            return true;
        }

        /// <summary>上下标吃的那一个东西：一组，或者一个原子，或者一条命令。</summary>
        private bool TryOperand(out MathNode node)
        {
            node = new MathRow([]);

            if (_index >= source.Length)
            {
                return Fail(MathErrorCodes.MissingArgument, _index, "上下标后面没有东西");
            }

            if (source[_index] == '{')
            {
                _index++;

                if (!TryRow(stopAtBrace: true, out var inner) || !TryClose())
                {
                    return false;
                }

                node = inner;
                return true;
            }

            return TryAtom(out node);
        }

        /// <summary>一个原子：一段字、一条命令。</summary>
        private bool TryAtom(out MathNode node)
        {
            node = new MathRow([]);

            if (_index >= source.Length)
            {
                return Fail(MathErrorCodes.MissingArgument, _index, "这里要有一个东西");
            }

            var current = source[_index];

            if (current == '\\')
            {
                return TryCommand(out node);
            }

            if (char.IsAsciiDigit(current))
            {
                var start = _index;

                while (_index < source.Length && char.IsAsciiDigit(source[_index]))
                {
                    _index++;
                }

                node = new MathAtom(source[start.._index], italic: false);
                return true;
            }

            _index++;

            // 字母当变量（斜体），其余（运算符、括号、逗号）当符号（正体）。
            node = new MathAtom(current.ToString(), italic: char.IsAsciiLetter(current));
            return true;
        }

        private bool TryCommand(out MathNode node)
        {
            node = new MathRow([]);

            var at = _index;
            _index++;

            if (_index >= source.Length)
            {
                return Fail(MathErrorCodes.UnknownCommand, at, "反斜杠后面没有命令名");
            }

            // 命令名要么是一串字母，要么是紧跟其后的一个非字母字符（例如 \, 与 \;）。
            if (!char.IsAsciiLetter(source[_index]))
            {
                var symbol = source[_index].ToString();
                _index++;

                if (Spaces.TryGetValue(symbol, out var width))
                {
                    node = new MathSpace(width);
                    return true;
                }

                if (symbol is "{" or "}")
                {
                    node = new MathAtom(symbol, italic: false);
                    return true;
                }

                return Fail(MathErrorCodes.UnknownCommand, at, $"不认得 \\{symbol}");
            }

            var start = _index;

            while (_index < source.Length && char.IsAsciiLetter(source[_index]))
            {
                _index++;
            }

            var name = source[start.._index];

            switch (name)
            {
                case "frac":
                    return TryFraction(out node);

                case "sqrt":
                    return TryRadical(out node);
            }

            if (Spaces.TryGetValue(name, out var space))
            {
                node = new MathSpace(space);
                return true;
            }

            if (Symbols.TryGetValue(name, out var text))
            {
                node = new MathAtom(text, italic: false);
                return true;
            }

            if (Functions.Contains(name))
            {
                // 函数名后面留一点空隙，否则 sin x 会挤成 sinx。
                node = new MathRow([new MathAtom(name, italic: false), new MathSpace(0.167)]);
                return true;
            }

            return Fail(MathErrorCodes.UnknownCommand, at, $"不认得 \\{name}");
        }

        private bool TryFraction(out MathNode node)
        {
            node = new MathRow([]);

            if (!TryArgument(out var numerator) || !TryArgument(out var denominator))
            {
                return false;
            }

            node = new MathFraction(numerator, denominator);
            return true;
        }

        private bool TryRadical(out MathNode node)
        {
            node = new MathRow([]);

            if (!TryArgument(out var radicand))
            {
                return false;
            }

            node = new MathRadical(radicand);
            return true;
        }

        /// <summary>结构命令吃的那个花括号组。少了、空了都算错。</summary>
        private bool TryArgument(out MathNode node)
        {
            node = new MathRow([]);

            if (_index >= source.Length || source[_index] != '{')
            {
                return Fail(MathErrorCodes.MissingArgument, _index, "这里要有一个花括号组");
            }

            _index++;

            if (!TryRow(stopAtBrace: true, out var inner) || !TryClose())
            {
                return false;
            }

            if (inner is MathRow { Children.Count: 0 })
            {
                return Fail(MathErrorCodes.MissingArgument, _index, "花括号组里是空的");
            }

            node = inner;
            return true;
        }

        private bool Fail(string code, int position, string detail)
        {
            Error = new MathSyntaxError(code, Math.Min(position, source.Length), detail);
            return false;
        }
    }
}
