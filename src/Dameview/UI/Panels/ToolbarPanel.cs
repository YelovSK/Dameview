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
    private int _capturedButton = -1;

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

    public bool HandlePointer(in UiPointerEvent input, SizeF size)
    {
        bool visibilityChanged = false;
        if (input.Kind == UiPointerEventKind.Moved && _capturedButton < 0)
        {
            visibilityChanged = _visibility.SetTarget(
                IsPointerNearToolbar(input.Position) ? 1.0f : 0.0f);
        }

        UiPointerEvent visualInput = input with
        {
            Position = new PointF(
                input.Position.X,
                input.Position.Y - GetSlideOffset(size)),
        };

        if (_capturedButton >= 0)
        {
            int capturedButton = _capturedButton;
            RectangleF bounds = GetButtonBounds(capturedButton, size);
            bool handled = _buttons[capturedButton].HandlePointer(
                visualInput.ToLocal(bounds),
                bounds.Size);

            if (input.Kind == UiPointerEventKind.Released)
            {
                _capturedButton = -1;
            }

            return handled || visibilityChanged;
        }

        switch (input.Kind)
        {
            case UiPointerEventKind.Moved:
                bool changed = false;
                for (int index = 0; index < _buttons.Length; index++)
                {
                    RectangleF bounds = GetButtonBounds(index, size);
                    changed |= _buttons[index].HandlePointer(
                        visualInput.ToLocal(bounds),
                        bounds.Size);
                }

                return changed || visibilityChanged;

            case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _visibility.SetTarget(1.0f);
                int pressedButton = HitTest(visualInput.Position, size);
                if (pressedButton >= 0)
                {
                    RectangleF bounds = GetButtonBounds(pressedButton, size);
                    if (_buttons[pressedButton].HandlePointer(
                        visualInput.ToLocal(bounds),
                        bounds.Size))
                    {
                        _capturedButton = pressedButton;
                    }
                }

                return true;

            case UiPointerEventKind.Released:
                return true;

            case UiPointerEventKind.DoubleClicked when input.Button == PointerButton.Primary:
                int hitButton = HitTest(visualInput.Position, size);
                if (hitButton >= 0)
                {
                    RectangleF bounds = GetButtonBounds(hitButton, size);
                    _buttons[hitButton].HandlePointer(
                        visualInput.ToLocal(bounds),
                        bounds.Size);
                }

                return true;

            case UiPointerEventKind.Wheel:
                return true;

            default:
                return false;
        }
    }

    public void Dispose()
    {
        foreach (Button button in _buttons)
        {
            button.Dispose();
        }

        _borderBrush.Dispose();
        _backgroundBrush.Dispose();
    }

    private int HitTest(PointF position, SizeF size)
    {
        for (int index = 0; index < _buttons.Length; index++)
        {
            if (GetButtonBounds(index, size).Contains(position))
            {
                return index;
            }
        }

        return -1;
    }

    private bool IsPointerNearToolbar(PointF position)
    {
        float activationDistance = 28.0f * _dpi / 96.0f;
        return position.Y >= -activationDistance;
    }

    private float GetSlideOffset(SizeF size)
    {
        return (1.0f - _visibility.Current) * size.Height;
    }

    private RectangleF GetButtonBounds(int index, SizeF size)
    {
        float scale = _dpi / 96.0f;
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
