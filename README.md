# DuetDiagram

对人和 LLM 双友好的流程图 / 框图绘制软件。

**核心命题**：人和 LLM 通过同一套命令接口操作同一份文档，能力完全对等。
IR 是唯一事实源；GUI 能做的，LLM 通过工具都能做；LLM 能表达的，GUI 都有对应入口。
区别只在操作来源标记和可选的冲突策略。

完整方案见《对人和 LLM 双友好的流程图/框图绘制软件——完整方案.md》。

## 当前状态

Phase 0a / 0b、Phase 1、Phase 2、Phase 3 的代码都落地了。Phase 4（丰富功能）的任务集
已产出，全部待开工。
逐阶段的结论、取证数据与仍然开着的决策门见 `tasks/README.md`，工程清单见 `AGENTS.md`。

**两个决策门都还开着，卡在同一件事上。** 自有 DSL 的去留与布局引擎的主选都要读
`reports/compare-blind/` 下的人工评分，而那份评分要人来打——编码 agent 判不了
自己写的东西好不好用。所以这两项不是「还没做」，是「做不了」。

**已知阻塞**：原生编译发布需要 Visual Studio 的「使用 C++ 的桌面开发」工作负载，
本机未安装，因此这项验收尚未关闭。见 `reports/phase0a.md`。

## 快速开始

```bash
# 编译（要求 .NET SDK 10.0.303+）
dotnet build DuetDiagram.slnx -c Release

# 测试
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj

# 按分类跑
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=Atomicity"

# 界面栈自检：脱屏渲染一帧后退出，用退出码表达结果
dotnet run --project DuetDiagram.App -c Release -- --selftest --out reports/phase2-selftest.png

# 布局引擎约束取证（两款候选跑同一套判据）
dotnet run --project tools/Poc/LayoutCandidates -c Release

# 原生编译冒烟（需要 C++ 桌面开发工作负载）
dotnet run --project DuetDiagram.AotSmokeTest -c Release

# LOC 报告
dotnet run --project tools/LocCounter -- --root .
```

全部验收命令按分类列在 `AGENTS.md` 的「验收命令」一节。

## 仓库结构

```
DuetDiagram.slnx            解决方案（slnx 格式）
Directory.Build.props       公共编译属性（net10.0 / 警告即错误 / 裁剪与原生编译分析器）
Directory.Packages.props    精确版本锁定（禁止通配）
global.json                 固定 SDK + 启用 Microsoft.Testing.Platform
AGENTS.md                   Agent 契约：13 条不可违反的约束 + 已知差异 + 验收命令
DuetDiagram.Core            IR、命令总线、日志、历史、广播、序列化、工作区与文档锁（只依赖 BCL）
DuetDiagram.Layout          布局引擎封装、四级降级与四项约束补齐
DuetDiagram.Render          空间索引、绘制列表、视口变换与虚拟化
DuetDiagram.Mermaid         Mermaid 的词法、语法、导入与导出
DuetDiagram.Dsl             自有 DSL 的词法、语法与语义映射
DuetDiagram.Llm             八个粗粒度工具的定义、参数 schema 与参数校验
DuetDiagram.App             界面主程序（画布、面板、自检与基准开关）
DuetDiagram.*.Tests         各层的单元与约束测试
DuetDiagram.E2E.Tests       无头模式下的端到端用例
DuetDiagram.AotSmokeTest    原生编译冒烟
DuetDiagram.Benchmarks      性能基线
docs/                       架构、IR Schema、错误码、命令清单、渲染管线、GUI
reports/                    阶段验证结论与取证数据
tasks/                      面向 coding agent 的任务 YAML 与 DAG
tools/LocCounter            LOC 统计（不进 sln）
tools/CompareHarness        对比测试的语料、盲评装置与谓词评分（不进 sln）
tools/UserStudy             用户测试的量表与统计判定（不进 sln）
tools/Poc/                  依赖验证脚手架（不进 sln）
.github/workflows/ci.yml    编译 / 测试 / 契约门禁 / 原生发布
```

## 给 Agent 的入口

先读 `AGENTS.md`，再读 `tasks/README.md` 找可认领的任务。
`AGENTS.md` 里的 13 条约束都有对应测试或 CI 门禁，违反即视为回归。
