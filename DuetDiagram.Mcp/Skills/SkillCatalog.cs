using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DuetDiagram.Mcp.Skills;

/// <summary>
/// Skill 的目录：有哪几份、每一份讲什么、以及一条 <c>skill://</c> URI 该取出哪段文字。
/// </summary>
/// <remarks>
/// <para>
/// **它只管取，不管什么时候取。** 「按任务匹配」这件事发生在调用方那一侧：清单里有每一份的
/// 一句话说明，调用方照着挑。把选择做进这里的话，这一层就得知道调用方手上是什么任务，
/// 而那正是它拿不到的东西。
/// </para>
/// <para>
/// 内容全部来自随程序集走的嵌入资源。运行时去读仓库里那条路径是不行的：
/// 服务端是被客户端拉起来的子进程，工作目录不由它决定，而找不到文件的表现是
/// 「这份 Skill 是空的」——一个静默的错误答案。
/// </para>
/// </remarks>
public sealed class SkillCatalog
{
    /// <summary>URI 的协议名。</summary>
    public const string Scheme = "skill";

    /// <summary>正文与资源文件的类型。</summary>
    public const string Markdown = "text/markdown";

    /// <summary>
    /// 随程序集走的那一份。
    /// </summary>
    /// <remarks>
    /// 用懒初始化而不是静态字段：静态字段按声明次序初始化，而读资源要用到下面那两张表——
    /// 次序一颠倒，拿到的是一张还没填的表，表现是「一份 Skill 都没有」。
    /// </remarks>
    public static SkillCatalog Default => Shared.Value;

    private static readonly Lazy<SkillCatalog> Shared =
        new(() => Load(typeof(SkillCatalog).Assembly));

    /// <summary>两份正文各自的嵌入资源名。</summary>
    private static readonly string[] BodyResources =
    [
        "DuetDiagram.Mcp.Skills.diagram-workflow.md",
        "DuetDiagram.Mcp.Skills.diagram-syntax.md",
    ];

    /// <summary>
    /// 资源文件：哪一份 Skill 下的哪条路径，取自哪一份嵌入资源。
    /// </summary>
    /// <remarks>
    /// 那张表放在代码里而不是写在正文头部：这里填的是嵌入资源的名字，那是构建期的事实，
    /// 正文写不出来也验不了。写错在这里会在装载时就报出来，写错在正文里则是一份取不到的资源。
    /// </remarks>
    private static readonly AssetFile[] AssetFiles =
    [
        new(
            "diagram-syntax",
            "dsl",
            "DuetDiagram.Mcp.Skills.dsl-syntax.md",
            "DSL 完整语法与示例",
            "要写或核对一段 DSL 文本时取它。语法、已定的默认值、映射阶段的规则都在里面。"),
    ];

    private readonly IReadOnlyList<SkillResource> _skills;
    private readonly Dictionary<string, SkillResource> _byName;

    /// <summary>按给定的几份建一个目录。名字重复时抛异常。</summary>
    public SkillCatalog(IEnumerable<SkillResource> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);

        _skills = [.. skills];
        _byName = new Dictionary<string, SkillResource>(StringComparer.Ordinal);

