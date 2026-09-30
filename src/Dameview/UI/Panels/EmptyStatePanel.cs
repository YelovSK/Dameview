using System.Drawing;
using Dameview.Commands;
using Dameview.Imaging.Decoding;
using Dameview.Rendering;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.Viewing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class EmptyStatePanel : UiElement, IDisposable
{
    private const float CardHeight = 408.0f;
    private const float RowWidth = 280.0f;
    private static readonly UiFont TitleFont = new(30.0f, FontWeight.SemiBold, TextAlignment.Center, Wrapping: WordWrapping.Wrap);
    private static readonly UiFont BodyFont = new(15.0f, FontWeight.Normal, TextAlignment.Center, Wrapping: WordWrapping.Wrap);
    private static readonly UiFont CaptionFont = new(12.0f, FontWeight.Medium, TextAlignment.Center, Wrapping: WordWrapping.Wrap);

    private readonly CommandRow[] _rows;
    private ID2D1Bitmap1 _icon;

    internal EmptyStatePanel(
        ID2D1DeviceContext deviceContext,
        ICommandRunner commands,
        ViewerPane pane,
        ViewerKeyBindings keyBindings)
    {
        _icon = LoadApplicationIcon(deviceContext);
        CommandRow Row(Command command) =>
            new(command, () => commands.Execute(command, CommandContext.For(pane.ActiveTab)));

        _rows = [Row(AppCommands.OpenFile), Row(AppCommands.ShowCommandPalette), Row(AppCommands.ShowSettings)];
        foreach (CommandRow row in _rows)
        {
            AddChild(row);
        }

        ApplyKeyBindings(keyBindings);
    }

    internal void ApplyKeyBindings(ViewerKeyBindings keyBindings)
    {
        foreach (CommandRow row in _rows)
        {
            row.Shortcut = keyBindings.GetShortcuts(row.Command) is [var first, ..] ? first.Text : null;
        }
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        foreach (CommandRow row in _rows)
        {
            row.Measure(new SizeF(RowWidth, CommandRow.Height));
        }

        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        EmptyStateLayout layout = CalculateLayout(finalSize);
        float y = layout.Commands.Y;
        foreach (CommandRow row in _rows)
        {
            row.Arrange(new RectangleF(layout.Commands.X, y, layout.Commands.Width, CommandRow.Height));
            y += CommandRow.Height + UiDesign.SmallSpacing;
        }
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        EmptyStateLayout layout = CalculateLayout(Bounds.Size);

        RoundedRectangle card = new(
            layout.Card,
            24.0f,
            24.0f);
        context.FillRoundedRectangle(card, context.Palette.Surface);
        context.DrawRoundedRectangle(card, context.Palette.SurfaceBorder);

        float markSize = 72.0f;
        float markX = (Bounds.Width - markSize) / 2.0f;
        float markY = layout.Card.Y + 32.0f;
        RoundedRectangle mark = new(
            new RectangleF(markX, markY, markSize, markSize),
            18.0f,
            18.0f);
        float iconSize = markSize - 8.0f;
        context.DrawBitmap(
            _icon,
            new Rect(markX + 4.0f, markY + 4.0f, iconSize, iconSize),
            new Rect(0.0f, 0.0f, _icon.PixelSize.Width, _icon.PixelSize.Height));

        context.DrawText(
            "Dameview",
            TitleFont,
            new Rect(layout.Card.X + 24.0f, markY + markSize + 24.0f, layout.Card.Width - 48.0f, 44.0f),
            context.Palette.PrimaryText);

        context.DrawText(
            "Drop an image here to open it.",
            BodyFont,
            new Rect(layout.Card.X + 24.0f, markY + markSize + 68.0f, layout.Card.Width - 48.0f, 32.0f),
            context.Palette.SecondaryText);

        RoundedRectangle pill = new(
            layout.Caption,
            16.0f,
            16.0f);
        context.DrawRoundedRectangle(pill, context.Palette.SurfaceBorder);
        context.DrawText(
            "Win32  •  Direct2D  •  Native AOT",
            CaptionFont,
            new Rect(layout.Caption.X, layout.Caption.Y, layout.Caption.Width, layout.Caption.Height),
            context.Palette.SecondaryText);
    }

    internal void RecreateDeviceResources(ID2D1DeviceContext deviceContext)
    {
        _icon.Dispose();
        _icon = LoadApplicationIcon(deviceContext);
    }

    public void Dispose()
    {
        _icon.Dispose();
    }

    private static ID2D1Bitmap1 LoadApplicationIcon(ID2D1DeviceContext deviceContext)
    {
        using Stream stream = typeof(EmptyStatePanel).Assembly.GetManifestResourceStream(
            "Dameview.Assets.dameview.png")
            ?? throw new InvalidOperationException("The embedded application icon could not be found.");
        using var decoder = new ImageDecoder();
        return D2DBitmapFactory.Create(deviceContext, decoder.Decode(stream));
    }

    private static EmptyStateLayout CalculateLayout(SizeF size)
    {
        float cardWidth = MathF.Min(560.0f, MathF.Max(280.0f, size.Width - 48.0f));
        float cardX = (size.Width - cardWidth) / 2.0f;
        float cardY = (size.Height - CardHeight) / 2.0f;
        var card = new RectangleF(cardX, cardY, cardWidth, CardHeight);
        float rowWidth = MathF.Min(RowWidth, cardWidth - 48.0f);
        var commands = new RectangleF(
            (size.Width - rowWidth) / 2.0f,
            card.Y + 216.0f,
            rowWidth,
            (3.0f * CommandRow.Height) + (2.0f * UiDesign.SmallSpacing));
        var caption = new RectangleF((size.Width - 254.0f) / 2.0f, commands.Bottom + 16.0f, 254.0f, 32.0f);
        return new EmptyStateLayout(card, commands, caption);
    }

    private readonly record struct EmptyStateLayout(
        RectangleF Card,
        RectangleF Commands,
        RectangleF Caption);

    private sealed class CommandRow : InteractiveControl
    {
        internal const float Height = 36.0f;

        private const float TextPadding = 14.0f;
        private static readonly UiFont LabelFont = new(UiDesign.BodyFontSize, FontWeight.SemiBold);
        private static readonly UiFont ShortcutFont = new(12.0f, Alignment: TextAlignment.Trailing);

        private readonly Action _execute;

        internal CommandRow(Command command, Action execute)
        {
            Command = command;
            _execute = execute;
        }

        internal Command Command { get; }

        internal string? Shortcut
        {
            get;
            set
            {
                if (field != value)
                {
                    field = value;
                    InvalidateVisual();
                }
            }
        }

        protected override SizeF MeasureCore(SizeF availableSize) => new(availableSize.Width, Height);

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

            var text = new Rect(TextPadding, 0.0f, MathF.Max(0.0f, Bounds.Width - 2.0f * TextPadding), Bounds.Height);
            context.DrawText(Command.Label, LabelFont, text, context.Palette.PrimaryText, DrawTextOptions.Clip);
            if (Shortcut is { } shortcut)
            {
                context.DrawText(shortcut, ShortcutFont, text, context.Palette.SecondaryText, DrawTextOptions.Clip);
            }
        }

        protected override void Activate() => _execute();
    }
}
