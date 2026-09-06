using System.Drawing;
using Dameview.UI.Layout;
using Vortice.DirectWrite;

namespace Dameview.UI.Components;

internal sealed class SettingsRow : UiElement, IDisposable
{
    private readonly TextBlock _label;
    private readonly StackPanel _stack;

    internal SettingsRow(
        IDWriteFactory factory,
        string label,
        UiElement content)
    {
        _label = new TextBlock(
            factory,
            label,
            UiTextStyle.Body,
            UiTextTone.Secondary,
            UiTextWrapping.NoWrap);
        _stack = new StackPanel(
            UiOrientation.Vertical,
            UiDesign.SmallSpacing,
            StackPanelDistribution.Natural,
            _label,
            content);
        AddChild(_stack);
    }

    protected override SizeF MeasureCore(SizeF availableSize) => _stack.Measure(availableSize);

    protected override void ArrangeCore(SizeF finalSize)
    {
        _stack.Arrange(new RectangleF(PointF.Empty, finalSize));
    }

    protected override bool HitTestCore(PointF position) => false;

    public void Dispose() => _label.Dispose();
}
