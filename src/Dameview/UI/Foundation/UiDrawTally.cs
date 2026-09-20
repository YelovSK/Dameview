namespace Dameview.UI.Foundation;

/// <summary>Counts what a frame's drawing actually asked of Direct2D.</summary>
/// <remarks>
/// Every element pays for a transform and a clip whether or not it paints anything, so knowing
/// how many were visited alongside how many brushes were prepared says whether a slow frame is
/// issuing too many calls or paying too much for each one.
/// </remarks>
internal sealed class UiDrawTally
{
    internal int Elements { get; set; }
    internal int Operations { get; set; }

    internal void Reset()
    {
        Elements = 0;
        Operations = 0;
    }
}
