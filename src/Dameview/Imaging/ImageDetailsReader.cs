using Dameview.Diagnostics;
using Dameview.Win32;

namespace Dameview.Imaging;

/// <summary>What the info panel shows about an image file, each value formatted as Explorer shows it.</summary>
internal sealed record ImageDetails(
    string Folder,
    string? Type,
    string? BitDepth,
    string? Modified,
    string? Created,
    string? Taken,
    string? CameraMaker,
    string? CameraModel,
    string? Lens,
    string? FNumber,
    string? ExposureTime,
    string? Iso,
    string? FocalLength);

/// <summary>Reads <see cref="ImageDetails"/> off the window thread.</summary>
internal sealed class ImageDetailsReader : IDisposable
{
    private static readonly string[] PropertyNames =
    [
        "System.ItemTypeText",
        "System.Image.BitDepth",
        "System.DateModified",
        "System.DateCreated",
        "System.Photo.DateTaken",
        "System.Photo.CameraManufacturer",
        "System.Photo.CameraModel",
        "System.Photo.LensModel",
        "System.Photo.FNumber",
        "System.Photo.ExposureTime",
        "System.Photo.ISOSpeed",
        "System.Photo.FocalLength",
    ];

    // Started on the first read, which only happens once the info panel is opened.
    private ComWorkerQueue<object?>? _worker;

    /// <remarks>A file whose properties cannot be read still gets its folder.</remarks>
    internal async Task<ImageDetails> ReadAsync(string path, CancellationToken cancellationToken)
    {
        _worker ??= new ComWorkerQueue<object?>("Dameview image details", 1, static () => null);
        string?[] values;
        try
        {
            values = await _worker.Enqueue(
                (_, _) => ShellProperties.Read(path, PropertyNames),
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Error("Info", $"Could not read the properties of '{Path.GetFileName(path)}'.", exception);
            values = new string?[PropertyNames.Length];
        }

        return new ImageDetails(
            Path.GetDirectoryName(path) ?? string.Empty,
            values[0],
            values[1],
            values[2],
            values[3],
            values[4],
            values[5],
            values[6],
            values[7],
            values[8],
            values[9],
            values[10],
            values[11]);
    }

    public void Dispose() => _worker?.Dispose();
}
