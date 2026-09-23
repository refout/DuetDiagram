# 错误码

来源：`DuetDiagram.Core/Commands/ErrorCodes.cs`。GUI 处理方式见方案 §9.6。

## 命令错误（由 `Fail` / `Conflict` 产生）

| 码 | 类型 | 触发 | GUI 处理 |
|---|---|---|---|
| `DUPLICATE_ID` | 校验失败 | 节点或边 id 已存在 | 高亮冲突 ID |
| `EDGE_TARGET_MISSING` | 校验失败 | 边的 `to` **节点或组合**不存在 | 提示「是否创建？」 |
| `EDGE_SOURCE_MISSING` | 校验失败 | 边的 `from` **节点或组合**不存在 | 提示「是否创建？」 |
| `GROUP_MEMBER_MISSING` | 校验失败 | 组合的成员不存在 | 提示「是否创建？」 |
| `GROUP_CYCLE` | 校验失败 | 组合的父子关系成环 | 高亮循环 |
| `VERSION_CONFLICT` | 并发冲突 | MCP 客户端版本落后 | 弹可视化 diff 对话框 |
| `INVALID_EXPECTED_VERSION` | 参数错误 | 客户端版本 > 服务端版本 | 状态栏提示「内部错误，已记录日志」 |
| `EXPECTED_VERSION_REQUIRED` | 参数错误 | MCP 模式未携带 `VersionCheckRequest` | 状态栏提示 |
| `LAYOUT_ALL_LEVELS_TIMEOUT` | 布局失败 | 各级降级全部失败 | 状态栏红色提示 + 三选项，画面保留上一次成功的结果 |
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
| `EDGE_PORT_ON_COMPOSITE` | 校验失败 | 边的一端是组合，却指定了端口。**组合没有端口** | 高亮该边 |
| `MEMBERSHIP_MISMATCH` | 校验失败 | 组合的成员列表与成员的父级互相矛盾 | 高亮两者 |
| `PARENT_MISSING` | 校验失败 | 节点或组合的父级指向不存在的组合 | 提示「是否创建？」 |
| `TAG_MEMBER_MISSING` | 校验失败 | 标签的成员不存在 | 高亮该标签 |
| `ACTION_TARGET_MISSING` | 校验失败 | 动作的目标不存在 | 高亮该动作 |
| `LAYOUT_NODE_MISSING` | 校验失败 | 四类布局约束引用的节点不存在 | 高亮该约束 |
| `LAYOUT_ORDER_EDGE_MISSING` | 校验失败 | 层内次序引用的边不存在，或不是主语节点的出边 | 高亮该约束 |
| `LAYOUT_CONSTRAINT_INVALID` | 校验失败 | 约束本身不成立：成员不够、主语缺失或多余、成员重复 | 状态栏一句话 |
| `LAYOUT_CONSTRAINT_MISSING` | 校验失败 | 要删除的布局约束不存在 | 状态栏一句话 |
| `PALETTE_ENTRY_MISSING` | 校验失败 | 要改或要删的调色板条目不存在 | 状态栏一句话 |
| `PALETTE_ENTRY_IN_USE` | 校验失败 | 这条调色板条目还被样式令牌引用着，删除会让那些元素悄悄变样 | 高亮引用它的元素 |
| `COMPOSITE_MISSING` | 校验失败 | 要操作的组合不存在 | 状态栏一句话 |
| `COMPOSITE_TOO_DEEP` | 校验失败 | 组合的嵌套深度超过上限 | 状态栏一句话 |
| `LAYER_MISSING` | 校验失败 | 要操作的图层不存在 | 状态栏一句话 |
| `PAGE_MISSING` | 校验失败 | 要操作的页面不存在 | 状态栏一句话 |
| `PAGE_REQUIRED` | 校验失败 | 要删的是最后一页，文档至少要留一页 | 状态栏一句话 |
| `TAG_MISSING` | 校验失败 | 要操作的标签不存在 | 状态栏一句话 |
| `ACTION_MISSING` | 校验失败 | 要操作的动作不存在 | 状态栏一句话 |
| `DOCUMENT_READ_ONLY` | 所有权 | 另一个进程正拿着这份文档，这一份只读 | 状态栏灰显一句话 |

`PAGE_REQUIRED` 与「页面不存在」分开：那一种是"这个标识写错了或已经没了"，处置是换一个标识；
这一种是"这一页确实在，但删掉之后文档就没有页了"，处置是先建一页再删。
渲染层拿到空页面集合时该画什么没有定义，所以这条限制放在命令层挡住，
而不是留给渲染层去兜底。

