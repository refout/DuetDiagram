# 错误码

来源：`DuetDiagram.Core/Commands/ErrorCodes.cs`。GUI 处理方式见方案 §9.6。

## 命令错误（由 `Fail` / `Conflict` 产生）

| 码 | 类型 | 触发 | GUI 处理 |
|---|---|---|---|
| `DUPLICATE_ID` | 校验失败 | 节点或边 id 已存在 | 高亮冲突 ID |
| `EDGE_TARGET_MISSING` | 校验失败 | 边的 `to` 节点不存在 | 提示「是否创建？」 |
| `EDGE_SOURCE_MISSING` | 校验失败 | 边的 `from` 节点不存在 | 提示「是否创建？」 |
| `GROUP_MEMBER_MISSING` | 校验失败 | — （Phase 4） | 提示 |
| `GROUP_CYCLE` | 校验失败 | — （Phase 4） | 高亮循环 |
| `VERSION_CONFLICT` | 并发冲突 | MCP 客户端版本落后 | 弹可视化 diff 对话框 |
| `INVALID_EXPECTED_VERSION` | 参数错误 | 客户端版本 > 服务端版本 | 状态栏提示「内部错误，已记录日志」 |
| `EXPECTED_VERSION_REQUIRED` | 参数错误 | MCP 模式未携带 `VersionCheckRequest` | 状态栏提示 |
| `LAYOUT_ALL_LEVELS_TIMEOUT` | 布局失败 | — （Phase 1） | 状态栏红色提示 + 三选项 |
| `MCP_UNAUTHORIZED` | 认证失败 | — （Phase 3） | 提示「认证失败」 |
| `MCP_RATE_LIMITED` | 速率超限 | — （Phase 3） | 状态栏提示「请求过于频繁」 |
| `INTERNAL_ERROR` | 内部错误 | `Apply` 抛异常 | 状态栏提示 + 日志 |

`INTERNAL_ERROR` 可重试，`VERSION_CONFLICT` 可重试 —— 两者由 `CommandResult.IsRetryable` 判定。

## 本轮扩展

方案 §18 的表以行为例、未穷举命令级前置条件。以下三条按同一命名风格补充，
**后续任务必须复用，不得另起名字**：

| 码 | 类型 | 触发 |
|---|---|---|
| `NODE_MISSING` | 校验失败 | 被操作的节点不存在（如 `remove-node`） |
| `EDGE_MISSING` | 校验失败 | 被操作的边不存在 |
| `INVALID_ID` | 校验失败 | id 为空或全空白 |

## Sidecar 状态码（独立表，不属于 `CommandError`）

| 码 | 触发 | 处理 |
|---|---|---|
| `SIDECAR_HASH_MISMATCH` | `layout.json` 哈希与文档不符 | 加载时静默重布局 |
| `SIDECAR_CORRUPT` | `user.json` 无法解析 | 加载时弹恢复对话框 |

## 非错误消息（由 `NoOp` 返回）

`CommandResult.NoOp` 的 `IsSuccess = true`、`IsNoOp = true`、`IsEffectiveSuccess = false`。
GUI 在状态栏灰色显示，**不弹窗**。

| 消息 | 触发 |
|---|---|
| `无可撤销操作` | 撤销栈为空 |
| `无可重做操作` | 重做栈为空 |

## Payload 约定

`CommandError.Payload` 承载 id、字段名等短字符串。
**绝不携带异常类型名**（AGENTS.md 约定 9）；类型名只写应用日志。
门禁：`AtomicityTests.Partial_write_is_rolled_back(throwAfterPartialWrite: True)`。
