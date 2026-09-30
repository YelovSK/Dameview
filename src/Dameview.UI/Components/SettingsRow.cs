using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;

namespace Dameview.UI.Components;

internal sealed class SettingsRow : UiElement
{
    internal SettingsRow(
        string label,
        UiElement content)
    {
        var labelText = new TextBlock(label, UiTextStyle.Body, UiTextTone.Secondary, UiTextWrapping.NoWrap);
        AddChild(new StackPanel(UiOrientation.Vertical, labelText, content)
        {
            Spacing = UiDesign.SmallSpacing,
        });
    }

    protected override bool HitTestCore(PointF position) => false;
}
