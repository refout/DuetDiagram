namespace DuetDiagram.Render;

/// <summary>
/// 画布每帧怎么取要画的东西。
/// </summary>
/// <remarks>
/// 两档之间没有中间态。做成"部分剔除"之类连续调节的话，
/// 判断"这一帧该走哪条路"会多出一堆阈值，而多出来的那些阈值没人能说清该取多少。
/// </remarks>
public enum RenderMode
{
    /// <summary>
    /// 把绘制列表里的每一条都执行一遍。
    /// </summary>
    /// <remarks>
    /// 元素少的时候这条更快：它没有查询开销，也不必先建索引。
    /// </remarks>
    Immediate,

    /// <summary>
    /// 按视口从索引里取出可见的那部分，只画它们。
    /// </summary>
    /// <remarks>
    /// 元素多、而视口里只看得到一小部分时这条更快。
    /// 画得少省下的是光栅化与几何构造，而这两样正是大图上的主要成本。
    /// </remarks>
    Virtualized,
}
