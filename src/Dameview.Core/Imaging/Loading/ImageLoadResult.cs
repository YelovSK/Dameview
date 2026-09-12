namespace Dameview.Imaging;

internal abstract record ImageLoadResult(string Path);

// Ownership transfers to the receiver when this result is delivered.
internal sealed record ImageLoaded(
    string Path,
    ImageRepresentation Representation,
    bool IsPreview = false) : ImageLoadResult(Path), IDisposable
{
    public void Dispose() => Representation.Dispose();
}

internal sealed record ImageLoadFailed(
    string Path,
    Exception Exception) : ImageLoadResult(Path);
