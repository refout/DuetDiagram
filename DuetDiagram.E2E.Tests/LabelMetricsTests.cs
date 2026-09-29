using System.Globalization;
using Avalonia.Media;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 标签在节点框里摆得对不对：量出来的宽度与画出来的宽度是不是同一件事。
/// </summary>
/// <remarks>
/// <para>
/// 节点框的宽高由度量那一步定，字由绘制方画。两边对同一段字量出不同的宽度时，
/// 字会溢出框、并且在框里偏向一边——因为"居中"是按量出来的宽度算的。
/// </para>
/// <para>
/// **这一条必须在有界面这一层验。** 度量走 Skia，画布走界面框架的字体栈，
/// 两者对"这段字用哪个字体"各有各的回退规则；渲染层里只有前者，问不出后者的答案。
/// 快照也验不了：快照里的位置是按度量摆的，度量偏了它跟着偏，两边自洽。
/// </para>
/// <para>
/// 判据取绘制方量出来的宽度，不取写死的像素数：字的实际宽度随机器上装的字体变，
/// 写死的话换一台机器就红，而红的原因与要验的东西无关。
/// </para>
/// </remarks>
public sealed class LabelMetricsTests
{
    [Fact]
    [Trait("Category", "Canvas")]
    public async Task A_label_is_centred_in_its_node_at_the_width_it_is_drawn()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var model = canvas.Model!;

            var nodes = model.DrawList.Commands
                .OfType<DrawShape>()
                .ToDictionary(shape => shape.ElementId, shape => shape.Rect);
            var labels = model.DrawList.Commands.OfType<DrawText>().ToList();

            labels.Should().NotBeEmpty("示例文档要真的画出标签来");

            var checkedNodes = 0;
            var wide = 0;

            foreach (var label in labels)
            {
                if (!nodes.TryGetValue(label.ElementId, out var node))
                {
                    // 连线标签没有自己的形状，它的框是整条连线的外接框，不适用这一条。
                    continue;
                }

                var drawn = Drawn(label);

                (label.Box.X + (drawn.Width / 2)).Should().BeApproximately(
                    node.CenterX,
                    0.5,
                    $"「{label.Text}」画出来的中心要落在节点框的中心上；"
                    + "量出来的比画出来的窄时，字会偏到一边并且溢出框");

                checkedNodes++;

                if (drawn.Width > label.FontSize * 1.1)
                {
                    wide++;
                }
            }

            checkedNodes.Should().BeGreaterThan(0, "至少要有节点的标签参与这一条");

            // 全角字一个占一个字号那么宽，半角的西文占不到一半。有全角字参与，
            // 这条才真的碰到"点名的是西文家族、而这段字画不出来"那条路——
            // 那正是量出来的与画出来的差得最远的一种。
            wide.Should().BeGreaterThan(
                0,
                "示例文档里有全角标签，这条要真的走到字体回退上，否则它验的是一段两边都不会偏的字");

            window.Close();
        });
    }

    /// <summary>按画布那一套取字体与字号，交给界面框架量一遍。</summary>
    private static FormattedText Drawn(DrawText text) =>
        new(
            text.Text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily(string.IsNullOrWhiteSpace(text.FontFamily)
                    ? FontFamily.Default.Name
                    : text.FontFamily),
                text.Italic ? FontStyle.Italic : FontStyle.Normal,
                text.Weight == DuetDiagram.Core.Model.FontWeight.Bold
                    ? Avalonia.Media.FontWeight.Bold
                    : Avalonia.Media.FontWeight.Normal),
            text.FontSize,
            Brushes.Black);
}
