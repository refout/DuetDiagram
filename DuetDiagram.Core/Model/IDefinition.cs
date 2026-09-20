namespace DuetDiagram.Core.Model;

/// <summary>
/// 文档里带唯一标识的定义。
/// </summary>
/// <remarks>
/// 抽出这个接口是为了让九个集共用同一套查找与索引逻辑。
/// 九个集合各写一遍"有没有"、"找出来"、"在第几个"，重复的不只是代码量，
/// 还有出错的机会——大小写比较、找不到时的返回值，任何一处不一致都会变成难查的行为差异。
/// </remarks>
public interface IDefinition
{
    /// <summary>文档内唯一标识。比较一律区分大小写。</summary>
    string Id { get; }
}
