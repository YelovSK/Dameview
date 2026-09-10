using System.Drawing;
using Dameview.Commands;
using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Settings;
using Dameview.UI;
using Dameview.UI.Panels;
using Dameview.Updates;
using Vortice.DirectWrite;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class SettingsPanelTests
{
    [TestMethod]
    public void AppearanceTabCanDisableAnimations()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        var popupHost = new PopupHost();
        bool? animationsEnabled = null;
        var commands = new TestSettingsCommands { Animations = value => animationsEnabled = value };
        using var settings = new SettingsPanel(
            factory,
            popupHost,
            () => { },
            commands);
        var scene = new TestScene(settings, popupHost);
        var root = new UiRoot(scene, UiDpi.Default);
        root.Arrange(new SizeF(440.0f, 460.0f));
        root.SetFocus(settings.InitialFocus);

        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Space), settings, wrapFocus: true, directionalNavigation: true);

        Assert.AreEqual(false, animationsEnabled);
    }

    [TestMethod]
    public void AppearanceTabCanChangeGalleryThumbnailSize()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        var popupHost = new PopupHost();
        GalleryThumbnailSize? selectedSize = null;
        var commands = new TestSettingsCommands { GalleryThumbnailSize = value => selectedSize = value };
        using var settings = new SettingsPanel(
            factory,
            popupHost,
            () => { },
            commands);
        var scene = new TestScene(settings, popupHost);
        var root = new UiRoot(scene, UiDpi.Default);
        root.Arrange(new SizeF(440.0f, 460.0f));
        root.SetFocus(settings.InitialFocus);

        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Down), settings, wrapFocus: true, directionalNavigation: true);

        Assert.AreEqual(GalleryThumbnailSize.Large, selectedSize);
    }

    [TestMethod]
    public void SortingTabUsesDropdownsAndKeepsSortMeaning()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        var popupHost = new PopupHost();
        var selectedSorts = new List<FolderSort>();
        var commands = new TestSettingsCommands { Sort = selectedSorts.Add };
        using var settings = new SettingsPanel(
            factory,
            popupHost,
            () => { },
            commands);
        var scene = new TestScene(settings, popupHost);
        var root = new UiRoot(scene, UiDpi.Default);
        root.Arrange(new SizeF(440.0f, 220.0f));
        root.SetFocus(settings.InitialFocus);

        root.HandleKey(new UiKeyEvent(UiKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        UiElement pages = settings.Children[3];
        Assert.IsFalse(pages.Children[0].IsVisible);
        Assert.IsTrue(pages.Children[1].IsVisible);

        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Down), settings, wrapFocus: true, directionalNavigation: true);
        Assert.AreEqual(FolderSort.DateModifiedNewest, selectedSorts[^1]);

        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Down), settings, wrapFocus: true, directionalNavigation: true);
        Assert.AreEqual(FolderSort.DateModifiedOldest, selectedSorts[^1]);
    }

    [TestMethod]
    public void DropdownPopupIsPlacedOutsideTheSmallScrollViewport()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        var popupHost = new PopupHost();
        using var settings = new SettingsPanel(
            factory,
            popupHost,
            () => { },
            new TestSettingsCommands());
        var scene = new TestScene(settings, popupHost);
        var root = new UiRoot(scene, UiDpi.Default);
        var size = new SizeF(440.0f, 220.0f);
        root.Arrange(size);
        root.SetFocus(settings.InitialFocus);
        root.HandleKey(new UiKeyEvent(UiKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Enter), settings, wrapFocus: true, directionalNavigation: true);
        root.Arrange(size);
        for (int frame = 0; frame < 30; frame++)
        {
            root.Update(new UiUpdateContext(1.0 / 60.0));
            root.Arrange(size);
        }

        UiElement sortingScrollView = settings.Children[3].Children[1];
        RectangleF scrollBounds = sortingScrollView.GetBoundsRelativeTo(scene);
        RectangleF popupBounds = popupHost.Children[0].GetBoundsRelativeTo(scene);

        Assert.IsTrue(popupHost.IsOpen);
        Assert.IsGreaterThan(scrollBounds.Top, popupBounds.Bottom);
        Assert.IsLessThan(scrollBounds.Top, popupBounds.Top);
    }

    [TestMethod]
    public void AvailableUpdateCanBeActivatedFromUpdatesTab()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        var popupHost = new PopupHost();
        int activations = 0;
        var commands = new TestSettingsCommands { Activate = () => activations++ };
        using var settings = new SettingsPanel(
            factory,
            popupHost,
            () => { },
            commands);
        var scene = new TestScene(settings, popupHost);
        var root = new UiRoot(scene, UiDpi.Default);
        root.Arrange(new SizeF(440.0f, 460.0f));
        root.SetFocus(settings.InitialFocus);
        settings.ApplyUpdateState(new UpdateState(
            UpdateStatus.Available,
            new AppRelease("v2.0.0", new Version(2, 0, 0, 0))));

        root.HandleKey(new UiKeyEvent(UiKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new UiKeyEvent(UiKey.Enter), settings, wrapFocus: true, directionalNavigation: true);

        Assert.AreEqual(1, activations);
    }

    private sealed class TestSettingsCommands : ISettingsCommands
    {
        internal Action<Theme>? Theme { get; init; }
        internal Action<bool>? Animations { get; init; }
        internal Action<GalleryThumbnailSize>? GalleryThumbnailSize { get; init; }
        internal Action<FolderSort>? Sort { get; init; }
        internal Action? Activate { get; init; }

        public void SetTheme(Theme theme) => Theme?.Invoke(theme);
        public void SetAnimationsEnabled(bool enabled) => Animations?.Invoke(enabled);
        public void SetGalleryThumbnailSize(GalleryThumbnailSize size) => GalleryThumbnailSize?.Invoke(size);
        public void SetSort(FolderSort sort) => Sort?.Invoke(sort);
        public void ActivateUpdate() => Activate?.Invoke();
    }

    private sealed class TestScene : UiElement
    {
        private readonly SettingsPanel _settings;
        private readonly PopupHost _popupHost;

        internal TestScene(SettingsPanel settings, PopupHost popupHost)
        {
            _settings = settings;
            _popupHost = popupHost;
            AddChild(settings);
            AddChild(popupHost);
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            _settings.Measure(availableSize);
            _popupHost.Measure(availableSize);
            return availableSize;
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            var bounds = new RectangleF(PointF.Empty, finalSize);
            _settings.Arrange(bounds);
            _popupHost.Arrange(bounds);
        }
    }
}
