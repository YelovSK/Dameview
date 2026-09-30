using System.Drawing;
using System.Numerics;

namespace Dameview.Viewing;

internal sealed class ViewportAnimator
{
    private const double ZoomResponse = 25.0;
    private const double FitOrActualSizeResponse = 20.0;
    private const double ScaleCompletionRatio = 0.001;
    private const double PositionCompletionDistance = 0.25;
    private const double MomentumFriction = 6.0;
    private const double MinimumMomentumSpeed = 20.0;
    private const double MaximumReleaseDelaySeconds = 0.08;
    private const double VelocityTrackingResponse = 25.0;

    private readonly ImageViewport _viewport;
    private readonly TimeProvider _timeProvider;
    private Move? _move;
    private Pan? _pan;
    private Vector2 _velocity;
    // Whether the velocity comes from recent pointer movement rather than being stale.
    private bool _hasPointerVelocity;

    internal ViewportAnimator(ImageViewport viewport, TimeProvider? timeProvider = null)
    {
        _viewport = viewport;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Raised when an animation begins, so whoever draws the viewport can start updating it.</summary>
    internal event Action? Started;

    internal bool IsAnimating => _move is not null || (_pan is null && HasMomentum);

    internal void Reset()
    {
        _move = null;
        _pan = null;
        StopMomentum();
    }

    internal bool ZoomAt(PointF viewportPoint, int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return false;
        }

        // Wheel steps add up: a zoom that is still running continues from where it was headed.
        float baseScale = _move is { TargetMode: ViewportMode.Custom } zoom ? zoom.TargetScale : _viewport.Scale;
        float targetScale = _viewport.GetZoomScale(baseScale, wheelDelta);
        if (targetScale == _viewport.Scale)
        {
            _move = null;
            _velocity = Vector2.Zero;
            return false;
        }

        return StartMove(ViewportMode.Custom, targetScale, AnchorAt(viewportPoint), ZoomResponse);
    }

    internal void BeginPan(PointF pointer)
    {
        Reset();
        _pan = new Pan(pointer, _timeProvider.GetTimestamp());
    }

    internal bool PanTo(PointF pointer)
    {
        if (_pan is not { } pan)
        {
            return false;
        }

        Vector2 delta = pointer - pan.Pointer;
        long timestamp = _timeProvider.GetTimestamp();
        double elapsed = _timeProvider.GetElapsedTime(pan.Timestamp, timestamp).TotalSeconds;

        _viewport.PanBy(delta);
        TrackPointerVelocity(delta, elapsed);

        _pan = new Pan(pointer, timestamp);
        return delta != Vector2.Zero;
    }

    internal bool EndPan()
    {
        if (_pan is not { } pan)
        {
            return false;
        }

        _pan = null;
        double releaseDelay = _timeProvider
            .GetElapsedTime(pan.Timestamp, _timeProvider.GetTimestamp())
            .TotalSeconds;
        if (!_hasPointerVelocity || releaseDelay > MaximumReleaseDelaySeconds)
        {
            _velocity = Vector2.Zero;
        }

        if (!HasMomentum)
        {
            return false;
        }

        Started?.Invoke();
        return true;
    }

    // Unlike a wheel zoom, fitting and actual size also end a drag.
    internal bool Fit()
    {
        Reset();
        return StartMove(
            ViewportMode.Fit,
            _viewport.FitScale,
            new ZoomAnchor(_viewport.ViewportCenter, _viewport.ImageCenter),
            FitOrActualSizeResponse);
    }

    internal bool ShowActualSizeAt(PointF viewportPoint)
    {
        Reset();
        if (!_viewport.HasImage)
        {
            return false;
        }

        return StartMove(ViewportMode.ActualSize, 1.0f, AnchorAt(viewportPoint), FitOrActualSizeResponse);
    }

    internal bool ToggleFitAndActualSizeAt(PointF viewportPoint)
    {
        ViewportMode mode = _move?.TargetMode ?? _viewport.Mode;
        if (mode == ViewportMode.Fit)
        {
            return ShowActualSizeAt(viewportPoint);
        }

        return Fit();
    }

    internal bool Update(double elapsedSeconds, bool animationsEnabled = true)
    {
        if (!animationsEnabled)
        {
            if (_move is { } move)
            {
                Finish(move);
            }

            StopMomentum();
            return false;
        }

        if (IsAnimating && elapsedSeconds > 0.0)
        {
            UpdateMove(elapsedSeconds);
            UpdateMomentum(elapsedSeconds);
        }

        return IsAnimating;
    }

    private bool HasMomentum => _velocity.Length() >= MinimumMomentumSpeed;

    private ZoomAnchor AnchorAt(PointF viewportPoint) => new(viewportPoint, _viewport.ViewportToImage(viewportPoint));

    private void StopMomentum()
    {
        _hasPointerVelocity = false;
        _velocity = Vector2.Zero;
    }

