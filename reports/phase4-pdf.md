# PDF 导出选型报告

P4-18 的产物。方案 §2 把 PDF 导出标成「QuestPDF / PdfSharpCore / 位图嵌入，Phase 4 验证」——
也就是说主选没定。这一份把四条判据逐条核一遍，给出结论与理由。

**结论：用 SkiaSharp 自带的 PDF 写入器。不引入新依赖。**

三条判据（原生编译、许可证、中文字体嵌入）全部满足，第四条（矢量还是位图）选了矢量。
它不是方案里列的三个候选，是第四条路——而它赢的原因恰恰是"仓库里已经有 SkiaSharp"：
同一个原生库既负责量文本、画位图，也负责写 PDF，所以这条路**一个新包都不加**。

已知的代价只有一条，写在下面「缺口」一节：字体整份嵌入，含中文的文件通常十几兆。

---

## 一、四条判据

### 判据一：原生编译

**怎么判。** 仓库根上的 `Directory.Build.props` 对每个工程都开了
`IsAotCompatible` / `EnableAotAnalyzer` / `EnableTrimAnalyzer`，并且 `TreatWarningsAsErrors=true`。
所以"这个库在原生编译下会不会被裁坏"这件事，**建一次就有答案**：报了 `IL2026` / `IL3050`
之类的警告就是有问题，而警告即错误。脚手架工程刻意把"警告即错误"关掉（见下），
好让一个不合规的候选不至于把另外三个也拦住。

```bash
dotnet build tools/Poc/PdfCandidates/PdfCandidates.csproj -c Release
```

**结果。** 三个候选都报 0 条裁剪/AOT 警告，只有 QuestPDF 多出一条废弃警告：

```
Candidates.cs(115,17): warning CS0618: “ElementExtensions.Canvas(IContainer,
ElementExtensions.DrawOnCanvas)”已过时:“The Canvas API has been deprecated since
version 2024.3.0. Please use the .Svg(stringContent) API to provide custom content...”
    1 个警告
```

**没跑成的那一半：真的链接一次。** 本机缺 Visual Studio 的「使用 C++ 的桌面开发」工作负载，
所以 `ilc` 走到最后一步会停：

```bash
dotnet publish DuetDiagram.AotSmokeTest/DuetDiagram.AotSmokeTest.csproj -c Release
```

```
error : Platform linker not found. Ensure you have all the required prerequisites
documented at https://aka.ms/nativeaot-prerequisites, in particular the Desktop
Development for C++ workload in Visual Studio.
```

**这是 Phase 0 就记在账上的一处环境缺口（P0-02），不是这一条引入的。**
能验到的是静态分析这一层，验不到的是"链接出来之后跑不跑得起来"。
为了把这一层也尽量往前推，这一条顺手把渲染层纳进了原生冒烟工程
（见下面「落地时改了什么」），于是链条上被裁掉的话静态分析就会先报。

**判据一：过（静态分析），链接步骤本机跑不了，照实记。**

### 判据二：许可证

**这一条单独核，因为它选错不是技术债、是法律债。**

| 候选 | 许可 | 条件 |
|---|---|---|
| SkiaSharp | MIT（Skia 本体是 BSD 三条款） | 无条件 |
| QuestPDF | **双许可**：社区版 / 专业版 / 企业版 | 见下 |
| PdfSharpCore | MIT | 无条件。但它依赖 SixLabors.ImageSharp 与 SixLabors.Fonts，随它分发时那两个按 Apache-2.0 |

QuestPDF 的社区版不是"开源免费"那一类，而是一份**带条件的免费授权**。
许可选择指南（版本 3.0，2026-07-06 生效）里，免费只在下面几类情形成立：

1. 个人使用，且年总收入不超过第 6 类那个阈值；
2. 学习、评估、培训；
3. 慈善或公益组织；
4. 公立或非营利学术机构，用于教学、研究或自身的教务；
5. **按 OSI 认可的开源许可分发的开源项目**；
6. 年总收入不足 **一百万美元** 的组织（按共同控制合并计算）。

另有两条排除：**公共部门与政府机构、以及上市公司，无论营收多少都不适用社区版。**
不满足的必须买专业版或企业版才能上生产。

