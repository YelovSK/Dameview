using System.Drawing;
using System.Numerics;
using Dameview.Imaging;
using Dameview.Imaging.Loading;
using Dameview.Rendering;
using Dameview.UI.Foundation;
using Dameview.Viewing;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI.Presentation;

// UI-thread owned. The source is borrowed from ImageLoaded; this class owns the
// overview and tile GPU resources but never disposes the source itself.
internal sealed class TiledImageRenderer : IDisposable
{
    private const long MaximumTileBytes = 128L * 1024L * 1024L;

    private readonly ID2D1DeviceContext _deviceContext;
    private readonly IImageTileSource _source;
    private readonly ImageViewport _viewport;
    private readonly Action _invalidate;
    private readonly ID2D1Bitmap1 _overview;
    private readonly TileDecodeScheduler _scheduler;
    private readonly Dictionary<ImageTile, TileEntry> _tiles = [];
    private readonly HashSet<ImageTile> _desiredTiles = [];
    private readonly HashSet<ImageTile> _nextDesiredTiles = [];
    private readonly HashSet<ImageTile> _failedTiles = [];
    // The visible tiles, nearest the middle of the view first once sorted.
    private readonly List<TileCandidate> _candidates = [];
    private readonly List<ImageTile> _requests = [];
    private long _tileBytes;
    private bool _disposed;

    internal TiledImageRenderer(
        ID2D1DeviceContext deviceContext,
        IImageTileSource source,
        ImageViewport viewport,
        Action invalidate)
    {
        _deviceContext = deviceContext;
        _source = source;
        _viewport = viewport;
        _invalidate = invalidate;
        if (source.TileSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "Tile size must be positive.");
        }

