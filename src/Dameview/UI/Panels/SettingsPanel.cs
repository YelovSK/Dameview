using System.Drawing;
using Dameview.Navigation;
using Dameview.Settings;
using Dameview.UI.Components;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class SettingsPanel : ModalContent, IDisposable
{
    private static readonly (FolderSort First, FolderSort Second)[] Sorts =
    [
        (FolderSort.NameAscending, FolderSort.NameDescending),
        (FolderSort.DateModifiedNewest, FolderSort.DateModifiedOldest),
        (FolderSort.DateCreatedNewest, FolderSort.DateCreatedOldest),
        (FolderSort.SizeLargest, FolderSort.SizeSmallest),
    ];

    private readonly Button[] _buttons;
    private readonly IDWriteTextFormat _heading;
    private readonly IDWriteTextFormat _text;
    private readonly Action<FolderSort> _setSort;
    private int _field;
    private bool _second;

    internal SettingsPanel(IDWriteFactory factory, Action close,
        Action<ThemeMode> setTheme, Action<FolderSort> setSort, UiDesignTokens design)
    {
        _setSort = setSort;
        _heading = factory.CreateTextFormat(
            "Segoe UI Variable", FontWeight.SemiBold, FontStyle.Normal, design.HeadingFontSize);
        _text = factory.CreateTextFormat(
            "Segoe UI Variable", FontWeight.Normal, FontStyle.Normal, design.BodyFontSize);
        _buttons =
        [
            new Button(factory, "Close", close, design),
            new Button(factory, "Dark", () => setTheme(ThemeMode.Dark), design),
            new Button(factory, "Light", () => setTheme(ThemeMode.Light), design),
            new Button(factory, "Name", () => SelectSort(0, _second), design),
            new Button(factory, "Modified", () => SelectSort(1, _second), design),
            new Button(factory, "Created", () => SelectSort(2, _second), design),
            new Button(factory, "Size", () => SelectSort(3, _second), design),
            new Button(factory, "A–Z", () => SelectSort(_field, false), design),
            new Button(factory, "Z–A", () => SelectSort(_field, true), design),
        ];
        foreach (Button button in _buttons)
        {
            AddChild(button);
        }

        ApplySettings(new AppSettings());
    }

    internal override SizeF PreferredSize => new(440, 460);
    internal override UiElement InitialFocus => _buttons[1];
    internal string? Error { get; set; }

    internal void ApplySettings(AppSettings settings)
    {
        _field = Array.FindIndex(Sorts, pair => pair.First == settings.Sort || pair.Second == settings.Sort);
        _second = settings.Sort == Sorts[_field].Second;
        _buttons[1].IsSelected = settings.Theme == ThemeMode.Dark;
        _buttons[2].IsSelected = settings.Theme == ThemeMode.Light;
        for (int index = 0; index < Sorts.Length; index++)
        {
            _buttons[index + 3].IsSelected = index == _field;
        }

        _buttons[7].IsSelected = !_second;
        _buttons[8].IsSelected = _second;
        (_buttons[7].Label, _buttons[8].Label) = _field switch
        {
            0 => ("A–Z", "Z–A"),
            3 => ("Largest", "Smallest"),
            _ => ("Newest", "Oldest"),
        };
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        foreach (Button button in _buttons)
        {
            button.Measure(new SizeF(availableSize.Width, 36.0f));
        }

        return PreferredSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        for (int index = 0; index < _buttons.Length; index++)
        {
            _buttons[index].Arrange(GetButtonBounds(index, finalSize));
        }
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        float width = Bounds.Width;
        context.DrawText("Settings", _heading, new Rect(24, 22, MathF.Max(0, width - 140), 36), context.Palette.PrimaryText);
        context.DrawText("Theme", _text, new Rect(24, 80, width - 48, 24), context.Palette.SecondaryText);
        context.DrawText("Sort by", _text, new Rect(24, 164, width - 48, 24), context.Palette.SecondaryText);
        context.DrawText("Direction", _text, new Rect(24, 292, width - 48, 24), context.Palette.SecondaryText);
        context.DrawText(Error ?? "Changes are saved automatically.", _text,
            new Rect(24, 376, MathF.Max(0, width - 48), 72),
            Error is null ? context.Palette.SecondaryText : context.Palette.ErrorText);
    }

    public void Dispose()
    {
        foreach (Button button in _buttons)
        {
            button.Dispose();
        }

        _heading.Dispose();
        _text.Dispose();
    }

    private void SelectSort(int field, bool second) => _setSort(second ? Sorts[field].Second : Sorts[field].First);

    private static RectangleF GetButtonBounds(int index, SizeF size)
    {
        float width = size.Width;
        if (index == 0)
        {
            return new RectangleF(MathF.Max(0, width - 96), 22, 72, 36);
        }

        int column = (index - 1) % 2;
        float y = index switch
        {
            <= 2 => 108,
            <= 4 => 192,
            <= 6 => 236,
            _ => 320,
        };
        float buttonWidth = MathF.Max(0, (width - 56) / 2);
        return new RectangleF(24 + column * (buttonWidth + 8), y, buttonWidth, 36);
    }
}
