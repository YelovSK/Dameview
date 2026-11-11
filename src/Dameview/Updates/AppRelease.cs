namespace Dameview.Updates;

internal sealed record AppRelease(string Tag, Version Version)
{
    /// <returns>Null when the tag isn't a 'v' followed by a version.</returns>
    internal static AppRelease? FromTag(string tag)
    {
        if (tag.Length < 2
            || tag[0] is not ('v' or 'V')
            || !Version.TryParse(tag.AsSpan(1), out Version? version))
        {
            return null;
        }

        return new AppRelease(tag, Normalize(version));
    }

    internal static Version Normalize(Version version)
    {
        return new Version(
            version.Major,
            version.Minor,
            Math.Max(0, version.Build),
            Math.Max(0, version.Revision));
    }
}