**为什么这一条在本仓判不下来。** 第 5 类看起来最像我们，但**本仓根目录没有 LICENSE 文件**，
方案里也没写打算按哪个许可分发。没有那一份声明，就说不出"我们是 OSI 开源项目"这句话，
于是能不能免费只剩下第 6 类那个营收阈值——而营收是仓库里读不出来的事实。
方案 §2 的选型表把它列成"Phase 4 验证"，验证的正是这一件事，
而验证的结果是：**它取决于一个只有项目所有者能回答的问题。**

按任务卡"许可证要单独核，报告里要写明许可证与它的条件"，这就是那一条结论。
不把它写成"不过"——因为够不够免费可能真的够；但也不把它写成"过"，
因为**在拿到那个答案之前用它，是在拿一份法律风险换一点开发便利**。

**判据二：QuestPDF 取决于一个仓库外的事实，未决；另外三个过。**

### 判据三：中文字体嵌入

**怎么判。** 四个候选各画同一张图（一个矩形、一段西文、一段中文），
再把 PDF 自己的属性读出来。

```bash
dotnet run --project tools/Poc/PdfCandidates -c Release -- all
```

**结果。**

```
=== skia ===
font embedded True
cid font     True

=== pdfsharp ===
font embedded True
cid font     True

=== bitmap ===
font embedded False
cid font     False
```

`font embedded` 看的是文件里有没有 `/FontFile`，`cid font` 看的是有没有
`/Type0` 配 `Identity-H`。**后者才是"汉字能不能画出来"的分界**：
汉字在 PDF 里要按 CID 型字体嵌进去，只嵌一份西文子集是不够的。

三个"能写字"的候选里，**两个字体都嵌，位图那一档一个字体都不需要**（它本来就是像素）。

**判据三：SkiaSharp、PdfSharpCore、位图嵌入过；QuestPDF 没测到这一步（见判据四）。**

### 判据四：矢量还是位图

**位图那一档是兜底，不是主选。** 它什么都过得了——不加依赖、许可干净、
字不字体根本无所谓——但它与 PNG 导出重复了。PDF 相对 PNG 的意义正在于矢量：
放大不糊、文字能选中、能搜索。选了它，等于多花一个格式的名字换来一份更笨重的 PNG。

**结果。** `vector` 看的是文件里有没有 `/Subtype /Image`：

```
=== skia ===      vector True
=== pdfsharp ===  vector True
=== bitmap ===    vector False
```

而"矢量"还要再分一层：文字是真文字，还是被转成了路径？转成路径的话照样放大不糊，
但搜不到、选不中。把内容流解压出来看，SkiaSharp 那一份里是真的文字操作符，
而且每个字都带一份"这个字形是哪个字"的映射表：

```
BT
/F5 14 Tf
1 0 0 -1 65.800003 64.77861 Tm
<47E4> Tj
ET
```

```
1 beginbfchar
<47E4> <7532>
endbfchar
```

`<7532>` 是「甲」的码位。**所以这份 PDF 里的中文不只是看得见，还能搜、能复制。**

**QuestPDF 在判据四这一步被判死。** 它唯一的自由绘制入口 `Canvas` 已废弃，
而且**运行时直接抛异常**：

```
=== quest ===
result  画不出来：NotImplementedException: The Canvas API has been deprecated since
version 2024.3.0. Please use the .Svg(stringContent) API to provide custom content...
```

它的官方替代是 `.Svg(一段 SVG 文本)`。那条路走得通，但要走就得先把绘制列表导成 SVG、
再让 QuestPDF 把 SVG 渲染进 PDF——**这是把一条绘制路径变成两条**，
而"两条迟早对不上"正是 SVG 与 PNG 两个导出器当初共用一份画法的理由
（见 `SvgExporter` 与 `BitmapExporter` 类型上的说明）。换来的是一个更重的依赖栈
（它自己带 `QuestPdfSkia.dll` 与 `qpdf.dll` 两个原生库），
而这件事 SkiaSharp 已经能做，一个新包都不用加。

**判据四：选矢量；QuestPDF 在画布这一层被否，位图嵌入作为兜底保留但不用。**

