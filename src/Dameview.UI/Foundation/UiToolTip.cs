using System.Drawing;

namespace Dameview.UI.Foundation;

/// <param name="Bounds">
/// What the tooltip describes, in its element's coordinates, for elements that draw several
/// items of their own. The whole element when omitted.
/// </param>
internal sealed record UiToolTip(string Text, RectangleF? Bounds = null);
