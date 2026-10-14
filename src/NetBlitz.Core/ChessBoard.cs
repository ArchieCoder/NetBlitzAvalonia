using System.Text;

namespace NetBlitz.Core;

/// <summary>UI-independent port of CChessBoard. Squares use a1=0 through h8=63.</summary>
public sealed class ChessBoard
{
    private Piece?[] _cells = new Piece?[64];
    private int[] _pockets = new int[10];
    public Side Turn { get; private set; }
    public int CastlingRights { get; private set; } = 15; // WK, WQ, BK, BQ
    public int? EnPassant { get; private set; }
    public Move? LastMove { get; private set; }
    public int Ply { get; private set; }
    public Piece? this[int square] => square is >= 0 and < 64 ? _cells[square] : null;
    public int Pocket(Side side, PieceType type) => type < PieceType.King && type >= 0 ? _pockets[(int)side * 5 + (int)type] : 0;

    public ChessBoard()
    {
        PieceType[] back = [PieceType.Rook, PieceType.Knight, PieceType.Bishop, PieceType.Queen,
            PieceType.King, PieceType.Bishop, PieceType.Knight, PieceType.Rook];
        for (var file = 0; file < 8; file++)
        {
            _cells[file] = new(back[file], Side.White);
            _cells[8 + file] = new(PieceType.Pawn, Side.White);
            _cells[48 + file] = new(PieceType.Pawn, Side.Black);
            _cells[56 + file] = new(back[file], Side.Black);
        }
    }

    public BoardSnapshot Snapshot() => new((Piece?[])_cells.Clone(), Turn, CastlingRights,
        EnPassant, (int[])_pockets.Clone(), LastMove, Ply);

    public static ChessBoard FromSnapshot(BoardSnapshot snapshot)
    {
        if (snapshot.Cells.Length != 64 || snapshot.Pockets.Length != 10 || !Enum.IsDefined(snapshot.Turn)
            || snapshot.Pockets.Any(n => n < 0) || snapshot.CastlingRights is < 0 or > 15
            || snapshot.EnPassant is < 0 or > 63 || snapshot.Cells.Any(p => p is not null &&
                (!Enum.IsDefined(p.Type) || !Enum.IsDefined(p.Side))))
            throw new ArgumentException("Invalid board snapshot.", nameof(snapshot));
        return new ChessBoard
        {
            _cells = (Piece?[])snapshot.Cells.Clone(), _pockets = (int[])snapshot.Pockets.Clone(),
            Turn = snapshot.Turn, CastlingRights = snapshot.CastlingRights, EnPassant = snapshot.EnPassant,
            LastMove = snapshot.LastMove, Ply = snapshot.Ply
        };
    }

    private ChessBoard Clone() => FromSnapshot(Snapshot());

    public void AddToPocket(Side side, Piece piece)
    {
        if (piece.Type != PieceType.King)
            _pockets[(int)side * 5 + (int)(piece.Promoted ? PieceType.Pawn : piece.Type)]++;
    }

    public int KingSquare(Side side) => Array.FindIndex(_cells, p => p is { Type: PieceType.King } && p.Side == side);
    public bool IsCheck(Side side) => KingSquare(side) is var king && king >= 0 && IsAttacked(king, side.Opposite());

    public bool IsAttacked(int square, Side bySide)
    {
        for (var from = 0; from < 64; from++)
        {
            if (_cells[from] is not { } piece || piece.Side != bySide || from == square) continue;
            int dx = square % 8 - from % 8, dy = square / 8 - from / 8;
            bool attacks = piece.Type switch
            {
                PieceType.Pawn => Math.Abs(dx) == 1 && dy == (bySide == Side.White ? 1 : -1),
                PieceType.Knight => Math.Abs(dx) * Math.Abs(dy) == 2,
                PieceType.King => Math.Max(Math.Abs(dx), Math.Abs(dy)) == 1,
                PieceType.Bishop => Math.Abs(dx) == Math.Abs(dy) && ClearPath(from, square),
                PieceType.Rook => (dx == 0 || dy == 0) && ClearPath(from, square),
                PieceType.Queen => (dx == 0 || dy == 0 || Math.Abs(dx) == Math.Abs(dy)) && ClearPath(from, square),
                _ => false
            };
            if (attacks) return true;
        }
        return false;
    }

