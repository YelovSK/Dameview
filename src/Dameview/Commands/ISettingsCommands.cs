using Dameview.Navigation;
using Dameview.Settings;
using Dameview.UI;

namespace Dameview.Commands;

internal interface ISettingsCommands
{
    public void SetTheme(Theme theme);

    public void SetAnimationsEnabled(bool enabled);

    public void SetGalleryThumbnailSize(GalleryThumbnailSize size);

    public void SetSort(FolderSort sort);

    public void ActivateUpdate();
}
