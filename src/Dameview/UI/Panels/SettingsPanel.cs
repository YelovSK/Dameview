using System.Drawing;
using Dameview.Navigation;
using Dameview.Settings;
using Dameview.UI.Components;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class SettingsPanel : IModalContent, IDisposable
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
    private readonly UiPointerRouter _pointerRouter = new();
    private int _focus;
    private int _field;
    private bool _second;
    private float _dpi;

    internal SettingsPanel(IDWriteFactory factory, Action close,
        Action<ThemeMode> setTheme, Action<FolderSort> setSort, float dpi)
    {
        _dpi = dpi;
        _setSort = setSort;
        _heading = factory.CreateTextFormat("Segoe UI Variable", FontWeight.SemiBold, FontStyle.Normal, 22);
        _text = factory.CreateTextFormat("Segoe UI Variable", FontWeight.Normal, FontStyle.Normal, 14);
        _buttons =
        [
            new Button(factory, "Close", close),
            new Button(factory, "Dark", () => setTheme(ThemeMode.Dark)),
            new Button(factory, "Light", () => setTheme(ThemeMode.Light)),
            new Button(factory, "Name", () => SelectSort(0, _second)),
            new Button(factory, "Modified", () => SelectSort(1, _second)),
            new Button(factory, "Created", () => SelectSort(2, _second)),
            new Button(factory, "Size", () => SelectSort(3, _second)),
            new Button(factory, "A–Z", () => SelectSort(_field, false)),
            new Button(factory, "Z–A", () => SelectSort(_field, true)),
        ];
        ApplySettings(new AppSettings());
    }

    public SizeF PreferredSize => new(440, 460);
    internal string? Error { get; set; }

    internal void SetDpi(float dpi)
    {
        _dpi = dpi;
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

    public void Focus() => SetFocus(1);

    public void HandleKey(UiKeyEvent input)
    {
        switch (input.Key)
        {
            case 0x09: // Tab
                SetFocus((_focus + (input.Shift ? _buttons.Length - 1 : 1)) % _buttons.Length);
                break;
            case 0x25: // Left / Up
            case 0x26:
                SetFocus((_focus + _buttons.Length - 1) % _buttons.Length);
                break;
            case 0x27: // Right / Down
            case 0x28:
                SetFocus((_focus + 1) % _buttons.Length);
                break;
            case 0x20: // Space / Enter
            case 0x0D:
                _buttons[_focus].Activate();
                break;
        }
    }

    public RectangleF GetFocusBounds(SizeF size) => GetButtonBounds(_focus, size);

    public bool Update(in UiUpdateContext context)
    {
        bool continues = false;
        foreach (Button button in _buttons)
        {
            continues |= button.Update(context);
        }

        return continues;
    }

    public void Draw(in UiDrawContext context, SizeF size)
    {
        float width = context.PixelsToDips(size.Width);
        context.DrawText("Settings", _heading, new Rect(24, 22, MathF.Max(0, width - 140), 36), context.Palette.PrimaryText);
        context.DrawText("Theme", _text, new Rect(24, 80, width - 48, 24), context.Palette.SecondaryText);
        context.DrawText("Sort by", _text, new Rect(24, 164, width - 48, 24), context.Palette.SecondaryText);
        context.DrawText("Direction", _text, new Rect(24, 292, width - 48, 24), context.Palette.SecondaryText);
        for (int index = 0; index < _buttons.Length; index++)
        {
            context.DrawElement(_buttons[index], GetButtonBounds(index, size));
        }

        context.DrawText(Error ?? "Changes are saved automatically.", _text,
            new Rect(24, 376, MathF.Max(0, width - 48), 72),
            Error is null ? context.Palette.SecondaryText : context.Palette.ErrorText);
    }

    public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
    {
        if (input.Kind == UiPointerEventKind.Cancelled)
        {
            _pointerRouter.Cancel();
            foreach (Button button in _buttons)
            {
                button.HandlePointer(input, SizeF.Empty);
            }

            return new UiPointerResult(NeedsRepaint: true);
        }

        if (_pointerRouter.Captured is { } captured)
        {
            return _pointerRouter.DispatchCaptured(input, GetButtonBounds(Array.IndexOf(_buttons, captured), size));
        }

        bool repaint = false;
        for (int index = 0; index < _buttons.Length; index++)
        {
            RectangleF bounds = GetButtonBounds(index, size);
            if (input.Kind == UiPointerEventKind.Pressed && bounds.Contains(input.Position))
            {
                SetFocus(index);
            }

            UiPointerResult result = _pointerRouter.Route(_buttons[index], bounds, input,
                observeOutside: input.Kind == UiPointerEventKind.Moved);
            repaint |= result.NeedsRepaint;
            if (result.Consumed && input.Kind != UiPointerEventKind.Moved)
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

        _heading.Dispose();
        _text.Dispose();
    }

    private void SelectSort(int field, bool second) => _setSort(second ? Sorts[field].Second : Sorts[field].First);

    private void SetFocus(int index)
    {
        _buttons[_focus].IsFocused = false;
        _focus = index;
        _buttons[_focus].IsFocused = true;
    }

    private RectangleF GetButtonBounds(int index, SizeF size)
    {
        float scale = UiDpi.GetScale(_dpi);
        float width = size.Width / scale;
        if (index == 0)
        {
            return new RectangleF(MathF.Max(0, width - 96) * scale, 22 * scale, 72 * scale, 36 * scale);
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
        return new RectangleF((24 + column * (buttonWidth + 8)) * scale, y * scale, buttonWidth * scale, 36 * scale);
    }
}
