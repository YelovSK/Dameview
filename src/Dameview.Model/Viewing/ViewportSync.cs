using System.Drawing;

namespace Dameview.Viewing;

/// <summary>
/// Shows the same part of every image. Whichever viewport moved since the last sync leads, and
/// the others follow it, whatever the size of their images.
/// </summary>
internal sealed class ViewportSync
{
    private readonly Dictionary<ImageViewport, (float Scale, PointF Center)> _synced = [];

    /// <param name="preferred">Leads when it moved too, or when a viewport has joined since the last sync.</param>
    internal void Sync(List<ImageViewport> viewports, ImageViewport preferred)
    {
        bool joined = false;
        ImageViewport? moved = null;
        foreach (ImageViewport viewport in viewports)
        {
            if (!_synced.TryGetValue(viewport, out (float Scale, PointF Center) last))
            {
                joined = true;
            }
            else if (last != (viewport.Scale, viewport.Center) && (moved is null || viewport == preferred))
            {
                moved = viewport;
            }
        }

        if ((joined ? preferred : moved) is { HasImage: true } leader)
        {
            RelativeView view = leader.GetRelativeView();
            foreach (ImageViewport viewport in viewports)
            {
                if (viewport != leader)
                {
                    viewport.ShowRelativeView(view);
                }
            }
        }

        _synced.Clear();
        foreach (ImageViewport viewport in viewports)
        {
            _synced[viewport] = (viewport.Scale, viewport.Center);
        }
    }
}
