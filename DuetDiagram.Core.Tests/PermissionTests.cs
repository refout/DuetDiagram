using DuetDiagram.Core.Concurrency;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 权限分级：能不能改这份文档，以及能改哪些图层。
/// </summary>
/// <remarks>
/// 两个维度分开判，因为它们能判的地方不同：能不能改是粗的，一条命令是不是写入一眼看得出来；
/// 能改哪些图层是细的，只有解析过动作参数的那一层才知道一次写入点名了哪个图层。
/// 合成一个的话，粗的那一层要么得先替所有动作把参数读一遍，要么只能整体放行。
/// </remarks>
public sealed class PermissionTests
{
    #region 能不能改

    [Fact]
    [Trait("Category", "Permission")]
    public void A_read_only_subject_cannot_write_anything()
    {
        // 只读那一档连图层都不必谈：它一个字节都改不动。
        PermissionSet.ReadOnly.CanWrite.Should().BeFalse();

        PermissionSet.ReadOnly.AllowsLayer("public").Should().BeFalse();
        PermissionSet.ReadOnly.AllowsLayer(null).Should().BeFalse("没点名图层也是写入");
    }

    [Fact]
    [Trait("Category", "Permission")]
    public void An_unrestricted_subject_can_write_anywhere()
    {
        PermissionSet.Full.CanWrite.Should().BeTrue();
        PermissionSet.Full.Layers.Should().BeEmpty("空集合的意思是「不限图层」，不是「哪个都不许」");

        PermissionSet.Full.AllowsLayer("public").Should().BeTrue();
        PermissionSet.Full.AllowsLayer("secret").Should().BeTrue();
        PermissionSet.Full.AllowsLayer(null).Should().BeTrue();
    }

    #endregion

    #region 能改哪些图层

    [Fact]
    [Trait("Category", "Permission")]
    public void A_layer_scoped_subject_is_confined_to_its_layers()
    {
        var scoped = Scoped("public", "shared");

        scoped.CanWrite.Should().BeTrue();

        scoped.AllowsLayer("public").Should().BeTrue();
        scoped.AllowsLayer("shared").Should().BeTrue();
        scoped.AllowsLayer("secret").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Permission")]
    public void A_write_that_names_no_layer_is_not_a_layer_write()
    {
        // 加一个节点、连一条边都没把东西放进某个图层里，也就无从违反图层级的限制。
        // 想连它们一起挡下来的话，判据得是"这个元素现在落在哪个图层"——那是另一件事，
        // 要读文档，而且读出来的图层可能与命令执行那一刻的不是同一个。
        Scoped("public").AllowsLayer(null).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Permission")]
    public void An_empty_layer_set_means_no_restriction_rather_than_none_allowed()
    {
        // 两种意思各写一半的话，一份没写图层的凭据会变成一份什么都改不动的凭据，
        // 而配置它的人以为它与以前一样。
        var unrestricted = new PermissionSet { CanWrite = true };

        unrestricted.Layers.Should().BeEmpty();
        unrestricted.AllowsLayer("随便哪一层").Should().BeTrue();
    }

    #endregion

    #region 绑定主体

    [Fact]
    [Trait("Category", "Permission")]
    public void The_acl_binds_permissions_to_subjects()
    {
        var acl = new LayerAcl(
        [
            ("writer", PermissionSet.Full),
            ("reader", PermissionSet.ReadOnly),
            ("public-only", Scoped("public")),
        ]);

        acl.Subjects.Should().Equal("writer", "reader", "public-only");

        acl.For("writer").CanWrite.Should().BeTrue();
        acl.For("reader").CanWrite.Should().BeFalse();
        acl.For("public-only").AllowsLayer("public").Should().BeTrue();
        acl.For("public-only").AllowsLayer("secret").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Permission")]
    public void An_unknown_subject_gets_read_only()
    {
        // 默认放行的话，一处拼错的主体名会让限制静默失效——而那种失效不报错、不抛异常，
        // 看起来就像"本来就没有限制"，等发现时那份图已经被改过了。
        var acl = new LayerAcl([("writer", PermissionSet.Full)]);

        acl.For("谁都没登记过").CanWrite.Should().BeFalse();
        acl.For(null).CanWrite.Should().BeFalse();
        acl.For(null).AllowsLayer("public").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Permission")]
    public void A_subject_registered_twice_is_rejected()
    {
        // 后者覆盖前者的话，一份本来要收窄的配置会静默失效，而配置的人从启动日志里
        // 看不出任何异常。
        var register = () => new LayerAcl(
        [
            ("writer", PermissionSet.Full),
            ("writer", PermissionSet.ReadOnly),
        ]);

        register.Should().Throw<ArgumentException>().WithMessage("*writer*");
    }

    [Fact]
    [Trait("Category", "Permission")]
    public void The_acl_answers_the_layer_question_for_a_subject()
    {
        var acl = new LayerAcl([("public-only", Scoped("public"))]);

        acl.AllowsLayer("public-only", "public").Should().BeTrue();
        acl.AllowsLayer("public-only", "secret").Should().BeFalse();
        acl.AllowsLayer("没登记过", "public").Should().BeFalse("认不出的主体按只读");
    }

    #endregion

    private static PermissionSet Scoped(params string[] layers) => new()
    {
        CanWrite = true,
        Layers = new HashSet<string>(layers, StringComparer.Ordinal),
    };
}