---

## 二、四个候选并排

| | SkiaSharp 自带写入器 | QuestPDF 2026.9.0 | PdfSharpCore 1.3.67 | 位图嵌入 |
|---|---|---|---|---|
| 原生编译（静态分析） | 过，0 条裁剪/AOT 警告 | 过，但多一条废弃警告 | 过 | 过 |
| 许可证 | MIT，无条件 | 双许可，免费有条件且本仓判不了 | MIT，无条件 | MIT，无条件 |
| 中文字体嵌入 | 嵌 | 没测到 | 嵌 | 不适用（像素） |
| 矢量 | 是，文字可搜可复制 | 是 | 是 | **否** |
| 逐字节确定性 | **是** | 没测到 | **否** | 是 |
| 新依赖 | **0 个**（SkiaSharp 已在树里） | 1 包 + 2 个原生库 | 1 包 + SixLabors 两个包 | 0 个 |
| 示例文件大小 | 13,009,456 字节 | — | 38,422 字节 | 10,586 字节 |

### 确定性

这一条不是四条判据之一，但它是仓库的硬约束（"导出要确定性，与 SVG/PNG 同一条口径"），
所以也量了。做法是同一份内容连导两次、隔两秒再导一次，比字节。

```
=== skia ===      deterministic True    timestamp False
=== pdfsharp ===  deterministic False   timestamp True
```

**SkiaSharp 那一份两次逐字节相同，而它连时间戳都不写。**
这一点值得单独说：写入器在调用方不给日期时**不写日期**，而不是写当前时刻。
换成写当前时刻的话，同一份文档两次导出就不可能相同。

**PdfSharpCore 判死在这一条上。** 即使显式把 `CreationDate` 与 `ModificationDate`
设成同一个值，两次导出仍然不同——不一样的是两处：

```
/ID       A=/ID [<E7CB2C73E143014791405BFC87536F51>...
          B=/ID [<06CFB43E2A8F3D42873D84635C9F974C>...

/BaseFont A=/FontName /PDPJMJ+Segoe#20UI
          B=/FontName /AIHTAM+Segoe#20UI
```

一是文件标识 `/ID` 随机，二是**字体子集前缀那六个大写字母也是随机的**。
后者按 PDF 规范是合理的（避免同名子集撞车），但它让"导出的文件有没有变"这个问题
永远答不了，而 PDF 也就进不了版本控制。这不是配置能改的，是库内部的随机数。

另外两条关于 PdfSharpCore 的实测，一并记下：

- **它不认字体集合。** `msyh.ttc`（微软雅黑，本机的中文默认字体）直接抛
  `InvalidOperationException: TrueType collection fonts are not yet supported by PdfSharpCore.`
  要用它就得换一个单文件字体（黑体、宋体之类），而那是**用户的字体选择被库限制**。
- 它没有字体解析器，得自己实现一个 `IFontResolver`，并且不做文字整形。

### 字体的代价：整份嵌 vs 只嵌用到的

这是 SkiaSharp 那一份唯一的缺口，值得单独量清楚。`/Length1` 是嵌进去的字体程序的
未压缩长度，拿它跟源字体文件比：

| 候选 | 嵌进去的 | 源字体文件 | 比例 |
|---|---|---|---|
| SkiaSharp | `959752`（Segoe UI） | `segoeui.ttf` = 959,752 | **100%** |
| SkiaSharp | `19704352`（微软雅黑） | `msyh.ttc` = 19,704,352 | **100%** |
| PdfSharpCore | `53416`（Segoe UI） | `segoeui.ttf` = 959,752 | 5.6% |
| PdfSharpCore | `176280`（黑体） | `simhei.ttf` = 9,745,792 | 1.8% |

源文件大小可以自己核：

```bash
ls -l /c/Windows/Fonts/segoeui.ttf /c/Windows/Fonts/msyh.ttc /c/Windows/Fonts/simhei.ttf
```

**SkiaSharp 嵌的是整份字体程序，不做子集。** 一个汉字与一整页汉字代价一样
（实测：只写「开」一个字，文件 12,459,820 字节；写四行中文，12,459,985 字节，
差不到两百字节）。所以一份含中文的 PDF 通常在十几兆。

