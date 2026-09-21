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
Phase 1 除 P1-14 外全部 `done`——P1-01 ~ P1-13、P1-15 ~ P1-18 已落地。

**P1-14 的装置全齐了，缺的是人。** `tools/CompareHarness` 七条命令
（`generate` / `listings` / `semantic` / `score` / `verify` / `sample` / `agreement`）
能自动算的都算了。剩下三步：判 60 份抽样把谓词校一遍、再决定要不要评全量 150 份、
最后填决策门 1。**这三步编码 agent 做不了**，理由见 `reports/compare-blind/conclusion.md`：
评的人知道答案，而逐批严格度的漂移会伪装成组间差异。

还有一件事判定门本身回答不了：**它只能回答"在结构层面 DSL 有没有输"，
不能回答"该不该留"**——盲评清单里看不到布局表达能力（端口、样式、坐标、布局意图
只有一种格式能表达，放进去等于盖了组别的戳），而那恰恰是 DSL 存在的主要理由。
见 `conclusion.md` 第五节。

Phase 2 的任务 YAML **还没产出**。方案写的是「Phase N 的任务 YAML 在 Phase N-1 结束时产出」，
而 Phase 1 唯一没结的就是上面那一步人工活。是先开 Phase 2、还是等决策门 1，得定一个。

方案 Phase 1 清单里的 18 个 ID 至此都有对应文件。其中两个需要说明：

- **P1-18 是事后登记的**，状态 `done`。工程早在 Phase 0a 就建起来了，
  P1-15 又扩展了覆盖面，写文件只是把它登记回清单。
- **P1-09 / P1-17 的范围比方案原文大**：方案没有单列「文本映射到 IR」这一步，
  而 P1-08 / P1-16 都只做到语法树。不补上这一步，导入这条线没有产出物，
  下游的导出与对比评分也没有东西可评。理由写在各自 YAML 的 `scope_note` 里。

## 任务文件本身要能被解析

它们是给 agent 读的，读不了等于任务不存在。已经踩过一次：
P1-15 的 `expect` 值里有未加引号的冒号加空格，整个文件解析失败。

改动任务文件后跑一遍：

```bash
python -c "import yaml,glob; [yaml.safe_load(open(f,encoding='utf-8')) for f in glob.glob('tasks/**/*.yaml',recursive=True)]; print('ok')"
```

注意以 `*` 开头的列表项会被当成 YAML 别名，整段要加引号。
