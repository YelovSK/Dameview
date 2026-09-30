using System.Drawing;
using Dameview.Commands;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class ShortcutChip : InteractiveControl
{
    internal const float Height = 24.0f;

    private const float HorizontalPadding = 10.0f;
    private const float RemoveWidth = 22.0f;
    private const float MinimumWidth = 34.0f;
    private static readonly UiFont LabelFont = new(12.0f, FontWeight.SemiBold, TextAlignment.Center);
    private static readonly UiFont RemoveFont = new(15.0f, FontWeight.SemiBold, TextAlignment.Center);

    private const string RecordingLabel = "Press a key";

    private readonly Action<ViewerCommandShortcut> _recorded;
    private readonly Action _removed;
    private bool _removeHovered;

    /// <summary>
    /// A chip records a shortcut while it holds the keyboard. One without a shortcut is being
    /// added, and asks to be removed when it stops recording without one.
    /// </summary>
    internal ShortcutChip(Action<ViewerCommandShortcut> recorded, Action removed)
    {
        _recorded = recorded;
        _removed = removed;
    }

    internal ViewerCommandShortcut? Shortcut
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            InvalidateLayout();
        }
    }

    internal bool IsRecording
    {
        get;
        private set
        {
            field = value;
            _removeHovered = false;
            InvalidateLayout();
        }
    }

    internal override bool ObservePointerMoves => true;

    private string Label => IsRecording || Shortcut is null ? RecordingLabel : Shortcut.Value.Text;
    private bool CanRemove => IsRecording && Shortcut is not null;

    // Holding the keyboard also keeps a shortcut from firing the command it is being bound to.
    internal void BeginRecording()
    {
        Root?.CaptureKeyboard(this);
        IsRecording = true;
    }

    internal override bool OnKeyEvent(WindowKeyEvent input)
    {
        if (!IsRecording)
        {
            return base.OnKeyEvent(input);
        }

        switch (input.Key)
        {
            case WindowKey.Escape:
                Root?.ReleaseKeyboard(this);
                break;

            // Keys WindowKey does not name, such as a bare modifier, have no text form and so
            // could not be written to settings.
            case var key when !Enum.IsDefined(key):
                break;

            default:
                _recorded(new ViewerCommandShortcut(input.Key, input.Control, input.Shift));
                Root?.ReleaseKeyboard(this);
                break;
        }

        return true;
    }

    internal override void OnKeyboardCaptureLost()
    {
        IsRecording = false;
        if (Shortcut is null)
        {
            _removed();
        }
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float textWidth = MathF.Ceiling(TextLayouts
            .Get(Label, LabelFont, new SizeF(10_000.0f, Height))
            .Metrics.WidthIncludingTrailingWhitespace);
        float width = textWidth + (2.0f * HorizontalPadding) + (CanRemove ? RemoveWidth : 0.0f);
        return new SizeF(MathF.Max(MinimumWidth, width), Height);
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        if (CanRemove
            && input.Kind == WindowPointerEventKind.Pressed
            && input.Button == PointerButton.Primary
            && GetRemoveBounds().Contains(input.Position))
        {
            Root?.ReleaseKeyboard(this);
            _removed();
            return new UiPointerResult(Consumed: true, NeedsRepaint: true);
        }

        return base.OnPointerEvent(input);
    }

    protected override void ObservePointerMove(in WindowPointerEvent input)
    {
        bool hovered = CanRemove && GetRemoveBounds().Contains(input.Position);
        if (_removeHovered == hovered)
        {
            return;
        }

        _removeHovered = hovered;
        InvalidateVisual();
    }

    protected override void ObservePointerLeave()
    {
        if (_removeHovered)
        {
            _removeHovered = false;
            InvalidateVisual();
        }
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        var background = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(background, context.Palette.ControlSurface);
        if (HoverAmount > 0.0f)
        {
            context.FillRoundedRectangle(background, context.Palette.ControlHover, HoverAmount);
        }

        if (PressedAmount > 0.0f)
        {
            context.FillRoundedRectangle(background, context.Palette.ControlPressed, PressedAmount);
        }

        float labelWidth = Bounds.Width - (CanRemove ? RemoveWidth : 0.0f);
        context.DrawText(
            Label,
            LabelFont,
            new Rect(0.0f, 0.0f, labelWidth, Bounds.Height),
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);

        if (CanRemove)
        {
            RectangleF remove = GetRemoveBounds();
            Color4 error = context.Palette.ErrorText;
            context.DrawText(
                "×",
                RemoveFont,
                new Rect(remove.X, remove.Y, remove.Width, remove.Height),
                _removeHovered ? error : new Color4(error.R, error.G, error.B, 0.75f));
        }
    }

    protected override void Activate() => BeginRecording();

    private RectangleF GetRemoveBounds() =>
        new(MathF.Max(0.0f, Bounds.Width - RemoveWidth), 0.0f, RemoveWidth, Bounds.Height);
}
