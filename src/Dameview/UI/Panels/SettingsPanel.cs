using System.Drawing;
using Dameview.Commands;
using Dameview.Navigation;
using Dameview.Settings;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Updates;

namespace Dameview.UI.Panels;

internal sealed class SettingsPanel : ModalContent
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
    private readonly SegmentedControl<GalleryPlacement> _galleryPlacement;
    private readonly SegmentedControl<GalleryThumbnailSize> _galleryThumbnailSize;
    private readonly Toggle _animationsToggle;
    private readonly Toggle _singleInstanceToggle;
    private readonly Toggle _autoBalancePanesToggle;
    private readonly Dropdown<SortField> _sortField;
    private readonly SegmentedControl<SortDirection> _sortDirection;
    private readonly SettingsRow _themeRow;
    private readonly SettingsRow _galleryPlacementRow;
    private readonly SettingsRow _galleryThumbnailSizeRow;
    private readonly SettingsRow _sortFieldRow;
    private readonly SettingsRow _sortDirectionRow;
    private readonly TabStrip<SettingsTab> _tabs;
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
    private readonly ISettingsActions _commands;

    internal SettingsPanel(
        PopupHost popupHost,
        Action close,
        ISettingsActions commands)
    {
        _popupHost = popupHost;
        _commands = commands;
        _title = new TextBlock(
            "Settings",
            UiTextStyle.Heading,
            UiTextTone.Primary,
            UiTextWrapping.NoWrap);
        _message = new TextBlock(
            "Changes are saved automatically.",
            UiTextStyle.Body,
            UiTextTone.Secondary,
            UiTextWrapping.Wrap);
        _closeButton = new Button(
            UiTypography.CloseIcon,
            close,
            fontFamily: UiTypography.IconFontFamily,
            fontSize: 16.0f)
        {
            ToolTip = new("Close"),
            MaxWidth = 72.0f,
        };

        _themeDropdown = new Dropdown<ThemeId>(
            popupHost,
            Themes.All
                .Select(theme => new Choice<ThemeId>(theme.DisplayName, theme.Id))
                .ToArray(),
            ThemeId.Dark,
            theme => Update(settings => settings with { Theme = theme }));
        _themeRow = new SettingsRow("Theme", _themeDropdown);
        _galleryEnabledToggle = new Toggle(
            "Show gallery",
            value: true,
            enabled => Update(settings => settings with { GalleryEnabled = enabled }));
        _galleryPlacement = new SegmentedControl<GalleryPlacement>(
            [
                new("Left", GalleryPlacement.Left),
                new("Right", GalleryPlacement.Right),
                new("Top", GalleryPlacement.Top),
                new("Bottom", GalleryPlacement.Bottom),
            ],
            GalleryPlacement.Right,
            placement => Update(settings => settings with { GalleryPlacement = placement }));
        _galleryPlacementRow = new SettingsRow("Position", _galleryPlacement);
        _galleryThumbnailSize = new SegmentedControl<GalleryThumbnailSize>(
            [
                new("Small", GalleryThumbnailSize.Small),
                new("Medium", GalleryThumbnailSize.Medium),
                new("Large", GalleryThumbnailSize.Large),
            ],
            GalleryThumbnailSize.Medium,
            size => Update(settings => settings with { GalleryThumbnailSize = size }));
        _galleryThumbnailSizeRow = new SettingsRow("Thumbnail size", _galleryThumbnailSize);
        _animationsToggle = new Toggle(
            "Animations",
            value: true,
            enabled => Update(settings => settings with { AnimationsEnabled = enabled }));
        _singleInstanceToggle = new Toggle(
            "Open files in existing window (restart required)",
            value: true,
            enabled => Update(settings => settings with { SingleInstance = enabled }));
        _autoBalancePanesToggle = new Toggle(
            "Balance panes automatically",
            value: false,
            enabled => Update(settings => settings with { AutoBalancePanes = enabled }))
        {
            ToolTip = new("Give all panes equal space whenever a pane is split or closed"),
        };

        _sortField = new Dropdown<SortField>(
            popupHost,
            [
                new("Name", SortField.Name),
                new("Modified date", SortField.DateModified),
                new("Created date", SortField.DateCreated),
                new("Size", SortField.Size),
            ],
            SortField.Name,
            SetSortField);
        _sortDirection = new SegmentedControl<SortDirection>(
            [
                new("A–Z", SortDirection.First),
                new("Z–A", SortDirection.Second),
            ],
            SortDirection.First,
            SetSortDirection);
        _sortFieldRow = new SettingsRow("Sort by", _sortField);
        _sortDirectionRow = new SettingsRow("Direction", _sortDirection);

        _updateStatus = new TextBlock(
            string.Empty,
            UiTextStyle.Body,
            UiTextTone.Secondary,
            UiTextWrapping.Wrap);
        _updateButton = new Button("Check for updates", _commands.ActivateUpdate);

        static ScrollView Page(params UiElement[] settings) =>
            new(new StackPanel(UiOrientation.Vertical, settings) { Spacing = UiDesign.LargeSpacing });

        _appearancePage = Page(_themeRow, _animationsToggle);
        _layoutPage = Page(
            new SettingsGroup("Gallery", _galleryEnabledToggle, _galleryPlacementRow, _galleryThumbnailSizeRow),
            new SettingsGroup("Panes", _autoBalancePanesToggle));
        _behaviorPage = Page(_singleInstanceToggle);
        _sortingPage = Page(_sortFieldRow, _sortDirectionRow);
        _updatesPage = Page(_updateStatus, _updateButton);
        _pages = new Overlay(_appearancePage, _layoutPage, _sortingPage, _behaviorPage, _updatesPage)
        {
            Margin = new UiThickness(0.0f, UiDesign.SmallSpacing, 0.0f, 0.0f),
        };
        _tabs = new TabStrip<SettingsTab>(
            [
                new("Appearance", SettingsTab.Appearance),
                new("Layout", SettingsTab.Layout),
                new("Sorting", SettingsTab.Sorting),
                new("Behavior", SettingsTab.Behavior),
                new("Updates", SettingsTab.Updates),
            ],
            SettingsTab.Appearance,
            SelectTab);

        var header = new StackPanel(UiOrientation.Horizontal, _title, _closeButton)
        {
            Spacing = UiDesign.Spacing,
            Fill = _title,
        };
        AddChild(new StackPanel(UiOrientation.Vertical, header, _tabs, _pages, _message)
        {
            Spacing = 12.0f,
            Fill = _pages,
            Margin = new UiThickness(24.0f, 20.0f, 24.0f, 12.0f),
        });

        SelectTab(SettingsTab.Appearance);
        ApplySettings(new AppSettings());
        ApplyUpdateState(new UpdateState(UpdateStatus.Unavailable));
    }

    internal override SizeF PreferredSize => new(560.0f, 500.0f);
    internal override UiElement InitialFocus => _tabs.SelectedSegment;

    internal void ApplySettings(AppSettings settings)
    {
        _themeDropdown.SelectedValue = settings.Theme;
        _galleryEnabledToggle.Value = settings.GalleryEnabled;
        _galleryPlacement.SelectedValue = settings.GalleryPlacement;
        _galleryThumbnailSize.SelectedValue = settings.GalleryThumbnailSize;
        _animationsToggle.Value = settings.AnimationsEnabled;
        _singleInstanceToggle.Value = settings.SingleInstance;
        _autoBalancePanesToggle.Value = settings.AutoBalancePanes;

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

    private void SelectTab(SettingsTab tab)
    {
        _popupHost.Close();
        _appearancePage.IsVisible = tab == SettingsTab.Appearance;
        _layoutPage.IsVisible = tab == SettingsTab.Layout;
        _behaviorPage.IsVisible = tab == SettingsTab.Behavior;
        _sortingPage.IsVisible = tab == SettingsTab.Sorting;
        _updatesPage.IsVisible = tab == SettingsTab.Updates;
    }

    private void SetSortField(SortField field)
    {
        SortDefinition sort = Sorts[(int)field];
        UpdateDirectionLabels(sort);
        SetSort(_sortDirection.SelectedValue == SortDirection.First ? sort.First : sort.Second);
    }

    private void SetSortDirection(SortDirection direction)
    {
        SortDefinition sort = Sorts[(int)_sortField.SelectedValue];
        SetSort(direction == SortDirection.First ? sort.First : sort.Second);
    }

    private void SetSort(FolderSort sort) => Update(settings => settings with { Sort = sort });

    private void Update(Func<AppSettings, AppSettings> change) => _commands.UpdateSettings(change);

    private void UpdateDirectionLabels(SortDefinition sort)
    {
        _sortDirection.SetChoiceLabel(SortDirection.First, sort.FirstLabel);
        _sortDirection.SetChoiceLabel(SortDirection.Second, sort.SecondLabel);
    }

    private readonly record struct SortDefinition(
        SortField Field,
        FolderSort First,
        FolderSort Second,
        string FirstLabel,
        string SecondLabel);
}
