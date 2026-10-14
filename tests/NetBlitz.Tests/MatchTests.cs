using NetBlitz.Core;

namespace NetBlitz.Tests;

public sealed class MatchTests
{
    private static void Play(GameMatch match, int board, string from, string to) =>
        Assert.True(match.TryMove(board, Move.Parse(from, to), out var error), error);

    [Fact]
    public void FoolsMateEndsChessAndRecordsScore()
    {
        var game = new GameMatch(); game.Start();
        Play(game, 0, "f2", "f3"); Play(game, 0, "e7", "e5"); Play(game, 0, "g2", "g4"); Play(game, 0, "d8", "h4");
        Assert.Equal(GamePhase.Finished, game.Phase); Assert.Equal(1, game.WinnerTeam); Assert.Equal(1, game.Wins[1]);
        Assert.False(game.TryMove(0, Move.Parse("a2", "a3"), out _));
        game.Start(); Assert.Equal(1, game.Wins[1]); Assert.Empty(game.Moves); Assert.Equal(2, game.GameNumber);
    }

    [Fact]
    public void BughouseCaptureGoesToOppositeColorOnOtherBoard()
    {
        var game = new GameMatch(GameMode.Bughouse); game.Start();
        Play(game, 0, "e2", "e4"); Play(game, 0, "d7", "d5"); Play(game, 0, "e4", "d5");
        Assert.Equal(1, game.Boards[1].Pocket(Side.Black, PieceType.Pawn));
        Assert.Equal(0, game.Boards[0].Pocket(Side.White, PieceType.Pawn));
        Play(game, 1, "e2", "e4");
        Assert.True(game.TryMove(1, Move.Place(PieceType.Pawn, "e5"), out _));
        Assert.Equal(0, game.Boards[1].Pocket(Side.Black, PieceType.Pawn));
    }

    [Fact]
    public void IncrementAndBothBoardClocksUseElapsedTime()
    {
        var game = new GameMatch(GameMode.Bughouse, new() { InitialSeconds = 10, IncrementSeconds = 2 }); game.Start();
        game.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(new double[] { 7000, 10000, 7000, 10000 }, game.RemainingMs);
        Play(game, 0, "e2", "e4"); game.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new double[] { 9000, 9000, 6000, 10000 }, game.RemainingMs);
    }

    [Fact]
    public void ManualFlagRequiresOpposingTimeCall()
    {
        var game = new GameMatch(settings: new() { InitialSeconds = 1 }); game.Start();
        game.Advance(TimeSpan.FromSeconds(2)); Assert.Equal(GamePhase.Playing, game.Phase);
        Assert.False(game.CallTime(0)); Assert.True(game.CallTime(1)); Assert.Equal(1, game.WinnerTeam);
    }

    [Fact]
    public void AutoFlagEndsGameAndUnlimitedDoesNotTick()
    {
        var game = new GameMatch(settings: new() { InitialSeconds = 1, AutomaticFlag = true }); game.Start();
        game.Advance(TimeSpan.FromSeconds(2)); Assert.Equal(GamePhase.Finished, game.Phase); Assert.Equal(1, game.WinnerTeam);
        var unlimited = new GameMatch(settings: new() { InitialSeconds = 1, AutomaticFlag = true, UnlimitedTime = true }); unlimited.Start();
        unlimited.Advance(TimeSpan.FromDays(1)); Assert.Equal(GamePhase.Playing, unlimited.Phase); Assert.Equal(1000, unlimited.RemainingMs[0]);
    }

    [Fact]
    public void DrawOfferSentWithMoveAndOnlyOtherTeamCanAccept()
    {
        var game = new GameMatch(); game.Start(); Assert.True(game.OfferDraw(0));
        Assert.Null(game.DrawOfferTeam); Play(game, 0, "e2", "e4"); Assert.Equal(0, game.DrawOfferTeam);
        Assert.False(game.AcceptDraw(0)); Assert.True(game.AcceptDraw(1)); Assert.Equal(1, game.Draws);
    }

    [Fact]
    public void OpposingMoveDeclinesDrawAndCancelRetracts()
    {
        var game = new GameMatch(); game.Start(); game.OfferDraw(0); Play(game, 0, "e2", "e4");
        Play(game, 0, "e7", "e5"); Assert.Null(game.DrawOfferTeam);
        game.OfferDraw(0); game.CancelDraw(0); Play(game, 0, "d2", "d4"); Assert.Null(game.DrawOfferTeam);
    }

    [Fact]
    public void ResignationEndsBothBoardsButAbortDoesNotCountAsDraw()
    {
        var game = new GameMatch(GameMode.Bughouse); game.Start(); game.Resign(3);
        Assert.Equal(1, game.WinnerTeam); Assert.Equal(1, game.Wins[1]);
        game.Start(); game.Abort(); Assert.Equal(0, game.Draws);
    }

    [Fact]
    public void CannotChangeRulesMidGame()
    {
        var game = new GameMatch(); game.Start();
        Assert.Throws<InvalidOperationException>(() => game.Configure(GameMode.Bughouse, new()));
        Assert.Throws<ArgumentException>(() => new GameMatch(settings: new() { InitialSeconds = 0 }));
    }

    [Fact]
    public void KingCaptureEndsGameAndStalemateIsADraw()
    {
        var game = new GameMatch(settings: new() { CaptureKing = true }); game.Start();
        game.Boards[0] = ChessBoard.FromFen("4k3/8/8/8/8/8/4R3/4K3 w - - 0 1");
        Play(game, 0, "e2", "e8"); Assert.Equal(0, game.WinnerTeam); Assert.Contains("captured", game.Result);
        game = new GameMatch(); game.Start();
        game.Boards[0] = ChessBoard.FromFen("k7/8/1QK5/8/8/8/8/8 w - - 0 1");
        Play(game, 0, "b6", "c7"); Assert.Equal(GamePhase.Finished, game.Phase);
        Assert.Null(game.WinnerTeam); Assert.Equal(1, game.Draws); Assert.Contains("Stalemate", game.Result);
    }
}
