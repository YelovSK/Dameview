using System.Drawing;
using Dameview.UI.Foundation;

namespace Dameview.UI.Animation;

internal sealed class AnimatedRectangle
{
    private readonly AnimatedFloat _x;
    private readonly AnimatedFloat _y;
    private readonly AnimatedFloat _width;
    private readonly AnimatedFloat _height;

    internal AnimatedRectangle(double response, float completionDistance = 0.05f)
    {
        _x = new AnimatedFloat(0.0f, response, completionDistance);
        _y = new AnimatedFloat(0.0f, response, completionDistance);
        _width = new AnimatedFloat(0.0f, response, completionDistance);
        _height = new AnimatedFloat(0.0f, response, completionDistance);
    }

    internal RectangleF Current => new(_x.Current, _y.Current, _width.Current, _height.Current);

    internal void SetTarget(RectangleF target)
    {
        _x.SetTarget(target.X);
        _y.SetTarget(target.Y);
        _width.SetTarget(target.Width);
        _height.SetTarget(target.Height);
    }

    internal void SetValue(RectangleF value)
    {
        _x.SetValue(value.X);
        _y.SetValue(value.Y);
        _width.SetValue(value.Width);
        _height.SetValue(value.Height);
    }

    internal bool Update(in UiUpdateContext context) =>
        _x.Update(context) | _y.Update(context) | _width.Update(context) | _height.Update(context);
}
