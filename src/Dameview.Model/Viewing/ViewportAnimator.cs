using System.Drawing;
using System.Numerics;

namespace Dameview.Viewing;

internal sealed class ViewportAnimator
{
    private const double ZoomResponse = 25.0;
    private const double ZoomCompletionRatio = 0.001;
    private const double TransformResponse = 20.0;
    private const double CenterCompletionDistance = 0.25;
    private const double MomentumFriction = 6.0;
    private const double MinimumMomentumSpeed = 20.0;
    private const double MaximumReleaseDelaySeconds = 0.08;
    private const double VelocityTrackingResponse = 25.0;

    private readonly ImageViewport _viewport;
    private readonly TimeProvider _timeProvider;
    private Zoom? _zoom;
    private Transform? _transform;
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

    internal bool IsAnimating => _zoom is not null || _transform is not null || (_pan is null && HasMomentum);

    internal void Reset()
    {
        _zoom = null;
        _transform = null;
        _pan = null;
        StopMomentum();
    }

    internal bool ZoomAt(float viewportX, float viewportY, int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return false;
        }

        _velocity = Vector2.Zero;
        _transform = null;

        float baseScale = _zoom?.TargetScale ?? _viewport.Scale;
        float targetScale = _viewport.GetZoomScale(baseScale, wheelDelta);
        if (targetScale == _viewport.Scale)
        {
            _zoom = null;
            return false;
        }

