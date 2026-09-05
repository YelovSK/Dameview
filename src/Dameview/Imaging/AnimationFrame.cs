namespace Dameview.Imaging;

internal sealed record AnimationFrame(DecodedImage Image, TimeSpan Duration);

internal interface IAnimationSession : IDisposable
{
    public AnimationFrame FirstFrame { get; }

    public bool IsAnimated { get; }

    public bool IsComplete { get; }

    public bool IsInfiniteLoop { get; }

    public Exception? Error { get; }

    public bool TryGetReadyFrame(out AnimationFrame frame);
}
