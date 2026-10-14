using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NetBlitz.Core;
using NetBlitz.Network;

namespace NetBlitz.Desktop.Controls;

public sealed class BoardPane : Border
{
    public BoardControl Board { get; } = new();
    private readonly TextBlock _title = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = NetBlitzTheme.DeepBlue };
    private readonly TextBlock[] _names = [new(), new()];
    private readonly TextBlock[] _clocks = [new(), new()];
    private readonly Border[] _strips = [new(), new()];
    private readonly StackPanel[] _pockets = [new(), new()];
    private bool _flipped;
    public int BoardIndex { get; }

    public BoardPane(int boardIndex)
    {
        BoardIndex = boardIndex;
        Background = NetBlitzTheme.Surface; CornerRadius = new(12); Padding = new(12);
        BorderBrush = NetBlitzTheme.Frame; BorderThickness = new(1);
        var grid = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto,Auto"), RowSpacing = 5 };
        _title.Margin = new(4, 0, 0, 6); grid.Children.Add(_title);
        for (int index = 0; index < 2; index++)
        {
            _names[index].FontSize = 13; _names[index].VerticalAlignment = VerticalAlignment.Center;
            _names[index].TextTrimming = TextTrimming.CharacterEllipsis;
            _names[index].Foreground = NetBlitzTheme.Ink;
            _clocks[index].FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace");
            _clocks[index].FontSize = 20; _clocks[index].FontWeight = FontWeight.SemiBold;
            _clocks[index].HorizontalAlignment = HorizontalAlignment.Right;
            _clocks[index].Foreground = NetBlitzTheme.Ink;
            var strip = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            strip.Children.Add(_names[index]); strip.Children.Add(_clocks[index]); Grid.SetColumn(_clocks[index], 1);
            _strips[index].Child = strip; _strips[index].Padding = new(10, 4); _strips[index].CornerRadius = new(8);
            _strips[index].Background = NetBlitzTheme.Window;
            _strips[index].BorderBrush = NetBlitzTheme.Frame; _strips[index].BorderThickness = new(1);
            grid.Children.Add(_strips[index]); Grid.SetRow(_strips[index], index == 0 ? 1 : 4);
            _pockets[index].Orientation = Orientation.Horizontal; _pockets[index].Spacing = 4;
            _pockets[index].HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(_pockets[index]); Grid.SetRow(_pockets[index], index == 0 ? 2 : 5);
        }
        grid.Children.Add(Board); Grid.SetRow(Board, 3); Child = grid;
    }

    public void Update(MatchSnapshot match, RoomSnapshot? room, int playerId, Preferences preferences, bool flipped, bool reset)
    {
        _flipped = flipped; Board.Flipped = flipped;
        var snapshot = match.Boards[BoardIndex];
        Board.Bughouse = match.Mode == GameMode.Bughouse; Board.CaptureKing = match.Settings.CaptureKing;
        Board.HighlightLast = preferences.HighlightLast; Board.ShowCheck = preferences.ShowCheck;
        Board.CanPlay = match.Phase == GamePhase.Playing && (room is null || room.OwnerOf(Chess.Seat(BoardIndex, snapshot.Turn)) == playerId);
        Board.Update(snapshot, reset);
        string state = Board.Board.IsCheck(snapshot.Turn) ? " · CHECK" : "";
        _title.Text = $"BOARD {(char)('A' + BoardIndex)}   /   {snapshot.Turn.ToString().ToUpperInvariant()} TO MOVE{state}";
        for (int index = 0; index < 2; index++)
        {
            Side side = DisplaySide(index); int seat = Chess.Seat(BoardIndex, side);
            int owner = room?.OwnerOf(seat) ?? -1;
            var player = room?.Players.FirstOrDefault(p => p.Id == owner);
            string name = room is null ? side.ToString() : player?.Name ?? "Open seat";
            _names[index].Text = $"{(side == Side.White ? "○" : "●")}  {name}{(owner == playerId && room is not null ? " · you" : "")}";
            ToolTip.SetTip(_names[index], $"{side} · Team {Chess.Team(seat) + 1}");
            _pockets[index].Children.Clear();
            _pockets[index].IsVisible = match.Mode == GameMode.Bughouse;
            if (match.Mode != GameMode.Bughouse) continue;
            for (var type = PieceType.Pawn; type < PieceType.King; type++)
            {
                var pieceType = type; int count = Board.Board.Pocket(side, type);
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
                content.Children.Add(new PieceIcon { Piece = new(type, side), Width = 26, Height = 26 });
                content.Children.Add(new TextBlock { Text = count.ToString(), VerticalAlignment = VerticalAlignment.Center });
                var button = new Button { Content = content, Padding = new(5, 2),
                    IsEnabled = Board.CanPlay && snapshot.Turn == side && count > 0 };
                button.Classes.Add("pool");
                ToolTip.SetTip(button, $"Drop {side} {type} ({count} available)");
                Avalonia.Automation.AutomationProperties.SetName(button, $"Drop {side} {type}, {count} available");
                button.Click += (_, _) => Board.SelectPocket(pieceType);
                _pockets[index].Children.Add(button);
            }
        }
        UpdateClocks(match, 0);
    }

    private Side DisplaySide(int index) => (_flipped ? index == 0 : index == 1) ? Side.White : Side.Black;
    public void UpdateClocks(MatchSnapshot match, double elapsedMs)
    {
        for (int index = 0; index < 2; index++)
        {
            var side = DisplaySide(index); bool active = match.Phase == GamePhase.Playing && match.Boards[BoardIndex].Turn == side;
            double time = match.RemainingMs[Chess.Seat(BoardIndex, side)] - (active ? elapsedMs : 0);
            _clocks[index].Text = match.Settings.UnlimitedTime ? "∞" : FormatTime(time);
            bool lowTime = !match.Settings.UnlimitedTime && time <= 10000;
            _clocks[index].Foreground = lowTime ? NetBlitzTheme.Error : NetBlitzTheme.Ink;
            _clocks[index].Opacity = active || lowTime ? 1 : 0.65;
            _names[index].Foreground = active ? NetBlitzTheme.Ink : NetBlitzTheme.Muted;
            _names[index].FontWeight = active ? FontWeight.SemiBold : FontWeight.Regular;
            _strips[index].Background = active ? NetBlitzTheme.Surface : NetBlitzTheme.Window;
            _strips[index].BorderBrush = active ? NetBlitzTheme.DeepBlue : NetBlitzTheme.Frame;
            _strips[index].BorderThickness = new(active ? 1.5 : 1);
        }
    }
    public static string FormatTime(double ms)
    {
        var time = TimeSpan.FromMilliseconds(Math.Abs(ms));
        string sign = ms < 0 ? "−" : "";
        return time.TotalHours >= 1 ? $"{sign}{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{sign}{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }
}
