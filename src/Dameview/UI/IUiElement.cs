using System.Drawing;
using Dameview.Platform;

namespace Dameview.UI;

internal interface IUiElement
{
    public bool Update(in UiUpdateContext context)
    {
        return false;
    }

    public void Draw(in UiDrawContext context, SizeF size);

    public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size);
}

internal readonly record struct UiUpdateContext(double ElapsedSeconds);

internal readonly record struct UiPointerResult(
    bool Consumed = false,
    bool NeedsRepaint = false,
    bool CapturePointer = false);
