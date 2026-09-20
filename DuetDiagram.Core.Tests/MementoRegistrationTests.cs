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
/// P1 判据 #12：所有 Memento 派生类型必须注册多态序列化。
/// 漏注册在 AOT 下是**运行时**失败，因此这里用反射把它提前成编译期后的第一道门禁。
/// </summary>
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
        registered.Should().Equal(concrete, "新增 Memento 必须同时补 [JsonDerivedType]，否则 AOT 下反序列化会失败");
    }

    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void Discriminators_are_unique_and_non_empty()
    {
        var discriminators = typeof(CommandMemento)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => a.TypeDiscriminator as string)
            .ToArray();

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

        json.Should().Contain("\"$memento\":\"add-node\"");
    }
}
