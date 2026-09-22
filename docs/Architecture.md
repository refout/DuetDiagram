# 架构

## 分层

```
                 ┌────────── 入口层 ──────────┐
  GUI 画布/面板   内部 LLM   MCP stdio/HTTP   文件导入导出
        │            │            │              │
        └────────────┴────────────┴──────────────┘
                          │
                   ┌──────▼──────┐
                   │  DiagramCommandBus  │   ← 两条通路在此汇合
                   └──────┬──────┘
        校验 → 应用 → 版本/哈希 → 版本日志 → 审计日志 → 历史栈 → 广播
                          │
                   ┌──────▼──────┐
                   │ DiagramDocument (IR) │   ← 唯一事实源
                   └─────────────┘
```

「人和 LLM 能力对等」的落点就是这条汇合线：**命令之下没有任何东西知道变更来自谁**，
只有一个 `ChangeSource` 字段用于审计与变更高亮。

## 已实现模块

### `DuetDiagram.Core`（只依赖 BCL）

| 模块 | 文件 | 说明 |
|---|---|---|
| IR | `Model/` | `DiagramDocument` + `NodeDef` + `EdgeDef` + `CompositeDef` + `PageDef` / `LayerDef` + 形状与样式定义 + 调色板 + 布局提示 |
| 校验器 | `Model/DiagramValidator.cs` | 只报告不修改，返回结构化错误与修复建议。它服务的是**不经过命令层**的输入：加载外部文件、接收同步结果 |
| 冲突与字段元数据 | `Model/ChangeConflict.cs`、`Model/FieldRegistry.cs` | 冲突只判定不合并，写回必须走命令层；字段名取自登记表 |
| 命令契约 | `Commands/` | `IDiagramCommand`、`DiagramCommandBase`、`CommandMemento`、`CommandResult`、`CommandError`、`ValidationResult`、`FieldChange`、`ChangeContext`、`ISessionProvider`、`SessionIds`、`ErrorCodes` |
| 内置命令 | `Commands/Builtin/` | `AddNodeCommand`、`RemoveNodeCommand`、`ConnectEdgeCommand` |
| 版本日志 | `Logging/VersionLog.cs`、`DiffResult.cs`、`VersionEntry.cs` | 环形 100 条；`BuildDiff` 的五种结果 |
| 审计日志 | `Logging/AuditLog.cs` | 容量 1000 |
| 历史栈 | `History/HistoryStack.cs` | 撤销/重做双栈，各 500 |
| 广播 | `Broadcasting/` | `InProcessBroadcaster`（有界 1024 / DropOldest）、`NullChangeBroadcaster` |
| 命令总线 | `Bus/` | `Execute` / `ExecuteAsync` / `Undo` / `Redo`，门锁 + AsyncLocal 嵌套检测 |
| 序列化 | `Serialization/` | 源生成 JSON（AOT 安全）、结构哈希、视觉哈希 |
| Sidecar | `Sidecar/` | `layout.json` / `user.json` 的读写、路径推导、备份轮转与损坏恢复 |
| 工作区 | `Workspace/DiagramWorkspace.cs` | 文档 + 总线 + 广播器所有权 |
| 时间 | `Time/ITimeProvider.cs` | 命令总线填充时间戳的唯一来源 |
| 诊断 | `Diagnostics/` | 最小告警出口，避免 Core 依赖 `Microsoft.Extensions.Logging` |

### 其余工程

| 工程 | 说明 |
|---|---|
| `DuetDiagram.Layout` | 引擎封装（索引式入口）、四级降级与路径预算、级别唯一性、尝试记录，以及引擎不支持的四项补齐：固定位置、同层约束、层内次序、逐元素对齐 |
| `DuetDiagram.Render` | 四叉树空间索引、绘制列表（IR 与布局结果翻译成有序绘制指令）、视口变换与视口状态、视口虚拟化（剔除判据与换档编排）。画布控件在主程序 |
| `DuetDiagram.Mermaid` | 词法与语法、图类型识别、宽松模式导入（认不出的进导入报告）、导出 |
| `DuetDiagram.Dsl` | 词法与语法、语义映射（含五类布局意图与 `pin` 落到 sidecar） |
| `DuetDiagram.App` | 界面主程序：画布、视口交互与状态栏，含脱屏自检与帧率测量两个开关 |
| `DuetDiagram.E2E.Tests` | 无头模式下的端到端用例：起窗口、送输入、抓一帧、比像素 |

## 未实现模块

| 模块 | 方案位置 | 计划 |
|---|---|---|
| 诊断面板、属性面板与其余分区 | §九 GUI 设计、§十 性能策略 | Phase 2 |
| LLM 集成 / MCP Server / Skill 机制 | §六 / §七 / §八 | Phase 3 |
| 图层、页面、形状库、模板、组合、调色板、富文本与数学排版 | §四 核心数据模型、§13.8 | Phase 4 |
| 布局质量评分、主题、国际化与无障碍 | §13.9、§二十 | Phase 5 |

