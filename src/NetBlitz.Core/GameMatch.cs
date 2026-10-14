namespace NetBlitz.Core;

/// <summary>Serialized by the desktop dispatcher or RoomServer gate. Clocks use elapsed monotonic time.</summary>
public sealed class GameMatch
{
    public GameMode Mode { get; private set; }
    public GameSettings Settings { get; private set; }
    public GamePhase Phase { get; private set; }
    public ChessBoard[] Boards { get; private set; } = [];
    public double[] RemainingMs { get; private set; } = [];
    public string Result { get; private set; } = "Ready to play";
    public int? WinnerTeam { get; private set; }
    public int? DrawOfferTeam { get; private set; }
    public HashSet<int> PendingDrawSeats { get; } = [];
    public List<MoveEntry> Moves { get; } = [];
    public int[] Wins { get; } = new int[2];
    public int Draws { get; private set; }
    public int GameNumber { get; private set; }

    public GameMatch(GameMode mode = GameMode.Chess, GameSettings? settings = null)
    {
        Settings = settings ?? new();
        Configure(mode, Settings);
    }

    public void Configure(GameMode mode, GameSettings settings)
    {
        if (Phase == GamePhase.Playing) throw new InvalidOperationException("Finish or abort the current game first.");
        if (!Enum.IsDefined(mode) || !settings.IsValid) throw new ArgumentException("Invalid game settings.");
        Mode = mode; Settings = settings;
        ResetPosition(); Phase = GamePhase.Lobby; Result = "Ready to play";
    }

    private void ResetPosition()
    {
        Boards = Mode == GameMode.Chess ? [new()] : [new(), new()];
        RemainingMs = Enumerable.Repeat(Settings.InitialSeconds * 1000d, Boards.Length * 2).ToArray();
        Moves.Clear(); PendingDrawSeats.Clear(); DrawOfferTeam = null; WinnerTeam = null;
    }

    public void Start()
    {
        if (Phase == GamePhase.Playing) throw new InvalidOperationException("A game is already running.");
        ResetPosition(); Phase = GamePhase.Playing; Result = "Game in progress"; GameNumber++;
    }

    public void Advance(TimeSpan elapsed)
    {
        if (Phase != GamePhase.Playing || Settings.UnlimitedTime || elapsed <= TimeSpan.Zero) return;
        for (int board = 0; board < Boards.Length; board++)
        {
            int seat = Chess.Seat(board, Boards[board].Turn);
            RemainingMs[seat] -= elapsed.TotalMilliseconds;
        }
        if (!Settings.AutomaticFlag) return;
        // If one update crosses multiple deadlines, the earliest deadline decides the result.
        int expired = Enumerable.Range(0, RemainingMs.Length).Where(i => RemainingMs[i] <= 0)
            .OrderBy(i => RemainingMs[i]).DefaultIfEmpty(-1).First();
        if (expired >= 0) Finish(1 - Chess.Team(expired), $"{SeatLabel(expired)} ran out of time");
    }

    public bool TryMove(int boardIndex, Move move, out string error)
    {
        error = "";
        if (Phase != GamePhase.Playing) { error = "Start a game first."; return false; }
        if (boardIndex < 0 || boardIndex >= Boards.Length) { error = "Unknown board."; return false; }
        var board = Boards[boardIndex]; var side = board.Turn; int seat = Chess.Seat(boardIndex, side);
        int number = board.Ply / 2 + 1;
        if (!board.TryMove(move, Mode == GameMode.Bughouse, Settings.CaptureKing, out var captured, out var notation))
        { error = "That move is not legal."; return false; }
        if (!Settings.UnlimitedTime) RemainingMs[seat] += Settings.IncrementSeconds * 1000d;
        if (Mode == GameMode.Bughouse && captured is not null)
            Boards[1 - boardIndex].AddToPocket(side.Opposite(), captured);
        Moves.Add(new(boardIndex, number, side, notation));
        if (DrawOfferTeam != Chess.Team(seat)) DrawOfferTeam = null;
        if (PendingDrawSeats.Remove(seat)) DrawOfferTeam = Chess.Team(seat);

        if (board.KingSquare(board.Turn) < 0)
            Finish(Chess.Team(seat), $"{SeatLabel(Chess.Seat(boardIndex, board.Turn))}'s king was captured");
        // In the original king-capture variant, a move leaving the mover in check cannot claim mate.
        else if ((!Settings.CaptureKing || !board.IsCheck(side)) &&
                 !board.HasSafeEscape(Mode == GameMode.Bughouse, Settings.WaitForPartnerPiece))
        {
            if (board.IsCheck(board.Turn)) Finish(Chess.Team(seat), $"Checkmate on board {(char)('A' + boardIndex)}");
            else Finish(null, $"Stalemate on board {(char)('A' + boardIndex)}");
        }
        return true;
    }

    public bool OfferDraw(int seat)
    {
        if (Phase != GamePhase.Playing || seat < 0 || seat >= RemainingMs.Length) return false;
        PendingDrawSeats.Add(seat); return true;
    }
    public void CancelDraw(int seat)
    {
        PendingDrawSeats.Remove(seat);
        if (DrawOfferTeam == Chess.Team(seat)) DrawOfferTeam = null;
    }
    public bool AcceptDraw(int seat)
    {
        if (Phase != GamePhase.Playing || DrawOfferTeam is null || DrawOfferTeam == Chess.Team(seat)) return false;
        Finish(null, "Draw by agreement"); return true;
    }
    public bool CallTime(int seat)
    {
        if (Phase != GamePhase.Playing || Settings.UnlimitedTime || Settings.AutomaticFlag) return false;
        int opponent = Enumerable.Range(0, RemainingMs.Length).FirstOrDefault(i =>
            Chess.Team(i) != Chess.Team(seat) && RemainingMs[i] <= 0, -1);
        if (opponent < 0) return false;
        Finish(Chess.Team(seat), $"Time called on {SeatLabel(opponent)}"); return true;
    }
    public void Resign(int seat)
    {
        if (Phase == GamePhase.Playing && seat >= 0 && seat < RemainingMs.Length)
            Finish(1 - Chess.Team(seat), $"{SeatLabel(seat)} resigned");
    }
    public void Abort(string reason = "Game aborted")
    {
        if (Phase != GamePhase.Playing) return;
        Phase = GamePhase.Finished; Result = reason; WinnerTeam = null;
        DrawOfferTeam = null; PendingDrawSeats.Clear();
    }
    private void Finish(int? team, string reason)
    {
        Phase = GamePhase.Finished; WinnerTeam = team;
        if (team is { } winner) Wins[winner]++; else Draws++;
        string winnerName = Mode == GameMode.Chess ? (team == 0 ? "White" : "Black") : $"Team {team + 1}";
        Result = team is null ? reason : $"{winnerName} wins · {reason}";
        DrawOfferTeam = null; PendingDrawSeats.Clear();
    }
    public string SeatLabel(int seat) => $"{(Side)(seat % 2)}{(Mode == GameMode.Bughouse ? $" on board {(char)('A' + seat / 2)}" : "")}";
    public MatchSnapshot Snapshot() => new(Mode, Settings, Phase, Boards.Select(b => b.Snapshot()).ToArray(),
        (double[])RemainingMs.Clone(), Result, WinnerTeam, DrawOfferTeam, PendingDrawSeats.ToArray(),
        Moves.TakeLast(500).ToArray(), (int[])Wins.Clone(), Draws, GameNumber);
}
