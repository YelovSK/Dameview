namespace Dameview.Imaging.Loading;

internal interface IImageTileSource : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public int TileSize { get; }
    public DecodedImage Overview { get; }

    public IImageTileDecoder CreateTileDecoder();
}

internal interface IImageTileDecoder : IDisposable
{
    public DecodedImage DecodeTile(ImageTile tile, CancellationToken cancellationToken);
}

internal readonly record struct ImageTile(
    int X,
    int Y,
    int Width,
    int Height,
    int Level = 0)
{
    internal int SourceScale => GetSourceScale(Level);

    internal (int X, int Y, int Width, int Height) GetSourceBounds(
        int sourceWidth,
        int sourceHeight)
    {
        int scale = SourceScale;
        int sourceX = checked(X * scale);
        int sourceY = checked(Y * scale);
        int sourceRight = (int)Math.Min(sourceWidth, checked(((long)X + Width) * scale));
        int sourceBottom = (int)Math.Min(sourceHeight, checked(((long)Y + Height) * scale));
        return (sourceX, sourceY, sourceRight - sourceX, sourceBottom - sourceY);
    }

    internal static int GetLevelDimension(int sourceDimension, int level)
    {
        int scale = GetSourceScale(level);
        return (int)(((long)sourceDimension + scale - 1) / scale);
    }

    internal static int GetSourceScale(int level)
    {
        if (level is < 0 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        return 1 << level;
    }
}
