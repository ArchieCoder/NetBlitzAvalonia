using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NetBlitz.Core;
using NetBlitz.Desktop.Controls;
using NetBlitz.Network;

namespace NetBlitz.Desktop;

public partial class MainWindow : Window
{
    private Preferences _preferences;
    private GameMatch _local;
    private MatchSnapshot _match;
    private RoomSnapshot? _room;
    private RoomServer? _server;
    private RoomClient? _client;
    private readonly List<BoardPane> _panes = [];
    private readonly List<string> _chat = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private long _lastTick = Stopwatch.GetTimestamp();
    private long _lastSnapshot = Stopwatch.GetTimestamp();
    private bool _ready, _rendering, _flipped, _busy, _closing, _closeApproved;
    private int _generation, _lastGame = -1, _lastMoveCount = -1;
    private string _playersKey = "";
    private int PlayerId => _server is not null ? 0 : _client?.PlayerId ?? -1;
    private bool Online => _server is not null || _client is not null;
    private bool IsHost => !Online || _server is not null;
    private GameMode SelectedMode => ModePicker.SelectedIndex == 1 ? GameMode.Bughouse : GameMode.Chess;
    private int Actor => Online ? _room?.Players.FirstOrDefault(p => p.Id == PlayerId)?.Seat ?? 0 : Math.Max(0, ActorPicker.SelectedIndex);

    public MainWindow() : this(Preferences.Load()) { }

    public MainWindow(Preferences preferences)
    {
        _preferences = preferences;
        _local = new(GameMode.Chess, _preferences.Settings); _match = _local.Snapshot();
        InitializeComponent();
        _ready = true;
        _timer.Tick += (_, _) => Tick(); _timer.Start();
        Closing += OnClosing;
        RenderState();
    }

    private void Tick()
    {
        long now = Stopwatch.GetTimestamp();
        if (!Online)
        {
            var phase = _local.Phase;
            _local.Advance(Stopwatch.GetElapsedTime(_lastTick, now)); _match = _local.Snapshot();
            if (phase != _local.Phase) RenderState();
        }
        _lastTick = now;
        // Display interpolation only; the host remains authoritative for all network deadlines.
        double elapsed = Online ? Stopwatch.GetElapsedTime(_lastSnapshot, now).TotalMilliseconds : 0;
        foreach (var pane in _panes) pane.UpdateClocks(_match, elapsed);
    }

