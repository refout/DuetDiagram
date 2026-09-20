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

## 已实现模块（垂直切片）

| 模块 | 文件 | 说明 |
|---|---|---|
| IR | `Model/` | `DiagramDocument` + `NodeDef` + `EdgeDef` + 5 个枚举 |
| 命令契约 | `Commands/` | `IDiagramCommand`、`DiagramCommandBase`、`CommandMemento`、`CommandResult`、`CommandError`、`ValidationResult`、`FieldChange`、`ChangeContext`、`ISessionProvider`、`SessionIds` |
| 内置命令 | `Commands/Builtin/` | `AddNodeCommand`、`RemoveNodeCommand`、`ConnectEdgeCommand` |
| 版本日志 | `Logging/VersionLog.cs`、`DiffResult.cs`、`VersionEntry.cs` | 环形 100 条；`BuildDiff` 的五种结果 |
| 审计日志 | `Logging/AuditLog.cs` | 容量 1000 |
| 历史栈 | `History/HistoryStack.cs` | 撤销/重做双栈，各 500 |
| 广播 | `Broadcasting/` | `InProcessBroadcaster`（有界 1024 / DropOldest）、`NullChangeBroadcaster` |
| 命令总线 | `Bus/` | `Execute` / `ExecuteAsync` / `Undo` / `Redo`，门锁 + AsyncLocal 嵌套检测 |
| 序列化 | `Serialization/` | 源生成 JSON（AOT 安全）、结构哈希、视觉哈希 |
| 工作区 | `Workspace/DiagramWorkspace.cs` | 文档 + 总线 + 广播器所有权 |
| 诊断 | `Diagnostics/` | 最小告警出口，避免 Core 依赖 `Microsoft.Extensions.Logging` |

## 未实现模块

| 模块 | 方案位置 | 计划 |
|---|---|---|
| Sidecar（`layout.json` / `user.json` / `.dsl`） | §4.3 / §11 | Phase 1 P1-06 / P1-07 |
| 布局引擎 + 四级降级 | §5 | Phase 1 P1-11 / P1-12 |
| Mermaid 导入导出 | §二 | Phase 1 P1-08 ~ P1-10 |
| DSL 解析器 | §一 | Phase 1 P1-16 / P1-17 |
| 四叉树视口索引 | §十 | Phase 1 P1-13 |
| 校验器 / 冲突策略 | §4.4 | Phase 1 P1-04 / P1-05 |
| 渲染与导出 | §三 | Phase 2 |
| LLM / MCP / Skills | §六~八 | Phase 3 |

## 布局引擎的选型与已知缺口

**选型已确定**：`Mostlylucid.Dagre` 2.0.1 的**索引式布局入口**。
并排取证数据在 `reports/phase0a-layout.md`，复现命令：

```bash
dotnet run --project tools/Poc/LayoutCandidates -c Release
```

选它的原因是硬门槛：坐标必须是有界的。主选在那个判据上不成立——
同样 100 个节点、只多加了交叉边，主选算出的横向宽度从 1152 涨到 330248，
而备选在两个用例上都是 1044（加交叉前后完全不变）。

**两个已知缺口，必须由我们自建：**

| 缺口 | 影响 | 补齐思路 |
|---|---|---|
| 不支持固定位置 | 人工拖动的位置带不进下一次布局，手动微调形同虚设 | 布局后回填锚点，再按层做一次线性让位 |
| 不支持同层约束 | 表达不了"这两个是并列的" | 把同层组收缩成一个超节点参与布局，布局完成后再展开 |

这两项不是可以延后的细节：方案要求把人工拖动作为布局约束反馈，
缺口不补，用户的所有调整都会在下一次重排时丢失。

**补齐方案已经定下来并验证过**，完整设计、不变量与实测数据见 `docs/Layout-Constraints.md`，
结论已固化进正式工程：实现在 `DuetDiagram.Layout`，不变量测试在 `DuetDiagram.Layout.Tests`。要点：

- 无重叠是算法的必然结果，不是收敛目标。层内让位一次线性扫描精确可解。
- 同层组按展开后的总尺寸申报超节点，展开因此是纯局部操作，既不溢出也不挤压邻居。
- 让位方向垂直于分层方向，四个主方向都已验证。
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
