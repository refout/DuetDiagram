# 解析统计（不盲，可机械核对）

语料 150 份（3 组），模型 GLM-5.3-Flash。

这一份是**真实测量**，不是模拟：它由 `tools/CompareHarness` 用产品里那两个解析器
直接跑出来。四项指标里只有「解析错误率」不需要人判，就是这一项。

口径见 `docs/Compare-Criteria.md`：语法拒绝＝解析器报错、拿不到任何结构。
本表把「图类型不是流程图」单独列出来，因为它与语法写坏是两种毛病——
前者要改提示词，后者要改语法。

| 组 | 干净 | 有诊断但有结构 | 语法拒绝 | 图类型不对 | 拒绝合计 | 解析错误率 |
|---|---:|---:|---:|---:|---:|---:|
| `a-bare` | 47 | 0 | 0 | 3 | 3 | 6.0 % |
| `b-documented` | 49 | 1 | 0 | 0 | 0 | 0.0 % |
| `c-dsl` | 50 | 0 | 0 | 0 | 0 | 0.0 % |

## 判定门第四条：解析错误率

C 组 0/50（0.0 %），B 组 0/50（0.0 %）。

B 组一个拒绝都没有，相对下降无从谈起——按口径这一条**不达标**。
阈值写的是「低至少 50%」，分母为零时它没有意义，不能算通过。

## 逐条