    private void RenderState()
    {
        if (!_ready) return;
        _rendering = true;
        bool bughouse = _match.Mode == GameMode.Bughouse, playing = _match.Phase == GamePhase.Playing;
        ModePicker.SelectedIndex = bughouse ? 1 : 0; ModePicker.IsEnabled = !Online && !playing && !_busy;
        HostButton.IsEnabled = JoinButton.IsEnabled = !Online && !playing && !_busy;
        bool newGame = _lastGame != _match.GameNumber;
        if (_panes.Count != _match.Boards.Length)
        {
            _panes.Clear(); BoardsHost.Children.Clear();
            BoardsHost.ColumnDefinitions = new(bughouse ? "*,*" : "*");
            for (int i = 0; i < _match.Boards.Length; i++)
            {
                int index = i; var pane = new BoardPane(i);
                pane.Board.MoveRequested += move => _ = TryMove(index, move);
                _panes.Add(pane); BoardsHost.Children.Add(pane); Grid.SetColumn(pane, i);
            }
            _playersKey = "";
        }
        foreach (var pane in _panes)
        {
            bool orientation = pane.BoardIndex == 1;
            if (Online && _room is not null)
            {
                var controlled = Enumerable.Range(pane.BoardIndex * 2, 2).Where(seat => _room.OwnerOf(seat) == PlayerId).ToArray();
                if (controlled.Length > 0) orientation = controlled[0] % 2 == 1;
                else if (_room.Players.FirstOrDefault(p => p.Id == PlayerId) is { } me)
                    orientation = Chess.Team(me.Seat) == (pane.BoardIndex == 0 ? 1 : 0);
            }
            pane.Update(_match, _room, PlayerId, _preferences, orientation ^ _flipped, newGame);
        }
        _lastGame = _match.GameNumber;
        ResultLabel.Text = _match.Result;
        ScoreLabel.Text = bughouse ? $"Team 1  {_match.Wins[0]}  ·  Team 2  {_match.Wins[1]}  ·  Draws  {_match.Draws}"
            : $"White  {_match.Wins[0]}  ·  Black  {_match.Wins[1]}  ·  Draws  {_match.Draws}";
        bool teamsReady = _room is null || (_room.Players.Any(p => Chess.Team(p.Seat) == 0) && _room.Players.Any(p => Chess.Team(p.Seat) == 1));
        StartButton.IsEnabled = IsHost && !playing && teamsReady && !_busy;
        StartButton.Content = _match.Phase == GamePhase.Finished ? "New game" : "Start game";
        SwitchButton.IsEnabled = IsHost && !playing;
        SwitchButton.IsVisible = Online;
        ResignButton.IsEnabled = playing;
        AbortButton.IsEnabled = playing && IsHost;
        TimeCallButton.IsEnabled = playing && !_match.Settings.UnlimitedTime && !_match.Settings.AutomaticFlag;
        DrawButton.IsEnabled = playing;
        TeamChat.IsEnabled = Online && bughouse;
        ChatInput.IsEnabled = Online;
        ChatTab.IsVisible = Online;
        if (!Online && SidebarTabs.SelectedItem == ChatTab) SidebarTabs.SelectedIndex = 0;
        ActorPicker.IsVisible = !Online && !playing;
        if (ActorPicker.ItemCount != _match.Boards.Length * 2)
        {
            ActorPicker.ItemsSource = Enumerable.Range(0, _match.Boards.Length * 2).Select(seat =>
                $"Act as {(Side)(seat % 2)}{(bughouse ? $" on board {(char)('A' + seat / 2)}" : "")}").ToArray();
            ActorPicker.SelectedIndex = 0;
        }
        UpdateDrawButton();
        string playersKey = Online ? string.Join('|', _room!.Players.Select(p => $"{p.Id}/{p.Seat}/{p.Name}")) + playing : "local" + bughouse;
        if (_playersKey != playersKey)
        {
            _playersKey = playersKey; PlayersPanel.Children.Clear();
            if (!Online)
            {
                foreach (string label in bughouse ? new[] { "Team 1 · White A + Black B", "Team 2 · Black A + White B" } : new[] { "White vs Black · local players" })
                    PlayersPanel.Children.Add(new TextBlock { Text = label, FontSize = 12, Margin = new Avalonia.Thickness(0, 4) });
            }
            else
            for (int seat = 0; seat < _match.Boards.Length * 2; seat++)
            {
                int capturedSeat = seat;
                var player = _room?.Players.FirstOrDefault(p => p.Seat == seat);
                string description = $"{(bughouse ? $"{(char)('A' + seat / 2)} · " : "")}{(Side)(seat % 2)}";
                var button = new Button { Content = description + "  /  " + (Online ? player?.Name ?? "Take seat" : "Local"),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    IsEnabled = Online && !playing && player is null, FontSize = 12 };
                ToolTip.SetTip(button, $"Team {Chess.Team(seat) + 1}");
                button.Click += async (_, _) => await Send(new() { Kind = "seat", Seat = capturedSeat });
                PlayersPanel.Children.Add(button);
            }
        }
        LobbyHint.Text = Online ? (bughouse ? "Need one player on each team. Without a partner, you control both team seats." : "The host starts when both players have joined.")
            : "You control both sides. No computer opponent.";
        int moveCount = _match.Boards.Sum(b => b.Ply);
        if (_lastMoveCount != moveCount || newGame)
        {
            RenderMoves(); _lastMoveCount = moveCount;
        }
        _rendering = false;
    }

