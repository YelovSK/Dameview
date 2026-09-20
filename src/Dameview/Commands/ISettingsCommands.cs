using Dameview.Settings;

namespace Dameview.Commands;

internal interface ISettingsCommands
{
    /// <summary>Applies a change to the stored settings and saves them.</summary>
    public void UpdateSettings(Func<AppSettings, AppSettings> change);

    public void ActivateUpdate();
}