    private bool ClearPath(int from, int to)
    {
        int dx = Math.Sign(to % 8 - from % 8), dy = Math.Sign(to / 8 - from / 8);
        int x = from % 8 + dx, y = from / 8 + dy;
        while (y * 8 + x != to)
        {
            if (_cells[y * 8 + x] is not null) return false;
            x += dx; y += dy;
        }
        return true;
    }

    public bool IsLegal(Move move, bool bughouse = false, bool captureKing = false)
    {
        if (move.To is < 0 or > 63 || !Enum.IsDefined(move.Promotion)) return false;
        if (move.Drop is { } drop)
        {
            if (!bughouse || drop is < PieceType.Pawn or >= PieceType.King || move.From != -1
                || Pocket(Turn, drop) == 0 || _cells[move.To] is not null
                || (drop == PieceType.Pawn && move.To / 8 is 0 or 7)) return false;
        }
        else
        {
            if (move.From is < 0 or > 63 || move.From == move.To || _cells[move.From] is not { } piece
                || piece.Side != Turn || _cells[move.To]?.Side == Turn
                || (!captureKing && _cells[move.To]?.Type == PieceType.King)) return false;
            int dx = move.To % 8 - move.From % 8, dy = move.To / 8 - move.From / 8;
            bool valid = piece.Type switch
            {
                PieceType.Pawn => ValidPawn(move, dx, dy),
                PieceType.Knight => Math.Abs(dx) * Math.Abs(dy) == 2,
                PieceType.Bishop => Math.Abs(dx) == Math.Abs(dy) && ClearPath(move.From, move.To),
                PieceType.Rook => (dx == 0 || dy == 0) && ClearPath(move.From, move.To),
                PieceType.Queen => (dx == 0 || dy == 0 || Math.Abs(dx) == Math.Abs(dy)) && ClearPath(move.From, move.To),
                PieceType.King => Math.Max(Math.Abs(dx), Math.Abs(dy)) == 1 || ValidCastle(move),
                _ => false
            };
            if (!valid) return false;
        }
        var candidate = Clone();
        candidate.ApplyUnchecked(move);
        return captureKing || (candidate.KingSquare(Turn) >= 0 && !candidate.IsCheck(Turn));
    }

    private bool ValidPawn(Move move, int dx, int dy)
    {
        var direction = Turn == Side.White ? 1 : -1;
        if (move.To / 8 is 0 or 7 && move.Promotion is not (PieceType.Queen or PieceType.Rook or PieceType.Bishop or PieceType.Knight))
            return false;
        if (dx == 0)
            return _cells[move.To] is null && (dy == direction ||
                (dy == 2 * direction && move.From / 8 == (Turn == Side.White ? 1 : 6)
                 && _cells[move.From + 8 * direction] is null));
        if (Math.Abs(dx) != 1 || dy != direction) return false;
        return _cells[move.To] is not null || (EnPassant == move.To &&
            _cells[move.To - direction * 8] is { Type: PieceType.Pawn } victim && victim.Side != Turn);
    }