    private void RenderMoves()
    {
        bool bughouse = _match.Mode == GameMode.Bughouse;
        MoveBoardPicker.IsVisible = bughouse;
        int board = bughouse && MoveBoardPicker.SelectedIndex == 1 ? 1 : 0;
        // Pair by move number, not list position: bughouse plies interleave, and retained
        // network history can begin with Black's move after older entries are trimmed.
        var rows = _match.Moves.Where(move => move.Board == board)
            .GroupBy(move => move.Number).OrderBy(group => group.Key)
            .Select(group => new MoveHistoryRow(group.Key,
                group.FirstOrDefault(move => move.Side == Side.White)?.Notation ?? "",
                group.FirstOrDefault(move => move.Side == Side.Black)?.Notation ?? "")).ToArray();
        MovesList.ItemsSource = rows;
        MovesEmpty.IsVisible = rows.Length == 0;
        Dispatcher.UIThread.Post(() => MovesScroll.ScrollToEnd(), DispatcherPriority.Loaded);
    }

    private void OnMoveBoardChanged(object? sender, SelectionChangedEventArgs e)
    { if (_ready && !_rendering) RenderMoves(); }

    private void UpdateDrawButton()
    {
        DrawButton.Content = _match.DrawOfferTeam is { } team && team != Chess.Team(Actor) ? "Accept draw"
            : _match.PendingDrawSeats.Contains(Actor) || _match.DrawOfferTeam == Chess.Team(Actor) ? "Cancel draw offer" : "Offer draw";
    }
    private async Task TryMove(int board, Move move)
    {
        if (_busy) return;
        int game = _match.GameNumber, ply = _match.Boards[board].Ply;
        if (move.Drop is null && _match.Boards[board].Cells[move.From]?.Type == PieceType.Pawn && move.To / 8 is 0 or 7)
        {
            _busy = true;
            try
            {
                var promotion = await Dialogs.Promotion(this, _match.Boards[board].Turn);
                if (promotion is null) return;
                move = move with { Promotion = promotion.Value };
            }
            finally { _busy = false; }
        }
        if (game != _match.GameNumber || ply != _match.Boards[board].Ply) return;
        await Send(new() { Kind = "move", Board = board, Move = move, GameNumber = game, ExpectedPly = ply });
    }

