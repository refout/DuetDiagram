using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DuetDiagram.App;

/// <summary>
/// 帧率基线测量。
/// </summary>
/// <remarks>
/// <para>
/// 测的是**光栅化一帧的耗时**，由它反推可达到的帧率上限。之所以不在真实窗口里数帧：
/// 那样要依赖桌面会话、受合成器与垂直同步影响，数字噪声大且不可复现。
/// 光栅化耗时是帧率的主导因素，也是后续优化真正要压的部分。
/// </para>
/// <para>
/// 第一个参数是矩形数量，第二个是测量帧数。先跑若干帧预热再计时，
/// 否则即时编译与字形缓存的首帧成本会被算进去，把结果拉高几倍。
/// </para>
/// </remarks>
internal static class FrameBenchmark
{
    private const int DefaultWidth = 1920;

    private const int DefaultHeight = 1080;

    private const int WarmupFrames = 10;

    public static int Run(int rectangleCount, int frameCount)
    {
        Console.WriteLine("帧率基线测量");
        Console.WriteLine($"绘制矩形 {rectangleCount} 个，画布 {DefaultWidth}×{DefaultHeight}，测量 {frameCount} 帧");
        Console.WriteLine();

        var startup = Stopwatch.StartNew();
        Program.BuildAvaloniaApp().SetupWithoutStarting();
        startup.Stop();

        var field = new RectangleField(rectangleCount);
        var size = new Size(DefaultWidth, DefaultHeight);

        field.Measure(size);
        field.Arrange(new Rect(size));

        var pixelSize = new PixelSize(DefaultWidth, DefaultHeight);

        // 预热：即时编译与内部缓存的首帧成本不能算进稳态帧率。
        for (var i = 0; i < WarmupFrames; i++)
        {
            using var warmup = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            warmup.Render(field);
        }

        var elapsed = new List<double>(frameCount);
        var stopwatch = new Stopwatch();

        for (var i = 0; i < frameCount; i++)
        {
            stopwatch.Restart();

            using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            bitmap.Render(field);

            stopwatch.Stop();
            elapsed.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        elapsed.Sort();

        var mean = elapsed.Average();
        var median = elapsed[elapsed.Count / 2];
        var best = elapsed[0];
        var worst = elapsed[^1];

        Console.WriteLine($"  平台就绪          {startup.Elapsed.TotalMilliseconds,8:0.0} ms");
        Console.WriteLine($"  单帧平均          {mean,8:0.00} ms  → 约 {1000 / mean,6:0.0} 帧每秒");
        Console.WriteLine($"  单帧中位          {median,8:0.00} ms  → 约 {1000 / median,6:0.0} 帧每秒");
        Console.WriteLine($"  单帧最快          {best,8:0.00} ms");
        Console.WriteLine($"  单帧最慢          {worst,8:0.00} ms");
        Console.WriteLine($"  每秒矩形数        {rectangleCount * 1000 / mean,8:0}");

        return 0;
    }

    /// <summary>
    /// 铺满画布的矩形阵列。
    /// </summary>
    /// <remarks>
    /// 矩形的位置、颜色、尺寸都有变化，避免所有元素完全相同导致测量失真——
    /// 完全相同的图元可能被底层合并或命中缓存，测出来的耗时会明显偏乐观。
    /// 每个矩形都带一笔描边，与真实图元一致。
    /// </remarks>
    private sealed class RectangleField(int count) : Control
    {
        public override void Render(DrawingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var width = Bounds.Width;
            var height = Bounds.Height;

            if (width <= 0 || height <= 0 || count <= 0)
            {
                return;
            }

            // 排成接近正方的网格，让图形铺满画布。
            var columns = (int)Math.Ceiling(Math.Sqrt(count * width / height));
            columns = Math.Max(1, columns);

            var rows = (int)Math.Ceiling((double)count / columns);
            var cellWidth = width / columns;
            var cellHeight = height / rows;

            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED)), 1);

            for (var i = 0; i < count; i++)
            {
                var column = i % columns;
                var row = i / columns;

                // 留出间隙，并让尺寸随序号变化，避免图元完全一致。
                var inset = 2 + (i % 3);
                var rect = new Rect(
                    (column * cellWidth) + inset,
                    (row * cellHeight) + inset,
                    Math.Max(1, cellWidth - (inset * 2)),
                    Math.Max(1, cellHeight - (inset * 2)));

                var fill = new SolidColorBrush(Color.FromRgb(
                    (byte)(0xE0 + (i % 32)),
                    (byte)(0xE8 + (i % 24)),
                    (byte)(0xF0 + (i % 16))));

                context.DrawRectangle(fill, pen, rect);
            }
        }
    }
}