## 布局引擎的选型与已知缺口

**选型已确定**：`Mostlylucid.Dagre` 2.0.1 的**索引式布局入口**。
并排取证数据在 `reports/phase0a-layout.md`，复现命令：

```bash
dotnet run --project tools/Poc/LayoutCandidates -c Release
```

选它的原因是硬门槛：坐标必须是有界的。主选在那个判据上不成立——
同样 100 个节点、只多加了交叉边，主选算出的横向宽度从 1152 涨到 330248，
而备选在两个用例上都是 1044（加交叉前后完全不变）。

**四个已知缺口，必须由我们自建：**

| 缺口 | 影响 | 补齐思路 |
|---|---|---|
| 不支持固定位置 | 人工拖动的位置带不进下一次布局，手动微调形同虚设 | 布局后回填锚点，再按层做一次线性让位 |
| 不支持同层约束 | 表达不了"这两个是并列的" | 把同层组收缩成一个超节点参与布局，布局完成后再展开 |
| 不支持层内次序 | 表达不了"判定之后先走通过再走不通过"，连线交叉数随声明顺序漂 | 按给定次序把成员换位到它们自己占过的槽位上，再跑一次行内让位 |
| 不支持逐元素对齐 | 表达不了"这几个排成一列" | 在垂直于分层方向的那个轴上取齐；组里有固定成员时以它为准 |

四项都不是可以延后的细节：方案要求把人工拖动作为布局约束反馈，
缺口不补，用户的所有调整都会在下一次重排时丢失。

**补齐方案已经定下来并验证过**，完整设计、不变量与实测数据见 `docs/Layout-Constraints.md`，
结论已固化进正式工程：实现在 `DuetDiagram.Layout`，不变量测试在 `DuetDiagram.Layout.Tests`。要点：

- 无重叠是算法的必然结果，不是收敛目标。层内让位一次线性扫描精确可解。
- 同层组按展开后的总尺寸申报超节点，展开因此是纯局部操作，既不溢出也不挤压邻居。
- 让位方向垂直于分层方向，四个主方向都已验证。对齐轴与让位轴是同一个，理由相同。
- 约束作用在不同的自由度上（分层、层内左右关系、跨层的那个坐标），所以能同时成立；
  不能同时成立时如实报出来，不靠优先级悄悄丢掉一条。
- 节点的坐标一旦被移动，折线必须跟着重算；折线路由必须用空间索引，
  写成逐边遍历全部节点是平方复杂度，实测比索引化慢十倍。
- 补齐逻辑合计 5.3 毫秒（1000 节点、950 条边、250 个固定节点），
  引擎耗时是它的十倍以上。后续优化性能时应当先看引擎。

**实现时的注意事项：**

- 必须用索引式入口。默认入口在 1000 节点多层图上要 1204 毫秒，已经超出 1000 毫秒的预算。
- 建立图时必须传复合模式。非复合模式下布局主流程会抛异常，连四节点的菱形图都跑不完。
- 该库的说明文件与真实接口有多处不符（命名空间、构造函数签名），不要照抄说明写代码。
- 想知道某个能力支不支持，要看**行为**而不是接口里有没有同名字段。
  该库的节点类型里有坐标字段，但它们是算法的输出，写进去会被覆盖。

## 两条关键不变量

**1. 版本单调。** 每次成功且非空操作的命令都让 `DiagramDocument.Version` +1；
失败、被拒、`NoOp` 都不推进。撤销与重做也会推进版本 —— 版本描述的是**状态序列**，
不是「变更次数」。因此 `VersionLog` 的区间 `(from, to]` 永远可以完整重建状态。

**2. 结构哈希 ⟂ 视觉哈希。**
`StructuralHash` 只覆盖「谁和谁相连」+ `Parent`，决定是否需要重布局；
`VisualHash` 覆盖标签、形状、样式令牌，决定是否需要重绘。
两者都不覆盖 `Version` 与自身，避免自引用。
门禁：`Structural_hash_ignores_labels_but_visual_hash_does_not`。

## 构建环境要求

| 需求 | 用途 | 现状 |
|---|---|---|
| .NET SDK 10.0.303 | 全部 | 已满足 |
| NuGet 源 `nuget.azure.cn` | 还原 | 已在 `nuget.config` 固定 |
| MSVC「使用 C++ 的桌面开发」 | `PublishAot` 的原生链接步骤 | **本机缺失** |

缺少 MSVC 时 `dotnet publish` 会在 `Platform linker not found` 处失败，
但 IL 编译与 AOT 分析器已经在 `dotnet build` 阶段跑完（0 警告）。
此时可用 `dotnet run --project DuetDiagram.AotSmokeTest` 验证同一套源生成路径的逻辑，
它**不能**替代 AOT 发布验收（P1 判据 #9 / #21）。