| 组 | 提示词 | 结局 | 节点 | 分组 | 边 | 诊断 | 端点是分组的边 |
|---|---|---|---:|---:|---:|---:|---|
| `a-bare` | C01 | Clean | 11 | 3 | 16 | 0 |  |
| `a-bare` | C02 | Unsupported | 0 | 0 | 0 | 1 |  |
| `a-bare` | C03 | Unsupported | 0 | 0 | 0 | 1 |  |
| `a-bare` | C04 | Clean | 9 | 5 | 4 | 0 | ODS -> DWD；DWD -> DWS；DWS -> ADS |
| `a-bare` | C05 | Clean | 16 | 0 | 20 | 0 |  |
| `a-bare` | C06 | Clean | 10 | 5 | 13 | 0 |  |
| `a-bare` | C07 | Clean | 13 | 4 | 23 | 0 | ING -> MC；APP -> MC；DATA -> MC；ING -> LA；APP -> LA；DATA -> LA |
| `a-bare` | C08 | Clean | 15 | 5 | 16 | 0 |  |
| `a-bare` | C09 | Clean | 10 | 2 | 10 | 0 |  |
| `a-bare` | C10 | Clean | 27 | 4 | 32 | 0 | W1 -> SHARED；W2 -> SHARED；W3 -> SHARED |
| `a-bare` | M01 | Unsupported | 0 | 0 | 0 | 1 |  |
| `a-bare` | M02 | Clean | 9 | 0 | 12 | 0 |  |
| `a-bare` | M03 | Clean | 12 | 0 | 12 | 0 |  |
| `a-bare` | M04 | Clean | 13 | 0 | 15 | 0 |  |
| `a-bare` | M05 | Clean | 13 | 0 | 13 | 0 |  |
| `a-bare` | M06 | Clean | 12 | 0 | 13 | 0 |  |
| `a-bare` | M07 | Clean | 7 | 0 | 8 | 0 |  |
| `a-bare` | M08 | Clean | 13 | 0 | 16 | 0 |  |
| `a-bare` | M09 | Clean | 10 | 0 | 12 | 0 |  |
| `a-bare` | M10 | Clean | 18 | 0 | 21 | 0 |  |
| `a-bare` | M11 | Clean | 15 | 0 | 19 | 0 |  |
| `a-bare` | M12 | Clean | 12 | 0 | 15 | 0 |  |
| `a-bare` | M13 | Clean | 10 | 0 | 9 | 0 |  |
| `a-bare` | M14 | Clean | 11 | 0 | 11 | 0 |  |
| `a-bare` | M15 | Clean | 9 | 2 | 9 | 0 |  |
| `a-bare` | M16 | Clean | 7 | 0 | 7 | 0 |  |
| `a-bare` | M17 | Clean | 16 | 0 | 19 | 0 |  |
| `a-bare` | M18 | Clean | 13 | 0 | 15 | 0 |  |
| `a-bare` | M19 | Clean | 13 | 0 | 15 | 0 |  |
| `a-bare` | M20 | Clean | 10 | 0 | 9 | 0 |  |
| `a-bare` | S01 | Clean | 6 | 0 | 6 | 0 |  |
| `a-bare` | S02 | Clean | 7 | 0 | 6 | 0 |  |
| `a-bare` | S03 | Clean | 9 | 0 | 10 | 0 |  |
| `a-bare` | S04 | Clean | 6 | 0 | 6 | 0 |  |
| `a-bare` | S05 | Clean | 7 | 0 | 7 | 0 |  |
| `a-bare` | S06 | Clean | 6 | 0 | 5 | 0 |  |
| `a-bare` | S07 | Clean | 9 | 0 | 10 | 0 |  |
| `a-bare` | S08 | Clean | 8 | 0 | 8 | 0 |  |
| `a-bare` | S09 | Clean | 7 | 0 | 7 | 0 |  |
| `a-bare` | S10 | Clean | 5 | 0 | 4 | 0 |  |
| `a-bare` | S11 | Clean | 5 | 0 | 5 | 0 |  |
| `a-bare` | S12 | Clean | 7 | 0 | 6 | 0 |  |
| `a-bare` | S13 | Clean | 4 | 0 | 3 | 0 |  |
| `a-bare` | S14 | Clean | 7 | 0 | 6 | 0 |  |
| `a-bare` | S15 | Clean | 6 | 0 | 5 | 0 |  |
| `a-bare` | S16 | Clean | 5 | 0 | 4 | 0 |  |
| `a-bare` | S17 | Clean | 6 | 0 | 6 | 0 |  |
| `a-bare` | S18 | Clean | 7 | 0 | 8 | 0 |  |
| `a-bare` | S19 | Clean | 5 | 0 | 5 | 0 |  |
| `a-bare` | S20 | Clean | 6 | 0 | 5 | 0 |  |
| `b-documented` | C01 | Clean | 12 | 3 | 17 | 0 |  |
| `b-documented` | C02 | Clean | 15 | 5 | 16 | 0 |  |
| `b-documented` | C03 | Clean | 9 | 2 | 10 | 0 |  |
| `b-documented` | C04 | Clean | 9 | 6 | 4 | 0 | ods -> dwd；dwd -> dws；dws -> ads |
| `b-documented` | C05 | Clean | 16 | 2 | 20 | 0 |  |
| `b-documented` | C06 | Clean | 10 | 5 | 13 | 0 | L1 -> L3；L3 -> L5 |
| `b-documented` | C07 | Clean | 12 | 4 | 23 | 0 | MET -> NS_INGRESS；MET -> NS_APP；MET -> NS_DATA；LOG -> NS_INGRESS；LOG -> NS_APP；LOG -> NS_DATA |
| `b-documented` | C08 | Clean | 21 | 5 | 22 | 0 |  |
| `b-documented` | C09 | Clean | 10 | 2 | 10 | 0 |  |
| `b-documented` | C10 | Clean | 22 | 4 | 28 | 0 | W1 -> COMMON；W2 -> COMMON；W3 -> COMMON |
| `b-documented` | M01 | Clean | 7 | 0 | 6 | 0 |  |
| `b-documented` | M02 | Clean | 10 | 0 | 13 | 0 |  |
| `b-documented` | M03 | Clean | 14 | 2 | 13 | 0 |  |
| `b-documented` | M04 | Clean | 12 | 0 | 14 | 0 |  |
| `b-documented` | M05 | Clean | 13 | 0 | 13 | 0 |  |
| `b-documented` | M06 | Clean | 15 | 0 | 16 | 0 |  |
| `b-documented` | M07 | Clean | 8 | 0 | 10 | 0 |  |
| `b-documented` | M08 | Clean | 14 | 2 | 17 | 0 |  |
| `b-documented` | M09 | Clean | 11 | 0 | 12 | 0 |  |
| `b-documented` | M10 | Clean | 14 | 0 | 16 | 0 |  |
| `b-documented` | M11 | Clean | 13 | 0 | 17 | 0 |  |
| `b-documented` | M12 | Clean | 11 | 0 | 14 | 0 |  |
| `b-documented` | M13 | Clean | 12 | 0 | 13 | 0 |  |
| `b-documented` | M14 | Clean | 11 | 1 | 12 | 0 |  |
| `b-documented` | M15 | Clean | 11 | 0 | 12 | 0 |  |
| `b-documented` | M16 | Clean | 9 | 3 | 10 | 0 |  |
| `b-documented` | M17 | Clean | 16 | 1 | 18 | 0 |  |
| `b-documented` | M18 | Clean | 13 | 0 | 15 | 0 |  |
| `b-documented` | M19 | Clean | 11 | 0 | 13 | 0 |  |
| `b-documented` | M20 | Clean | 14 | 0 | 14 | 0 |  |
| `b-documented` | S01 | Clean | 6 | 0 | 6 | 0 |  |
| `b-documented` | S02 | Clean | 6 | 0 | 5 | 0 |  |
| `b-documented` | S03 | Clean | 7 | 0 | 6 | 0 |  |
| `b-documented` | S04 | Partial | 8 | 0 | 7 | 1 |  |
| `b-documented` | S05 | Clean | 5 | 0 | 4 | 0 |  |
| `b-documented` | S06 | Clean | 6 | 0 | 5 | 0 |  |
| `b-documented` | S07 | Clean | 10 | 0 | 10 | 0 |  |
| `b-documented` | S08 | Clean | 7 | 0 | 7 | 0 |  |
| `b-documented` | S09 | Clean | 6 | 0 | 6 | 0 |  |
| `b-documented` | S10 | Clean | 5 | 0 | 4 | 0 |  |
| `b-documented` | S11 | Clean | 7 | 0 | 7 | 0 |  |
| `b-documented` | S12 | Clean | 7 | 0 | 7 | 0 |  |
| `b-documented` | S13 | Clean | 4 | 0 | 3 | 0 |  |
| `b-documented` | S14 | Clean | 7 | 0 | 6 | 0 |  |
| `b-documented` | S15 | Clean | 6 | 0 | 5 | 0 |  |
| `b-documented` | S16 | Clean | 7 | 0 | 6 | 0 |  |
| `b-documented` | S17 | Clean | 8 | 0 | 9 | 0 |  |
| `b-documented` | S18 | Clean | 6 | 0 | 7 | 0 |  |
| `b-documented` | S19 | Clean | 7 | 0 | 7 | 0 |  |
| `b-documented` | S20 | Clean | 6 | 0 | 5 | 0 |  |
| `c-dsl` | C01 | Clean | 11 | 3 | 16 | 0 |  |
| `c-dsl` | C02 | Clean | 15 | 5 | 17 | 0 | payStart -> pay；pay -> payCb |
| `c-dsl` | C03 | Clean | 9 | 2 | 10 | 0 |  |
| `c-dsl` | C04 | Clean | 10 | 4 | 13 | 0 |  |
| `c-dsl` | C05 | Clean | 13 | 2 | 19 | 0 |  |
| `c-dsl` | C06 | Clean | 10 | 5 | 11 | 0 |  |
| `c-dsl` | C07 | Clean | 12 | 4 | 20 | 0 |  |
| `c-dsl` | C08 | Clean | 16 | 5 | 16 | 0 |  |
| `c-dsl` | C09 | Clean | 10 | 2 | 10 | 0 |  |
| `c-dsl` | C10 | Clean | 18 | 4 | 22 | 0 |  |
| `c-dsl` | M01 | Clean | 6 | 0 | 5 | 0 |  |
| `c-dsl` | M02 | Clean | 7 | 0 | 10 | 0 |  |
| `c-dsl` | M03 | Clean | 12 | 2 | 12 | 0 |  |
| `c-dsl` | M04 | Clean | 9 | 0 | 10 | 0 |  |
| `c-dsl` | M05 | Clean | 13 | 0 | 14 | 0 |  |
| `c-dsl` | M06 | Clean | 13 | 0 | 14 | 0 |  |
| `c-dsl` | M07 | Clean | 7 | 0 | 9 | 0 |  |
| `c-dsl` | M08 | Clean | 13 | 2 | 15 | 0 |  |
| `c-dsl` | M09 | Clean | 9 | 0 | 10 | 0 |  |
| `c-dsl` | M10 | Clean | 14 | 0 | 16 | 0 |  |
| `c-dsl` | M11 | Clean | 12 | 0 | 14 | 0 |  |
| `c-dsl` | M12 | Clean | 9 | 0 | 11 | 0 |  |
| `c-dsl` | M13 | Clean | 9 | 1 | 8 | 0 |  |
| `c-dsl` | M14 | Clean | 9 | 1 | 9 | 0 |  |
| `c-dsl` | M15 | Clean | 12 | 0 | 13 | 0 |  |
| `c-dsl` | M16 | Clean | 7 | 2 | 8 | 0 |  |
| `c-dsl` | M17 | Clean | 16 | 1 | 18 | 0 |  |
| `c-dsl` | M18 | Clean | 12 | 0 | 14 | 0 |  |
| `c-dsl` | M19 | Clean | 10 | 1 | 11 | 0 |  |
| `c-dsl` | M20 | Clean | 8 | 0 | 7 | 0 |  |
| `c-dsl` | S01 | Clean | 5 | 0 | 5 | 0 |  |
| `c-dsl` | S02 | Clean | 6 | 0 | 5 | 0 |  |
| `c-dsl` | S03 | Clean | 5 | 0 | 4 | 0 |  |
| `c-dsl` | S04 | Clean | 7 | 0 | 7 | 0 |  |
| `c-dsl` | S05 | Clean | 5 | 0 | 4 | 0 |  |
| `c-dsl` | S06 | Clean | 8 | 0 | 7 | 0 |  |
| `c-dsl` | S07 | Clean | 8 | 0 | 9 | 0 |  |
| `c-dsl` | S08 | Clean | 7 | 0 | 6 | 0 |  |
| `c-dsl` | S09 | Clean | 7 | 0 | 7 | 0 |  |
| `c-dsl` | S10 | Clean | 5 | 0 | 4 | 0 |  |
| `c-dsl` | S11 | Clean | 7 | 0 | 7 | 0 |  |
| `c-dsl` | S12 | Clean | 5 | 0 | 4 | 0 |  |
| `c-dsl` | S13 | Clean | 4 | 0 | 3 | 0 |  |
| `c-dsl` | S14 | Clean | 6 | 2 | 5 | 0 |  |
| `c-dsl` | S15 | Clean | 6 | 0 | 5 | 0 |  |
| `c-dsl` | S16 | Clean | 5 | 0 | 4 | 0 |  |
| `c-dsl` | S17 | Clean | 6 | 0 | 6 | 0 |  |
| `c-dsl` | S18 | Clean | 5 | 0 | 5 | 0 |  |
| `c-dsl` | S19 | Clean | 7 | 0 | 7 | 0 |  |
| `c-dsl` | S20 | Clean | 6 | 0 | 5 | 0 |  |

