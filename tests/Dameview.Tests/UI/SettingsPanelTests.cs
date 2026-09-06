using System.Drawing;
using Dameview.Navigation;
using Dameview.Platform;
using Dameview.UI;
using Dameview.UI.Panels;
using Vortice.DirectWrite;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class SettingsPanelTests
{
    [TestMethod]
    public void SortingTabUsesDropdownsAndKeepsSortMeaning()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        var popupHost = new PopupHost();
        var selectedSorts = new List<FolderSort>();
        using var settings = new SettingsPanel(
            factory,
            popupHost,
            () => { },
            _ => { },
            selectedSorts.Add);
        var scene = new TestScene(settings, popupHost);
        var root = new UiRoot(scene, UiDpi.Default);
        root.Arrange(new SizeF(440.0f, 220.0f));
        root.SetFocus(settings.InitialFocus);

        root.HandleKey(new UiKeyEvent(UiKey.Right), settings, wrapFocus: true, directionalNavigation: true);
        UiElement pages = settings.Children[3];
        Assert.IsFalse(pages.Children[0].IsVisible);
        Assert.IsTrue(pages.Children[1].IsVisible);

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
            _ => { },
            _ => { });
        var scene = new TestScene(settings, popupHost);
        var root = new UiRoot(scene, UiDpi.Default);
        var size = new SizeF(440.0f, 220.0f);
        root.Arrange(size);
        root.SetFocus(settings.InitialFocus);
        root.HandleKey(new UiKeyEvent(UiKey.Right), settings, wrapFocus: true, directionalNavigation: true);
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
