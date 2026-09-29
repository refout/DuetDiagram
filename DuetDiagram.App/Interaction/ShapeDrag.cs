using Avalonia.Input;

namespace DuetDiagram.App.Interaction;

/// <summary>
/// 从形状面板拖一个形状到画布上时，那一包数据里装的是什么。
/// </summary>
/// <remarks>
/// <para>
/// **用应用私有格式，不用文本。** 文本格式的话，从别的程序里拖一段字进来也会新建节点——
/// 那不是一个有意义的动作，而用户看到的是"画布上莫名多了一个方块"，说不出它是怎么来的。
/// </para>
/// <para>
/// 格式标识只在本应用内比对，起拖的那一边与接落点的那一边读的是同一个
/// <see cref="Format"/>，所以标识本身随便取，只要两处一致。两处各写一个字符串的话，
/// 表现是"拖过去什么也没发生"，而那正是最难查的一类失败。
/// </para>
/// </remarks>
public static class ShapeDrag
{
    /// <summary>这一包数据用的格式。值是形状名。</summary>
    public static readonly DataFormat<string> Format =
        DataFormat.CreateStringApplicationFormat("duetdiagram.shape");

    /// <summary>把形状名装成一包可拖的数据。</summary>
    /// <param name="shapeName">形状名，与形状库、属性面板用的是同一个。</param>
    public static IDataTransfer Pack(string shapeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shapeName);

        var transfer = new DataTransfer();

        transfer.Add(DataTransferItem.Create(Format, shapeName));

        return transfer;
    }

    /// <summary>
    /// 从一包数据里取出形状名。
    /// </summary>
    /// <returns>是这一包数据时给出形状名，否则为空。</returns>
    public static string? Read(IDataTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        return transfer.TryGetValue(Format) is { Length: > 0 } name ? name : null;
    }
}
