namespace DuetDiagram.Render.Tests;

/// <summary>
/// 绘制列表的快照比对。
/// </summary>
/// <remarks>
/// <para>
/// 快照是仓库里的文本文件，比对的是绘制列表的规范文本。**逐字节比**，
/// 不做任何容差——容差会让"差了零点零几"这类真实变化被吞掉，而坐标算错往往就是差这么点。
/// </para>
/// <para>
/// 不一致时把实际结果写成 <c>.received.txt</c> 并**报出第一处差异所在的行**。
/// 行首就是指令类型与元素标识，所以这条报错直接说出了是哪个元素的哪一条指令变了，
/// 而不是只说"快照不一样"。
/// </para>
/// <para>
/// 快照文件缺**失时报错而不是自动生成**。自动生成会让第一次运行永远通过，
/// 而那份刚生成的快照没人看过，等于把当前行为当成了期望行为。
/// </para>
/// </remarks>
internal static class Snapshot
{
    private const string DirectoryName = "Scenes";

    /// <summary>比对一份快照。不一致时抛异常并留下实际结果。</summary>
    public static void Match(string name, string actual)
    {
        var directory = SceneDirectory();
        var verified = Path.Combine(directory, name + ".txt");
        var received = Path.Combine(directory, name + ".received.txt");

        // 目录在第一次跑、还没确认过任何快照时并不存在。
        Directory.CreateDirectory(directory);

        if (!File.Exists(verified))
        {
            File.WriteAllText(received, actual);

            throw new InvalidOperationException(
                $"快照 {name} 不存在。核对过 {received} 之后，把它改名成 {verified} 再提交。");
        }

        var expected = Normalize(File.ReadAllText(verified));

        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(received, actual);

        throw new InvalidOperationException(Explain(name, expected, actual, received));
    }

    private static string Explain(string name, string expected, string actual, string received)
    {
        var left = expected.Split('\n');
        var right = actual.Split('\n');
        var lines = Math.Max(left.Length, right.Length);

        for (var index = 0; index < lines; index++)
        {
            var was = index < left.Length ? left[index] : "<没有这一行>";
            var now = index < right.Length ? right[index] : "<没有这一行>";

            if (!string.Equals(was, now, StringComparison.Ordinal))
            {
                return $"快照 {name} 第 {index + 1} 行不一致。\n"
                    + $"  期望：{was}\n"
                    + $"  实际：{now}\n"
                    + $"完整结果见 {received}。";
            }
        }

        return $"快照 {name} 不一致，但逐行比下来没找到差异。多半是行尾符或编码的问题。";
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string SceneDirectory() =>
        Path.Combine(RepositoryRoot(), "DuetDiagram.Render.Tests", DirectoryName);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DuetDiagram.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("从测试程序集的位置找不到仓库根（含 DuetDiagram.slnx 的目录）。");
    }
}
