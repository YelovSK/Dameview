namespace Dameview.Imaging;

/// <summary>A decoded animation frame and the time it should remain displayed.</summary>
internal sealed record AnimationFrame(DecodedImage Image, TimeSpan Duration);

/// <summary>Represents a progressively decoded animation and its frame ownership.</summary>
internal interface IAnimationSession : IDisposable
{
    /// <summary>The first frame, available before background decoding completes.</summary>
    public AnimationFrame FirstFrame { get; }

    /// <summary>Whether the source contains more than one frame.</summary>
    public bool IsAnimated { get; }

    /// <summary>Whether no more frames will be produced.</summary>
    public bool IsComplete { get; }

    /// <summary>The terminal decode error, if decoding failed.</summary>
    public Exception? Error { get; }

    /// <summary>Attempts to take the next decoded frame without waiting.</summary>
    /// <returns><see langword="true"/> when a frame was returned; otherwise the session may still be decoding or complete.</returns>
    public bool TryGetReadyFrame(out AnimationFrame frame);
}
