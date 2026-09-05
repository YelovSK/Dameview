using System.Drawing;
using Dameview.Navigation;
using Dameview.Settings;
using Dameview.UI.Components;
using Dameview.UI.Layout;
using Vortice.DirectWrite;

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
    private readonly StackPanel[] _buttonRows;
    private readonly TextBlock _title;
    private readonly TextBlock _themeHeading;
    private readonly TextBlock _sortHeading;
    private readonly TextBlock _directionHeading;
    private readonly TextBlock _message;
    private readonly Action<FolderSort> _setSort;
    private string? _error;
    private int _field;
    private bool _second;

    internal SettingsPanel(IDWriteFactory factory, Action close,
        Action<ThemeMode> setTheme, Action<FolderSort> setSort, UiDesignTokens design)
    {
        _setSort = setSort;
        _title = CreateText(factory, "Settings", UiTextStyle.Heading, UiTextTone.Primary, design);
        _themeHeading = CreateText(factory, "Theme", UiTextStyle.Body, UiTextTone.Secondary, design);
        _sortHeading = CreateText(factory, "Sort by", UiTextStyle.Body, UiTextTone.Secondary, design);
        _directionHeading = CreateText(factory, "Direction", UiTextStyle.Body, UiTextTone.Secondary, design);
        _message = new TextBlock(
            factory,
            "Changes are saved automatically.",
            UiTextStyle.Body,
            UiTextTone.Secondary,
            UiTextWrapping.Wrap,
            design);
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
        _buttonRows =
        [
            CreateRow(design, _buttons[1], _buttons[2]),
            CreateRow(design, _buttons[3], _buttons[4]),
            CreateRow(design, _buttons[5], _buttons[6]),
            CreateRow(design, _buttons[7], _buttons[8]),
        ];
        AddChild(_title);
        AddChild(_themeHeading);
        AddChild(_sortHeading);
        AddChild(_directionHeading);
        AddChild(_message);
        AddChild(_buttons[0]);
        foreach (StackPanel row in _buttonRows)
        {
            AddChild(row);
        }

        ApplySettings(new AppSettings());
    }

    internal override SizeF PreferredSize => new(440, 460);
    internal override UiElement InitialFocus => _buttons[1];
    internal string? Error
    {
        get => _error;
        set
        {
            _error = value;
            _message.Text = value ?? "Changes are saved automatically.";
            _message.Tone = value is null ? UiTextTone.Secondary : UiTextTone.Error;
        }
    }

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
        _title.Measure(new SizeF(MathF.Max(0.0f, availableSize.Width - 140.0f), 36.0f));
        _themeHeading.Measure(new SizeF(MathF.Max(0.0f, availableSize.Width - 48.0f), 24.0f));
        _sortHeading.Measure(new SizeF(MathF.Max(0.0f, availableSize.Width - 48.0f), 24.0f));
        _directionHeading.Measure(new SizeF(MathF.Max(0.0f, availableSize.Width - 48.0f), 24.0f));
        _message.Measure(new SizeF(MathF.Max(0.0f, availableSize.Width - 48.0f), 72.0f));
        _buttons[0].Measure(new SizeF(72.0f, 36.0f));
        foreach (StackPanel row in _buttonRows)
        {
            row.Measure(new SizeF(MathF.Max(0.0f, availableSize.Width - 48.0f), 36.0f));
        }

        return PreferredSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        _title.Arrange(new RectangleF(24.0f, 22.0f, MathF.Max(0.0f, finalSize.Width - 140.0f), 36.0f));
        _themeHeading.Arrange(new RectangleF(24.0f, 80.0f, MathF.Max(0.0f, finalSize.Width - 48.0f), 24.0f));
        _sortHeading.Arrange(new RectangleF(24.0f, 164.0f, MathF.Max(0.0f, finalSize.Width - 48.0f), 24.0f));
        _directionHeading.Arrange(new RectangleF(24.0f, 292.0f, MathF.Max(0.0f, finalSize.Width - 48.0f), 24.0f));
        _message.Arrange(new RectangleF(24.0f, 376.0f, MathF.Max(0.0f, finalSize.Width - 48.0f), 72.0f));
        _buttons[0].Arrange(new RectangleF(MathF.Max(0.0f, finalSize.Width - 96.0f), 22.0f, 72.0f, 36.0f));
        float rowWidth = MathF.Max(0.0f, finalSize.Width - 48.0f);
        float[] rowY = [108.0f, 192.0f, 236.0f, 320.0f];
        for (int index = 0; index < _buttonRows.Length; index++)
        {
            _buttonRows[index].Arrange(new RectangleF(24.0f, rowY[index], rowWidth, 36.0f));
        }
    }

    public void Dispose()
    {
        foreach (Button button in _buttons)
        {
            button.Dispose();
        }

        _title.Dispose();
        _themeHeading.Dispose();
        _sortHeading.Dispose();
        _directionHeading.Dispose();
        _message.Dispose();
    }

    private void SelectSort(int field, bool second) => _setSort(second ? Sorts[field].Second : Sorts[field].First);

    private static TextBlock CreateText(
        IDWriteFactory factory,
        string text,
        UiTextStyle style,
        UiTextTone tone,
        UiDesignTokens design)
    {
        return new TextBlock(factory, text, style, tone, UiTextWrapping.NoWrap, design);
    }

    private static StackPanel CreateRow(UiDesignTokens design, params UiElement[] children)
    {
        return new StackPanel(
            UiOrientation.Horizontal,
            design.SmallSpacing,
            StackPanelDistribution.Equal,
            children);
    }
}
