# 任务 DAG

面向 coding agent 的任务描述。**单任务单 PR，不超过 500 行变更。**
人类只写任务、审 PR、做决策。

## 模板

```yaml
id: P0-02                     # 任务 ID，同时是分支名与 PR 标题前缀
title: 一句话说清交付物
phase: 0a                     # 0a / 0b / 1 / 2 / 3 / 4 / 5
status: pending               # pending / in-progress / done / blocked
depends_on: [P0-01]           # 这些任务必须已合并到 main
estimated_loc: 150
files:
  create: [路径...]            # 新建
  modify: [路径...]            # 修改（已存在的）
  test:   [路径...]            # 测试文件
acceptance:
  - command: dotnet build ...
    expect: exit 0
constraints:                  # 本任务不得违反的约束
  - ...
context:                      # 自包含上下文：要读的文档与代码
  - ...
```

## DAG（Phase 0a）

```
P0-01 NuGet 版本核查+锁定 ──┬── P0-02 Avalonia+SkiaSharp AOT 最小应用
                            ├── P0-03 Mermaider Sugiyama 约束验证
                            ├── P0-04 Mostlylucid.Dagre 复合图+AOT
                            ├── P0-05 ModelContextProtocol 传输+initialize
                            └── P0-09 MEAI+MCP 工具共用 PoC

P0-08 LLM 客户端（从 Day 1 并行，不阻塞）
```

**决策门 0** 在 Phase 0a 结束时判定：全绿（按计划）/ 黄（走备选）/ 红（人类决策）。

## Agent 执行流程

1. 读任务 YAML 与它指向的 `context` 文档
2. 检查 `depends_on` 全部已合并到 `main`
3. 从 `main` 拉分支，分支名 `{id}-{slug}`
4. 实现代码 + 测试
5. 本地跑 `acceptance` 全部通过
6. 提 PR：变更摘要 + 验收输出 + 未决问题 + 依赖解锁清单
7. 人类 review（只审不改）
8. 合并后下游任务自动解锁

## 冲突与失败

- 两个 agent 改同一文件：后合并者负责 rebase
- 冲突超过 50 行：人类仲裁
- 失败自动重试 3 次（每次从干净分支），3 次后标记 `blocked`

## 已完成

**进度以各 YAML 里的 `status` 为准，不要在这里另维护一份清单。**
两份清单必然分叉，而分叉之后没人知道该信哪一份——本文件原先就只写着
「P1-01 / P1-02 / P1-03 已落地」，那时已经有十四个任务完成了。

当前（2026-09-21）：Phase 0a / 0b 除 P0-02 标 `blocked` 外全部 `done`；
Phase 1 已落地 P1-01 ~ P1-08、P1-11 ~ P1-13、P1-15；
P1-09 / P1-10（Mermaid 导入导出）已写好任务文件待做。

**方案 Phase 1 清单里还有四个任务没有任务文件**：P1-14（DSL vs Mermaid 对比）、
P1-16 / P1-17（DSL 词法语法与语义映射）、P1-18（AotSmokeTest 独立工程，
工程本身已存在，是 P1-15 带出来的）。`docs/Architecture.md` 引用到它们，
但按 YAML 逐个推进的工作流在它们那里会断。