        _overview = D2DBitmapFactory.Create(deviceContext, source.Overview);
        _scheduler = new TileDecodeScheduler(source, CompleteTile);
    }

    internal void Draw(in UiDrawContext context, float viewportWidthPixels, float viewportHeightPixels)
    {
        // Everything below is drawn in stored image pixels.
        Matrix3x2 toViewport = _viewport.GetImageTransform(new SizeF(_source.Width, _source.Height));
        using TransformScope scope = context.PushTransform(
            toViewport * Matrix3x2.CreateScale(context.PixelsToDips(1.0f)));
        context.DrawBitmap(_overview, new Rect(0.0f, 0.0f, _source.Width, _source.Height));

        CollectVisibleTiles(toViewport, viewportWidthPixels, viewportHeightPixels);
        SelectAndRequestTiles();
        foreach (TileCandidate candidate in _candidates)
        {
            if (!_tiles.TryGetValue(candidate.Tile, out TileEntry? entry))
            {
                continue;
            }

            entry.LastUsed = Environment.TickCount64;
            (int sourceX, int sourceY, int sourceWidth, int sourceHeight) =
                candidate.Tile.GetSourceBounds(_source.Width, _source.Height);
            context.DrawBitmap(entry.Bitmap, new Rect(sourceX, sourceY, sourceWidth, sourceHeight));
        }
    }

    internal static int SelectMipLevel(float viewportScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportScale);
        int level = (int)Math.Floor(Math.Log2(1.0 / viewportScale));
        return Math.Clamp(level, 0, 30);
    }

    // Leaves no candidates when the overview is already sharp enough or nothing is in view.
    private void CollectVisibleTiles(Matrix3x2 toViewport, float viewportWidthPixels, float viewportHeightPixels)
    {
        _candidates.Clear();
        float scale = _viewport.Scale;
        float overviewScale = MathF.Min(
            (float)_overview.PixelSize.Width / _source.Width,
            (float)_overview.PixelSize.Height / _source.Height);
        if (scale <= overviewScale)
        {
            return;
        }

        // All four corners, since the image can be partway through a turn.
        Matrix3x2.Invert(toViewport, out Matrix3x2 toImage);
        var topLeft = Vector2.Transform(Vector2.Zero, toImage);
        var topRight = Vector2.Transform(new Vector2(viewportWidthPixels, 0.0f), toImage);
        var bottomLeft = Vector2.Transform(new Vector2(0.0f, viewportHeightPixels), toImage);
        var bottomRight = Vector2.Transform(new Vector2(viewportWidthPixels, viewportHeightPixels), toImage);
        var visibleMin = Vector2.Min(Vector2.Min(topLeft, topRight), Vector2.Min(bottomLeft, bottomRight));
        var visibleMax = Vector2.Max(Vector2.Max(topLeft, topRight), Vector2.Max(bottomLeft, bottomRight));
        int left = Math.Clamp((int)MathF.Floor(visibleMin.X), 0, _source.Width - 1);
        int top = Math.Clamp((int)MathF.Floor(visibleMin.Y), 0, _source.Height - 1);
        int right = Math.Clamp((int)MathF.Ceiling(visibleMax.X), 0, _source.Width);
        int bottom = Math.Clamp((int)MathF.Ceiling(visibleMax.Y), 0, _source.Height);
        if (right <= left || bottom <= top)
        {
            return;
        }

        int level = SelectMipLevel(scale);
        int sourceScale = ImageTile.GetSourceScale(level);
        int levelWidth = ImageTile.GetLevelDimension(_source.Width, level);
        int levelHeight = ImageTile.GetLevelDimension(_source.Height, level);
        int levelLeft = left / sourceScale;
        int levelTop = top / sourceScale;
        int levelRight = Math.Min(
            levelWidth,
            (int)(((long)right + sourceScale - 1) / sourceScale));
        int levelBottom = Math.Min(
            levelHeight,
            (int)(((long)bottom + sourceScale - 1) / sourceScale));
        int tileSize = _source.TileSize;

        int firstTileX = levelLeft / tileSize;
        int firstTileY = levelTop / tileSize;
        int lastTileX = Math.Max(firstTileX, (levelRight - 1) / tileSize);
        int lastTileY = Math.Max(firstTileY, (levelBottom - 1) / tileSize);
        double centerX = (levelLeft + levelRight) / 2.0;
        double centerY = (levelTop + levelBottom) / 2.0;

        for (int tileY = firstTileY; tileY <= lastTileY; tileY++)
        {
            for (int tileX = firstTileX; tileX <= lastTileX; tileX++)
            {
                int x = tileX * tileSize;
                int y = tileY * tileSize;
                int width = Math.Min(tileSize, levelWidth - x);
                int height = Math.Min(tileSize, levelHeight - y);
                double deltaX = x + width / 2.0 - centerX;
                double deltaY = y + height / 2.0 - centerY;
                _candidates.Add(new TileCandidate(
                    new ImageTile(x, y, width, height, level),
                    deltaX * deltaX + deltaY * deltaY));
            }
        }
    }

    private void SelectAndRequestTiles()
    {
        _candidates.Sort(static (left, right) => left.Priority.CompareTo(right.Priority));
        _nextDesiredTiles.Clear();
        _requests.Clear();
        long selectedBytes = 0;
        // The overview remains beneath tiles outside this cache-sized detail set.
        foreach (TileCandidate candidate in _candidates)
        {
            ImageTile tile = candidate.Tile;
            if (_failedTiles.Contains(tile))
            {
                continue;
            }

            long bytes = GetTileBytes(tile);
            if (selectedBytes + bytes > MaximumTileBytes
                || _nextDesiredTiles.Count == TileDecodeScheduler.MaximumPendingTiles)
            {
                continue;
            }

            selectedBytes += bytes;
            _nextDesiredTiles.Add(tile);
            if (!_tiles.ContainsKey(tile))
            {
                _requests.Add(tile);
            }
        }

        if (_desiredTiles.SetEquals(_nextDesiredTiles))
        {
            return;
        }

        _desiredTiles.Clear();
        _desiredTiles.UnionWith(_nextDesiredTiles);
        _scheduler.ReplaceRequests(_requests);
    }

    // A decode that is no longer wanted is kept anyway: eviction drops unwanted tiles first,
    // and panning back would otherwise pay for the same decode again.
    private void CompleteTile(ImageTile tile, DecodedImage? image)
    {
        if (_disposed)
        {
            return;
        }

        if (image is null)
        {
            FailTile(tile);
            return;
        }

        ID2D1Bitmap1 bitmap;
        try
        {
            bitmap = D2DBitmapFactory.Create(_deviceContext, image);
        }
        catch
        {
            FailTile(tile);
            return;
        }

        if (_tiles.Remove(tile, out TileEntry? previous))
        {
            _tileBytes -= GetTileBytes(tile);
            previous.Bitmap.Dispose();
        }

        _tiles.Add(tile, new TileEntry(bitmap));
        _tileBytes += GetTileBytes(tile);
        EvictTiles();
        _invalidate();
    }

    private void FailTile(ImageTile tile)
    {
        if (_desiredTiles.Remove(tile))
        {
            _failedTiles.Add(tile);
            _invalidate();
        }
    }

    private void EvictTiles()
    {
        // The wanted tiles fit the budget on their own, so going over it always leaves an
        // unwanted tile to evict.
        while (_tileBytes > MaximumTileBytes)
        {
            ImageTile oldestKey = default;
            TileEntry? oldest = null;
            foreach ((ImageTile key, TileEntry candidate) in _tiles)
            {
                if (!_desiredTiles.Contains(key)
                    && (oldest is null || candidate.LastUsed < oldest.LastUsed))
                {
                    oldestKey = key;
                    oldest = candidate;
                }
            }

            if (oldest is null)
            {
                return;
            }

            _tiles.Remove(oldestKey);
            _tileBytes -= GetTileBytes(oldestKey);
            oldest.Bitmap.Dispose();
        }
    }

    private static long GetTileBytes(ImageTile tile) => (long)tile.Width * tile.Height * 4;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scheduler.Dispose();
        _overview.Dispose();
        foreach (TileEntry entry in _tiles.Values)
        {
            entry.Bitmap.Dispose();
        }
    }

    private readonly record struct TileCandidate(ImageTile Tile, double Priority);

    private sealed class TileEntry(ID2D1Bitmap1 bitmap)
    {
        internal ID2D1Bitmap1 Bitmap { get; } = bitmap;
        internal long LastUsed { get; set; } = Environment.TickCount64;
    }
}