    private bool ValidCastle(Move move)
    {
        int home = Turn == Side.White ? 4 : 60;
        if (move.From != home || (move.To != home - 2 && move.To != home + 2)) return false;
        bool kingSide = move.To > home;
        int right = (Turn == Side.White ? 1 : 4) * (kingSide ? 1 : 2);
        int rook = home + (kingSide ? 3 : -4);
        if ((CastlingRights & right) == 0 || _cells[rook] is not { Type: PieceType.Rook, Promoted: false } r
            || r.Side != Turn || !ClearPath(home, rook) || IsCheck(Turn)) return false;
        // Test on the post-step board: moving the king can uncover a sliding attack.
        var candidate = Clone();
        candidate._cells[home] = null;
        int step = home + (kingSide ? 1 : -1);
        candidate._cells[step] = new(PieceType.King, Turn);
        if (candidate.IsAttacked(step, Turn.Opposite())) return false;
        candidate._cells[step] = null;
        candidate._cells[move.To] = new(PieceType.King, Turn);
        return !candidate.IsAttacked(move.To, Turn.Opposite());
    }

    public IEnumerable<Move> LegalMoves(bool bughouse = false, bool captureKing = false)
    {
        for (var from = 0; from < 64; from++)
        {
            if (_cells[from]?.Side != Turn) continue;
            for (var to = 0; to < 64; to++)
            {
                var move = new Move(from, to);
                if (!IsLegal(move, bughouse, captureKing)) continue;
                yield return move;
                if (_cells[from]?.Type == PieceType.Pawn && to / 8 is 0 or 7)
                    foreach (var type in new[] { PieceType.Rook, PieceType.Bishop, PieceType.Knight })
                        yield return move with { Promotion = type };
            }
        }
        if (!bughouse) yield break;
        for (var type = PieceType.Pawn; type < PieceType.King; type++)
        {
            if (Pocket(Turn, type) == 0) continue;
            for (var to = 0; to < 64; to++)
            {
                var move = new Move(-1, to, Drop: type);
                if (IsLegal(move, true, captureKing)) yield return move;
            }
        }
    }

    public bool HasSafeEscape(bool bughouse, bool waitForPartner)
    {
        if (LegalMoves(bughouse).Any()) return true;
        if (!bughouse || !waitForPartner) return false;
        // CChessBoard::VerifyMate temporarily adds a rook to allow waiting for a future block.
        var candidate = Clone();
        candidate.AddToPocket(Turn, new(PieceType.Rook, Turn));
        for (int to = 0; to < 64; to++)
            if (candidate.IsLegal(new Move(-1, to, Drop: PieceType.Rook), true)) return true;
        return false;
    }

    public bool TryMove(Move move, bool bughouse, bool captureKing, out Piece? captured, out string notation)
    {
        captured = null; notation = "";
        if (!IsLegal(move, bughouse, captureKing)) return false;
        var piece = move.Drop is { } drop ? new Piece(drop, Turn) : _cells[move.From]!;
        bool castle = piece.Type == PieceType.King && move.Drop is null && Math.Abs(move.To - move.From) == 2;
        captured = ApplyUnchecked(move);
        notation = move.Drop is not null ? $"{Chess.Letter(piece.Type)}@{Chess.SquareName(move.To)}"
            : castle ? (move.To > move.From ? "O-O" : "O-O-O")
            : $"{(piece.Type == PieceType.Pawn ? "" : Chess.Letter(piece.Type))}{Chess.SquareName(move.From)}{(captured is null ? "–" : "×")}{Chess.SquareName(move.To)}";
        if (piece.Type == PieceType.Pawn && move.Drop is null && move.To / 8 is 0 or 7)
            notation += "=" + Chess.Letter(move.Promotion);
        if (IsCheck(Turn)) notation += "+";
        return true;
    }

