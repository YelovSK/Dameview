using System.Drawing;
using Dameview.Commands;
using Dameview.Navigation;
using Dameview.Settings;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Updates;
using Vortice.DirectWrite;

namespace Dameview.UI.Panels;

internal sealed class SettingsPanel : ModalContent, IDisposable
{
    private enum SettingsTab
    {
        Appearance,
        Layout,
        Sorting,
        Behavior,
        Updates,
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
    private readonly Dropdown<ThemeId> _themeDropdown;
    private readonly Toggle _galleryEnabledToggle;
    private readonly Dropdown<GalleryPlacement> _galleryPlacementDropdown;
    private readonly Dropdown<GalleryThumbnailSize> _galleryThumbnailSizeDropdown;
    private readonly Toggle _animationsToggle;
    private readonly Toggle _singleInstanceToggle;
    private readonly Dropdown<SortField> _sortField;
    private readonly Dropdown<SortDirection> _sortDirection;
    private readonly SettingsRow _themeRow;
    private readonly SettingsRow _galleryPlacementRow;
    private readonly SettingsRow _galleryThumbnailSizeRow;
    private readonly SettingsRow _sortFieldRow;
    private readonly SettingsRow _sortDirectionRow;
    private readonly TabStrip _tabs;
    private readonly ScrollView _appearancePage;
    private readonly ScrollView _layoutPage;
    private readonly ScrollView _behaviorPage;
    private readonly ScrollView _sortingPage;
    private readonly ScrollView _updatesPage;
    private readonly Overlay _pages;
    private readonly TextBlock _title;
    private readonly TextBlock _message;
    private readonly TextBlock _updateStatus;
    private readonly Button _updateButton;
    private readonly PopupHost _popupHost;
    private readonly ISettingsCommands _commands;

    internal SettingsPanel(
        IDWriteFactory factory,
        PopupHost popupHost,
        Action close,
        ISettingsCommands commands)
    {
        _popupHost = popupHost;
        _commands = commands;
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

        _themeDropdown = new Dropdown<ThemeId>(
            factory,
            popupHost,
            Themes.All
                .Select(theme => new DropdownOption<ThemeId>(theme.DisplayName, theme.Id))
                .ToArray(),
            ThemeId.Dark,
            _commands.SetTheme);
        _themeRow = new SettingsRow(factory, "Theme", _themeDropdown);
        _galleryEnabledToggle = new Toggle(
            factory,
            "Show gallery",
            value: true,
            _commands.SetGalleryEnabled);
        _galleryPlacementDropdown = new Dropdown<GalleryPlacement>(
            factory,
            popupHost,
            [
                new("Right", GalleryPlacement.Right),
                new("Left", GalleryPlacement.Left),
                new("Top", GalleryPlacement.Top),
                new("Bottom", GalleryPlacement.Bottom),
            ],
            GalleryPlacement.Right,
            _commands.SetGalleryPlacement);
        _galleryPlacementRow = new SettingsRow(factory, "Gallery position", _galleryPlacementDropdown);
        _galleryThumbnailSizeDropdown = new Dropdown<GalleryThumbnailSize>(
            factory,
            popupHost,
            [
                new("Small", GalleryThumbnailSize.Small),
                new("Medium", GalleryThumbnailSize.Medium),
                new("Large", GalleryThumbnailSize.Large),
            ],
            GalleryThumbnailSize.Medium,
            _commands.SetGalleryThumbnailSize);
        _galleryThumbnailSizeRow = new SettingsRow(
            factory,
            "Gallery thumbnails",
            _galleryThumbnailSizeDropdown);
        _animationsToggle = new Toggle(
            factory,
            "Animations",
            value: true,
            _commands.SetAnimationsEnabled);
        _singleInstanceToggle = new Toggle(
            factory,
            "Open files in existing window (restart required)",
            value: true,
            _commands.SetSingleInstance);

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

        _updateStatus = new TextBlock(
            factory,
            string.Empty,
            UiTextStyle.Body,
            UiTextTone.Secondary,
            UiTextWrapping.Wrap);
        _updateButton = new Button(factory, "Check for updates", _commands.ActivateUpdate);

        var appearanceContent = new StackPanel(
            UiOrientation.Vertical,
            UiDesign.LargeSpacing,
            StackPanelDistribution.Natural,
            _themeRow,
            _animationsToggle);
        var layoutContent = new StackPanel(
            UiOrientation.Vertical,
            UiDesign.LargeSpacing,
            StackPanelDistribution.Natural,
            _galleryEnabledToggle,
            _galleryPlacementRow,
            _galleryThumbnailSizeRow);
        var behaviorContent = new StackPanel(
            UiOrientation.Vertical,
            UiDesign.LargeSpacing,
            StackPanelDistribution.Natural,
            _singleInstanceToggle);
        var sortingContent = new StackPanel(
            UiOrientation.Vertical,
            UiDesign.LargeSpacing,
            StackPanelDistribution.Natural,
            _sortFieldRow,
            _sortDirectionRow);
        var updatesContent = new StackPanel(
            UiOrientation.Vertical,
            UiDesign.LargeSpacing,
            StackPanelDistribution.Natural,
            _updateStatus,
            _updateButton);
        _appearancePage = new ScrollView(appearanceContent);
        _layoutPage = new ScrollView(layoutContent);
        _behaviorPage = new ScrollView(behaviorContent);
        _sortingPage = new ScrollView(sortingContent);
        _updatesPage = new ScrollView(updatesContent);
        _pages = new Overlay(_appearancePage, _layoutPage, _sortingPage, _behaviorPage, _updatesPage);
        _tabs = new TabStrip(
            factory,
            ["Appearance", "Layout", "Sorting", "Behavior", "Updates"],
            (int)SettingsTab.Appearance,
            SelectTab);

        AddChild(_title);
        AddChild(_closeButton);
        AddChild(_tabs);
        AddChild(_pages);
        AddChild(_message);

        SelectTab((int)SettingsTab.Appearance);
        ApplySettings(new AppSettings());
        ApplyUpdateState(new UpdateState(UpdateStatus.Unavailable));
    }

