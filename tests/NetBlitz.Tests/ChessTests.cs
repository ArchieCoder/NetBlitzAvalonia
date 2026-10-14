using NetBlitz.Core;

namespace NetBlitz.Tests;

public sealed class ChessTests
{
    private static void Play(ChessBoard board, string from, string to) =>
        Assert.True(board.TryMove(Move.Parse(from, to), false, false, out _, out _), $"{from}-{to}");
    private static long Perft(ChessBoard board, int depth)
    {
        if (depth == 0) return 1;
        long nodes = 0;
        foreach (var move in board.LegalMoves())
        {
            var next = ChessBoard.FromSnapshot(board.Snapshot());
            Assert.True(next.TryMove(move, false, false, out _, out _));
            nodes += Perft(next, depth - 1);
        }
        return nodes;
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 400)]
    [InlineData(3, 8902)]
    [InlineData(4, 197281)]
    public void StartingPositionMatchesKnownMoveTree(int depth, long expected) => Assert.Equal(expected, Perft(new(), depth));

    [Theory]
    [InlineData(1, 48)]
    [InlineData(2, 2039)]
    public void KiwipeteExercisesCastlingAndChecks(int depth, long expected)
    {
        var board = ChessBoard.FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
        Assert.Equal(expected, Perft(board, depth));
    }

    [Theory]
    [InlineData(1, 14)]
    [InlineData(2, 191)]
    [InlineData(3, 2812)]
    public void RookPawnEndgameMoveTree(int depth, long expected) => Assert.Equal(expected,
        Perft(ChessBoard.FromFen("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1"), depth));

    [Theory]
    [InlineData("e2", "e5")]
    [InlineData("e7", "e5")]
    [InlineData("a1", "a3")]
    [InlineData("e1", "e1")]
    public void IllegalMoveDoesNotMutatePosition(string from, string to)
    {
        var board = new ChessBoard(); string before = board.ToFen();
        Assert.False(board.TryMove(Move.Parse(from, to), false, false, out _, out _));
        Assert.Equal(before, board.ToFen());
    }

    [Fact]
    public void EnPassantRemovesCapturedPawnAndExpires()
    {
        var board = new ChessBoard();
        Play(board, "e2", "e4"); Play(board, "a7", "a6"); Play(board, "e4", "e5"); Play(board, "d7", "d5");
        Assert.True(board.TryMove(Move.Parse("e5", "d6"), false, false, out var captured, out _));
        Assert.Equal(PieceType.Pawn, captured?.Type); Assert.Null(board[Chess.Square("d5")]);
        var expired = ChessBoard.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
        Play(expired, "e1", "e2"); Play(expired, "e8", "e7");
        Assert.False(expired.IsLegal(Move.Parse("e5", "d6")));
    }

    [Fact]
    public void EnPassantCannotExposeOwnKing()
    {
        var board = ChessBoard.FromFen("k3r3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
        Assert.False(board.IsLegal(Move.Parse("e5", "d6")));
    }

    [Fact]
    public void CastlingMovesRookAndCannotBeRepeated()
    {
        var board = ChessBoard.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Play(board, "e1", "g1"); Assert.Equal(PieceType.Rook, board[Chess.Square("f1")]?.Type);
        Assert.Null(board[Chess.Square("h1")]); Assert.Equal(0, board.CastlingRights & 3);
    }

    [Theory]
    [InlineData("4kr2/8/8/8/8/8/8/4K2R w K - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w K - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K2R~ w K - 0 1")]
    public void CannotCastleThroughAttackOrWithMissingOrPromotedRook(string fen) =>
        Assert.False(ChessBoard.FromFen(fen).IsLegal(Move.Parse("e1", "g1")));

    [Fact]
    public void CapturedRookPermanentlyLosesCastlingRight()
    {
        var board = ChessBoard.FromFen("r3k3/8/8/8/8/8/8/R3K3 b Qq - 0 1");
        Play(board, "a8", "a1"); Assert.Equal(0, board.CastlingRights & 2);
    }

    [Theory]
    [InlineData(PieceType.Queen)]
    [InlineData(PieceType.Rook)]
    [InlineData(PieceType.Bishop)]
    [InlineData(PieceType.Knight)]
    public void PromotionPreservesPawnIdentity(PieceType type)
    {
        var board = ChessBoard.FromFen("7k/P7/8/8/8/8/8/4K3 w - - 0 1");
        Assert.True(board.TryMove(Move.Parse("a7", "a8", type), false, false, out _, out _));
        Assert.Equal(new Piece(type, Side.White, true), board[Chess.Square("a8")]);
        var receiving = new ChessBoard(); receiving.AddToPocket(Side.White, board[Chess.Square("a8")]!);
        Assert.Equal(1, receiving.Pocket(Side.White, PieceType.Pawn));
        Assert.Equal(0, receiving.Pocket(Side.White, type));
    }

    [Fact]
    public void DropUsesPoolAndCannotPlacePawnOnBackRankOrOccupiedSquare()
    {
        var board = ChessBoard.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1");
        Assert.False(board.IsLegal(Move.Place(PieceType.Pawn, "e4"), true));
        board.AddToPocket(Side.White, new(PieceType.Pawn, Side.White));
        Assert.False(board.IsLegal(Move.Place(PieceType.Pawn, "a1"), true));
        Assert.False(board.IsLegal(Move.Place(PieceType.Pawn, "a8"), true));
        Assert.False(board.IsLegal(Move.Place(PieceType.Pawn, "e1"), true));
        Assert.False(board.IsLegal(Move.Place(PieceType.Pawn, "e4"), false));
        Assert.True(board.TryMove(Move.Place(PieceType.Pawn, "e4"), true, false, out _, out _));
        Assert.Equal(0, board.Pocket(Side.White, PieceType.Pawn)); Assert.Equal(Side.Black, board.Turn);
    }

    [Fact]
    public void DropMustResolveCheckAndClearsEnPassant()
    {
        var board = ChessBoard.FromFen("k3r3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
        board.AddToPocket(Side.White, new(PieceType.Knight, Side.White));
        // Remove the e5 shield to expose the king.
        var snapshot = board.Snapshot(); snapshot.Cells[Chess.Square("e5")] = null; board = ChessBoard.FromSnapshot(snapshot);
        Assert.False(board.IsLegal(Move.Place(PieceType.Knight, "a3"), true));
        Assert.True(board.TryMove(Move.Place(PieceType.Knight, "e3"), true, false, out _, out _));
        Assert.Null(board.EnPassant);
    }

    [Fact]
    public void LegacyBughouseWaitingRuleAllowsFutureBlock()
    {
        var board = ChessBoard.FromFen("k3r3/8/8/8/8/8/3P1P2/3PKP2 w - - 0 1");
        Assert.True(board.IsCheck(Side.White));
        Assert.False(board.HasSafeEscape(false, false));
        Assert.False(board.HasSafeEscape(true, false));
        Assert.True(board.HasSafeEscape(true, true));
        Assert.Equal(0, board.Pocket(Side.White, PieceType.Rook));
    }

    [Fact]
    public void CaptureKingVariantAllowsExposingKing()
    {
        var board = ChessBoard.FromFen("k3r3/8/8/8/8/8/4R3/4K3 w - - 0 1");
        Assert.False(board.IsLegal(Move.Parse("e2", "a2")));
        Assert.True(board.IsLegal(Move.Parse("e2", "a2"), captureKing: true));
        var capture = ChessBoard.FromFen("4k3/8/8/8/8/8/4R3/4K3 w - - 0 1");
        Assert.False(capture.IsLegal(Move.Parse("e2", "e8")));
        Assert.True(capture.TryMove(Move.Parse("e2", "e8"), false, true, out var king, out _));
        Assert.Equal(PieceType.King, king?.Type);
    }

    [Fact]
    public void SnapshotDoesNotShareMutableArrays()
    {
        var board = new ChessBoard(); var snapshot = board.Snapshot(); snapshot.Cells[0] = null;
        Assert.NotNull(board[0]);
        var copy = ChessBoard.FromSnapshot(board.Snapshot()); Play(copy, "e2", "e4"); Assert.NotNull(board[Chess.Square("e2")]);
    }

    [Fact]
    public void OutOfRangeNetworkMovesAreRejected()
    {
        var board = new ChessBoard();
        Assert.False(board.IsLegal(new(-5, 500)));
        Assert.False(board.IsLegal(new(-1, 20, Drop: (PieceType)100), true));
    }
}
