using System.Drawing;
using Dameview.Commands;
using Dameview.Imaging.Decoding;
using Dameview.Rendering;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Viewing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class EmptyStatePanel : UiElement, IDisposable
{
    private readonly AppIcon _icon;
    private readonly CommandRow[] _rows;

    internal EmptyStatePanel(
        ID2D1DeviceContext deviceContext,
        ICommandRunner commands,
        ViewerPane pane,
        ViewerKeyBindings keyBindings)
    {
        CommandRow Row(Command command) =>
            new(command, () => commands.Execute(command, CommandContext.For(pane.ActiveTab)));

        _icon = new AppIcon(deviceContext)
        {
            HorizontalAlignment = UiAlignment.Center,
            Margin = new UiThickness(4.0f),
        };
        _rows = [Row(AppCommands.OpenFile), Row(AppCommands.ShowCommandPalette), Row(AppCommands.ShowSettings)];
        var title = new TextBlock("Dameview", UiTextStyle.Title, UiTextTone.Primary, UiTextWrapping.Wrap, TextAlignment.Center)
        {
            Margin = new UiThickness(0.0f, UiDesign.LargeSpacing, 0.0f, 0.0f),
        };
        var hint = new TextBlock(
            "Drop an image here to open it.",
            UiTextStyle.Body,
            UiTextTone.Secondary,
            UiTextWrapping.Wrap,
            TextAlignment.Center);
        var commandList = new StackPanel(UiOrientation.Vertical, _rows)
        {
            Spacing = UiDesign.SmallSpacing,
            MaxWidth = 280.0f,
            Margin = new UiThickness(0.0f, 24.0f, 0.0f, 0.0f),
        };
        var techText = new TextBlock(
            "Win32  •  Direct2D  •  Native AOT",
            UiTextStyle.Caption,
            UiTextTone.Secondary,
            UiTextWrapping.NoWrap)
        {
            Margin = new UiThickness(20.0f, 8.0f),
        };
        var techPill = new Surface(techText)
        {
            CornerRadius = 16.0f,
            Fill = UiSurfaceFill.None,
            HorizontalAlignment = UiAlignment.Center,
            Margin = new UiThickness(0.0f, 20.0f, 0.0f, 0.0f),
        };
        var content = new StackPanel(UiOrientation.Vertical, _icon, title, hint, commandList, techPill)
        {
            Margin = new UiThickness(24.0f, 32.0f, 24.0f, 28.0f),
        };
        AddChild(new Surface(content)
        {
            CornerRadius = 24.0f,
            HorizontalAlignment = UiAlignment.Center,
            VerticalAlignment = UiAlignment.Center,
            MaxWidth = 560.0f,
            Margin = new UiThickness(24.0f),
        });

        ApplyKeyBindings(keyBindings);
    }

    internal void ApplyKeyBindings(ViewerKeyBindings keyBindings)
    {
        foreach (CommandRow row in _rows)
        {
            row.Shortcut = keyBindings.GetShortcuts(row.Command) is [var first, ..] ? first.Text : null;
        }
    }

    internal void RecreateDeviceResources(ID2D1DeviceContext deviceContext) =>
        _icon.RecreateDeviceResources(deviceContext);

    public void Dispose() => _icon.Dispose();

    private sealed class AppIcon : UiElement, IDisposable
    {
        private const float Size = 64.0f;

        private ID2D1Bitmap1 _bitmap;

        internal AppIcon(ID2D1DeviceContext deviceContext) => _bitmap = Load(deviceContext);

        internal void RecreateDeviceResources(ID2D1DeviceContext deviceContext)
        {
            _bitmap.Dispose();
            _bitmap = Load(deviceContext);
        }

        public void Dispose() => _bitmap.Dispose();

        protected override SizeF MeasureCore(SizeF availableSize) => new(Size, Size);

        protected override void DrawCore(in UiDrawContext context) =>
            context.DrawBitmap(
                _bitmap,
                new Rect(0.0f, 0.0f, Size, Size),
                new Rect(0.0f, 0.0f, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height));

        private static ID2D1Bitmap1 Load(ID2D1DeviceContext deviceContext)
        {
            using Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream(
                "Dameview.Assets.dameview.png")
                ?? throw new InvalidOperationException("The embedded application icon could not be found.");
            using var decoder = new ImageDecoder();
            return D2DBitmapFactory.Create(deviceContext, decoder.Decode(stream));
        }
    }

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
