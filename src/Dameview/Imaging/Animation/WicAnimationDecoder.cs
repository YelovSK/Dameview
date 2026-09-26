using System.Collections.Concurrent;
using Dameview.Win32;
using Vortice.WIC;

namespace Dameview.Imaging.Animation;

internal abstract class WicAnimationDecoder : IAnimatedImageDecoder
{
    public abstract bool CanDecode(string path);

    public IAnimationSession Open(string path) => new Session(Path.GetFullPath(path), this);

    protected abstract int ReadLoopCount(IWICBitmapDecoder decoder);

    protected abstract IEnumerable<AnimationFrame> DecodeFrames(
        IWICImagingFactory2 factory,
        IWICBitmapDecoder decoder);

    private sealed class Session : IAnimationSession
    {
        private const int QueueCapacity = 2;
        private readonly Lock _stateLock = new();
        private readonly BlockingCollection<AnimationFrame> _frames = new(QueueCapacity);
        private readonly CancellationTokenSource _cancellation = new();
        private readonly int _frameCount;
        private readonly int _loopCount;
        private bool _isComplete;
        private bool _disposed;
        private bool _resourcesDisposed;

        internal Session(string path, WicAnimationDecoder format)
        {
            using var factory = new IWICImagingFactory2();
            using IWICBitmapDecoder decoder = factory.CreateDecoderFromFileName(
                path, FileAccess.Read, DecodeOptions.CacheOnLoad);
            _frameCount = checked((int)decoder.FrameCount);
            if (_frameCount == 0)
            {
                throw new InvalidDataException("The image contains no frames.");
            }

            using IEnumerator<AnimationFrame> frames = format.DecodeFrames(factory, decoder).GetEnumerator();
            if (!frames.MoveNext())
            {
                throw new InvalidDataException("The image contains no frames.");
            }

            FirstFrame = frames.Current;
            _loopCount = _frameCount > 1 ? format.ReadLoopCount(decoder) : 1;
            if (_frameCount > 1)
            {
                var worker = new Thread(() => DecodeFrames(path, format))
                {
                    IsBackground = true,
                    Name = "Dameview animation decoder",
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
        public bool IsAnimated => _frameCount > 1;
        private bool IsInfiniteLoop => _loopCount == 0;
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
                    return field;
                }
            }

            private set;
        }

        public bool TryGetReadyFrame(out AnimationFrame frame) => _frames.TryTake(out frame!);

        private void DecodeFrames(string path, WicAnimationDecoder format)
        {
            bool comInitialized = false;
            try
            {
                NativeMethods.InitializeComApartment(ComApartment.MultiThreaded);
                comInitialized = true;
                using var factory = new IWICImagingFactory2();
                using IWICBitmapDecoder decoder = factory.CreateDecoderFromFileName(
                    path, FileAccess.Read, DecodeOptions.CacheOnLoad);
                long completedLoops = 0;
                bool firstFrame = true;
                while (!_cancellation.IsCancellationRequested &&
                       (IsInfiniteLoop || completedLoops < _loopCount))
                {
                    foreach (AnimationFrame frame in format.DecodeFrames(factory, decoder))
                    {
                        if (_cancellation.IsCancellationRequested)
                        {
                            return;
                        }

                        // Rebuild format-specific state on the worker, but do not queue the first frame twice.
                        if (firstFrame)
                        {
                            firstFrame = false;
                            continue;
                        }

                        _frames.Add(frame, _cancellation.Token);
                    }

                    completedLoops++;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                lock (_stateLock)
                {
                    Error = exception;
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