这不是配置能改的——Skia 的 PDF 后端没有带 TrueType 子集器，`SKDocumentPdfMetadata`
上也没有对应的开关。**这是这条路真实的代价，而它换来的东西也真实**：
收件人不需要装那份字体，线条与文字都是矢量的，能搜、能选中、能放大。

**要小文件就走 PNG 那一档**（几万字节），代价是文字搜不到。
这一条已经写进导出的丢失清单，用户拿到一份十几兆的文件时不会以为是哪里写错了。

---

## 三、落地时改了什么

**`PdfExporter` 不自己画，它复用位图那一份画法。** 这是这一条里最要紧的一处结构决定。

P4-17 的 `BitmapExporter` 里原本有整段绘制代码（形状路径、箭头、虚线、字体回退、装饰线）。
PDF 也要画同样的东西，于是把那段抽成了 `CanvasPainter`，两个导出器共用：

- `CanvasPainter` 知道"绘制列表怎么变成图元"；
- `BitmapExporter` 只知道"开一块离屏画布、定范围、编成 PNG"；
- `PdfExporter` 只知道"开一份 PDF、按范围给每页定纸张、把画布交出去"。

所以"PDF 与 PNG 画出来不一样"这种偏差在结构上不可能发生——两边调用的是同一段代码，
而不是靠两条约定各自维护。位图那 30 条用例在抽完之后全绿，是这次重构没走样的证据。

**SkiaSharp 那条路一个新包都不加**，所以 `Directory.Packages.props` 里
没有新增产品依赖；新增的两条登记（QuestPDF 与 PdfSharpCore）只被
`tools/Poc/PdfCandidates` 这一个取证脚手架引用，与 `Sugiyama` 当初的情形一样。

**渲染层进了原生冒烟工程。** `DuetDiagram.AotSmokeTest` 原先只引 Core 与 Layout，
现在多引一个 Render，并且真的走一遍完整链路写出一份 PDF
（量尺寸 → 求解布局 → 翻译成绘制指令 → 写文件）。判据落在文件自己身上：
开头四字节是 `%PDF`、页数与报出来的一致、字体真的嵌进去了。

---

## 四、缺口

1. **原生编译只验到静态分析这一层。** `ilc` 的最后一步在本机跑不了（缺 MSVC 工作负载，
   P0-02 就记过的环境缺口）。静态分析 0 警告说明我们的代码与三个候选都没有可被裁掉的
   反射用法，但"链接出来之后跑不跑得起来"没有实测。这一条不能算过。

2. **字体整份嵌入，含中文的文件十几兆。** 见上。库内无开关，属于 Skia 后端的固有行为。

3. **纸张尺寸被取整到整点。** 1123×794 的页面折成 842.25×595.5 点，落到文件里是 842×595。
   差不到一个点，打印出来看不出来，但精确到小数点的纸张办不到。

4. **QuestPDF 的 `.Svg` 那条替代路没有实测。** 它在判据二与判据四上已经被判掉，
   再测它只是多一份用不上的数据。真要回看，脚手架里加一个候选即可。

5. **本仓没有 LICENSE 文件。** 这不是 PDF 这一条的问题，但它正是 QuestPDF
   判不下来的原因。要不要开源、按哪个许可开源，是项目所有者的问题；
   答案会影响的不止 PDF 这一处依赖。

---

## 五、怎么自己跑

```bash
# 四个候选的运行时事实（字节数、矢量与否、字体嵌没嵌、确定性、整份还是子集）
dotnet run --project tools/Poc/PdfCandidates -c Release -- all

# 单看某一个
dotnet run --project tools/Poc/PdfCandidates -c Release -- quest

# 原生编译的静态分析那一层
dotnet build tools/Poc/PdfCandidates/PdfCandidates.csproj -c Release

# 原生冒烟（含 PDF 那一段）
dotnet run --project DuetDiagram.AotSmokeTest -c Release
dotnet publish DuetDiagram.AotSmokeTest/DuetDiagram.AotSmokeTest.csproj -c Release   # 本机停在第 3 条缺口
```