        _zoom = new Zoom(targetScale, AnchorAt(viewportX, viewportY));
        Started?.Invoke();
        return true;
    }

    internal void BeginPan(float pointerX, float pointerY)
    {
        _zoom = null;
        _transform = null;
        _pan = new Pan(new PointF(pointerX, pointerY), _timeProvider.GetTimestamp());
        StopMomentum();
    }

    internal bool PanTo(float pointerX, float pointerY)
    {
        if (_pan is not { } pan)
        {
            return false;
        }

        var delta = new Vector2(pointerX - pan.Pointer.X, pointerY - pan.Pointer.Y);
        long timestamp = _timeProvider.GetTimestamp();
        double elapsed = _timeProvider.GetElapsedTime(pan.Timestamp, timestamp).TotalSeconds;

        _viewport.PanBy(delta.X, delta.Y);
        TrackPointerVelocity(delta, elapsed);

        _pan = new Pan(new PointF(pointerX, pointerY), timestamp);
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

    internal bool Fit() => StartTransform(new Transform(_viewport.FitScale, _viewport.ImageCenter, Anchor: null));

    internal bool ShowActualSizeAt(float viewportX, float viewportY)
    {
        Reset();
        if (!_viewport.HasImage)
        {
            return false;
        }

        ZoomAnchor anchor = AnchorAt(viewportX, viewportY);
        PointF targetCenter = _viewport.GetCenterAtScale(1.0f, viewportX, viewportY, anchor.Image);
        return StartTransform(new Transform(1.0f, targetCenter, anchor));
    }

    internal bool ToggleFitAndActualSizeAt(float viewportX, float viewportY)
    {
        ViewportMode mode = _transform?.TargetMode
            ?? (_zoom is not null ? ViewportMode.Custom : _viewport.Mode);
        if (mode == ViewportMode.Fit)
        {
            return ShowActualSizeAt(viewportX, viewportY);
        }

        return Fit();
    }

    internal bool Update(double elapsedSeconds, bool animationsEnabled = true)
    {
        if (!animationsEnabled)
        {
            CompleteAnimations();
            return false;
        }

        if (IsAnimating && elapsedSeconds > 0.0)
        {
            UpdateZoom(elapsedSeconds);
            UpdateTransform(elapsedSeconds);
            UpdateMomentum(elapsedSeconds);
        }

        return IsAnimating;
    }

    private bool HasMomentum => _velocity.Length() >= MinimumMomentumSpeed;

    private ZoomAnchor AnchorAt(float viewportX, float viewportY) =>
        new(new PointF(viewportX, viewportY), _viewport.ViewportToImage(viewportX, viewportY));

    private void StopMomentum()
    {
        _hasPointerVelocity = false;
        _velocity = Vector2.Zero;
    }

    private void CompleteAnimations()
    {
        if (_zoom is { } zoom)
        {
            _zoom = null;
            SetScaleAt(zoom.TargetScale, zoom.Anchor);
        }

        if (_transform is { } transform)
        {
            CompleteTransform(transform);
        }

        StopMomentum();
    }

    private bool StartTransform(Transform transform)
    {
        Reset();
        if (_viewport.Scale == transform.TargetScale && _viewport.Center == transform.TargetCenter)
        {
            CompleteTransform(transform);
            return false;
        }

        _transform = transform;
        Started?.Invoke();
        return true;
    }

    private void TrackPointerVelocity(Vector2 delta, double elapsed)
    {
        if (elapsed <= 0.0 || elapsed > MaximumReleaseDelaySeconds)
        {
            StopMomentum();
            return;
        }

        Vector2 instantaneous = delta / (float)elapsed;
        float blend = (float)(1.0 - Math.Exp(-VelocityTrackingResponse * elapsed));
        _velocity += (instantaneous - _velocity) * blend;
        _hasPointerVelocity = true;
    }

    private void UpdateZoom(double elapsed)
    {
        if (_zoom is not { } zoom)
        {
            return;
        }

        double blend = 1.0 - Math.Exp(-ZoomResponse * elapsed);
        float scale = (float)Math.Exp(
            Math.Log(_viewport.Scale)
            + ((Math.Log(zoom.TargetScale) - Math.Log(_viewport.Scale)) * blend));

        bool complete = Math.Abs(scale - zoom.TargetScale)
            <= zoom.TargetScale * ZoomCompletionRatio;
        if (complete)
        {
            scale = zoom.TargetScale;
            _zoom = null;
        }

        SetScaleAt(scale, zoom.Anchor);
    }

    private void UpdateTransform(double elapsed)
    {
        if (_transform is not { } transform)
        {
            return;
        }

        double blend = 1.0 - Math.Exp(-TransformResponse * elapsed);
        float scale = (float)(_viewport.Scale + ((transform.TargetScale - _viewport.Scale) * blend));
        PointF viewportCenter = _viewport.ViewportCenter;
        PointF imageCenter = _viewport.ImageCenter;
        PointF currentScreenCenter = ImageCenterToViewport(
            imageCenter,
            viewportCenter,
            _viewport.Center,
            _viewport.Scale);
        PointF targetScreenCenter = ImageCenterToViewport(
            imageCenter,
            viewportCenter,
            transform.TargetCenter,
            transform.TargetScale);
        PointF screenCenter = new(
            (float)(currentScreenCenter.X + ((targetScreenCenter.X - currentScreenCenter.X) * blend)),
            (float)(currentScreenCenter.Y + ((targetScreenCenter.Y - currentScreenCenter.Y) * blend)));
        PointF center = new(
            imageCenter.X - ((screenCenter.X - viewportCenter.X) / scale),
            imageCenter.Y - ((screenCenter.Y - viewportCenter.Y) / scale));

        float centerDistanceX = screenCenter.X - targetScreenCenter.X;
        float centerDistanceY = screenCenter.Y - targetScreenCenter.Y;
        bool scaleComplete = Math.Abs(scale - transform.TargetScale)
            <= transform.TargetScale * ZoomCompletionRatio;
        bool centerComplete = Math.Sqrt(
            (centerDistanceX * centerDistanceX) + (centerDistanceY * centerDistanceY))
            <= CenterCompletionDistance;

        if (scaleComplete && centerComplete)
        {
            CompleteTransform(transform);
            return;
        }

        _viewport.SetAnimatedTransform(scale, center);
    }

    private void CompleteTransform(Transform transform)
    {
        _transform = null;
        if (transform.Anchor is { } anchor)
        {
            _viewport.SetActualSizeAt(anchor.Viewport.X, anchor.Viewport.Y, anchor.Image);
        }
        else
        {
            _viewport.Fit();
        }
    }

    private void SetScaleAt(float scale, ZoomAnchor anchor) =>
        _viewport.SetScaleAt(scale, anchor.Viewport.X, anchor.Viewport.Y, anchor.Image);

    private static PointF ImageCenterToViewport(
        PointF imageCenter,
        PointF viewportCenter,
        PointF center,
        float scale)
    {
        return new PointF(
            viewportCenter.X + ((imageCenter.X - center.X) * scale),
            viewportCenter.Y + ((imageCenter.Y - center.Y) * scale));
    }

    private void UpdateMomentum(double elapsed)
    {
        if (_pan is not null || !HasMomentum)
        {
            _velocity = Vector2.Zero;
            return;
        }

        double decay = Math.Exp(-MomentumFriction * elapsed);
        Vector2 step = _velocity * (float)((1.0 - decay) / MomentumFriction);
        _viewport.PanBy(step.X, step.Y);
        _velocity *= (float)decay;
    }

    /// <summary>A viewport point that stays over the same image point while the scale changes.</summary>
    private readonly record struct ZoomAnchor(PointF Viewport, PointF Image);

    private readonly record struct Zoom(float TargetScale, ZoomAnchor Anchor);

    /// <summary>An animated fit, or with an anchor, an animated change to actual size around it.</summary>
    private readonly record struct Transform(float TargetScale, PointF TargetCenter, ZoomAnchor? Anchor)
    {
        internal ViewportMode TargetMode => Anchor is null ? ViewportMode.Fit : ViewportMode.ActualSize;
    }

    private readonly record struct Pan(PointF Pointer, long Timestamp);
}
