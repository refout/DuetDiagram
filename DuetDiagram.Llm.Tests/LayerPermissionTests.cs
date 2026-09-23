using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Concurrency;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 图层级权限：工具层在动作参数上判"这次写入点名了哪个图层"。
/// </summary>
/// <remarks>
/// <para>
/// 这一档只能落在工具层。一次写入有没有点名图层、点名的是哪个，藏在动作参数里：
/// 改节点归属时图层写在 <c>value</c> 上，建图层与改图层时写在 <c>id</c> 上。
/// 传输层要判就得把这张对照表抄一份，而抄漏的那一格会静默放行——那份图被改了一角，
/// 谁都不报错。
/// </para>
/// <para>
/// 粗的那一档（能不能改这份文档）不在这里判：判据是"这份凭据的权限档"，
/// 而那只有传输层拿得到。两处合起来覆盖全部，各判各看得到的那一半。
/// </para>
/// </remarks>
public sealed class LayerPermissionTests
{
    #region 许

    [Fact]
    [Trait("Category", "Permission")]
    public void A_scoped_subject_can_write_to_its_own_layer()
    {
        var registry = Harness.Registry(permissions: Scoped("public"));

        var result = Harness.Invoke(registry, DiagramToolset.Edit, """{"action":"create-layer","id":"public"}""");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Permission")]
    public void An_unrestricted_subject_can_write_anywhere()
    {
        var registry = Harness.Registry();

        Harness.Invoke(registry, DiagramToolset.Edit, """{"action":"create-layer","id":"secret"}""")
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Permission")]
    public void A_write_that_names_no_layer_is_not_blocked()
    {
        // 加一个节点没把东西放进某个图层里，也就无从违反图层级的限制。
        var registry = Harness.Registry(permissions: Scoped("public"));

        Harness.Invoke(registry, DiagramToolset.Edit, """{"action":"add-node","id":"a","label":"甲"}""")
            .IsSuccess.Should().BeTrue();
    }

    #endregion

    #region 拒

    [Fact]
    [Trait("Category", "Permission")]
    public void Creating_a_layer_outside_the_allowed_set_is_refused()
    {
        var registry = Harness.Registry(permissions: Scoped("public"));

        var result = Harness.Invoke(registry, DiagramToolset.Edit, """{"action":"create-layer","id":"secret"}""");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayerForbidden);
    }

    [Fact]
    [Trait("Category", "Permission")]
    public void Renaming_and_reordering_a_layer_are_checked_too()
    {
        // 建、改、排序三条命令点名的都是 id。漏判其中一条，那条命令就是一条能改到别的图层的路。
        var registry = Harness.Registry(permissions: Scoped("public"));

        Harness.Invoke(registry, DiagramToolset.Edit, """{"action":"create-layer","id":"public"}""")
            .IsSuccess.Should().BeTrue();

        Harness.Invoke(registry, DiagramToolset.Edit, """{"action":"rename-layer","id":"secret","label":"另一层"}""")
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayerForbidden);

        Harness.Invoke(registry, DiagramToolset.Edit, """{"action":"reorder-layer","id":"secret","index":0}""")
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayerForbidden);
    }

    [Fact]
    [Trait("Category", "Permission")]
    public void Moving_a_node_into_a_forbidden_layer_is_refused()
    {
        var registry = Harness.Registry(permissions: Scoped("public"));

        Harness.Invoke(registry, DiagramToolset.Edit, """{"action":"add-node","id":"a","label":"甲"}""")
            .IsSuccess.Should().BeTrue();

        var result = Harness.Invoke(
            registry,
            DiagramToolset.Edit,
            """{"action":"set-node-field","id":"a","field":"layer","value":"secret"}""");

        result.IsSuccess.Should().BeFalse();

        // 图层标识写在 value 上，不是 id 上：报错参数写错的话，模型会去改一个不相干的字段。
        result.Errors.Should().ContainSingle().Which.Parameter.Should().Be("value");
        result.Errors[0].Expected.Should().Contain("public", "可用的图层要列出来，否则模型只能猜着换一个再撞一次");
    }

    [Fact]
    [Trait("Category", "Permission")]
    public void A_read_only_subject_cannot_touch_any_layer()
    {
        var registry = Harness.Registry(permissions: PermissionSet.ReadOnly);

        Harness.Invoke(registry, DiagramToolset.Edit, """{"action":"create-layer","id":"public"}""")
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayerForbidden);
    }

    [Fact]
    [Trait("Category", "Permission")]
    public void A_refused_write_leaves_the_document_untouched()
    {
        // 判在发命令之前，所以那条命令一条都没发出去。文档没动，版本号也没动。
        var document = new DiagramDocument("layer-doc");
        var registry = Harness.Registry(document, permissions: Scoped("public"));

        Harness.Invoke(registry, DiagramToolset.Edit, """{"action":"create-layer","id":"secret"}""")
            .IsSuccess.Should().BeFalse();

        document.Layers.Should().BeEmpty();
        document.Version.Should().Be(0);
    }

    #endregion

    private static PermissionSet Scoped(params string[] layers) => new()
    {
        CanWrite = true,
        Layers = new HashSet<string>(layers, StringComparer.Ordinal),
    };
}
