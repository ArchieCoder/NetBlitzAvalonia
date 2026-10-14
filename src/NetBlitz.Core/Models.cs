namespace NetBlitz.Core;

public enum Side { White, Black }
public enum PieceType { Pawn, Rook, Knight, Bishop, Queen, King }
public enum GameMode { Chess, Bughouse }
public enum GamePhase { Lobby, Playing, Finished }

public static class Chess
{
    public static Side Opposite(this Side side) => side == Side.White ? Side.Black : Side.White;
    public static int Seat(int board, Side side) => board * 2 + (int)side;
    public static int Team(int seat) => seat is 0 or 3 ? 0 : 1;
    public static int Partner(int seat) => 3 - seat;
    public static string SquareName(int square) => $"{(char)('a' + square % 8)}{square / 8 + 1}";
    public static int Square(string name)
    {
        if (name.Length != 2 || name[0] is < 'a' or > 'h' || name[1] is < '1' or > '8')
            throw new ArgumentException("Use a square such as e4.", nameof(name));
        return (name[1] - '1') * 8 + name[0] - 'a';
    }
    public static string Letter(PieceType type) => type switch
    {
        PieceType.Pawn => "P", PieceType.Rook => "R", PieceType.Knight => "N",
        PieceType.Bishop => "B", PieceType.Queen => "Q", _ => "K"
    };
}

public sealed record Piece(PieceType Type, Side Side, bool Promoted = false);
public sealed record Move(int From, int To, PieceType Promotion = PieceType.Queen, PieceType? Drop = null)
{
    public static Move Parse(string from, string to, PieceType promotion = PieceType.Queen) =>
        new(Chess.Square(from), Chess.Square(to), promotion);
    public static Move Place(PieceType piece, string to) => new(-1, Chess.Square(to), Drop: piece);
}

public sealed record GameSettings
{
    public int InitialSeconds { get; init; } = 300;
    public int IncrementSeconds { get; init; }
    public bool UnlimitedTime { get; init; }
    public bool AutomaticFlag { get; init; }
    public bool CaptureKing { get; init; }
    public bool WaitForPartnerPiece { get; init; } = true;

    public bool IsValid => InitialSeconds is >= 1 and <= 359999 && IncrementSeconds is >= 0 and <= 59;
}

public sealed record BoardSnapshot(Piece?[] Cells, Side Turn, int CastlingRights, int? EnPassant,
    int[] Pockets, Move? LastMove, int Ply);
public sealed record MoveEntry(int Board, int Number, Side Side, string Notation);
public sealed record MatchSnapshot(GameMode Mode, GameSettings Settings, GamePhase Phase,
    BoardSnapshot[] Boards, double[] RemainingMs, string Result, int? WinnerTeam,
    int? DrawOfferTeam, int[] PendingDrawSeats, MoveEntry[] Moves, int[] Wins, int Draws, int GameNumber);