## 诊断原文

- `a-bare` / C02（Unsupported）
  - 1:1 Sequence 无法用当前的数据模型表达。
- `a-bare` / C03（Unsupported）
  - 1:1 暂不支持解析 State，IR 里有对应的图类型，只是语法还没做。
- `a-bare` / M01（Unsupported）
  - 1:1 暂不支持解析 State，IR 里有对应的图类型，只是语法还没做。
- `b-documented` / S04（Partial）
  - 5:5 暂不支持 click 指令，已跳过。

## 语义拒绝率：现在算不出来

口径要求把语义拒绝（引用了不存在的节点、分组成环等）与语法拒绝分开报。
**这一半现在做不了**，原因在解析器的抽象语法树上：

- Mermaid 的解析器读到 `A --> B` 会顺手把 A、B 记成节点；DSL 的解析器只记边。
  于是「引用了不存在的节点」在 Mermaid 侧根本无法表达，在 DSL 侧却能看见。
  直接比这一项，比的是两个解析器的宽松程度，不是两种格式。
- 分组的外层字段由解析器的栈赋值，成环写不出来；成员表又是从父级推出来的，
  所以「分组成环」「成员不存在」同样不可表达。

真正的语义校验在 `DiagramValidator` 里，它吃的是 IR。要算这一项，
得先有 Mermaid→IR（P1-09）与 DSL→IR（P1-17）的映射。**在那之前这一项留空**，
填一个估计值进去等于把猜测洗成测量。

