using System.Drawing;
using Dameview.Commands;
using Dameview.Navigation;
using Dameview.Settings;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Panels;
using Dameview.Updates;
using Dameview.Win32.Input;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class SettingsPanelTests
{
    [TestMethod]
    public void AppearanceTabCanDisableAnimations()
    {
        var popupHost = new PopupHost();
        var commands = new TestSettingsCommands();
        var settings = new SettingsPanel(
            popupHost,
            () => { },
            commands);
        var scene = new TestScene(settings, popupHost);
        var root = new UiRoot(scene, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(440.0f, 460.0f));
        root.SetFocus(settings.InitialFocus);

        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Space), settings, wrapFocus: true, directionalNavigation: true);

        Assert.IsFalse(commands.Settings.AnimationsEnabled);
    }

    [TestMethod]
    public void LayoutTabCanConfigureTheGallery()
    {
        var popupHost = new PopupHost();
        var commands = new TestSettingsCommands();
        var settings = new SettingsPanel(
            popupHost,
            () => { },
            commands);
        var scene = new TestScene(settings, popupHost);
        var root = new UiRoot(scene, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(440.0f, 460.0f));
        root.SetFocus(settings.InitialFocus);

        root.HandleKey(new WindowKeyEvent(WindowKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Space), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Down), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Down), settings, wrapFocus: true, directionalNavigation: true);

        Assert.IsFalse(commands.Settings.GalleryEnabled);
        Assert.AreEqual(GalleryPlacement.Left, commands.Settings.GalleryPlacement);
        Assert.AreEqual(GalleryThumbnailSize.Large, commands.Settings.GalleryThumbnailSize);
    }

    [TestMethod]
    public void SortingTabUsesDropdownsAndKeepsSortMeaning()
    {
        var popupHost = new PopupHost();
        var commands = new TestSettingsCommands();
        var settings = new SettingsPanel(
            popupHost,
            () => { },
            commands);
        var scene = new TestScene(settings, popupHost);
        var root = new UiRoot(scene, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(440.0f, 220.0f));
        root.SetFocus(settings.InitialFocus);

        root.HandleKey(new WindowKeyEvent(WindowKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Down), settings, wrapFocus: true, directionalNavigation: true);
        Assert.AreEqual(FolderSort.DateModifiedNewest, commands.Settings.Sort);

        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Down), settings, wrapFocus: true, directionalNavigation: true);
        Assert.AreEqual(FolderSort.DateModifiedOldest, commands.Settings.Sort);
    }

    [TestMethod]
    public void DropdownPopupIsPlacedOutsideTheSmallScrollViewport()
    {
        var popupHost = new PopupHost();
        var settings = new SettingsPanel(
            popupHost,
            () => { },
            new TestSettingsCommands());
        var scene = new TestScene(settings, popupHost);
        var root = new UiRoot(scene, UiDpi.Default, TestTextLayouts.Shared);
        var size = new SizeF(440.0f, 220.0f);
        root.Arrange(size);
        root.SetFocus(settings.InitialFocus);
        root.HandleKey(new WindowKeyEvent(WindowKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Enter), settings, wrapFocus: true, directionalNavigation: true);
        root.Arrange(size);
        for (int frame = 0; frame < 30; frame++)
        {
            root.Update(new UiUpdateContext(1.0 / 60.0));
            root.Arrange(size);
        }

        UiElement sortingScrollView = settings.Children[3].Children[2];
        RectangleF scrollBounds = sortingScrollView.GetBoundsRelativeTo(scene);
        RectangleF popupBounds = popupHost.Children[0].GetBoundsRelativeTo(scene);

        Assert.IsTrue(popupHost.IsOpen);
        Assert.IsGreaterThan(scrollBounds.Top, popupBounds.Bottom);
        Assert.IsLessThan(scrollBounds.Top, popupBounds.Top);
    }

    [TestMethod]
    public void AvailableUpdateCanBeActivatedFromUpdatesTab()
    {
        var popupHost = new PopupHost();
        int activations = 0;
        var commands = new TestSettingsCommands { Activate = () => activations++ };
        var settings = new SettingsPanel(
            popupHost,
            () => { },
            commands);
        var scene = new TestScene(settings, popupHost);
        var root = new UiRoot(scene, UiDpi.Default, TestTextLayouts.Shared);
        root.Arrange(new SizeF(440.0f, 460.0f));
        root.SetFocus(settings.InitialFocus);
        settings.ApplyUpdateState(new UpdateState(
            UpdateStatus.Available,
            new AppRelease("v2.0.0", new Version(2, 0, 0, 0))));

        root.HandleKey(new WindowKeyEvent(WindowKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Tab), settings, wrapFocus: true, directionalNavigation: true);
        root.HandleKey(new WindowKeyEvent(WindowKey.Enter), settings, wrapFocus: true, directionalNavigation: true);

        Assert.AreEqual(1, activations);
    }

    private sealed class TestSettingsCommands : ISettingsCommands
    {
        internal Action? Activate { get; init; }

        /// <summary>The settings as the panel has left them.</summary>
        internal AppSettings Settings { get; private set; } = new();

        public void UpdateSettings(Func<AppSettings, AppSettings> change) => Settings = change(Settings);

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
