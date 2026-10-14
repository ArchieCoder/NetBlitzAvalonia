using Avalonia.Media;

namespace NetBlitz.Desktop;

/// <summary>Net Blitz's blue board and navy/yellow chrome, sampled from the original artwork.</summary>
public static class NetBlitzTheme
{
    public static readonly IBrush LightSquare = Brush.Parse("#9FC7EF");
    public static readonly IBrush DarkSquare = Brush.Parse("#007FF0");
    public static readonly IBrush DeepBlue = Brush.Parse("#07078D");
    public static readonly IBrush HoverBlue = Brush.Parse("#2020B0");
    public static readonly IBrush PressedBlue = Brush.Parse("#000066");
    public static readonly IBrush Yellow = Brush.Parse("#FFE600");
    public static readonly IBrush Window = Brush.Parse("#EFEFF6");
    public static readonly IBrush Surface = Brush.Parse("#FCFCFF");
    public static readonly IBrush Ink = Brush.Parse("#101060");
    public static readonly IBrush Muted = Brush.Parse("#505B80");
    public static readonly IBrush Frame = Brush.Parse("#C8CBE4");
    public static readonly IBrush Disabled = Brush.Parse("#E1E4F0");
    public static readonly IBrush DisabledText = Brush.Parse("#68728E");
    public static readonly IBrush PoolHover = Brush.Parse("#C6E4FF");
    public static readonly IBrush Selected = Brush.Parse("#50FFFFFF");
    public static readonly IBrush Check = Brush.Parse("#BFEB6464");
    public static readonly IBrush Target = Brush.Parse("#8507078D");
    public static readonly IBrush ClockWarning = Brush.Parse("#FFB6B6");
    public static readonly IBrush Error = Brush.Parse("#AF2028");
}
