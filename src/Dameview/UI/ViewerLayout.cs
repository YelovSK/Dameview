using System.Drawing;

namespace Dameview.UI;

internal readonly record struct ViewerLayout(RectangleF Content)
{
    internal static ViewerLayout Calculate(SizeF size)
    {
        return new ViewerLayout(new RectangleF(0.0f, 0.0f, size.Width, size.Height));
    }
}
