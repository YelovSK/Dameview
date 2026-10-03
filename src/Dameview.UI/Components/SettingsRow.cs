using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;

namespace Dameview.UI.Components;

internal sealed class SettingsRow : UiElement
{
    internal SettingsRow(
        string label,
        UiElement content,
        string? toolTip = null)
    {
        var labelText = new TextBlock(label, UiTextStyle.Body, UiTextTone.Secondary, UiTextWrapping.NoWrap)
        {
            // Only as wide as its text, so a tooltip shows over the words and not the empty row.
            HorizontalAlignment = UiAlignment.Start,
            ToolTip = toolTip is null ? null : new UiToolTip(toolTip),
        };
        AddChild(new StackPanel(UiOrientation.Vertical, labelText, content)
        {
            Spacing = UiDesign.SmallSpacing,
        });
    }

    protected override bool HitTestCore(PointF position) => false;
}
