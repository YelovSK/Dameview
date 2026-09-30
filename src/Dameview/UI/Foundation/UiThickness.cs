namespace Dameview.UI.Foundation;

/// <summary>Space on each side of an element, in DIPs.</summary>
internal readonly record struct UiThickness(float Left, float Top, float Right, float Bottom)
{
    internal UiThickness(float uniform)
        : this(uniform, uniform, uniform, uniform)
    {
    }

    internal UiThickness(float horizontal, float vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }

    internal float Horizontal => Left + Right;
    internal float Vertical => Top + Bottom;
}
