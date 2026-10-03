using Dameview.Imaging.Decoding;
using SharpGen.Runtime;
using Vortice.Mathematics;
using Vortice.WIC;

namespace Dameview.Imaging.Loading;

internal sealed class WicImageTileSource : IImageTileSource
{
    private const int DefaultTileSize = 512;
    private const int OverviewMaximumDimension = 2048;

    private readonly string _path;
    private bool _disposed;

    private WicImageTileSource(
        string path,
        int width,
        int height,
        ImageOrientation orientation,
        DecodedImage overview)
    {
        _path = path;
        Width = width;
        Height = height;
        Orientation = orientation;
        Overview = overview;
    }

    public int Width { get; }
    public int Height { get; }
    public ImageOrientation Orientation { get; }
    public int TileSize => DefaultTileSize;
    public DecodedImage Overview { get; }

    internal static IImageTileSource Open(string path)
    {
        string fullPath = Path.GetFullPath(path);
        using var factory = new IWICImagingFactory2();
        using IWICBitmapDecoder decoder = factory.CreateDecoderFromFileName(
            fullPath,
            FileAccess.Read,
            DecodeOptions.CacheOnDemand);
        using IWICBitmapFrameDecode frame = decoder.GetFrame(0);

        int width = frame.Size.Width;
        int height = frame.Size.Height;
        (int overviewWidth, int overviewHeight) = CalculateOverviewSize(width, height);
        DecodedImage overview = DecodeSource(factory, frame, overviewWidth, overviewHeight);
        return new WicImageTileSource(fullPath, width, height, ImageDecoder.GetOrientation(frame), overview);
    }

    public IImageTileDecoder CreateTileDecoder()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new TileDecoder(_path, Width, Height);
    }

    private static DecodedImage DecodeTileCore(
        IWICImagingFactory2 factory,
        IWICBitmapFrameDecode frame,
        int imageWidth,
        int imageHeight,
        ImageTile tile,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        (int x, int y, int width, int height) = tile.GetSourceBounds(imageWidth, imageHeight);
        using IWICBitmapClipper clipper = factory.CreateBitmapClipper();
        clipper.Initialize(frame, new RectI(x, y, width, height));
        using IWICFormatConverter converter = factory.CreateFormatConverter();
        using IWICBitmapScaler? scaler = width == tile.Width && height == tile.Height
            ? null
            : factory.CreateBitmapScaler();
        if (scaler is null)
        {
            converter.Initialize(clipper, PixelFormat.Format32bppPBGRA).CheckError();
        }
        else
        {
            scaler.Initialize(
                clipper,
                (uint)tile.Width,
                (uint)tile.Height,
                BitmapInterpolationMode.Fant);
            converter.Initialize(scaler, PixelFormat.Format32bppPBGRA).CheckError();
        }

        int stride = DecodedImage.GetStride(tile.Width);
        byte[] pixels = GC.AllocateUninitializedArray<byte>(checked(stride * tile.Height));
        converter.CopyPixels((uint)stride, pixels);
        token.ThrowIfCancellationRequested();
        return new DecodedImage(tile.Width, tile.Height, stride, pixels);
    }

    private sealed class TileDecoder : IImageTileDecoder
    {
        private readonly IWICImagingFactory2 _factory;
        private readonly int _width;
        private readonly int _height;
        private IWICBitmapDecoder? _decoder;
        private IWICBitmapFrameDecode? _frame;
        private bool _disposed;

        internal TileDecoder(string path, int width, int height)
        {
            _factory = new IWICImagingFactory2();
            _width = width;
            _height = height;
            try
            {
                _decoder = _factory.CreateDecoderFromFileName(
                    path,
                    FileAccess.Read,
                    DecodeOptions.CacheOnDemand);
                _frame = _decoder.GetFrame(0);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public DecodedImage DecodeTile(ImageTile tile, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int levelWidth = ImageTile.GetLevelDimension(_width, tile.Level);
            int levelHeight = ImageTile.GetLevelDimension(_height, tile.Level);
            if (tile.X < 0 || tile.Y < 0 || tile.Width <= 0 || tile.Height <= 0
                || tile.X > levelWidth - tile.Width || tile.Y > levelHeight - tile.Height)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tile),
                    "The requested tile is outside the image.");
            }

            return DecodeTileCore(_factory, _frame!, _width, _height, tile, cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _frame?.Dispose();
            _frame = null;
            _decoder?.Dispose();
            _decoder = null;
            _factory.Dispose();
        }
    }

    private static DecodedImage DecodeSource(
        IWICImagingFactory factory,
        IWICBitmapFrameDecode frame,
        int width,
        int height)
    {
        using IWICBitmapScaler scaler = factory.CreateBitmapScaler();
        scaler.Initialize(frame, (uint)width, (uint)height, BitmapInterpolationMode.Fant);
        using IWICFormatConverter converter = factory.CreateFormatConverter();
        converter.Initialize(scaler, PixelFormat.Format32bppPBGRA).CheckError();
        int stride = DecodedImage.GetStride(width);
        byte[] pixels = GC.AllocateUninitializedArray<byte>(checked(stride * height));
        converter.CopyPixels((uint)stride, pixels);
        return new DecodedImage(width, height, stride, pixels);
    }

    private static (int Width, int Height) CalculateOverviewSize(int width, int height)
    {
        if (width >= height)
        {
            return (OverviewMaximumDimension, Math.Max(1, (int)((long)OverviewMaximumDimension * height / width)));
        }

        return (Math.Max(1, (int)((long)OverviewMaximumDimension * width / height)), OverviewMaximumDimension);
    }

    public void Dispose()
    {
        _disposed = true;
        Overview.Pixels.AsSpan().Clear();
    }
}
