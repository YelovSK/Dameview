using System.Drawing;
using Dameview.Commands;
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

    internal void SetDpi(float dpi)
    {
        _dpi = dpi;
    }

    public void Draw(in UiDrawContext context, SizeF size)
    {
        float width = context.PixelsToDips(size.Width);
        float height = context.PixelsToDips(size.Height);
        if (width <= 0.0f || height <= 0.0f)
        {
            return;
        }

        ID2D1RenderTarget renderTarget = context.RenderTarget;
        var panel = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, width, height),
            12.0f,
            12.0f);
        renderTarget.FillRoundedRectangle(panel, _backgroundBrush);
        renderTarget.DrawRoundedRectangle(panel, _borderBrush);

        for (int index = 0; index < _buttons.Length; index++)
        {
            context.DrawElement(_buttons[index], GetButtonBounds(index, size));
        }
    }

    public bool HandlePointer(in UiPointerEvent input, SizeF size)
    {
        if (_capturedButton >= 0)
        {
            int capturedButton = _capturedButton;
            RectangleF bounds = GetButtonBounds(capturedButton, size);
            bool handled = _buttons[capturedButton].HandlePointer(
                input.ToLocal(bounds),
                bounds.Size);

            if (input.Kind == UiPointerEventKind.Released)
            {
                _capturedButton = -1;
            }

            return handled;
        }

        switch (input.Kind)
        {
            case UiPointerEventKind.Moved:
                bool changed = false;
                for (int index = 0; index < _buttons.Length; index++)
                {
                    RectangleF bounds = GetButtonBounds(index, size);
                    changed |= _buttons[index].HandlePointer(
                        input.ToLocal(bounds),
                        bounds.Size);
                }

                return changed;

            case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                int pressedButton = HitTest(input.Position, size);
                if (pressedButton >= 0)
                {
                    RectangleF bounds = GetButtonBounds(pressedButton, size);
                    if (_buttons[pressedButton].HandlePointer(
                        input.ToLocal(bounds),
                        bounds.Size))
                    {
                        _capturedButton = pressedButton;
                    }
                }

                return true;

            case UiPointerEventKind.Released:
                return true;

            case UiPointerEventKind.DoubleClicked when input.Button == PointerButton.Primary:
                int hitButton = HitTest(input.Position, size);
                if (hitButton >= 0)
                {
                    RectangleF bounds = GetButtonBounds(hitButton, size);
                    _buttons[hitButton].HandlePointer(
                        input.ToLocal(bounds),
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