    internal override SizeF PreferredSize => new(500.0f, 460.0f);
    internal override UiElement InitialFocus => _tabs.SelectedTab;
    internal string? Error
    {
        get;
        set
        {
            field = value;
            _message.Text = value ?? "Changes are saved automatically.";
            _message.Tone = value is null ? UiTextTone.Secondary : UiTextTone.Error;
        }
    }

    internal void ApplySettings(AppSettings settings)
    {
        _themeDropdown.SelectedValue = settings.Theme;
        _galleryEnabledToggle.Value = settings.GalleryEnabled;
        _galleryPlacementDropdown.SelectedValue = settings.GalleryPlacement;
        _galleryThumbnailSizeDropdown.SelectedValue = settings.GalleryThumbnailSize;
        _animationsToggle.Value = settings.AnimationsEnabled;
        _singleInstanceToggle.Value = settings.SingleInstance;

        SortDefinition sort = Array.Find(
            Sorts,
            candidate => candidate.First == settings.Sort || candidate.Second == settings.Sort);
        _sortField.SelectedValue = sort.Field;
        UpdateDirectionLabels(sort);
        _sortDirection.SelectedValue = settings.Sort == sort.First
            ? SortDirection.First
            : SortDirection.Second;
    }

    internal void ApplyUpdateState(UpdateState state)
    {
        _updateStatus.Tone = state.Status == UpdateStatus.Failed
            ? UiTextTone.Error
            : UiTextTone.Secondary;
        _updateButton.IsVisible = state.Status != UpdateStatus.Unavailable;
        _updateButton.IsEnabled = state.Status is UpdateStatus.Idle
            or UpdateStatus.Current
            or UpdateStatus.Available
            or UpdateStatus.Failed;

        (_updateStatus.Text, _updateButton.Label) = state.Status switch
        {
            UpdateStatus.Unavailable => (
                "Updates are only available when running the installed copy of Dameview.",
                "Check for updates"),
            UpdateStatus.Idle => ("Check whether a newer Dameview release is available.", "Check for updates"),
            UpdateStatus.Checking => ("Checking for updates…", "Checking…"),
            UpdateStatus.Current => ($"Dameview {state.Release?.Tag} is up to date.", "Check again"),
            UpdateStatus.Available => ($"Dameview {state.Release?.Tag} is available.", $"Update to {state.Release?.Tag}"),
            UpdateStatus.Downloading => ($"Downloading Dameview {state.Release?.Tag}…", "Downloading…"),
            UpdateStatus.Applying => ("Restarting Dameview to apply the update…", "Restarting…"),
            UpdateStatus.Failed => (state.Error ?? "The update failed.", "Try again"),
            _ => throw new InvalidOperationException("Unknown update status."),
        };
        InvalidateLayout();
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
        _themeDropdown.Dispose();
        _galleryEnabledToggle.Dispose();
        _galleryPlacementDropdown.Dispose();
        _galleryThumbnailSizeDropdown.Dispose();
        _animationsToggle.Dispose();
        _sortField.Dispose();
        _sortDirection.Dispose();
        _themeRow.Dispose();
        _galleryPlacementRow.Dispose();
        _galleryThumbnailSizeRow.Dispose();
        _sortFieldRow.Dispose();
        _sortDirectionRow.Dispose();
        _updateStatus.Dispose();
        _updateButton.Dispose();
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
        _layoutPage.IsVisible = index == (int)SettingsTab.Layout;
        _behaviorPage.IsVisible = index == (int)SettingsTab.Behavior;
        _sortingPage.IsVisible = index == (int)SettingsTab.Sorting;
        _updatesPage.IsVisible = index == (int)SettingsTab.Updates;
    }

    private void SetSortField(SortField field)
    {
        SortDefinition sort = Sorts[(int)field];
        UpdateDirectionLabels(sort);
        _commands.SetSort(_sortDirection.SelectedValue == SortDirection.First ? sort.First : sort.Second);
    }

    private void SetSortDirection(SortDirection direction)
    {
        SortDefinition sort = Sorts[(int)_sortField.SelectedValue];
        _commands.SetSort(direction == SortDirection.First ? sort.First : sort.Second);
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