`PALETTE_ENTRY_IN_USE` 与删边那一处留下的悬空引用**刻意不同**：删边之后约束指向一条
不存在的边，这件事由整体校验器报 `LAYOUT_ORDER_EDGE_MISSING`，所以命令层留着不管；
而删掉一个还在被引用的调色板条目，渲染层只是静默退回元素自己的样式，
**没有任何东西会报**，用户看到的是一批节点悄悄换了颜色。判据是「这件事有没有第二个地方能看见」。
引用者有三条路径：节点与边上的样式令牌，以及标签的颜色——标签色取的也是令牌名。
删标签与删动作则**没有**这个问题：那两样与元素的关系是单向的，元素上没有回指字段，
删掉之后不会留下任何悬空引用，所以不需要挡住。

`DOCUMENT_READ_ONLY` 与「版本冲突」分开：版本冲突是"你手上的副本旧了，同步一下再来"，
处置是同步；这个码是"这一次操作在这份文档上根本不允许"，处置是去改那一份。
报成版本冲突的话，用户会一遍遍重试，而重试永远不会成功。

后两个码与「引用的元素不存在」分开：那两种说明图里少了东西，处置是补上缺的元素；
这两个说明这条约束从一开始就描述不出任何东西（或已经不在了），处置是改这条约束的写法。

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

## 界面呈现（机器可读）

界面按这张表决定一个错误码怎么呈现。表与 `DuetDiagram.App/Services/ErrorPresenterTable.cs`
**逐行一致**，门禁是 `Category=ErrorPresentation`：拿错误码清单逐个查这张表，漏一个就红。

| 码 | 呈现 |
|---|---|
| `DUPLICATE_ID` | `HighlightTargets` |
| `EDGE_TARGET_MISSING` | `PromptCreate` |
| `EDGE_SOURCE_MISSING` | `PromptCreate` |
| `GROUP_MEMBER_MISSING` | `PromptCreate` |
| `GROUP_CYCLE` | `HighlightTargets` |
| `VERSION_CONFLICT` | `VersionDiffDialog` |
| `INVALID_EXPECTED_VERSION` | `StatusBar` |
| `EXPECTED_VERSION_REQUIRED` | `StatusBar` |
| `LAYOUT_ALL_LEVELS_TIMEOUT` | `LayoutFailureDialog` |
| `MCP_UNAUTHORIZED` | `AuthFailed` |
| `MCP_RATE_LIMITED` | `StatusBar` |
| `MCP_FORBIDDEN` | `StatusBar` |
| `MCP_PATH_ESCAPED` | `StatusBar` |
| `MCP_TIMEOUT` | `StatusBar` |
| `INTERNAL_ERROR` | `StatusBar` |
| `NODE_MISSING` | `StatusBar` |
| `EDGE_MISSING` | `StatusBar` |
| `INVALID_ID` | `StatusBar` |
| `FIELD_UNKNOWN` | `StatusBar` |
| `FIELD_VALUE_INVALID` | `StatusBar` |
| `EDGE_PORT_MISSING` | `HighlightTargets` |
| `EDGE_PORT_ON_COMPOSITE` | `HighlightTargets` |
| `LAYOUT_NODE_MISSING` | `HighlightTargets` |
| `LAYOUT_ORDER_EDGE_MISSING` | `HighlightTargets` |
| `MEMBERSHIP_MISMATCH` | `HighlightTargets` |
| `PARENT_MISSING` | `PromptCreate` |
| `TAG_MEMBER_MISSING` | `HighlightTargets` |
| `ACTION_TARGET_MISSING` | `HighlightTargets` |
| `LAYOUT_CONSTRAINT_INVALID` | `StatusBar` |
| `LAYOUT_CONSTRAINT_MISSING` | `StatusBar` |
| `PALETTE_ENTRY_MISSING` | `StatusBar` |
| `PALETTE_ENTRY_IN_USE` | `HighlightTargets` |
| `COMPOSITE_MISSING` | `StatusBar` |
| `COMPOSITE_TOO_DEEP` | `StatusBar` |
| `LAYER_MISSING` | `StatusBar` |
| `PAGE_MISSING` | `StatusBar` |
| `PAGE_REQUIRED` | `StatusBar` |
| `TAG_MISSING` | `StatusBar` |
| `ACTION_MISSING` | `StatusBar` |
| `DOCUMENT_READ_ONLY` | `StatusBarMuted` |

呈现方式只有这几种，因为用户能做的事只有这几种：

