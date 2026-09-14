using System.Drawing;
using Dameview.Imaging.Decoding;
using SharpGen.Runtime;
using Vortice.Mathematics;
using Vortice.WIC;

namespace Dameview.Imaging.Loading;

internal sealed class WicImageTileSource : IImageTileSource
{
    private const int DefaultTileSize = 512;
    private const int OverviewMaximumDimension = 2048;
    private const int BytesPerPixel = 4;

    private readonly string _path;
    private readonly int _rawWidth;
    private readonly int _rawHeight;
    private readonly ExifOrientation _orientation;
    private bool _disposed;

    private WicImageTileSource(
        string path,
        int rawWidth,
        int rawHeight,
        ExifOrientation orientation,
        DecodedImage overview)
    {
        _path = path;
        _rawWidth = rawWidth;
        _rawHeight = rawHeight;
        _orientation = orientation;
        Overview = overview;
        (Width, Height) = GetDisplaySize(rawWidth, rawHeight, orientation);
    }

    public int Width { get; }
    public int Height { get; }
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

        int rawWidth = frame.Size.Width;
        int rawHeight = frame.Size.Height;
        ExifOrientation orientation = ImageDecoder.GetExifOrientation(frame);
        (int overviewWidth, int overviewHeight) = CalculateOverviewSize(rawWidth, rawHeight);
        DecodedImage overview = DecodeSource(
            factory,
            frame,
            orientation,
            overviewWidth,
            overviewHeight);
        return new WicImageTileSource(fullPath, rawWidth, rawHeight, orientation, overview);
    }

    public IImageTileDecoder CreateTileDecoder()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new TileDecoder(
            _path,
            _rawWidth,
            _rawHeight,
            _orientation,
            Width,
            Height);
    }

    private static DecodedImage DecodeTileCore(
        IWICImagingFactory2 factory,
        IWICBitmapFrameDecode frame,
        ExifOrientation orientation,
        int rawImageWidth,
        int rawImageHeight,
        int displayImageWidth,
        int displayImageHeight,
        ImageTile tile,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        (int displayX, int displayY, int displayWidth, int displayHeight) =
            tile.GetSourceBounds(displayImageWidth, displayImageHeight);
        (int rawX, int rawY, int rawWidth, int rawHeight) = GetRawBounds(
            orientation,
            rawImageWidth,
            rawImageHeight,
            displayX,
            displayY,
            displayWidth,
            displayHeight);
        using IWICBitmapClipper clipper = factory.CreateBitmapClipper();
        clipper.Initialize(frame, new RectI(rawX, rawY, rawWidth, rawHeight));
        using IWICFormatConverter converter = factory.CreateFormatConverter();
        bool swapsDimensions = SwapsDimensions(orientation);
        int outputRawWidth = swapsDimensions ? tile.Height : tile.Width;
        int outputRawHeight = swapsDimensions ? tile.Width : tile.Height;
        using IWICBitmapScaler? scaler = rawWidth == outputRawWidth && rawHeight == outputRawHeight
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
                (uint)outputRawWidth,
                (uint)outputRawHeight,
                BitmapInterpolationMode.Fant);
            converter.Initialize(scaler, PixelFormat.Format32bppPBGRA).CheckError();
        }

        int rawStride = checked(outputRawWidth * BytesPerPixel);
        byte[] rawPixels = GC.AllocateUninitializedArray<byte>(checked(rawStride * outputRawHeight));
        converter.CopyPixels((uint)rawStride, rawPixels);
        token.ThrowIfCancellationRequested();

        return TransformTile(
            orientation,
            outputRawWidth,
            outputRawHeight,
            rawPixels,
            rawStride);
    }

    private sealed class TileDecoder : IImageTileDecoder
    {
        private readonly IWICImagingFactory2 _factory;
        private readonly int _rawWidth;
        private readonly int _rawHeight;
        private readonly ExifOrientation _orientation;
        private readonly int _width;
        private readonly int _height;
        private IWICBitmapDecoder? _decoder;
        private IWICBitmapFrameDecode? _frame;
        private bool _disposed;

        internal TileDecoder(
            string path,
            int rawWidth,
            int rawHeight,
            ExifOrientation orientation,
            int width,
            int height)
        {
            _factory = new IWICImagingFactory2();
            _rawWidth = rawWidth;
            _rawHeight = rawHeight;
            _orientation = orientation;
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

            return DecodeTileCore(
                _factory,
                _frame!,
                _orientation,
                _rawWidth,
                _rawHeight,
                _width,
                _height,
                tile,
                cancellationToken);
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

    internal static DecodedImage TransformTile(
        ExifOrientation orientation,
        int rawImageWidth,
        int rawImageHeight,
        byte[] rawPixels,
        int rawStride)
    {
        (int width, int height) = GetDisplaySize(
            rawImageWidth,
            rawImageHeight,
            orientation);
        int outputStride = checked(width * BytesPerPixel);
        if (orientation == ExifOrientation.Normal
            && rawStride == outputStride
            && rawPixels.Length == checked(outputStride * height))
        {
            return new DecodedImage(width, height, outputStride, rawPixels);
        }

        byte[] output = GC.AllocateUninitializedArray<byte>(checked(outputStride * height));
        if (orientation == ExifOrientation.MirrorVertical)
        {
            for (int localY = 0; localY < height; localY++)
            {
                Point source = MapDisplayToRaw(
                    orientation,
                    rawImageWidth,
                    rawImageHeight,
                    0,
                    localY);
                Buffer.BlockCopy(
                    rawPixels,
                    source.Y * rawStride + source.X * BytesPerPixel,
                    output,
                    localY * outputStride,
                    outputStride);
            }

            return new DecodedImage(width, height, outputStride, output);
        }

        for (int localY = 0; localY < height; localY++)
        {
            for (int localX = 0; localX < width; localX++)
            {
                Point source = MapDisplayToRaw(
                    orientation,
                    rawImageWidth,
                    rawImageHeight,
                    localX,
                    localY);
                int sourceOffset = source.Y * rawStride + source.X * BytesPerPixel;
                int outputOffset = localY * outputStride + localX * BytesPerPixel;
                output[outputOffset] = rawPixels[sourceOffset];
                output[outputOffset + 1] = rawPixels[sourceOffset + 1];
                output[outputOffset + 2] = rawPixels[sourceOffset + 2];
                output[outputOffset + 3] = rawPixels[sourceOffset + 3];
            }
        }

        return new DecodedImage(width, height, outputStride, output);
    }

    internal static (int X, int Y, int Width, int Height) GetRawBounds(
        ExifOrientation orientation,
        int rawImageWidth,
        int rawImageHeight,
        int x,
        int y,
        int width,
        int height)
    {
        Span<Point> corners =
        [
            MapDisplayToRaw(orientation, rawImageWidth, rawImageHeight, x, y),
            MapDisplayToRaw(orientation, rawImageWidth, rawImageHeight, x + width - 1, y),
            MapDisplayToRaw(orientation, rawImageWidth, rawImageHeight, x, y + height - 1),
            MapDisplayToRaw(orientation, rawImageWidth, rawImageHeight, x + width - 1, y + height - 1),
        ];
        int minX = corners[0].X;
        int maxX = corners[0].X;
        int minY = corners[0].Y;
        int maxY = corners[0].Y;
        foreach (Point corner in corners)
        {
            minX = Math.Min(minX, corner.X);
            maxX = Math.Max(maxX, corner.X);
            minY = Math.Min(minY, corner.Y);
            maxY = Math.Max(maxY, corner.Y);
        }

        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Point MapDisplayToRaw(
        ExifOrientation orientation,
        int rawImageWidth,
        int rawImageHeight,
        int x,
        int y)
    {
        return orientation switch
        {
            ExifOrientation.MirrorHorizontal => new Point(rawImageWidth - 1 - x, y),
            ExifOrientation.Rotate180 => new Point(rawImageWidth - 1 - x, rawImageHeight - 1 - y),
            ExifOrientation.MirrorVertical => new Point(x, rawImageHeight - 1 - y),
            ExifOrientation.Transpose => new Point(y, x),
            ExifOrientation.Rotate90Clockwise => new Point(y, rawImageHeight - 1 - x),
            ExifOrientation.Transverse => new Point(rawImageWidth - 1 - y, rawImageHeight - 1 - x),
            ExifOrientation.Rotate270Clockwise => new Point(rawImageWidth - 1 - y, x),
            _ => new Point(x, y),
        };
    }

    private static DecodedImage DecodeSource(
        IWICImagingFactory factory,
        IWICBitmapFrameDecode frame,
        ExifOrientation orientation,
        int width,
        int height)
    {
        using IWICBitmapScaler scaler = factory.CreateBitmapScaler();
        scaler.Initialize(frame, (uint)width, (uint)height, BitmapInterpolationMode.Fant);
        using IWICFormatConverter converter = factory.CreateFormatConverter();
        converter.Initialize(scaler, PixelFormat.Format32bppPBGRA).CheckError();
        int stride = checked(width * BytesPerPixel);
        byte[] pixels = GC.AllocateUninitializedArray<byte>(checked(stride * height));
        converter.CopyPixels((uint)stride, pixels);
        return ImageDecoder.ApplyExifOrientation(orientation, width, height, stride, pixels);
    }

    private static (int Width, int Height) CalculateOverviewSize(int width, int height)
    {
        if (width >= height)
        {
            return (OverviewMaximumDimension, Math.Max(1, (int)((long)OverviewMaximumDimension * height / width)));
        }

        return (Math.Max(1, (int)((long)OverviewMaximumDimension * width / height)), OverviewMaximumDimension);
    }

    internal static (int Width, int Height) GetDisplaySize(
        int width,
        int height,
        ExifOrientation orientation)
    {
        return SwapsDimensions(orientation)
            ? (height, width)
            : (width, height);
    }

    private static bool SwapsDimensions(ExifOrientation orientation) =>
        orientation is ExifOrientation.Transpose
            or ExifOrientation.Rotate90Clockwise
            or ExifOrientation.Transverse
            or ExifOrientation.Rotate270Clockwise;

    public void Dispose()
    {
        _disposed = true;
        Overview.Pixels.AsSpan().Clear();
    }
}
