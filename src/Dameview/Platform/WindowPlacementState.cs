namespace Dameview.Platform;

internal sealed record WindowPlacementState
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool Maximized { get; init; }

    internal bool IsUsable => Width >= 320 && Height >= 240;
}
