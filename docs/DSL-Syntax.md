# DSL 语法

## 它为什么存在

方案里 DSL 是"LLM 纯文本模式的入口"，人工不直接接触。它的对手是 Mermaid。

**先说清楚一件事：DSL 在省 token 上没有明显优势。**

| | Mermaid | 本 DSL |
|---|---|---|
| 一个带形状的节点 | `A([开始])` | `start "开始" shape=stadium` |
| 一条带标签的边 | `A -->|否| B` | `check -> fail "否"` |

简单图上两者相当，甚至 Mermaid 更短——它把节点定义压进了边里。
所以"省 token"**不是**造 DSL 的理由。

真正的理由是另外四条：

1. **只有一种写法。** Mermaid 有大量方言和等价写法，同一个图形有很多种文本，
   解析器要考虑的分支数不胜数。本 DSL 每种意图只有一种写法。
2. **布局意图是一等公民。** 同层、层内次序、对齐、固定位置，Mermaid 一个都表达不了，
   只能在导出后手工调整。这些恰恰是"人工拖动作为约束反馈"的实现基础。
3. **样式用令牌。** `style=danger` 而不是原始颜色值。令牌表由调色板提供，
   模型不需要知道 `#cc0000` 这种细节，换了主题也不用改文本。
4. **解析器可以按设计做到容错。** 语法是我们自己的，哪里宽松、哪里报错由我们定，
   而不是被别人的方言牵着走。

第 2 条是最有分量的：**Mermaid 无法表达的那些意图，正是这个软件的核心功能。**

## 设计原则

1. **行式**：每行一个声明，没有跨行的表达式。
2. **标识与显示文本分离**：标识是 ASCII 短名（解析器与命令层用它），
   显示文本可以是任何语言、任何字符。
3. **属性统一用 `key=value`**：要加新属性不必发明新语法。
4. **边界显式**：分组用 `end` 收尾，不依赖缩进正确。缩进只是为了可读。
5. **未声明即隐式创建**：节点在首次被引用时自动出现，省 token 且不要求声明顺序。
6. **注释以 `#` 开头**。

## 完整语法

### 版本声明（可选）

```
dsl 1
```

缺省视为当前版本。写了但版本不认识时，解析器记一条 issue 但**不报错**——
这样旧文件在新版本里仍然能打开并给出可用的结果。

### 图类型与方向

```
kind flowchart        # flowchart | flow | block | state，缺省 flowchart
direction LR          # TD | TB | BT | LR | RL，缺省 TD
```

`direction` 是方向的唯一来源，布局引擎只从这里读。

### 节点

```
标识 ["显示文本"] [属性...]
```

```dsl
start   "开始"     shape=stadium
check   "校验"     shape=diamond
fail    "失败"     style=danger
db      "数据库"   shape=cylinder
tmp                # 未给显示文本，用标识本身
```

显示文本可以省略。省略时用标识作为显示文本。

可用属性：

| 属性 | 取值 | 说明 |
|---|---|---|
| `shape` | `rect` `rounded` `stadium` `diamond` `circle` `hexagon` `parallelogram` `cylinder` | 缺省 `rect` |
| `style` | 调色板令牌名，如 `primary` `success` `warning` `danger` `muted` | 缺省无 |
| `layer` | 图层名 | 缺省无 |
| `desc` | 引号包裹的说明 | 不参与渲染 |
| `ports` | 见下 | 缺省无 |

### 边

```
[标识:] 从 -> 到 ["标签"] [属性...]
```

```dsl
start -> check
check -> fail  "否"
check -> pass  "是"
fail  -> check "重试" line=dashed
```

**两端节点不需要预先声明**，首次出现时自动创建（矩形、标签等于标识）。

同一对节点之间有多条边时，可以给边命名加以区分：

```dsl
e1: a -> b "第一条"
e2: a -> b "第二条"
```

不给标识时解析器自动生成。