| 呈现 | 用户看到什么 |
|---|---|
| HighlightTargets | 相关元素被标出来，不弹窗 |
| PromptCreate | 被问一句「要不要创建」，由用户决定 |
| VersionDiffDialog | 弹差异对话框，看得到冲突在哪 |
| StatusBar | 状态栏给一句话 |
| StatusBarMuted | 状态栏灰显一句话，**不弹窗**。无操作的失败走这一档 |
| AuthFailed | 提示认证失败 |
| LayoutFailureDialog | 弹布局失败提示，给重试 / 手动布局 / 简化图三个选项 |

呈现方式**由这张表决定，不在各处就地判断**。就地判断的结果是同一个错误码在两个入口
给出两种呈现，而用户以为遇到的是两个问题。

`LAYOUT_ALL_LEVELS_TIMEOUT` 的呈现里还带一句补充：试了几级，以及这一轮降级丢掉了哪些约束。
去掉哪一条是用户的判断，界面只负责把候选摆出来。

## 修复建议（机器可读）

模型拿到一次失败之后要能自己改好，所以回灌给它的内容里除了错误码，还要有
「哪个参数错了」与「下一步怎么改」。这两样来自下面这张表，与
`DuetDiagram.Llm/Loop/RepairHints.cs` **逐行一致**，门禁是 `Category=RepairHint`：
拿两套错误码逐个查这张表，漏一个就红。

这张表比上面那张呈现表多覆盖一套码：命令层的前置检查（`ErrorCodes`）**和**
工具层的参数校验（`ToolErrorCodes`）。回灌给模型的失败两条来源都会出现，
而界面只看得到前一套——这是两张表唯一的结构差异。

「可用取值」不在这张表里：可用的动作名、样式令牌、字段名、已登记的工具名都随文档与工具表变，
静态表里写不出来，写死了就是错的。它们由失败现场填进错误自己的期望字段。

