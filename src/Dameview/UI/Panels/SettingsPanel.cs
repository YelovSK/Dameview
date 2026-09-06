using System.Drawing;
using Dameview.Navigation;
using Dameview.Settings;
using Dameview.UI.Components;
using Dameview.UI.Layout;
using Vortice.DirectWrite;

namespace Dameview.UI.Panels;

internal sealed class SettingsPanel : ModalContent, IDisposable
{
    private enum SettingsTab
    {
        Appearance,
        Sorting,
    }

    private enum SortField
    {
        Name,
        DateModified,
        DateCreated,
        Size,
    }

    private enum SortDirection
    {
        First,
        Second,
    }

    private static readonly SortDefinition[] Sorts =
    [
        new(SortField.Name, FolderSort.NameAscending, FolderSort.NameDescending, "A–Z", "Z–A"),
        new(SortField.DateModified, FolderSort.DateModifiedNewest, FolderSort.DateModifiedOldest, "Newest", "Oldest"),
        new(SortField.DateCreated, FolderSort.DateCreatedNewest, FolderSort.DateCreatedOldest, "Newest", "Oldest"),
        new(SortField.Size, FolderSort.SizeLargest, FolderSort.SizeSmallest, "Largest", "Smallest"),
    ];

    private readonly Button _closeButton;
    private readonly Button[] _themeButtons;
    private readonly Dropdown<SortField> _sortField;
    private readonly Dropdown<SortDirection> _sortDirection;
    private readonly SettingsRow _themeRow;
    private readonly SettingsRow _sortFieldRow;
    private readonly SettingsRow _sortDirectionRow;
    private readonly TabStrip _tabs;
    private readonly ScrollView _appearancePage;
    private readonly ScrollView _sortingPage;
    private readonly Overlay _pages;
    private readonly TextBlock _title;
    private readonly TextBlock _message;
    private readonly PopupHost _popupHost;
    private readonly Action<FolderSort> _setSort;
    private string? _error;

    internal SettingsPanel(
        IDWriteFactory factory,
        PopupHost popupHost,
        Action close,
        Action<ThemeMode> setTheme,
        Action<FolderSort> setSort)
    {
        _popupHost = popupHost;
        _setSort = setSort;
        _title = new TextBlock(
            factory,
            "Settings",
            UiTextStyle.Heading,
            UiTextTone.Primary,
            UiTextWrapping.NoWrap);
        _message = new TextBlock(
            factory,
            "Changes are saved automatically.",
            UiTextStyle.Body,
            UiTextTone.Secondary,
            UiTextWrapping.Wrap);
        _closeButton = new Button(
            factory,
            UiTypography.CloseIcon,
            close,
            fontFamily: UiTypography.IconFontFamily,
            fontSize: 16.0f);

        _themeButtons =
        [
            new Button(factory, "Dark", () => setTheme(ThemeMode.Dark)),
            new Button(factory, "Light", () => setTheme(ThemeMode.Light)),
        ];
        var themeChoices = new StackPanel(
            UiOrientation.Horizontal,
            UiDesign.SmallSpacing,
            StackPanelDistribution.Equal,
            _themeButtons);
        _themeRow = new SettingsRow(factory, "Theme", themeChoices);

        _sortField = new Dropdown<SortField>(
            factory,
            popupHost,
            [
                new("Name", SortField.Name),
                new("Modified date", SortField.DateModified),
                new("Created date", SortField.DateCreated),
                new("Size", SortField.Size),
            ],
            SortField.Name,
            SetSortField);
        _sortDirection = new Dropdown<SortDirection>(
            factory,
            popupHost,
            [
                new("A–Z", SortDirection.First),
                new("Z–A", SortDirection.Second),
            ],
            SortDirection.First,
            SetSortDirection);
        _sortFieldRow = new SettingsRow(factory, "Sort by", _sortField);
        _sortDirectionRow = new SettingsRow(factory, "Direction", _sortDirection);

        var appearanceContent = new StackPanel(
            UiOrientation.Vertical,
            UiDesign.LargeSpacing,
            StackPanelDistribution.Natural,
            _themeRow);
        var sortingContent = new StackPanel(
            UiOrientation.Vertical,
            UiDesign.LargeSpacing,
            StackPanelDistribution.Natural,
            _sortFieldRow,
            _sortDirectionRow);
        _appearancePage = new ScrollView(appearanceContent);
        _sortingPage = new ScrollView(sortingContent);
        _pages = new Overlay(_appearancePage, _sortingPage);
        _tabs = new TabStrip(
            factory,
            ["Appearance", "Sorting"],
            (int)SettingsTab.Appearance,
            SelectTab);

        AddChild(_title);
        AddChild(_closeButton);
        AddChild(_tabs);
        AddChild(_pages);
        AddChild(_message);

        SelectTab((int)SettingsTab.Appearance);
        ApplySettings(new AppSettings());
    }

