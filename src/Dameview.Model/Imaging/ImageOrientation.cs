using System.Drawing;
using System.Numerics;

namespace Dameview.Imaging;

/// <summary>
/// How stored pixels are turned to be shown: mirrored left to right first if
/// <see cref="Mirrored"/>, then turned clockwise by <see cref="QuarterTurns"/> quarter turns.
/// The default leaves the image as stored.
/// </summary>
internal readonly record struct ImageOrientation
{
    private ImageOrientation(int quarterTurns, bool mirrored)
    {
        QuarterTurns = ((quarterTurns % 4) + 4) % 4;
        Mirrored = mirrored;
    }

    internal int QuarterTurns { get; }
    internal bool Mirrored { get; }
    internal bool SwapsDimensions => QuarterTurns % 2 == 1;

    /// <summary>Reads the EXIF orientation tag. Unknown values leave the image as stored.</summary>
    internal static ImageOrientation FromExif(int value) => value switch
    {
        2 => new(0, mirrored: true),
        3 => new(2, mirrored: false),
        4 => new(2, mirrored: true),
        5 => new(3, mirrored: true),
        6 => new(1, mirrored: false),
        7 => new(1, mirrored: true),
        8 => new(3, mirrored: false),
        _ => default,
    };

    internal ImageOrientation RotateClockwise() => new(QuarterTurns + 1, Mirrored);

    internal ImageOrientation RotateCounterclockwise() => new(QuarterTurns - 1, Mirrored);

    // Mirroring after a turn is the same as mirroring first and then turning the other way.
    internal ImageOrientation FlipHorizontal() => new(-QuarterTurns, !Mirrored);

    internal ImageOrientation FlipVertical() => new(2 - QuarterTurns, !Mirrored);

    internal Size Apply(Size storedSize) =>
        SwapsDimensions ? new Size(storedSize.Height, storedSize.Width) : storedSize;

    internal SizeF Apply(SizeF storedSize) =>
        SwapsDimensions ? new SizeF(storedSize.Height, storedSize.Width) : storedSize;

    /// <summary>Maps stored coordinates onto oriented ones, for an image of <paramref name="storedSize"/>.</summary>
    internal Matrix3x2 GetTransform(SizeF storedSize)
    {
        float width = storedSize.Width;
        float height = storedSize.Height;
        Matrix3x2 mirror = Mirrored ? new Matrix3x2(-1, 0, 0, 1, width, 0) : Matrix3x2.Identity;
        Matrix3x2 turn = QuarterTurns switch
        {
            1 => new Matrix3x2(0, 1, -1, 0, height, 0),
            2 => new Matrix3x2(-1, 0, 0, -1, width, height),
            3 => new Matrix3x2(0, -1, 1, 0, 0, width),
            _ => Matrix3x2.Identity,
        };
        return mirror * turn;
    }
}
