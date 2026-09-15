namespace Dameview.Settings;

internal sealed class SettingsServiceOptions
{
    internal TimeSpan ReloadDelay { get; init; } = TimeSpan.FromMilliseconds(150);
    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(150);
}
