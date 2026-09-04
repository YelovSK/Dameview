using System.Drawing;
using System.Numerics;
using Dameview.Commands;
using Dameview.UI.Animation;
using Dameview.UI.Components;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class ToolbarPanel : IUiElement, IDisposable
{
    private const float Padding = 6.0f;
    private const float ButtonGap = 4.0f;

    private readonly ID2D1SolidColorBrush _backgroundBrush;
    private readonly ID2D1SolidColorBrush _borderBrush;
    private readonly Button[] _buttons;
    private readonly AnimatedFloat _visibility = new(0.0f, 14.0);
    private float _dpi;
    private readonly UiPointerRouter _pointerRouter = new();

    internal ToolbarPanel(
        ID2D1RenderTarget renderTarget,
        IDWriteFactory directWriteFactory,
        UiTheme theme,
        IViewerCommands commands,
        float dpi)
    {
        _dpi = dpi;
        _backgroundBrush = renderTarget.CreateSolidColorBrush(theme.OverlaySurface);
        _borderBrush = renderTarget.CreateSolidColorBrush(theme.SurfaceBorder);

        _buttons =
        [
            new Button(renderTarget, directWriteFactory, theme, "←", commands.ShowPreviousImage),
            new Button(renderTarget, directWriteFactory, theme, "→", commands.ShowNextImage),
            new Button(renderTarget, directWriteFactory, theme, "Fit", commands.FitImage),
            new Button(renderTarget, directWriteFactory, theme, "1:1", commands.ShowActualSize),
        ];
    }

    internal void Show()
    {
        _visibility.SetTarget(1.0f);
    }

    internal void SetDpi(float dpi)
    {
        _dpi = dpi;
    }

    public bool Update(in UiUpdateContext context)
    {
        bool continues = _visibility.Update(context.ElapsedSeconds);
        foreach (Button button in _buttons)
        {
            continues |= button.Update(context);
        }

        return continues;
    }

    public void Draw(in UiDrawContext context, SizeF size)
    {
        float opacity = _visibility.Current;
        if (opacity <= 0.0f)
        {
            return;
        }

        float width = context.PixelsToDips(size.Width);
        float height = context.PixelsToDips(size.Height);
        if (width <= 0.0f || height <= 0.0f)
        {
            return;
        }

        ID2D1RenderTarget renderTarget = context.RenderTarget;
        _backgroundBrush.Opacity = context.Opacity * opacity;
        _borderBrush.Opacity = context.Opacity * opacity;
        Matrix3x2 previousTransform = renderTarget.Transform;
        float offsetY = context.PixelsToDips(GetSlideOffset(size));
        renderTarget.Transform = Matrix3x2.CreateTranslation(0.0f, offsetY)
            * previousTransform;

        try
        {
            var panel = new RoundedRectangle(
                new RectangleF(0.0f, 0.0f, width, height),
                12.0f,
                12.0f);
            renderTarget.FillRoundedRectangle(panel, _backgroundBrush);
            renderTarget.DrawRoundedRectangle(panel, _borderBrush);

            UiDrawContext buttonContext = context.WithOpacity(opacity);
            for (int index = 0; index < _buttons.Length; index++)
            {
                buttonContext.DrawElement(_buttons[index], GetButtonBounds(index, size));
            }
        }
        finally
        {
            renderTarget.Transform = previousTransform;
        }
    }

    public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
    {
        if (input.Kind == UiPointerEventKind.Cancelled)
        {
            return _pointerRouter.Cancel();
        }

        bool repaint = false;
        if (input.Kind == UiPointerEventKind.Moved && _pointerRouter.Captured is null)
        {
            repaint = _visibility.SetTarget(IsPointerNearToolbar(input.Position) ? 1.0f : 0.0f);
        }

        UiPointerEvent visualInput = input with
        {
            Position = new PointF(input.Position.X, input.Position.Y - GetSlideOffset(size)),
        };
        if (_pointerRouter.Captured is IUiElement captured)
        {
            int index = Array.IndexOf(_buttons, captured);
            return _pointerRouter.DispatchCaptured(visualInput, GetButtonBounds(index, size));
        }

        bool inside = _visibility.Current > 0.0f
            && new RectangleF(PointF.Empty, size).Contains(input.Position)
            && new RectangleF(PointF.Empty, size).Contains(visualInput.Position);
        if (input.Kind == UiPointerEventKind.Moved)
        {
            // Every button observes movement so the previous hover can clear.
            for (int index = 0; index < _buttons.Length; index++)
            {
                UiPointerEvent hoverInput = inside ? visualInput : input with
                {
                    Kind = UiPointerEventKind.Cancelled,
                };
                repaint |= _pointerRouter.Route(_buttons[index], GetButtonBounds(index, size),
                    hoverInput, observeOutside: true).NeedsRepaint;
            }

            return new UiPointerResult(Consumed: inside, NeedsRepaint: repaint);
        }

        if (!inside)
        {
            return new UiPointerResult(NeedsRepaint: repaint);
        }

        for (int index = 0; index < _buttons.Length; index++)
        {
            UiPointerResult result = _pointerRouter.Route(_buttons[index], GetButtonBounds(index, size), visualInput);
            repaint |= result.NeedsRepaint;
            if (result.Consumed)
            {
                return result with { NeedsRepaint = repaint };
            }
        }

        return new UiPointerResult(Consumed: true, NeedsRepaint: repaint);
    }

    public void Dispose()
    {
        _pointerRouter.Cancel();
        foreach (Button button in _buttons)
        {
            button.Dispose();
        }

        _borderBrush.Dispose();
        _backgroundBrush.Dispose();
    }

    private bool IsPointerNearToolbar(PointF position)
    {
        float activationDistance = UiDpi.DipsToPixels(28.0f, _dpi);
        return position.Y >= -activationDistance;
    }

    private float GetSlideOffset(SizeF size)
    {
        return (1.0f - _visibility.Current) * size.Height;
    }

    private RectangleF GetButtonBounds(int index, SizeF size)
    {
        float scale = UiDpi.GetScale(_dpi);
        float padding = Padding * scale;
        float buttonGap = ButtonGap * scale;
        float totalGaps = buttonGap * (_buttons.Length - 1);
        float buttonWidth = MathF.Max(
            0.0f,
            (size.Width - (2.0f * padding) - totalGaps) / _buttons.Length);
        return new RectangleF(
            padding + (index * (buttonWidth + buttonGap)),
            padding,
            buttonWidth,
            MathF.Max(0.0f, size.Height - (2.0f * padding)));
    }
}