    private bool StartMove(ViewportMode targetMode, float targetScale, ZoomAnchor anchor, double response)
    {
        _move = null;
        _velocity = Vector2.Zero;

        // Where the image ends up is worked out once, here, including keeping it inside the
        // window edges. Working it out again on every frame would make the image change
        // direction halfway through, when it grows past an edge.
        PointF targetCenter = _viewport.GetCenterAtScale(targetScale, anchor.Viewport, anchor.Image);
        var move = new Move(
            targetMode,
            anchor,
            response,
            _viewport.Scale,
            targetScale,
            ImageMiddleOnScreen(_viewport.Center, _viewport.Scale),
            ImageMiddleOnScreen(targetCenter, targetScale),
            Progress: 0.0f);

        if (_viewport.Scale == targetScale && _viewport.Center == targetCenter)
        {
            Finish(move);
            return false;
        }

        _move = move;
        Started?.Invoke();
        return true;
    }

    private void UpdateMove(double elapsed)
    {
        if (_move is not { } move)
        {
            return;
        }

        float progress = move.Progress + ((1.0f - move.Progress) * ShareOfRemainingWay(move.Response, elapsed));
        float scale = move.ScaleAt(progress);
        PointF middle = move.MiddleAt(scale, progress);
        if (move.IsDone(scale, middle))
        {
            Finish(move);
            return;
        }

        _viewport.SetAnimatedTransform(scale, CenterForImageMiddle(middle, scale));
        _move = move with { Progress = progress };
    }

    // Lands exactly on the end state, so the viewport also ends up in the right mode.
    private void Finish(Move move)
    {
        _move = null;
        switch (move.TargetMode)
        {
            case ViewportMode.Fit:
                _viewport.Fit();
                break;

            case ViewportMode.ActualSize:
                _viewport.SetActualSizeAt(move.Anchor.Viewport, move.Anchor.Image);
                break;

            default:
                _viewport.SetScaleAt(move.TargetScale, move.Anchor.Viewport, move.Anchor.Image);
                break;
        }
    }

    // Where the middle of the image is on screen, when the viewport shows center at scale.
    private PointF ImageMiddleOnScreen(PointF center, float scale) =>
        _viewport.ViewportCenter + ((_viewport.ImageCenter - center) * scale);

    // The opposite: which image point to show at the viewport's center, so the middle of the
    // image lands on screen at middle.
    private PointF CenterForImageMiddle(PointF middle, float scale) =>
        _viewport.ImageCenter - ((middle - _viewport.ViewportCenter) / scale);

    // Animations cover the same share of the way that is left on every frame. They start fast
    // and slow down as they get close. A higher response covers a bigger share.
    private static float ShareOfRemainingWay(double response, double elapsed) =>
        (float)(1.0 - Math.Exp(-response * elapsed));

    private void TrackPointerVelocity(Vector2 delta, double elapsed)
    {
        if (elapsed <= 0.0 || elapsed > MaximumReleaseDelaySeconds)
        {
            StopMomentum();
            return;
        }

        Vector2 instantaneous = delta / (float)elapsed;
        _velocity += (instantaneous - _velocity) * ShareOfRemainingWay(VelocityTrackingResponse, elapsed);
        _hasPointerVelocity = true;
    }

    private void UpdateMomentum(double elapsed)
    {
        if (_pan is not null || !HasMomentum)
        {
            _velocity = Vector2.Zero;
            return;
        }

        double decay = Math.Exp(-MomentumFriction * elapsed);
        _viewport.PanBy(_velocity * (float)((1.0 - decay) / MomentumFriction));
        _velocity *= (float)decay;
    }

    /// <summary>A viewport point that stays over the same image point while the scale changes.</summary>
    private readonly record struct ZoomAnchor(PointF Viewport, PointF Image);

    /// <summary>A smooth change of scale and position: a wheel zoom, a fit, or a jump to actual size.</summary>
    /// <param name="TargetMode">What the viewport becomes at the end. A wheel zoom ends as custom.</param>
    /// <param name="Response">How fast it moves. Higher is faster.</param>
    /// <param name="StartOnScreen">Where the middle of the image was on screen at the start.</param>
    /// <param name="TargetOnScreen">Where the middle of the image will be on screen at the end.</param>
    /// <param name="Progress">How far along it is, from 0 at the start to 1 at the end.</param>
    private readonly record struct Move(
        ViewportMode TargetMode,
        ZoomAnchor Anchor,
        double Response,
        float StartScale,
        float TargetScale,
        PointF StartOnScreen,
        PointF TargetOnScreen,
        float Progress)
    {
        // The scale grows or shrinks by the same factor for the same progress,
        // so every wheel step feels the same.
        internal float ScaleAt(float progress) => StartScale * MathF.Pow(TargetScale / StartScale, progress);

        // The image moves in step with the scale. That keeps the point under the pointer in place.
        // If only the position changes, it moves in step with progress instead.
        internal PointF MiddleAt(float scale, float progress)
        {
            float moved = ScaleChanges ? (scale - StartScale) / (TargetScale - StartScale) : progress;
            return StartOnScreen + ((TargetOnScreen - StartOnScreen) * moved);
        }

        internal bool IsDone(float scale, PointF middle) =>
            IsClose(scale, TargetScale)
            && (middle - TargetOnScreen).Length() <= PositionCompletionDistance;

        private bool ScaleChanges => !IsClose(StartScale, TargetScale);

        private static bool IsClose(float scale, float targetScale) =>
            Math.Abs(scale - targetScale) <= targetScale * ScaleCompletionRatio;
    }

    private readonly record struct Pan(PointF Pointer, long Timestamp);
}
