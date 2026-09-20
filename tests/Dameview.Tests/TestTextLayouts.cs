using Dameview.UI.Foundation;
using Vortice.DirectWrite;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Tests;

/// <summary>One text layout cache for the whole test run, so a root can be built without owning one.</summary>
internal static class TestTextLayouts
{
    internal static readonly UiTextLayoutCache Shared =
        new(DWriteCreateFactory<IDWriteFactory>());
}
