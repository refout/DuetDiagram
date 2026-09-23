using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Tools.McpHarness;

// 这一层是实机验收的入口：拿真的服务端可执行文件，把 §15.2 那张 MCP 清单上的各项
// 逐条在协议层跑一遍。退出码表达整体结果——它是给 CI 与脚本看的，
// 而逐项的那几行是给人看的。
//
// 真实代理那一步（Codex CLI / Claude Code 接上标准输入输出服务端）装置替不了：
// 那两个是别人的程序，要装、要凭据、要有人看着跑。所以这里只提供 `agent` 这个动作，
// 把接上去要用的命令行原样印出来，由人去执行。
return await RunAsync(args).ConfigureAwait(false);

static async Task<int> RunAsync(string[] args)
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;

    var action = args.FirstOrDefault(argument => !argument.StartsWith('-')) ?? "protocol";

    return action switch
    {
        "protocol" => await ProtocolAsync().ConfigureAwait(false),
        "agent" => Agent(),
        _ => Unknown(action),
    };
}

static async Task<int> ProtocolAsync()
{
    using var cancellation = new CancellationTokenSource();

    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    IReadOnlyList<Check> checks;

    try
    {
        checks = await Scenarios.RunAsync(cancellation.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("被叫停了。");

        return 2;
    }

    foreach (var check in checks)
    {
        Console.WriteLine($"{(check.Passed ? "[通过]" : "[未通过]")} {check.Name}：{check.Detail}");
    }

    var failed = checks.Count(check => !check.Passed);

    Console.WriteLine();
    Console.WriteLine($"{checks.Count - failed}/{checks.Count} 项通过。");

    return failed == 0 ? 0 : 1;
}

/// <summary>
/// 把真实代理接上去要用的命令行印出来。
/// </summary>
/// <remarks>
/// **它不替人跑代理。** Codex CLI 与 Claude Code 是别人的程序，要装、要凭据、
/// 要有人看着；这一层能做的只是把接上去要的那一串东西准备对——
/// 路径、开关、工作区。印出来由人执行，跑没跑过、结果如何，由报告照实记。
/// </remarks>
static int Agent()
{
    string server;

    try
    {
        server = HarnessSession.ServerPath();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);

        return 1;
    }

    var workspace = HarnessSession.NewWorkspace();
    var document = Path.Combine(workspace, "agent.json");

    // 先把那份文档写出来。服务端按路径起的时候是**读**那个文件，不存在就直接报错起不来——
    // 而这一档的本意是让人把它接上去，不是让人先自己去造一份合法的文档。
    File.WriteAllText(
        document,
        DiagramSerializer.SerializeFull(new DiagramDocument("agent-document")));

    Console.WriteLine("标准输入输出服务端：");
    Console.WriteLine($"  \"{server}\" {DuetDiagram.Mcp.Program.DocumentSwitch} \"{document}\"");
    Console.WriteLine();
    Console.WriteLine("Claude Code 的 MCP 配置：");
    Console.WriteLine($"  claude mcp add duetdiagram -- \"{server}\" {DuetDiagram.Mcp.Program.DocumentSwitch} \"{document}\"");
    Console.WriteLine();
    Console.WriteLine("Codex CLI 的 MCP 配置：");
    Console.WriteLine($"  [mcp_servers.duetdiagram]");
    Console.WriteLine($"  command = \"{server}\"");
    Console.WriteLine($"  args = [\"{DuetDiagram.Mcp.Program.DocumentSwitch}\", \"{document}\"]");
    Console.WriteLine();
    Console.WriteLine($"工作区（临时）：{workspace}");

    return 0;
}

static int Unknown(string action)
{
    Console.Error.WriteLine($"不认得动作 {action}。可用：protocol、agent。");

    return 2;
}