        foreach (var skill in _skills)
        {
            if (!_byName.TryAdd(skill.Name, skill))
            {
                throw new ArgumentException($"Skill 名 {skill.Name} 有两份。名字是 URI 里那一段，不能有两个。", nameof(skills));
            }
        }
    }

    /// <summary>有哪几份，按登记次序。</summary>
    public IReadOnlyList<SkillResource> Skills => _skills;

    /// <summary>按标识取一份。没有这一份时返回空。</summary>
    public SkillResource? Find(string name) =>
        name is not null && _byName.TryGetValue(name, out var skill) ? skill : null;

    /// <summary>
    /// 取一条 URI 指向的那段文字。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 认不出来的一律返回空，不抛异常：URI 来自协议对端，那是外部输入，
    /// 不是内部调用方的笔误。空与「这一段是空的」是两件事，后者在装载时就被挡下了。
    /// </para>
    /// <para>
    /// **判据是解析之后的段，不是原始字符串。** 前缀比较挡不住 <c>..</c>，
    /// 也挡不住百分号编码过的 <c>..</c>；而直接交给 <see cref="Uri"/> 去解析的话，
    /// 它自己会把 <c>..</c> 规范化掉——拿它的结果做判断，等于把要判的东西先擦掉了。
    /// 所以这里自己切段、先解码再归一，弹到 Skill 名那一层就拒绝。
    /// </para>
    /// </remarks>
    /// <param name="uri">要取的 URI。</param>
    public string? Read(string uri)
    {
        if (!TrySplit(uri, out var name, out var path) || Find(name) is not { } skill)
        {
            return null;
        }

        if (path.Length == 0)
        {
            return skill.Body;
        }

        foreach (var asset in skill.Assets)
        {
            if (string.Equals(asset.Path, path, StringComparison.Ordinal))
            {
                return asset.Text;
            }
        }

        return null;
    }

    /// <summary>
    /// 挂到协议上的那一份。
    /// </summary>
    /// <remarks>
    /// 每一份资源带着自己的一句话说明与版本。说明进清单，调用方照着它挑；
    /// 版本让调用方判断手上那一份还是不是当前这一份——正文改了不会以别的方式暴露出来，
    /// 一份过期的正文读起来与新的没有区别。
    /// </remarks>
    public IReadOnlyList<McpServerResource> ToMcpResources()
    {
        var resources = new List<McpServerResource>();

        foreach (var skill in _skills)
        {
            resources.Add(Resource(skill.Uri, skill.Name, skill.Title, skill.Summary, skill.Version));

            foreach (var asset in skill.Assets)
            {
                resources.Add(Resource(
                    $"{skill.Uri}/{asset.Path}",
                    $"{skill.Name}-{asset.Path.Replace('/', '-')}",
                    asset.Title,
                    asset.Summary,
                    skill.Version));
            }
        }

        return resources;
    }

    /// <summary>从程序集里装载全部 Skill。</summary>
    /// <remarks>
    /// 正文头部缺一项就抛异常，不退回一个缺省值：那几项都是构建期的事实，
    /// 缺一项说明资源本身有问题，而缺省值会让它一路装下去、只在调用方那里表现成一句空话。
    /// </remarks>
    public static SkillCatalog Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var skills = new List<SkillResource>();

        foreach (var name in BodyResources)
        {
            var skill = Parse(ReadResource(assembly, name));

            skills.Add(skill with
            {
                Assets =
                [
                    .. AssetFiles
                        .Where(file => string.Equals(file.Skill, skill.Name, StringComparison.Ordinal))
                        .Select(file => Asset(assembly, file)),
                ],
            });
        }

        return new SkillCatalog(skills);
    }

    /// <summary>一条 <c>skill://</c> URI 切出来的两段。</summary>
    private static bool TrySplit(string? uri, out string name, out string path)
    {
        name = string.Empty;
        path = string.Empty;

        if (uri is null)
        {
            return false;
        }

        var prefix = $"{Scheme}://";

        if (!uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = uri[prefix.Length..];

        // 查询与片段在这条通道上没有含义。不切掉的话，带片段的那条 URI 会变成一个谁也不认识的名字。
        var cut = rest.IndexOfAny(['#', '?']);

        if (cut >= 0)
        {
            rest = rest[..cut];
        }

        var segments = new List<string>();

        foreach (var raw in rest.Split('/'))
        {
            string segment;

            try
            {
                segment = Uri.UnescapeDataString(raw);
            }
            catch (UriFormatException)
            {
                return false;
            }

            // 解码之后才判：`%2E%2E` 与 `..` 是同一个东西，判在解码之前等于没判。
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                // 弹到 Skill 名那一层就拒绝。Skill 名就是这一层的根，越过去之后没有落点，
                // 而「先出去再绕回来」这种写法只在绕过检查时才有意义。
                if (segments.Count <= 1)
                {
                    return false;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            // 解码之后又出现分隔符，说明这一段想跨过一层。
            if (segment.Contains('/') || segment.Contains('\\'))
            {
                return false;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            return false;
        }

        name = segments[0];
        path = string.Join('/', segments.Skip(1));

        return true;
    }

    /// <summary>把一份正文读成一条记录。</summary>
    private static SkillResource Parse(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        if (lines.Length < 2 || lines[0].Trim() != "---")
        {
            throw new InvalidDataException("Skill 正文要以一行 --- 开头，后面是那几项元数据。");
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var end = -1;

        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            if (line == "---")
            {
                end = index;
                break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon <= 0)
            {
                throw new InvalidDataException($"Skill 正文头部这一行不是 key: value：{line}");
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            // 不认识的键直接拒绝：多写一个键（比如把 summary 拼错）不会有别的地方报出来，
            // 而它的表现是清单里少一句话，调用方于是无从判断该不该取这一份。
            if (key is not ("name" or "title" or "version" or "summary"))
            {
                throw new InvalidDataException($"Skill 正文头部有不认识的键 {key}。");
            }

            if (!fields.TryAdd(key, value))
            {
                throw new InvalidDataException($"Skill 正文头部把 {key} 写了两遍。");
            }
        }

        if (end < 0)
        {
            throw new InvalidDataException("Skill 正文的头部没有收尾的那一行 ---。");
        }

        var body = string.Join('\n', lines[(end + 1)..]).Trim();

        if (body.Length == 0)
        {
            throw new InvalidDataException("Skill 正文是空的。取一份空的东西比取不到更糟：调用方以为它拿到了。");
        }

        return new SkillResource
        {
            Name = Required(fields, "name"),
            Title = Required(fields, "title"),
            Version = Required(fields, "version"),
            Summary = Required(fields, "summary"),
            Body = body,
        };
    }

    private static string Required(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && value.Length > 0
            ? value
            : throw new InvalidDataException($"Skill 正文头部缺 {key}。");

    private static SkillAsset Asset(Assembly assembly, AssetFile file) => new()
    {
        Path = file.Path,
        Title = file.Title,
        Summary = file.Summary,
        Text = ReadResource(assembly, file.Resource),
    };

    /// <summary>读一份嵌入资源。</summary>
    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"程序集 {assembly.GetName().Name} 里没有 {name} 这一份嵌入资源。");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    /// <summary>挂一份资源上去。取的时候回到 <see cref="Read"/>，不把文字再存一遍。</summary>
    private McpServerResource Resource(string uri, string name, string title, string description, string version) =>
        McpServerResource.Create(
            (RequestContext<ReadResourceRequestParams> _) => new TextResourceContents
            {
                Uri = uri,
                MimeType = Markdown,
                Text = Read(uri)
                    ?? throw new InvalidOperationException($"{uri} 是挂上去过的，读的时候却不见了。"),
            },
            new McpServerResourceCreateOptions
            {
                UriTemplate = uri,
                Name = name,
                Title = title,
                Description = description,
                MimeType = Markdown,
                Meta = new JsonObject { ["version"] = version },
            });

    /// <summary>一份资源文件在程序集里的落点。</summary>
    private sealed record AssetFile(string Skill, string Path, string Resource, string Title, string Summary);
}
