using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NetBlitz.Core;

namespace NetBlitz.Desktop.Controls;

public sealed class BoardControl : Control
{
    private ChessBoard _board = new();
    private int? _selected;
    private PieceType? _drop;
    private HashSet<int> _targets = [];
    private int? _pressed;
    private Point _pressPoint;
    private Point _pointer;
    private bool _dragging;
    private int _keyboardSquare = Chess.Square("e2");
    private int _ply = -1;
    public bool Flipped { get; set; }
    public bool CanPlay { get; set; }
    public bool Bughouse { get; set; }
    public bool CaptureKing { get; set; }
    public bool HighlightLast { get; set; } = true;
    public bool ShowCheck { get; set; } = true;
    public ChessBoard Board => _board;
    public event Action<Move>? MoveRequested;
    private static readonly IBrush Light = NetBlitzTheme.LightSquare, Dark = NetBlitzTheme.DarkSquare,
        Accent = NetBlitzTheme.Yellow, Selected = NetBlitzTheme.Selected,
        Check = NetBlitzTheme.Check, Dot = NetBlitzTheme.Target;

    public BoardControl()
    {
        Focusable = true;
        MinWidth = 240; MinHeight = 240;
        AutomationProperties.SetName(this, "Chess board. Arrow keys select a square; Enter selects or moves; Escape cancels.");
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    public void Update(BoardSnapshot snapshot, bool reset = false)
    {
        if (_ply != snapshot.Ply || reset || !CanPlay) ClearSelection();
        _ply = snapshot.Ply; _board = ChessBoard.FromSnapshot(snapshot);
        if (_selected is not null || _drop is not null) UpdateTargets();
        InvalidateVisual();
    }
    public void ClearSelection() { _selected = null; _drop = null; _targets.Clear(); _dragging = false; InvalidateVisual(); }
    public void SelectPocket(PieceType piece)
    {
        if (!CanPlay || _board.Pocket(_board.Turn, piece) == 0) return;
        _selected = null; _drop = piece; UpdateTargets(); Focus(); InvalidateVisual();
    }
    private void UpdateTargets()
    {
        _targets.Clear();
        for (int to = 0; to < 64; to++)
            if (_board.IsLegal(new(_selected ?? -1, to, Drop: _drop), Bughouse, CaptureKing)) _targets.Add(to);
    }
    private (double X, double Y, double Size) Layout()
    {
        double size = Math.Max(0, Math.Min(Bounds.Width - 36, Bounds.Height - 36));
        return ((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size);
    }
    public Point SquareCenter(int square)
    {
        var (x, y, size) = Layout(); double cell = size / 8;
        return new(x + (Flipped ? 7 - square % 8 : square % 8) * cell + cell / 2,
            y + (Flipped ? square / 8 : 7 - square / 8) * cell + cell / 2);
    }
    private int? Hit(Point point)
    {
        var (x, y, size) = Layout();
        if (size <= 0 || point.X < x || point.Y < y || point.X >= x + size || point.Y >= y + size) return null;
        int file = (int)((point.X - x) / (size / 8)), rank = (int)((point.Y - y) / (size / 8));
        return Flipped ? rank * 8 + 7 - file : (7 - rank) * 8 + file;
    }

    public override void Render(DrawingContext context)
    {
        var (x, y, size) = Layout(); if (size <= 0) return;
        double cell = size / 8;
        int king = ShowCheck && _board.IsCheck(_board.Turn) ? _board.KingSquare(_board.Turn) : -1;
        for (int square = 0; square < 64; square++)
        {
            var center = SquareCenter(square); var rect = new Rect(center.X - cell / 2, center.Y - cell / 2, cell, cell);
            context.FillRectangle((square % 8 + square / 8) % 2 == 0 ? Dark : Light, rect);
            if (HighlightLast && _board.LastMove is { } last && (last.From == square || last.To == square))
                context.DrawRectangle(null, new Pen(Accent, Math.Max(2, cell * .04)), rect.Deflate(2));
            if (square == king) context.FillRectangle(Check, rect);
            if (square == _selected)
            {
                context.FillRectangle(Selected, rect);
                context.DrawRectangle(null, new Pen(Accent, 3), rect.Deflate(2));
            }
            if (_targets.Contains(square))
            {
                if (_board[square] is null) context.DrawEllipse(Dot, null, center, cell * .13, cell * .13);
                else context.DrawEllipse(null, new Pen(Dot, cell * .08), center, cell * .44, cell * .44);
            }
            if (_board[square] is { } piece && !(_dragging && _selected == square))
                PieceArtwork.Draw(context, piece, rect);
            if (IsKeyboardFocusWithin && _keyboardSquare == square)
                context.DrawRectangle(null, new Pen(Accent, 2), rect.Deflate(2));
        }
        for (int i = 0; i < 8; i++)
        {
            DrawText(context, ((char)('a' + (Flipped ? 7 - i : i))).ToString(), new(x + i * cell + cell / 2 - 4, y + size + 3));
            DrawText(context, (Flipped ? i + 1 : 8 - i).ToString(), new(x - 16, y + i * cell + cell / 2 - 7));
        }
        if (_dragging && _selected is { } from && _board[from] is { } moving)
            PieceArtwork.Draw(context, moving, new Rect(_pointer.X - cell / 2, _pointer.Y - cell / 2, cell, cell));
    }
    private static void DrawText(DrawingContext context, string text, Point point) => context.DrawText(
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, NetBlitzTheme.Muted), point);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) { ClearSelection(); return; }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !CanPlay) return;
        Focus(); _pressed = Hit(e.GetPosition(this)); _pressPoint = _pointer = e.GetPosition(this);
        if (_pressed is not { } square) return;
        _keyboardSquare = square;
        if (_board[square]?.Side == _board.Turn)
        {
            _selected = square; _drop = null; UpdateTargets();
        }
        e.Pointer.Capture(this); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); _pointer = e.GetPosition(this);
        if (_pressed == _selected && _pressed is not null && Math.Abs(_pointer.X - _pressPoint.X) + Math.Abs(_pointer.Y - _pressPoint.Y) > 5) _dragging = true;
        if (_dragging) InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var target = Hit(e.GetPosition(this));
        bool validPress = _pressed is not null;
        _pressed = null; _dragging = false; e.Pointer.Capture(null);
        if (validPress && CanPlay && target is { } square && _targets.Contains(square)) RequestMove(square);
        InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { base.OnPointerCaptureLost(e); _pressed = null; _dragging = false; InvalidateVisual(); }
    private void RequestMove(int to)
    {
        var move = new Move(_selected ?? -1, to, Drop: _drop); ClearSelection(); MoveRequested?.Invoke(move);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int direction = Flipped ? -1 : 1;
        int x = _keyboardSquare % 8, y = _keyboardSquare / 8;
        switch (e.Key)
        {
            case Key.Left: x -= direction; break;
            case Key.Right: x += direction; break;
            case Key.Up: y += direction; break;
            case Key.Down: y -= direction; break;
            case Key.Escape: ClearSelection(); break;
            case Key.Enter:
            case Key.Space:
                if (CanPlay && _targets.Contains(_keyboardSquare)) RequestMove(_keyboardSquare);
                else if (CanPlay && _board[_keyboardSquare]?.Side == _board.Turn)
                { _selected = _keyboardSquare; _drop = null; UpdateTargets(); }
                break;
            default: return;
        }
        _keyboardSquare = Math.Clamp(y, 0, 7) * 8 + Math.Clamp(x, 0, 7);
        AutomationProperties.SetName(this, $"{Chess.SquareName(_keyboardSquare)}: {_board[_keyboardSquare]?.ToString() ?? "empty"}");
        InvalidateVisual(); e.Handled = true;
    }
}
