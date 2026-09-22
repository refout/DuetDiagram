using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 整体校验：结构化错误、相关标识与修复建议，以及它不改文档。
/// </summary>
public sealed class ValidateToolTests
{
    [Fact]
    [Trait("Category", "ValidateTool")]
    public void A_clean_document_has_no_issues()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"b"}""");
        Harness.Edit(registry, """{"action":"connect-edge","id":"e1","from":"a","to":"b"}""");

        var result = Harness.Invoke(registry, DiagramToolset.Validate, """{}""");

        result.IsSuccess.Should().BeTrue();
        result.Data!.Value.GetProperty("issueCount").GetInt32().Should().Be(0);
        result.Message.Should().Contain("通过");
    }

    [Fact]
    [Trait("Category", "ValidateTool")]
    public void Issues_come_back_with_a_code_an_identifier_and_a_suggestion()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"b"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"c"}""");
        Harness.Edit(registry, """{"action":"connect-edge","id":"ab","from":"a","to":"b"}""");
        Harness.Edit(registry, """{"action":"connect-edge","id":"ac","from":"a","to":"c"}""");
        Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"add-constraint","kind":"order","subject":"a","memberIds":["ab","ac"]}""");

        // 删边不清理引用了它的层内次序，留着让整体校验器报出来。
        Harness.Edit(registry, """{"action":"disconnect-edge","id":"ac"}""").IsSuccess.Should().BeTrue();

        var result = Harness.Invoke(registry, DiagramToolset.Validate, """{}""");

        result.IsSuccess.Should().BeTrue();
        result.Data!.Value.GetProperty("issueCount").GetInt32().Should().BeGreaterThan(0);

        var issue = result.Data!.Value.GetProperty("issues")[0];

        issue.GetProperty("code").GetString().Should().Be(ErrorCodes.LayoutOrderEdgeMissing);
        issue.GetProperty("relatedId").GetString().Should().Be("a", "界面靠它定位到具体元素");
        issue.GetProperty("message").GetString().Should().Contain("ac");
        issue.GetProperty("suggestion").GetString().Should().NotBeNullOrWhiteSpace(
            "只说错不说怎么改等于把问题丢回给用户");
    }

    [Fact]
    [Trait("Category", "ValidateTool")]
    public void Validation_does_not_touch_the_version_or_the_history()
    {
        var document = new DiagramDocument("doc");
        var context = Harness.Context(document);
        var registry = ToolRegistry.CreateDefault(context);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var version = document.Version;
        var history = context.Bus.Context.History.UndoCount;

        Harness.Invoke(registry, DiagramToolset.Validate, """{}""").IsSuccess.Should().BeTrue();

        document.Version.Should().Be(version, "校验是只读的，不得触发版本号变化");
        context.Bus.Context.History.UndoCount.Should().Be(history, "校验不进撤销栈");
    }

    [Fact]
    [Trait("Category", "ValidateTool")]
    public void A_scope_that_is_not_wired_yet_says_so()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(registry, DiagramToolset.Validate, """{"scope":"page:p1"}""");

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ToolErrorCodes.NotSupported);
        result.Errors[0].Parameter.Should().Be("scope");
    }
}
