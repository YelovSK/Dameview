using System.Drawing;

namespace Dameview.Viewing;

internal sealed class ViewportAnimator
{
    private const double ZoomResponse = 18.0;
    private const double ZoomCompletionRatio = 0.001;
    private const double CenteredTransitionResponse = 14.0;
    private const double CenterCompletionDistance = 0.25;
    private const double MomentumFriction = 6.0;
    private const double MinimumMomentumSpeed = 20.0;
    private const double MaximumReleaseDelaySeconds = 0.08;
    private const double VelocityTrackingResponse = 25.0;

    private readonly ImageViewport _viewport;
    private readonly TimeProvider _timeProvider;
    private bool _zooming;
    private float _targetScale;
    private float _zoomViewportX;
    private float _zoomViewportY;
    private PointF _zoomImagePosition;
    private ViewportMode _zoomTargetMode;
    private bool _centering;
    private PointF _targetCenter;
    private bool _panning;
    private float _pointerX;
    private float _pointerY;
    private long _pointerTimestamp;
    private bool _hasPointerVelocity;
    private double _velocityX;
    private double _velocityY;

    internal ViewportAnimator(ImageViewport viewport, TimeProvider? timeProvider = null)
    {
        _viewport = viewport;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal bool IsAnimating => _zooming || _centering || (!_panning && HasMomentum);

    internal void Reset()
    {
        _zooming = false;
        _centering = false;
        _panning = false;
        _hasPointerVelocity = false;
        _velocityX = 0.0;
        _velocityY = 0.0;
    }

    internal bool ZoomAt(float viewportX, float viewportY, int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return false;
        }

        _velocityX = 0.0;
        _velocityY = 0.0;
        _centering = false;

        float baseScale = _zooming ? _targetScale : _viewport.Scale;
        float targetScale = _viewport.GetZoomScale(baseScale, wheelDelta);
        if (targetScale == _viewport.Scale)
        {
            _zooming = false;
            return false;
        }

        _targetScale = targetScale;
        _zoomViewportX = viewportX;
        _zoomViewportY = viewportY;
        _zoomImagePosition = _viewport.ViewportToImage(viewportX, viewportY);
        _zoomTargetMode = ViewportMode.Custom;
        _zooming = true;
        return true;
    }

    internal void BeginPan(float pointerX, float pointerY)
    {
        _zooming = false;
        _centering = false;
        _panning = true;
        _pointerX = pointerX;
        _pointerY = pointerY;
        _pointerTimestamp = _timeProvider.GetTimestamp();
        _hasPointerVelocity = false;
        _velocityX = 0.0;
        _velocityY = 0.0;
    }

    internal bool PanTo(float pointerX, float pointerY)
    {
        if (!_panning)
        {
            return false;
        }

        float deltaX = pointerX - _pointerX;
        float deltaY = pointerY - _pointerY;
        long timestamp = _timeProvider.GetTimestamp();
        double elapsed = _timeProvider.GetElapsedTime(_pointerTimestamp, timestamp).TotalSeconds;

        _viewport.PanBy(deltaX, deltaY);
        TrackPointerVelocity(deltaX, deltaY, elapsed);

        _pointerX = pointerX;
        _pointerY = pointerY;
        _pointerTimestamp = timestamp;
        return deltaX != 0.0f || deltaY != 0.0f;
    }

    internal bool EndPan()
    {
        if (!_panning)
        {
            return false;
        }

        _panning = false;
        double releaseDelay = _timeProvider
            .GetElapsedTime(_pointerTimestamp, _timeProvider.GetTimestamp())
            .TotalSeconds;
        if (!_hasPointerVelocity || releaseDelay > MaximumReleaseDelaySeconds)
        {
            _velocityX = 0.0;
            _velocityY = 0.0;
        }

        return HasMomentum;
    }

    internal bool Fit()
    {
        return StartCenteredTransition(_viewport.FitScale);
    }

    internal bool ShowActualSizeAt(float viewportX, float viewportY)
    {
        Reset();
        if (!_viewport.HasImage)
        {
            return false;
        }

        _targetScale = 1.0f;
        _zoomViewportX = viewportX;
        _zoomViewportY = viewportY;
        _zoomImagePosition = _viewport.ViewportToImage(viewportX, viewportY);
        _zoomTargetMode = ViewportMode.ActualSize;

        if (_viewport.Scale == _targetScale)
        {
            _viewport.SetActualSizeAt(
                _zoomViewportX,
                _zoomViewportY,
                _zoomImagePosition);
            return false;
        }

        _zooming = true;
        return true;
    }

