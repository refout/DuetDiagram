using System.Reflection;
using System.Text.Json.Serialization;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 多态类型的注册完整性。
/// </summary>
/// <remarks>
/// <para>
/// 漏注册多态子类型的失败方式很隐蔽：编译通过，单机运行也通过，
/// 只有 AOT 发布之后第一次反序列化那个类型才会炸。也就是说它在开发阶段完全不可见。
/// </para>
/// <para>
/// 这里用反射把程序集里的具体子类型全部枚举出来，跟已标注的集合做全等比较，
/// 把"运行时才发现"提前成"改完代码立刻发现"。
/// 全等而不是"包含"是有意的：多标注一个不存在的类型同样是错误。
/// </para>
/// </remarks>
public sealed class MementoRegistrationTests
{
    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void Every_concrete_memento_is_registered_as_a_derived_type()
    {
        var concrete = typeof(CommandMemento).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(CommandMemento).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        var registered = typeof(CommandMemento)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => a.DerivedType)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        concrete.Should().NotBeEmpty();
        registered.Should().Equal(concrete, "每个具体的 memento 都必须在基类上标注多态标签，否则 AOT 下反序列化会失败");
    }

    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void Discriminators_are_unique_and_non_empty()
    {
        var discriminators = typeof(CommandMemento)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => a.TypeDiscriminator as string)
            .ToArray();

        // 标签重复会让两个类型解析成同一个，写入时无法区分；为空则退化成非法 JSON 键。
        discriminators.Should().OnlyHaveUniqueItems();
        discriminators.Should().AllSatisfy(d => d.Should().NotBeNullOrEmpty());
    }

    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void Every_concrete_diff_is_registered_as_a_derived_type()
    {
        var concrete = typeof(DiffResult).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(DiffResult).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        var registered = typeof(DiffResult)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => a.DerivedType)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        concrete.Should().HaveCount(5);
        registered.Should().Equal(concrete);
    }

    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void Memento_payload_is_self_describing()
    {
        var json = DiagramSerializer.SerializeMemento(new AddNodeMemento
        {
            Node = new NodeDef { Id = "n1" },
            Index = 0,
        });

        // 载荷自带类型标记。少了它，接收端只能靠猜字段组合来还原类型。
        json.Should().Contain("\"$memento\":\"add-node\"");
    }
}