可用属性：

| 属性 | 取值 | 缺省 |
|---|---|---|
| `line` | `solid` `dashed` `dotted` | `solid` |
| `arrow` | `none` `arrow` `open` `circle` `cross` | `arrow` |
| `style` | 调色板令牌名 | 无 |

用 `--` 代替 `->` 表示无箭头的连线，等价于 `arrow=none`。

### 分组

三种分组，语义不同：

| 关键字 | 对应 | 含义 |
|---|---|---|
| `group` | 分组 | 把若干节点圈在一起 |
| `lane` | 泳道 | 按责任方划分的条带 |
| `subflow` | 子流程 | 可以折叠的嵌套流程 |

```
group 标识 "显示文本"
  ...成员声明...
end
```

```dsl
group backend "后端"
  api "API 服务"
  db  "数据库" shape=cylinder
  api -> db
end
```

- 分组块内的节点声明自动归属该分组。
- **边不受分组影响**，可以写在任何位置并引用任何节点，包括跨组的。
- 分组可以嵌套，缩进只为了可读，边界由 `end` 决定。

### 布局意图

这几条是 Mermaid 表达不了的，也是 DSL 的主要理由。

```dsl
same-rank pass, fail              # 强制这几个节点同处一层
order check: pass, fail           # 指定判定节点出边的层内次序
align pass, fail                  # 让这几个节点对齐
place fail right-of pass          # 相对位置
pin fail at 640, 320              # 固定坐标，人工拖动后写回这里

node-spacing 40                   # 同层节点间距，缺省 40
layer-spacing 70                  # 层间距，缺省 70
```

`same-rank` 与 `pin` 的区别：同层只约束"在不在同一层"，固定坐标还约束具体位置。
前者让引擎自己算坐标，后者完全接管。

### 端口

```
节点标识 "显示文本" ports=端口名:方位,端口名:方位
```

方位取 `left` `right` `top` `bottom`。

```dsl
api "API" ports=req:left,resp:right
web -> api.req "请求"
```

边上用 `节点.端口` 指定连到哪个端口。省略端口时由布局引擎自动选边。

### 注释

```
# 这是注释，整行忽略
```

## 完整示例

```dsl
dsl 1
kind flowchart
direction LR

start   "开始"   shape=stadium
input   "输入账号密码"
check   "校验"   shape=diamond
ok      "成功"   style=success
bad     "失败"   style=danger

start -> input
input -> check
check -> bad  "否"
check -> ok   "是"
bad   -> input "重试"

same-rank bad, ok
pin check at 420, 200
```

## 与 Mermaid 的表达能力对照

| 意图 | Mermaid | 本 DSL |
|---|---|---|
| 节点与边 | 有 | 有 |
| 形状 | 有 | 有 |
| 分组 | `subgraph` | `group` / `lane` / `subflow` |
| 样式 | 内联样式字符串或 classDef | 调色板令牌 |
| 同层约束 | **无** | `same-rank` |
| 层内次序 | **无** | `order` |
| 对齐 | **无** | `align` |
| 固定位置 | **无** | `pin` |
| 端口 | **无** | `ports` 与 `节点.端口` |
| 相对位置 | **无** | `place` |

下表中标"无"的三项，是人工微调在重排后被保住的前提。没有它们，
用户每次拖动都会在下一次重排时白做。

## 待定

以下几项在语法定稿前需要明确，它们会影响解析器实现：

| 项 | 现状 |
|---|---|
| 属性值的引号规则 | 当前约定：显示文本与 `desc` 必须加引号，属性值不加引号。属性值里要出现空格时怎么办尚未定 |
| 转义 | 显示文本里的引号与换行如何转义尚未定 |
| 数值带单位 | `pin` 的坐标当前是无单位数字，是否需要支持百分比未定 |
| 错误恢复粒度 | 一行解析失败时，是跳过该行继续，还是整份文件拒绝，尚未定 |