    private async Task Send(Packet packet)
    {
        try
        {
            if (_server is not null)
            {
                var error = _server.ExecuteHost(packet); if (error is not null) StatusLabel.Text = error;
                else { _room = _server.Snapshot(); SetState(_room); }
            }
            else if (_client is not null) await _client.SendAsync(packet);
            else
            {
                Tick();
                switch (packet.Kind)
                {
                    case "start": _local.Start(); _lastTick = Stopwatch.GetTimestamp(); break;
                    case "move":
                        if (!_local.TryMove(packet.Board, packet.Move!, out var error)) { StatusLabel.Text = error; return; }
                        ActorPicker.SelectedIndex = Chess.Seat(packet.Board, _local.Boards[packet.Board].Turn); break;
                    case "abort": _local.Abort(); break;
                    case "resign": _local.Resign(Actor); break;
                    case "offer-draw": _local.OfferDraw(Actor); break;
                    case "cancel-draw": _local.CancelDraw(Actor); break;
                    case "accept-draw": _local.AcceptDraw(Actor); break;
                    case "time-call": if (!_local.CallTime(Actor)) StatusLabel.Text = "No opponent can be flagged right now."; break;
                }
                _match = _local.Snapshot(); RenderState();
            }
            if (packet.Kind == "offer-draw") StatusLabel.Text = "The draw offer will be sent with that player’s next move.";
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        { StatusLabel.Text = ex.Message; }
    }

    private void SetState(RoomSnapshot room)
    {
        if (_room is not null && room.Revision < _room.Revision) return;
        _room = room; _match = room.Match; _lastSnapshot = Stopwatch.GetTimestamp(); RenderState();
    }
    private void AddMessage(Packet packet)
    {
        if (packet.Kind == "error") { StatusLabel.Text = packet.Text; return; }
        _chat.Add((packet.TeamOnly ? "[TEAM] " : "") + packet.Text);
        if (_chat.Count > 200) _chat.RemoveAt(0);
        ChatText.Text = string.Join("\n\n", _chat); ChatScroll.ScrollToEnd();
    }
    private async Task Disconnect()
    {
        _generation++;
        var client = _client; var server = _server;
        _client = null; _server = null; _room = null;
        if (client is not null) await client.DisposeAsync();
        if (server is not null) await server.DisposeAsync();
        _playersKey = ""; _chat.Clear(); ChatText.Text = "";
    }
    private void ResetLocal(GameMode mode)
    {
        _local = new(mode, _preferences.Settings); _match = _local.Snapshot();
        _lastTick = Stopwatch.GetTimestamp(); _flipped = false; _lastGame = -1;
        ConnectionLabel.Text = "LOCAL · PASS & PLAY"; RenderState();
    }
    private async void OnLocal(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if ((Online || _match.Phase == GamePhase.Playing) && !await Dialogs.Confirm(this, "Leave this game?", "The active game or network room will be closed.")) return;
        await Disconnect(); ResetLocal(SelectedMode);
    }
    private void OnModeChanged(object? sender, SelectionChangedEventArgs e)
    { if (_ready && !_rendering && !Online && _match.Phase != GamePhase.Playing) ResetLocal(SelectedMode); }
    private async void OnHost(object? sender, RoutedEventArgs e) => await Connect(true);
    private async void OnJoin(object? sender, RoutedEventArgs e) => await Connect(false);
    private async Task Connect(bool hosting)
    {
        if (_busy || Online || _match.Phase == GamePhase.Playing) return;
        var details = await Dialogs.Connection(this, _preferences, hosting); if (details is null) return;
        _busy = true; RenderState(); int generation = ++_generation;
        try
        {
            _preferences = _preferences with { Nickname = details.Value.Name, Host = details.Value.Host, Port = details.Value.Port };
            SavePreferences();
            StatusLabel.Text = hosting ? "Creating room…" : "Connecting…";
            if (hosting)
            {
                _server = new(details.Value.Name, SelectedMode, _match.Settings, details.Value.Port);
                _server.Changed += state => Dispatcher.UIThread.Post(() => { if (_generation == generation) SetState(state); });
                _server.Message += message => Dispatcher.UIThread.Post(() => { if (_generation == generation) AddMessage(message); });
                SetState(_server.Snapshot()); ConnectionLabel.Text = $"HOST · TCP {_server.Port}";
                StatusLabel.Text = "Room open. Share your computer’s LAN address and port with the other players.";
            }
            else
            {
                _client = new();
                _client.Changed += state => Dispatcher.UIThread.Post(() => { if (_generation == generation) SetState(state); });
                _client.Message += message => Dispatcher.UIThread.Post(() => { if (_generation == generation) AddMessage(message); });
                _client.Disconnected += message => Dispatcher.UIThread.Post(async () =>
                {
                    if (_generation != generation) return;
                    var mode = _match.Mode; await Disconnect(); ResetLocal(mode); StatusLabel.Text = message;
                });
                await _client.ConnectAsync(details.Value.Host, details.Value.Port, details.Value.Name, SelectedMode);
                SetState(_client.Room!); ConnectionLabel.Text = $"CONNECTED · {details.Value.Host}:{details.Value.Port}";
                StatusLabel.Text = "Connected. Choose a free seat or wait for the host to start.";
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or ArgumentException or ObjectDisposedException)
        { await Disconnect(); ResetLocal(SelectedMode); StatusLabel.Text = "Could not connect: " + ex.Message; }
        finally { _busy = false; RenderState(); }
    }
    private async void OnStart(object? sender, RoutedEventArgs e) => await Send(new() { Kind = "start" });
    private void OnFlip(object? sender, RoutedEventArgs e) { _flipped = !_flipped; RenderState(); }
    private async void OnSwitchColors(object? sender, RoutedEventArgs e) => await Send(new() { Kind = "switch-colors" });
    private async void OnAbort(object? sender, RoutedEventArgs e)
    { if (await Dialogs.Confirm(this, "Abort game?", "No win, loss or draw will be recorded.")) await Send(new() { Kind = "abort" }); }
    private async void OnResign(object? sender, RoutedEventArgs e)
    { if (await Dialogs.Confirm(this, "Resign game?", $"{(Side)(Actor % 2)}{(_match.Mode == GameMode.Bughouse ? " and their teammate" : "")} will lose this game.")) await Send(new() { Kind = "resign", Seat = Actor }); }
    private async void OnTimeCall(object? sender, RoutedEventArgs e) => await Send(new() { Kind = "time-call", Seat = Actor });
    private async void OnDraw(object? sender, RoutedEventArgs e)
    {
        string kind = _match.DrawOfferTeam is { } team && team != Chess.Team(Actor) ? "accept-draw"
            : _match.PendingDrawSeats.Contains(Actor) || _match.DrawOfferTeam == Chess.Team(Actor) ? "cancel-draw" : "offer-draw";
        await Send(new() { Kind = kind, Seat = Actor });
    }
    private void OnActorChanged(object? sender, SelectionChangedEventArgs e) { if (_ready && !_rendering) UpdateDrawButton(); }
    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        bool readOnly = !IsHost || _match.Phase == GamePhase.Playing;
        var changed = await Dialogs.Settings(this, _preferences, _match.Settings, readOnly); if (changed is null) return;
        _preferences = changed; SavePreferences();
        if (!readOnly)
        {
            if (_server is not null) await Send(new() { Kind = "settings", Settings = changed.Settings, Mode = _match.Mode });
            else { _local.Configure(_match.Mode, changed.Settings); _match = _local.Snapshot(); }
        }
        RenderState();
    }
    private void SavePreferences()
    {
        try { _preferences.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusLabel.Text = "Settings could not be saved: " + ex.Message; }
    }
    private async void OnAbout(object? sender, RoutedEventArgs e) => await Dialogs.About(this);
    private async void OnSendChat(object? sender, RoutedEventArgs e) => await SendChat();
    private async void OnChatKey(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { await SendChat(); e.Handled = true; } }
    private async Task SendChat()
    {
        if (!Online || string.IsNullOrWhiteSpace(ChatInput.Text)) return;
        string text = ChatInput.Text; ChatInput.Text = "";
        await Send(new() { Kind = "chat", Text = text, TeamOnly = TeamChat.IsEnabled && TeamChat.IsChecked == true });
    }
    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export game record", SuggestedFileName = $"netblitz-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            DefaultExtension = "txt", FileTypeChoices = [new("Text game record") { Patterns = ["*.txt"] }]
        });
        if (file is null) return;
        try
        {
            var record = new StringBuilder($"Net Blitz · {_match.Mode}\n{_match.Result}\n\n");
            foreach (var move in _match.Moves) record.AppendLine($"{(char)('A' + move.Board)} {move.Number}{(move.Side == Side.White ? "." : "...")} {move.Notation}");
            for (int i = 0; i < _match.Boards.Length; i++) record.AppendLine($"\nBoard {(char)('A' + i)} FEN: {ChessBoard.FromSnapshot(_match.Boards[i]).ToFen()}");
            record.AppendLine("\nCoordinate notation; the network history retains the most recent 500 plies.");
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream); await writer.WriteAsync(record.ToString());
            StatusLabel.Text = "Game record exported.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusLabel.Text = "Could not export: " + ex.Message; }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved) return;
        e.Cancel = true; if (_closing) return; _closing = true;
        if ((Online || _match.Phase == GamePhase.Playing) && !await Dialogs.Confirm(this, "Close Net Blitz?", "This will leave the room and end any active game."))
        { _closing = false; return; }
        _timer.Stop(); await Disconnect(); _closeApproved = true; Close();
    }
}
