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

## 整体校验使用的码

上面的码由命令的前置检查产生，在写入前挡住非法状态。
下面这些由加载文件、接收同步结果时的**整体校验**产生——那些入口不经过命令层。

命名风格一致，外部代理不需要区分来源，按同一张表处理即可。

| 码 | 类型 | 触发 | GUI 处理 |
|---|---|---|---|
| `EDGE_PORT_MISSING` | 校验失败 | 边指定的端口在节点上不存在 | 高亮该边 |
| `MEMBERSHIP_MISMATCH` | 校验失败 | 组合的成员列表与成员的父级互相矛盾 | 高亮两者 |
| `PARENT_MISSING` | 校验失败 | 节点或组合的父级指向不存在的组合 | 提示「是否创建？」 |
| `TAG_MEMBER_MISSING` | 校验失败 | 标签的成员不存在 | 高亮该标签 |
| `ACTION_TARGET_MISSING` | 校验失败 | 动作的目标不存在 | 高亮该动作 |

`DUPLICATE_ID`、`EDGE_SOURCE_MISSING`、`EDGE_TARGET_MISSING`、`GROUP_MEMBER_MISSING`、
`GROUP_CYCLE` 五个码两处都用，含义相同。

### 校验结果与线上形状是两个类型

| 类型 | 用途 | 字段 |
|---|---|---|
| `ValidationIssue` | 给人看、给界面高亮 | 码、说明、相关标识、**修复建议** |
| `CommandError` | 传给外部代理 | 码、载荷 |

`ValidationIssue.ToCommandError()` 做单向转换：线上形状是窄的，内部形状是宽的。

**每条校验问题都必须带修复建议。** 只说哪里错、不说怎么改，等于把问题原样丢回给用户。
这一点对模型同样重要：一个能听懂"该动哪里"的模型可以自己修好，
而只收到"标识重复"的模型只能猜。

修复建议目前是一句给人看的话，不是可直接执行的修补指令。
做成机器可执行的形态要等校验工具对外暴露时再定——现在猜一个结构，
很可能与那时真正需要的形状对不上。

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
