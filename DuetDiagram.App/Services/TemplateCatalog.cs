using System.Text.Json;
using DuetDiagram.Core.Templates;

namespace DuetDiagram.App.Services;

/// <summary>
/// 一个模板文件读不出来：哪一个、为什么。
/// </summary>
/// <param name="File">文件名，不含路径。面板上摆不下一条全路径。</param>
/// <param name="Reason">读不出来的原因，一句话。</param>
public sealed record TemplateLoadFailure(string File, string Reason);

/// <summary>
/// 模板目录：扫一遍，读得出来的列成清单，读不出来的单独记一笔。
/// </summary>
/// <remarks>
/// <para>
/// **目录可注入。** 缺省是程序集旁边的 <c>Templates</c>，而装好之后运行目录未必是它；
/// 写死一个相对路径的话，症状是"模板列表是空的"，且没有任何报错。
/// </para>
/// <para>
/// **一个文件读不出来不许把整张清单清空。** 这个目录是用户能往里放东西的地方，
/// 里面有一个手写坏的模板很正常；整张清单一起空掉的话，其余能用的模板也跟着不见了，
/// 而用户会以为模板功能坏了。坏的那几个单独列在下面，各自带一句原因。
/// </para>
/// <para>
/// **失败逐条记，不抛。** 调用方要的是"哪几个文件读不了、分别为什么"；
/// 抛出去的话它只能拿到第一个，于是修一个再来一个。
/// </para>
/// </remarks>
public sealed class TemplateCatalog
{
    /// <summary>模板文件的扩展名。目录里只有带这个后缀的文件算模板。</summary>
    public const string Suffix = ".template.json";

    /// <summary>写文件时用的临时后缀。带它就不算模板，扫目录时会跳过。</summary>
    private const string TempSuffix = ".tmp";

    /// <summary>
    /// 扫一个目录。不传就是程序集旁边的缺省目录。
    /// </summary>
    /// <param name="directory">从哪儿找模板。</param>
    public TemplateCatalog(string? directory = null)
    {
        Directory = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;

        Reload();
    }

    /// <summary>缺省的模板目录：程序集旁边的 <c>Templates</c>。</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "Templates");

    /// <summary>扫的是哪个目录。</summary>
    public string Directory { get; }

    /// <summary>读得出来的模板，按文件名排。</summary>
    public IReadOnlyList<TemplateDocument> Entries { get; private set; } = [];

    /// <summary>读不出来的那些文件。</summary>
    public IReadOnlyList<TemplateLoadFailure> Failures { get; private set; } = [];

    /// <summary>
    /// 重新扫一遍目录。
    /// </summary>
    /// <remarks>
    /// 目录不存在按"没有模板"处理，不报错：一份没带模板的安装就是这个样子，
    /// 报成错误的话，用户会去找一个本来就不该存在的问题。
    /// </remarks>
    public void Reload()
    {
        var entries = new List<TemplateDocument>();
        var failures = new List<TemplateLoadFailure>();

        if (System.IO.Directory.Exists(Directory))
        {
            foreach (var path in Files())
            {
                try
                {
                    entries.Add(TemplateDocument.Load(File.ReadAllText(path)));
                }
                catch (Exception exception) when (exception is TemplateFormatException
                    or JsonException
                    or IOException
                    or UnauthorizedAccessException)
                {
                    failures.Add(new TemplateLoadFailure(Path.GetFileName(path), exception.Message));
                }
            }
        }

        Entries = entries;
        Failures = failures;
    }

    /// <summary>
    /// 把一份模板写成目录里的一个新文件，返回写出来的路径。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **不覆盖已有的文件。** 同名时另起一个名字：模板是用户手写的东西，
    /// 悄悄盖掉一份他正指望着的模板，比多出一个带序号的文件糟得多。
    /// </para>
    /// <para>
    /// **先写临时文件再改名。** 直接往目标文件上写的话，写到一半断电留下的是半份内容，
    /// 而它读出来是一句格式错误，看不出是"写坏了"还是"本来就写错了"。
    /// </para>
    /// </remarks>
    /// <exception cref="IOException">目录建不出来或者文件写不进去。</exception>
    /// <exception cref="UnauthorizedAccessException">目录不许写。</exception>
    public string Save(TemplateDocument template)
    {
        ArgumentNullException.ThrowIfNull(template);

        System.IO.Directory.CreateDirectory(Directory);

        var path = Unique(Stem(template.Name));
        var temp = path + TempSuffix;

        File.WriteAllText(temp, TemplateDocument.Save(template));
        File.Move(temp, path);

        Reload();

        return path;
    }

    /// <summary>目录里带模板后缀的文件，按文件名排。</summary>
    /// <remarks>
    /// 排序是为了让清单的顺序稳定：目录自己的枚举顺序由文件系统给，
    /// 同一批文件在两台机器上可能列出两种次序，而面板每次重铺都会跟着变。
    /// </remarks>
    private IEnumerable<string> Files() =>
        System.IO.Directory.EnumerateFiles(Directory)
            .Where(path => path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

    /// <summary>一个还没被占用的文件名。</summary>
    private string Unique(string stem)
    {
        var candidate = Path.Combine(Directory, stem + Suffix);
        var suffix = 2;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(Directory, $"{stem}-{suffix++}{Suffix}");
        }

        return candidate;
    }

    /// <summary>模板名换成一个能当文件名的词。</summary>
    /// <remarks>
    /// 名字来自文档标识，而标识里允许出现路径分隔符与冒号这类字符。
    /// 不换的话，一个叫 <c>a/b</c> 的标识会把文件写到别的目录去。
    /// </remarks>
    private static string Stem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Select(c => invalid.Contains(c) ? '-' : c)]).Trim();

        return cleaned.Length == 0 ? "template" : cleaned;
    }
}
