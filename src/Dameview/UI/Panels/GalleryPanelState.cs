using Dameview.Navigation;
using Dameview.UI.Layout;

namespace Dameview.UI.Panels;

internal sealed class GalleryPanelState
{
    internal ScrollOffsetController ScrollOffset { get; } = new();
    internal UiOrientation Orientation { get; set; } = UiOrientation.Vertical;
    internal FolderEntry[] Entries { get; set; } = [];
    internal string? SelectedPath { get; set; }
}