能算的语义问题是「边指向分组」——它两边都可表达，见上表的最后一列。
这一列同时是 IR 端点约定那条待决策项的证据（2026-09-21 按「端点可以是组合」定下，见 `docs/IR-Schema.md`）。

**判据是标识字符串**：端点标识与某个分组标识相同就算一条。所以它有一个已知的误报模式——
节点与分组撞名时，指向节点的边会被记成组端点。`c-dsl` 的 C02 正是如此：
模型把泳道与其中一个节点都取名叫 `pay`，而那条边指的是节点。
Mermaid 侧的条目才是真正的子图端点（`ODS --> DWD` 那种分层流向的写法）；
DSL 侧的条目是撞名，它本身是真问题，但属 P1-17 的 `group-and-node-share-a-name`，不该算进这一列。

## 装置自身的两处缝

跑通之后才看得见的，记下来免得日后当成格式的差别：

**一、B 组的语法参考承诺了解析器不接受的写法。**
`arms/b-documented.md` 里写了 `linkStyle`，而解析器对它是「暂不支持，已跳过」并记一条诊断。
文档与解析器对不上，受罚的是模型。这次**没有被触发**——150 份里只有 `b-documented/S04`
用了一条 `click`（文档里根本没写，是模型自己的 Mermaid 常识），所以实际影响为零。
修法是把 `linkStyle` 从参考里删掉，但改参考就要整组重新生成语料，
为一条没有触发的缝付这个代价不值。**留作已知缺陷**。

**二、两种格式的抽象语法树形状不同。**
隐式节点、嵌套分组、边指向分组这三处两边不一样，已在 `Structure.cs` 里抹平，
抹平的方向写在那个文件的注释里。抹平是手工做的，**它是这个装置里最该被复查的一段**。

## 被截断的响应

没有。
