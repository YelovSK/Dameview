using System.Drawing;
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
    private readonly HashSet<ImageTile> _visibleTiles = [];
    private readonly HashSet<ImageTile> _nextVisibleTiles = [];
    private readonly HashSet<ImageTile> _desiredTiles = [];
    private readonly HashSet<ImageTile> _nextDesiredTiles = [];
    private readonly HashSet<ImageTile> _failedTiles = [];
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
        RectangleF destination = _viewport.GetDestinationRectangle();
        DrawBitmap(
            context,
            _overview,
            destination,
            _overview.PixelSize.Width,
            _overview.PixelSize.Height);

        float scale = _viewport.Scale;
        float overviewScale = MathF.Min(
            (float)_overview.PixelSize.Width / _source.Width,
            (float)_overview.PixelSize.Height / _source.Height);
        if (scale <= overviewScale)
        {
            ClearTileRequests();
            return;
        }

        int left = Math.Clamp((int)MathF.Floor(Math.Max(0.0f, -destination.X / scale)), 0, _source.Width - 1);
        int top = Math.Clamp((int)MathF.Floor(Math.Max(0.0f, -destination.Y / scale)), 0, _source.Height - 1);
        int right = Math.Clamp((int)MathF.Ceiling((viewportWidthPixels - destination.X) / scale), 0, _source.Width);
        int bottom = Math.Clamp((int)MathF.Ceiling((viewportHeightPixels - destination.Y) / scale), 0, _source.Height);
        if (right <= left || bottom <= top)
        {
            ClearTileRequests();
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

        _nextVisibleTiles.Clear();
        _candidates.Clear();
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
                var tile = new ImageTile(x, y, width, height, level);
                _nextVisibleTiles.Add(tile);
                double deltaX = x + width / 2.0 - centerX;
                double deltaY = y + height / 2.0 - centerY;
                _candidates.Add(new TileCandidate(tile, deltaX * deltaX + deltaY * deltaY));
            }
        }

        SelectAndRequestTiles();
        foreach (ImageTile tile in _visibleTiles)
        {
            if (!_tiles.TryGetValue(tile, out TileEntry? entry))
            {
                continue;
            }

            entry.LastUsed = Environment.TickCount64;
            (int sourceX, int sourceY, int sourceWidth, int sourceHeight) =
                tile.GetSourceBounds(_source.Width, _source.Height);
            DrawBitmap(context, entry.Bitmap, new RectangleF(
                destination.X + sourceX * scale,
                destination.Y + sourceY * scale,
                sourceWidth * scale,
                sourceHeight * scale), tile.Width, tile.Height);
        }
    }

    internal static int SelectMipLevel(float viewportScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportScale);
        int level = (int)Math.Floor(Math.Log2(1.0 / viewportScale));
        return Math.Clamp(level, 0, 30);
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

            long bytes = checked((long)tile.Width * tile.Height * 4);
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

        _visibleTiles.Clear();
        _visibleTiles.UnionWith(_nextVisibleTiles);
        if (_desiredTiles.SetEquals(_nextDesiredTiles))
        {
            return;
        }

        _desiredTiles.Clear();
        _desiredTiles.UnionWith(_nextDesiredTiles);
        _scheduler.ReplaceRequests(_requests);
    }

    private void ClearTileRequests()
    {
        _visibleTiles.Clear();
        if (_desiredTiles.Count == 0)
        {
            return;
        }

        _desiredTiles.Clear();
        _requests.Clear();
        _scheduler.ReplaceRequests(_requests);
    }

    private void CompleteTile(ImageTile tile, DecodedImage? image)
    {
        if (_disposed || !_desiredTiles.Contains(tile))
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
            _tileBytes -= previous.Bytes;
            previous.Bitmap.Dispose();
        }

        var entry = new TileEntry(bitmap, image.Pixels.LongLength);
        _tiles.Add(tile, entry);
        _tileBytes += entry.Bytes;
        EvictTiles();
        _invalidate();
    }

    private void FailTile(ImageTile tile)
    {
        if (!_disposed && _desiredTiles.Contains(tile))
        {
            _failedTiles.Add(tile);
            _desiredTiles.Remove(tile);
            _invalidate();
        }
    }

    private void EvictTiles()
    {
        while (_tileBytes > MaximumTileBytes && _tiles.Count > 0)
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
                foreach ((ImageTile key, TileEntry candidate) in _tiles)
                {
                    if (oldest is null || candidate.LastUsed < oldest.LastUsed)
                    {
                        oldestKey = key;
                        oldest = candidate;
                    }
                }
            }

            _tiles.Remove(oldestKey);
            _tileBytes -= oldest!.Bytes;
            oldest.Bitmap.Dispose();
        }
    }

    private static void DrawBitmap(
        in UiDrawContext context,
        ID2D1Bitmap1 bitmap,
        RectangleF destination,
        int sourceWidth,
        int sourceHeight)
    {
        context.DrawBitmap(
            bitmap,
            new Rect(
                context.PixelsToDips(destination.X),
                context.PixelsToDips(destination.Y),
                context.PixelsToDips(destination.Width),
                context.PixelsToDips(destination.Height)),
            new Rect(0.0f, 0.0f, sourceWidth, sourceHeight));
    }

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

        _tiles.Clear();
        _visibleTiles.Clear();
        _nextVisibleTiles.Clear();
        _desiredTiles.Clear();
        _nextDesiredTiles.Clear();
        _failedTiles.Clear();
        _candidates.Clear();
        _requests.Clear();
    }

    private readonly record struct TileCandidate(ImageTile Tile, double Priority);

    private sealed class TileEntry(ID2D1Bitmap1 bitmap, long bytes)
    {
        internal ID2D1Bitmap1 Bitmap { get; } = bitmap;
        internal long Bytes { get; } = bytes;
        internal long LastUsed { get; set; } = Environment.TickCount64;
    }
}