| 码 | 出错参数 | 修复建议 |
|---|---|---|
| `DUPLICATE_ID` | `id` | 换一个没被占用的标识；要保留同名元素的话，先读一次图看现在有哪些标识。 |
| `EDGE_TARGET_MISSING` | `to` | 终点不存在。先读一次图确认现有标识，或者先把终点建出来再连这条线。 |
| `EDGE_SOURCE_MISSING` | `from` | 起点不存在。先读一次图确认现有标识，或者先把起点建出来再连这条线。 |
| `GROUP_MEMBER_MISSING` | `memberIds` | 成员标识写错了或已经没了。读一次图核对之后重发，成员列表里只留确实存在的那些。 |
| `COMPOSITE_MISSING` | `id` | 这个组合已经不在了。读一次图看现有组合，或者先建一个再操作。 |
| `GROUP_CYCLE` | `targetId` | 目标组合在这个元素的子树里，移进去会成环。换一个不在它下面的组合，或者先把中间那一层解散。 |
| `COMPOSITE_TOO_DEEP` | `targetId` | 嵌套已经到上限了。别再往里套，或者先把中间那几层解散再套。 |
| `LAYER_MISSING` | `id` | 图层标识写错了或已经没了。读一次图看现有图层，或者先建一个。 |
| `PAGE_MISSING` | `id` | 页面标识写错了或已经没了。读一次图看现有页面，或者先建一页。 |
| `PAGE_REQUIRED` | `id` | 这是最后一页，删了文档就没有页了。先建一页，再删这一页。 |
| `TAG_MISSING` | `id` | 标签标识写错了或已经没了。读一次图看现有标签，或者先建一个。 |
| `ACTION_MISSING` | `id` | 动作标识写错了或已经没了。读一次图看现有动作，或者先建一个。 |
| `VERSION_CONFLICT` | — | 手上的副本旧了。先读一次图拿到当前版本号，再照着重发这一次改动。 |
| `INVALID_EXPECTED_VERSION` | — | 报的版本号比服务端还新，是参数写错了。重新读一次图拿真实版本号，不要照着上一次的数报。 |
| `EXPECTED_VERSION_REQUIRED` | — | 这条通路要求带上版本号。先读一次图，把读到的版本号带上来再发。 |
| `LAYOUT_ALL_LEVELS_TIMEOUT` | — | 布局算不出来。减少一些约束或者把图拆小；改布局参数重试之前，先想清楚是哪一条约束让它算不动。 |
| `MCP_UNAUTHORIZED` | — | 凭据没通过。这不是改参数能解决的，去换一份凭据或者找配置这件事的人。 |
| `MCP_RATE_LIMITED` | — | 请求太密了。等一会儿再发，别立刻重试——立刻重试只会再撞一次。 |
| `MCP_FORBIDDEN` | — | 凭据的权限档不够做这件事。换成能改这份图的凭据，或者把要做的事改成只读的那些。 |
| `MCP_PATH_ESCAPED` | — | 这个路径落在被限定的工作区之外，换凭据也打不开。把文件放进工作区，或者换一个在里面的路径。 |
| `MCP_TIMEOUT` | — | 这一次调用超时了，而文档一个字节都没改。把这一次的活拆小一点再发，或者过一会儿重试。 |
| `INTERNAL_ERROR` | — | 服务端内部出错。原样重试一次；再失败就别再试，把这一次调用报给人。 |
| `NODE_MISSING` | `id` | 节点标识写错了或已经没了。先读一次图拿到现有节点标识，再照着重发。 |
| `EDGE_MISSING` | `id` | 边标识写错了或已经没了。先读一次图拿到现有边标识，再照着重发。 |
| `INVALID_ID` | `id` | 标识不能是空的。给它一个小写字母、数字与连字符组成的名字。 |
| `FIELD_UNKNOWN` | `field` | 这个字段没登记过。先读一次图看这份文档认得的字段名，或者换一个字段。 |
| `FIELD_VALUE_INVALID` | `value` | 这个值的写法不对。按这个字段要的类型重写一次，字段名本身是对的、不用换。 |
| `EDGE_PORT_MISSING` | `from` | 这个节点上没有这个端口。去掉端点后面的端口名，或者换一个这个节点真有的端口。 |
| `EDGE_PORT_ON_COMPOSITE` | `from` | 组合没有端口。端点写成组合标识本身，不要在后面带端口名。 |
| `LAYOUT_NODE_MISSING` | `memberIds` | 这条约束引用的节点不存在。先读一次图核对成员标识，再重发这条约束。 |
| `LAYOUT_ORDER_EDGE_MISSING` | `memberIds` | 次序引用的边不存在，或者不是主语节点的出边。换一条主语节点真的有的出边。 |
| `LAYOUT_CONSTRAINT_INVALID` | `kind` | 这条约束本身不成立。核对种类与成员：同层与对齐要两个以上节点，层内次序要一个主语加它的出边。 |
| `LAYOUT_CONSTRAINT_MISSING` | `kind` | 要删的约束已经不在了。先读一次图看现在有哪些约束，别重复删同一条。 |
| `MEMBERSHIP_MISMATCH` | `memberIds` | 成员列表与父级对不上。把两边改成一致：要么把成员从列表里去掉，要么把它的父级改成这个组合。 |
| `PARENT_MISSING` | `targetId` | 父级指向的组合不存在。先把那个组合建出来，或者把父级换成一个存在的组合。 |
| `TAG_MEMBER_MISSING` | `memberIds` | 标签里的成员不存在。读一次图核对成员标识之后重发。 |
| `ACTION_TARGET_MISSING` | `targetId` | 动作的目标不存在。先把目标建出来，或者把目标换成一个存在的元素。 |
| `PALETTE_ENTRY_MISSING` | `token` | 这个样式令牌不在调色板里。先读一次图看现有令牌，或者先建一个。 |
| `PALETTE_ENTRY_IN_USE` | `token` | 还有元素在用这个令牌。先把引用它的节点、边与标签改成别的令牌，再删它。 |
| `DOCUMENT_READ_ONLY` | — | 这份文档正被另一个进程编辑着，这一份只读。去改那一份，或者等对方放开再来。 |
| `TOOL_UNKNOWN` | — | 工具名不在表里。换成已登记的那几个工具名，别自己拼一个。 |
| `TOOL_ARGUMENT_MISSING` | — | 必填参数没给。按报出来的那个参数名把它补上再发，其余参数不用动。 |
| `TOOL_ARGUMENT_INVALID` | — | 参数值不满足它的约束。按期望那一栏给的形式重写这个参数；形式对不上时这一次调用什么都没改。 |
| `TOOL_ARGUMENT_UNKNOWN` | — | 参数名不认得。把它删掉，或者换成期望那一栏列出的可用参数名。 |
| `TOOL_NOT_SUPPORTED` | — | 这条路还没接上，重试多少次都一样。换成现在支持的做法，或者把缺的那一样报给人。 |
| `TOOL_RETRY_EXHAUSTED` | — | 同一个调用已经连着试到上限了，别再原样重试。换一个做法，或者把这件事报给人。 |

出错参数为空的那几行不是漏了：内部错误、版本冲突、只读、以及工具层那五条参数校验
都不是某一个具体参数写错了。硬凑一个参数名的话，模型会去改一个根本不相干的字段，
而它改完之后拿到的还是同一个错误。

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
