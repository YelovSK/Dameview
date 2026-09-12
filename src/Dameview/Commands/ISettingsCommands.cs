using Dameview.Navigation;
using Dameview.Settings;

namespace Dameview.Commands;

internal interface ISettingsCommands
{
    public void SetTheme(ThemeId theme);

    public void SetAnimationsEnabled(bool enabled);

    public void SetGalleryThumbnailSize(GalleryThumbnailSize size);

    public void SetSort(FolderSort sort);

    public void ActivateUpdate();
}
