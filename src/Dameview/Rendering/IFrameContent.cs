using System.Drawing;
using Dameview.UI.Foundation;

namespace Dameview.Rendering;

/// <summary>What a frame shows, asked first what changed and then to draw at least that.</summary>
internal interface IFrameContent
{
    /// <summary>Returns what changed since the previous frame.</summary>
    public UiDirtyRegion PrepareFrame(Size pixelSize);

    /// <summary>Draws everything within the region, which can be larger than what changed.</summary>
    public void DrawFrame(UiDirtyRegion region);
}
