namespace DuetDiagram.Core.Shapes;

/// <summary>
/// 一批形状定义。
/// </summary>
/// <remarks>
/// <para>
/// **接口上不给任何 IO 能力。** 形状提供者是可能来自第三方的代码，而第三方实现
/// 是要被关起来的：它只该回答"我提供哪些形状、它们长什么样"，不该读文件、连网络。
/// 接口上不出现这类成员，是"它做不到"而不是"它不该做"——后者靠约定，前者靠类型。
/// </para>
/// <para>
/// **注册走编译期能看见的那条路。** 发布走原生编译，运行时反射加载程序集在原生下不可用；
/// 所以提供者是构造时传进来的一个实例，注册表不扫描程序集、不按名字反射建类型。
/// 加一个提供者就是加一行构造调用，编译器看得见。
/// </para>
/// </remarks>
public interface IShapeProvider
{
    /// <summary>这个提供者给出的全部形状，按注册顺序。</summary>
    IReadOnlyList<ShapeDefinition> Shapes { get; }
}