    internal bool ToggleFitAndActualSizeAt(float viewportX, float viewportY)
    {
        ViewportMode mode = _zooming ? _zoomTargetMode : _viewport.Mode;
        if (_centering || mode == ViewportMode.Fit)
        {
            return ShowActualSizeAt(viewportX, viewportY);
        }

        return Fit();
    }

    internal bool Update(double elapsedSeconds)
    {
        if (IsAnimating && elapsedSeconds > 0.0)
        {
            UpdateZoom(elapsedSeconds);
            UpdateCenteredTransition(elapsedSeconds);
            UpdateMomentum(elapsedSeconds);
        }

        return IsAnimating;
    }

    private bool HasMomentum => Math.Sqrt((_velocityX * _velocityX) + (_velocityY * _velocityY))
        >= MinimumMomentumSpeed;

    private bool StartCenteredTransition(float scale)
    {
        Reset();
        _targetScale = scale;
        _targetCenter = _viewport.ImageCenter;

        if (_viewport.Scale == _targetScale && _viewport.Center == _targetCenter)
        {
            CompleteCenteredTransition();
            return false;
        }

        _centering = true;
        return true;
    }

    private void TrackPointerVelocity(float deltaX, float deltaY, double elapsed)
    {
        if (elapsed <= 0.0 || elapsed > MaximumReleaseDelaySeconds)
        {
            _hasPointerVelocity = false;
            _velocityX = 0.0;
            _velocityY = 0.0;
            return;
        }

        double instantaneousX = deltaX / elapsed;
        double instantaneousY = deltaY / elapsed;
        if (!_hasPointerVelocity)
        {
            _velocityX = instantaneousX;
            _velocityY = instantaneousY;
            _hasPointerVelocity = true;
            return;
        }

        double blend = 1.0 - Math.Exp(-VelocityTrackingResponse * elapsed);
        _velocityX += (instantaneousX - _velocityX) * blend;
        _velocityY += (instantaneousY - _velocityY) * blend;
    }

    private void UpdateZoom(double elapsed)
    {
        if (!_zooming)
        {
            return;
        }

        double blend = 1.0 - Math.Exp(-ZoomResponse * elapsed);
        float scale = (float)Math.Exp(
            Math.Log(_viewport.Scale)
            + ((Math.Log(_targetScale) - Math.Log(_viewport.Scale)) * blend));

        bool complete = Math.Abs(scale - _targetScale)
            <= _targetScale * ZoomCompletionRatio;
        if (complete)
        {
            scale = _targetScale;
            _zooming = false;
        }

        if (complete && _zoomTargetMode == ViewportMode.ActualSize)
        {
            _viewport.SetActualSizeAt(
                _zoomViewportX,
                _zoomViewportY,
                _zoomImagePosition);
        }
        else
        {
            _viewport.SetScaleAt(
                scale,
                _zoomViewportX,
                _zoomViewportY,
                _zoomImagePosition);
        }
    }

    private void UpdateCenteredTransition(double elapsed)
    {
        if (!_centering)
        {
            return;
        }

        double blend = 1.0 - Math.Exp(-CenteredTransitionResponse * elapsed);
        float scale = (float)Math.Exp(
            Math.Log(_viewport.Scale)
            + ((Math.Log(_targetScale) - Math.Log(_viewport.Scale)) * blend));
        PointF center = new(
            (float)(_viewport.Center.X + ((_targetCenter.X - _viewport.Center.X) * blend)),
            (float)(_viewport.Center.Y + ((_targetCenter.Y - _viewport.Center.Y) * blend)));

        float centerDistanceX = (center.X - _targetCenter.X) * scale;
        float centerDistanceY = (center.Y - _targetCenter.Y) * scale;
        bool scaleComplete = Math.Abs(scale - _targetScale)
            <= _targetScale * ZoomCompletionRatio;
        bool centerComplete = Math.Sqrt(
            (centerDistanceX * centerDistanceX) + (centerDistanceY * centerDistanceY))
            <= CenterCompletionDistance;

        if (scaleComplete && centerComplete)
        {
            CompleteCenteredTransition();
            return;
        }

        _viewport.SetTransform(scale, center);
    }

    private void CompleteCenteredTransition()
    {
        _centering = false;
        _viewport.Fit();
    }

    private void UpdateMomentum(double elapsed)
    {
        if (_panning || !HasMomentum)
        {
            _velocityX = 0.0;
            _velocityY = 0.0;
            return;
        }

        double decay = Math.Exp(-MomentumFriction * elapsed);
        double distance = (1.0 - decay) / MomentumFriction;
        _viewport.PanBy(
            (float)(_velocityX * distance),
            (float)(_velocityY * distance));

        _velocityX *= decay;
        _velocityY *= decay;
    }
}
