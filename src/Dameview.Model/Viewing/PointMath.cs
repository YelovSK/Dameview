using System.Drawing;
using System.Numerics;

namespace Dameview.Viewing;

/// <summary>Arithmetic between positions (<see cref="PointF"/>) and the offsets between them (<see cref="Vector2"/>).</summary>
internal static class PointMath
{
    extension(PointF)
    {
        public static Vector2 operator -(PointF to, PointF from) => new(to.X - from.X, to.Y - from.Y);

        public static PointF operator +(PointF point, Vector2 offset) => new(point.X + offset.X, point.Y + offset.Y);

        public static PointF operator -(PointF point, Vector2 offset) => new(point.X - offset.X, point.Y - offset.Y);
    }
}
