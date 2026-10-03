using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;

namespace Dameview.UI.Components;

/// <summary>A heading over a few related settings, sitting closer to them than to what's above.</summary>
internal sealed class SettingsGroup : UiElement
{
    internal SettingsGroup(string heading, params UiElement[] settings)
    {
        var headingText = new TextBlock(heading, UiTextStyle.Label, UiTextTone.Primary, UiTextWrapping.NoWrap);
        var settingsStack = new StackPanel(UiOrientation.Vertical, settings)
        {
            Spacing = UiDesign.LargeSpacing,
        };
        AddChild(new StackPanel(UiOrientation.Vertical, headingText, settingsStack)
        {
            Spacing = UiDesign.Spacing,
        });
    }

    protected override bool HitTestCore(PointF position) => false;
}
