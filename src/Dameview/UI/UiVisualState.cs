namespace Dameview.UI;

[Flags]
internal enum UiVisualState
{
    None = 0,
    Hovered = 1 << 0,
    Pressed = 1 << 1,
    Focused = 1 << 2,
    Disabled = 1 << 3,
    Selected = 1 << 4,
    Open = 1 << 5,
}