    private Piece? ApplyUnchecked(Move move)
    {
        Piece? captured = _cells[move.To];
        if (move.Drop is { } drop)
        {
            _cells[move.To] = new(drop, Turn);
            _pockets[(int)Turn * 5 + (int)drop]--;
            EnPassant = null;
        }
        else
        {
            var piece = _cells[move.From]!;
            if (piece.Type == PieceType.Pawn && move.To == EnPassant && captured is null && move.From % 8 != move.To % 8)
            {
                int victim = move.To + (Turn == Side.White ? -8 : 8);
                captured = _cells[victim]; _cells[victim] = null;
            }
            if (piece.Type == PieceType.King)
            {
                CastlingRights &= Turn == Side.White ? ~3 : ~12;
                if (Math.Abs(move.To - move.From) == 2)
                {
                    int rook = move.From + (move.To > move.From ? 3 : -4);
                    int destination = (move.From + move.To) / 2;
                    _cells[destination] = _cells[rook]; _cells[rook] = null;
                }
            }
            RevokeRookRight(move.From); RevokeRookRight(move.To);
            EnPassant = piece.Type == PieceType.Pawn && Math.Abs(move.To - move.From) == 16
                ? (move.To + move.From) / 2 : null;
            _cells[move.From] = null;
            _cells[move.To] = piece.Type == PieceType.Pawn && move.To / 8 is 0 or 7
                ? new(move.Promotion, Turn, true) : piece;
        }
        Turn = Turn.Opposite(); LastMove = move; Ply++;
        return captured;
    }

    private void RevokeRookRight(int square) => CastlingRights &= square switch
    {
        0 => ~2, 7 => ~1, 56 => ~8, 63 => ~4, _ => 15
    };

    /// <summary>FEN is useful for analysis and repeatable rule tests; a ~ marks a promoted piece.</summary>
    public static ChessBoard FromFen(string fen)
    {
        var fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4) throw new FormatException("FEN requires position, side, castling and en passant fields.");
        var board = new ChessBoard { _cells = new Piece?[64], CastlingRights = 0 };
        var ranks = fields[0].Split('/');
        if (ranks.Length != 8) throw new FormatException("FEN requires eight ranks.");
        for (int row = 0; row < 8; row++)
        {
            int file = 0;
            foreach (char c in ranks[row])
            {
                if (c is >= '1' and <= '8') { file += c - '0'; continue; }
                if (c == '~' && file > 0 && board._cells[(7 - row) * 8 + file - 1] is { } promoted)
                { board._cells[(7 - row) * 8 + file - 1] = promoted with { Promoted = true }; continue; }
                int type = "prnbqk".IndexOf(char.ToLowerInvariant(c));
                if (type < 0 || file >= 8) throw new FormatException("Invalid FEN piece.");
                board._cells[(7 - row) * 8 + file++] = new((PieceType)type, char.IsUpper(c) ? Side.White : Side.Black);
            }
            if (file != 8) throw new FormatException("Each FEN rank must contain eight squares.");
        }
        board.Turn = fields[1] switch { "w" => Side.White, "b" => Side.Black, _ => throw new FormatException("Invalid side.") };
        foreach (char c in fields[2]) board.CastlingRights |= c switch
        { 'K' => 1, 'Q' => 2, 'k' => 4, 'q' => 8, '-' => 0, _ => throw new FormatException("Invalid castling field.") };
        board.EnPassant = fields[3] == "-" ? null : Chess.Square(fields[3]);
        return board;
    }

    public string ToFen()
    {
        var text = new StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                if (_cells[rank * 8 + file] is not { } piece) { empty++; continue; }
                if (empty > 0) { text.Append(empty); empty = 0; }
                char c = "prnbqk"[(int)piece.Type];
                text.Append(piece.Side == Side.White ? char.ToUpperInvariant(c) : c);
                if (piece.Promoted) text.Append('~');
            }
            if (empty > 0) text.Append(empty);
            if (rank > 0) text.Append('/');
        }
        text.Append(Turn == Side.White ? " w " : " b ");
        if (CastlingRights == 0) text.Append('-');
        else for (int i = 0; i < 4; i++) if ((CastlingRights & (1 << i)) != 0) text.Append("KQkq"[i]);
        text.Append(' ').Append(EnPassant is { } ep ? Chess.SquareName(ep) : "-");
        return text.Append(" 0 ").Append(Ply / 2 + 1).ToString();
    }
}