    internal override SizeF PreferredSize => new(440.0f, 460.0f);
    internal override UiElement InitialFocus => _tabs.SelectedTab;
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
        _themeButtons[0].IsSelected = settings.Theme == ThemeMode.Dark;
        _themeButtons[1].IsSelected = settings.Theme == ThemeMode.Light;

        SortDefinition sort = Array.Find(
            Sorts,
            candidate => candidate.First == settings.Sort || candidate.Second == settings.Sort);
        _sortField.SelectedValue = sort.Field;
        UpdateDirectionLabels(sort);
        _sortDirection.SelectedValue = settings.Sort == sort.First
            ? SortDirection.First
            : SortDirection.Second;
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float contentWidth = MathF.Max(0.0f, availableSize.Width - 48.0f);
        float bodyHeight = CalculateBodyHeight(availableSize.Height);
        _title.Measure(new SizeF(MathF.Max(0.0f, availableSize.Width - 140.0f), 36.0f));
        _closeButton.Measure(new SizeF(72.0f, 36.0f));
        _tabs.Measure(new SizeF(contentWidth, 36.0f));
        _pages.Measure(new SizeF(contentWidth, bodyHeight));
        _message.Measure(new SizeF(contentWidth, 40.0f));
        return PreferredSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        float contentWidth = MathF.Max(0.0f, finalSize.Width - 48.0f);
        float closeWidth = MathF.Min(72.0f, contentWidth);
        _title.Arrange(new RectangleF(24.0f, 20.0f, MathF.Max(0.0f, finalSize.Width - 140.0f), 36.0f));
        _closeButton.Arrange(new RectangleF(MathF.Max(24.0f, finalSize.Width - 96.0f), 20.0f, closeWidth, 36.0f));
        _tabs.Arrange(new RectangleF(24.0f, 68.0f, contentWidth, 36.0f));
        _pages.Arrange(new RectangleF(24.0f, 120.0f, contentWidth, CalculateBodyHeight(finalSize.Height)));

        bool showMessage = finalSize.Height >= 300.0f;
        _message.Arrange(showMessage
            ? new RectangleF(24.0f, finalSize.Height - 52.0f, contentWidth, 40.0f)
            : new RectangleF(24.0f, finalSize.Height, contentWidth, 0.0f));
    }

    public void Dispose()
    {
        _closeButton.Dispose();
        foreach (Button button in _themeButtons)
        {
            button.Dispose();
        }

        _sortField.Dispose();
        _sortDirection.Dispose();
        _themeRow.Dispose();
        _sortFieldRow.Dispose();
        _sortDirectionRow.Dispose();
        _tabs.Dispose();
        _title.Dispose();
        _message.Dispose();
    }

    private static float CalculateBodyHeight(float panelHeight)
    {
        float bottom = panelHeight >= 300.0f ? panelHeight - 64.0f : panelHeight - 12.0f;
        return MathF.Max(0.0f, bottom - 120.0f);
    }

    private void SelectTab(int index)
    {
        _popupHost.Close();
        _appearancePage.IsVisible = index == (int)SettingsTab.Appearance;
        _sortingPage.IsVisible = index == (int)SettingsTab.Sorting;
    }

    private void SetSortField(SortField field)
    {
        SortDefinition sort = Sorts[(int)field];
        UpdateDirectionLabels(sort);
        _setSort(_sortDirection.SelectedValue == SortDirection.First ? sort.First : sort.Second);
    }

    private void SetSortDirection(SortDirection direction)
    {
        SortDefinition sort = Sorts[(int)_sortField.SelectedValue];
        _setSort(direction == SortDirection.First ? sort.First : sort.Second);
    }

    private void UpdateDirectionLabels(SortDefinition sort)
    {
        _sortDirection.SetOptionLabel(SortDirection.First, sort.FirstLabel);
        _sortDirection.SetOptionLabel(SortDirection.Second, sort.SecondLabel);
    }

    private readonly record struct SortDefinition(
        SortField Field,
        FolderSort First,
        FolderSort Second,
        string FirstLabel,
        string SecondLabel);
}
