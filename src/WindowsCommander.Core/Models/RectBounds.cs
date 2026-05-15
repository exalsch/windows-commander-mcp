namespace WindowsCommander.Core.Models;

public sealed record RectBounds(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}
