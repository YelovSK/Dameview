using System.Collections.Concurrent;
using Dameview.Platform;
using SharpGen.Runtime;
using Vortice.WIC;

namespace Dameview.Imaging;

internal sealed class WicGifAnimationDecoder : IAnimatedImageDecoder
{
    public bool CanDecode(string path) =>
        string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase);

    public IAnimationSession Open(string path) => new Session(path);

    private sealed class Session : IAnimationSession
    {
        private const int QueueCapacity = 2;
        private readonly object _stateLock = new();
        private readonly BlockingCollection<AnimationFrame> _frames = new(QueueCapacity);
        private readonly CancellationTokenSource _cancellation = new();
        private readonly int _frameCount;
        private readonly int _loopCount;
        private Exception? _error;
        private bool _isComplete;
        private bool _disposed;
        private bool _resourcesDisposed;

        internal Session(string path)
        {
            using var factory = new IWICImagingFactory2();
            using IWICBitmapDecoder decoder = factory.CreateDecoderFromFileName(
                Path.GetFullPath(path), FileAccess.Read, DecodeOptions.CacheOnLoad);
            _frameCount = checked((int)decoder.FrameCount);
            if (_frameCount == 0)
            {
                throw new InvalidDataException("The GIF contains no frames.");
            }

            using IWICMetadataQueryReader metadata = decoder.MetadataQueryReader;
            Width = MetadataUInt(metadata, "/logscrdesc/Width");
            Height = MetadataUInt(metadata, "/logscrdesc/Height");
            if (Width <= 0 || Height <= 0)
            {
                throw new InvalidDataException("The GIF has an invalid canvas size.");
            }

            _loopCount = ReadLoopCount(metadata);

            FirstFrame = DecodeFrame(factory, decoder, 0, new byte[checked(Width * Height * 4)]);
            if (_frameCount > 1)
            {
                var worker = new Thread(() => DecodeFrames(path))
                {
                    IsBackground = true,
                    Name = "Dameview GIF decoder",
                };
                worker.Start();
            }
            else
            {
                _frames.CompleteAdding();
                _isComplete = true;
            }
        }

        public AnimationFrame FirstFrame { get; }
        public int Width { get; }
        public int Height { get; }
        public bool IsAnimated => _frameCount > 1;
        public bool IsInfiniteLoop => _loopCount == 0;
        public bool IsComplete
        {
            get
            {
                lock (_stateLock)
                {
                    return _isComplete;
                }
            }
        }

        public Exception? Error
        {
            get
            {
                lock (_stateLock)
                {
                    return _error;
                }
            }
        }

        public bool TryGetReadyFrame(out AnimationFrame frame) => _frames.TryTake(out frame!);

        private void DecodeFrames(string path)
        {
            bool comInitialized = false;
            try
            {
                NativeMethods.InitializeComApartment(ComApartment.MultiThreaded);
                comInitialized = true;
                using var factory = new IWICImagingFactory2();
                using IWICBitmapDecoder decoder = factory.CreateDecoderFromFileName(
                    Path.GetFullPath(path), FileAccess.Read, DecodeOptions.CacheOnLoad);
                byte[] canvas = new byte[checked(Width * Height * 4)];
                int completedLoops = 0;
                bool firstPass = true;

                while (!_cancellation.IsCancellationRequested &&
                       (IsInfiniteLoop || completedLoops < _loopCount))
                {
                    if (!firstPass)
                    {
                        Array.Clear(canvas);
                    }
                    else
                    {
                        DecodeFrame(factory, decoder, 0, canvas);
                    }

                    int startIndex = firstPass ? 1 : 0;
                    for (int index = startIndex; index < _frameCount; index++)
                    {
                        if (_cancellation.IsCancellationRequested)
                        {
                            return;
                        }

                        AnimationFrame frame = DecodeFrame(factory, decoder, index, canvas);
                        _frames.Add(frame, _cancellation.Token);
                    }

                    completedLoops++;
                    firstPass = false;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                lock (_stateLock)
                {
                    _error = exception;
                }
            }
            finally
            {
                _frames.CompleteAdding();
                bool disposeResources;
                lock (_stateLock)
                {
                    _isComplete = true;
                    disposeResources = _disposed;
                }

                if (disposeResources)
                {
                    DisposeResources();
                }

                if (comInitialized)
                {
                    NativeMethods.UninitializeComApartment();
                }
            }
        }

        private AnimationFrame DecodeFrame(
            IWICImagingFactory2 factory,
            IWICBitmapDecoder decoder,
            int index,
            byte[] canvas)
        {
            using IWICBitmapFrameDecode frame = decoder.GetFrame((uint)index);
            using IWICFormatConverter converter = factory.CreateFormatConverter();
            converter.Initialize(frame, PixelFormat.Format32bppPBGRA).CheckError();

            int frameWidth = frame.Size.Width;
            int frameHeight = frame.Size.Height;
            int frameStride = checked(frameWidth * 4);
            byte[] pixels = GC.AllocateUninitializedArray<byte>(checked(frameStride * frameHeight));
            converter.CopyPixels((uint)frameStride, pixels);

            int left = MetadataUInt(frame, "/imgdesc/Left");
            int top = MetadataUInt(frame, "/imgdesc/Top");
            if (frameWidth > Width || frameHeight > Height ||
                left > Width - frameWidth || top > Height - frameHeight)
            {
                throw new InvalidDataException("A GIF frame lies outside the image canvas.");
            }

            int disposal = MetadataUInt(frame, "/grctlext/Disposal");
            bool hasTransparency = MetadataUInt(frame, "/grctlext/TransparencyFlag") != 0;
            byte[]? previousCanvas = disposal == 3 ? (byte[])canvas.Clone() : null;
            for (int y = 0; y < frameHeight; y++)
            {
                int sourceOffset = y * frameStride;
                int destinationOffset = ((top + y) * Width + left) * 4;
                for (int x = 0; x < frameWidth; x++)
                {
                    int sourcePixel = sourceOffset + x * 4;
                    if (hasTransparency && pixels[sourcePixel + 3] == 0)
                    {
                        continue;
                    }

                    Buffer.BlockCopy(pixels, sourcePixel, canvas, destinationOffset + x * 4, 4);
                }
            }

            byte[] snapshot = (byte[])canvas.Clone();
            int delay = MetadataUInt(frame, "/grctlext/Delay");
            if (disposal == 2)
            {
                for (int y = 0; y < frameHeight; y++)
                {
                    Array.Clear(canvas, ((top + y) * Width + left) * 4, frameStride);
                }
            }
            else if (previousCanvas is not null)
            {
                Buffer.BlockCopy(previousCanvas, 0, canvas, 0, canvas.Length);
            }

            return new AnimationFrame(
                new DecodedImage(Width, Height, Width * 4, snapshot),
                TimeSpan.FromMilliseconds(Math.Clamp(delay * 10, 10, 60000)));
        }

        private static int MetadataUInt(IWICBitmapFrameDecode frame, string name)
        {
            try
            {
                using IWICMetadataQueryReader reader = frame.MetadataQueryReader;
                return MetadataUInt(reader, name);
            }
            catch (SharpGenException)
            {
                return 0;
            }
        }

        private static int MetadataUInt(IWICMetadataQueryReader reader, string name)
        {
            return Convert.ToInt32(
                reader.GetMetadataByName(name).Value,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int ReadLoopCount(IWICMetadataQueryReader metadata)
        {
            try
            {
                if (metadata.GetMetadataByName("/appext/application").Value is not byte[] application ||
                    (!application.AsSpan().SequenceEqual("NETSCAPE2.0"u8) &&
                     !application.AsSpan().SequenceEqual("ANIMEXTS1.0"u8)) ||
                    metadata.GetMetadataByName("/appext/data").Value is not byte[] data ||
                    data.Length < 4 || data[1] != 1)
                {
                    return 1;
                }

                int repetitions = data[2] | (data[3] << 8);
                return repetitions == 0 ? 0 : repetitions + 1;
            }
            catch (SharpGenException)
            {
                return 1;
            }
        }

        public void Dispose()
        {
            bool disposeResources;
            lock (_stateLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _cancellation.Cancel();
                disposeResources = _isComplete;
            }

            if (disposeResources)
            {
                DisposeResources();
            }
        }

        private void DisposeResources()
        {
            lock (_stateLock)
            {
                if (_resourcesDisposed)
                {
                    return;
                }

                _resourcesDisposed = true;
            }

            _frames.Dispose();
            _cancellation.Dispose();
        }
    }
}
