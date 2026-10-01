using System.Numerics;
using System.Runtime.InteropServices;

namespace Dameview.Imaging;

// Owns temporary CPU pixels used to upload one static image to a graphics backend.
internal sealed unsafe class DecodedImageUpload : IDisposable
{
    private const int BytesPerPixel = 4;

    private readonly SharedPixels _shared;
    private readonly Action? _released;
    private bool _disposed;

    private DecodedImageUpload(
        int width,
        int height,
        int stride,
        int length,
        ImageOrientation orientation,
        SharedPixels shared,
        Action? released = null)
    {
        Width = width;
        Height = height;
        Stride = stride;
        Length = length;
        Orientation = orientation;
        _shared = shared;
        _released = released;
    }

    internal int Width { get; }
    internal int Height { get; }
    internal int Stride { get; }
    internal int Length { get; }
    /// <summary>How the pixels, which are in stored order, are meant to be shown.</summary>
    internal ImageOrientation Orientation { get; }
    internal nint Pixels => !_disposed
        ? _shared.Pointer
        : throw new ObjectDisposedException(nameof(DecodedImageUpload));
    internal Span<byte> Span => new((void*)Pixels, Length);

    internal static DecodedImageUpload Allocate(
        int width,
        int height,
        int stride,
        ImageOrientation orientation = default)
    {
        int length = checked(stride * height);
        void* pixels = NativeMemory.Alloc((nuint)length);
        return new DecodedImageUpload(
            width,
            height,
            stride,
            length,
            orientation,
            new SharedPixels((nint)pixels, static value => NativeMemory.Free((void*)value)));
    }

    internal static DecodedImageUpload Rent(
        NativePixelBufferPool pool,
        int width,
        int height,
        int stride,
        ImageOrientation orientation = default)
    {
        int length = checked(stride * height);
        NativePixelBuffer buffer = pool.Rent(length);
        return new DecodedImageUpload(
            width,
            height,
            stride,
            length,
            orientation,
            new SharedPixels(buffer.Pointer, value => pool.Return(value, buffer.Capacity)));
    }

    internal DecodedImageUpload Retain(Action? released = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _shared.Retain();
        return new DecodedImageUpload(Width, Height, Stride, Length, Orientation, _shared, released);
    }

    /// <summary>Copies the pixels into the order they are shown in, for consumers that can't turn them.</summary>
    internal DecodedImageUpload CopyOriented()
    {
        var storedSize = new System.Drawing.Size(Width, Height);
        System.Drawing.Size size = Orientation.Apply(storedSize);
        Matrix3x2.Invert(Orientation.GetTransform(storedSize), out Matrix3x2 toStored);
        DecodedImageUpload output = Allocate(size.Width, size.Height, checked(size.Width * BytesPerPixel));
        Span<uint> source = MemoryMarshal.Cast<byte, uint>(Span);
        Span<uint> destination = MemoryMarshal.Cast<byte, uint>(output.Span);
        int sourceStridePixels = Stride / BytesPerPixel;
        for (int y = 0; y < size.Height; y++)
        {
            for (int x = 0; x < size.Width; x++)
            {
                // Pixel centers land exactly on pixel centers, so truncating finds the stored pixel.
                var stored = Vector2.Transform(new Vector2(x + 0.5f, y + 0.5f), toStored);
                destination[(y * size.Width) + x] = source[((int)stored.Y * sourceStridePixels) + (int)stored.X];
            }
        }

        return output;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shared.Release();
        _released?.Invoke();
    }

    private sealed class SharedPixels(nint pointer, Action<nint> release)
    {
        private nint _pointer = pointer;
        private int _references = 1;

        internal nint Pointer => Volatile.Read(ref _pointer);

        internal void Retain()
        {
            int references = Volatile.Read(ref _references);
            while (references > 0)
            {
                int observed = Interlocked.CompareExchange(
                    ref _references,
                    references + 1,
                    references);
                if (observed == references)
                {
                    return;
                }

                references = observed;
            }

            throw new ObjectDisposedException(nameof(SharedPixels));
        }

        internal void Release()
        {
            if (Interlocked.Decrement(ref _references) != 0)
            {
                return;
            }

            nint pixels = Interlocked.Exchange(ref _pointer, 0);
            release(pixels);
        }
    }
}

internal readonly record struct NativePixelBuffer(nint Pointer, int Capacity);

// Shared by the static-image workers. It bounds retained decode memory to
// two reusable buffers and frees any excess immediately.
internal sealed unsafe class NativePixelBufferPool : IDisposable
{
    private const int MaximumRetainedBuffers = 2;
    private readonly Lock _sync = new();
    private readonly List<NativePixelBuffer> _available = [];
    private bool _disposed;

    internal NativePixelBuffer Rent(int minimumCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumCapacity);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int bestIndex = -1;
            for (int index = 0; index < _available.Count; index++)
            {
                if (_available[index].Capacity >= minimumCapacity
                    && (bestIndex < 0
                        || _available[index].Capacity < _available[bestIndex].Capacity))
                {
                    bestIndex = index;
                }
            }

            if (bestIndex >= 0)
            {
                NativePixelBuffer buffer = _available[bestIndex];
                _available.RemoveAt(bestIndex);
                return buffer;
            }
        }

        return new NativePixelBuffer(
            (nint)NativeMemory.Alloc((nuint)minimumCapacity),
            minimumCapacity);
    }

    internal void Return(nint pointer, int capacity)
    {
        lock (_sync)
        {
            if (!_disposed && _available.Count < MaximumRetainedBuffers)
            {
                _available.Add(new NativePixelBuffer(pointer, capacity));
                return;
            }
        }

        NativeMemory.Free((void*)pointer);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (NativePixelBuffer buffer in _available)
            {
                NativeMemory.Free((void*)buffer.Pointer);
            }

            _available.Clear();
        }
    }
}
