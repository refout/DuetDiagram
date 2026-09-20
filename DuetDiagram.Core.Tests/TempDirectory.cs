namespace DuetDiagram.Core.Tests;

/// <summary>
/// 每个用例一个独立临时目录，用完即删。
/// </summary>
/// <remarks>
/// 文件相关的用例尤其需要这样：它们验的正是"文件落在哪里"，
/// 共用一个目录会让用例之间通过残留文件互相干扰，而且症状是"单独跑通过、一起跑失败"。
/// </remarks>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"duet-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>当前存在的文件数。用于断言"有没有留下多余的东西"。</summary>
    public string[] Files() => Directory.GetFiles(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响用例结论，留给系统临时目录自己收拾。
        }
    }
}
