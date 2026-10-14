using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using NetBlitz.Core;

namespace NetBlitz.Network;

/// <summary>The host owns all positions, player identities, clocks, and match results.</summary>
public sealed class RoomServer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TcpListener _listener;
    private readonly Dictionary<int, Peer> _peers = [];
    private readonly List<PlayerInfo> _players = [];
    private readonly ConcurrentDictionary<int, Peer> _connections = new();
    private readonly GameMatch _match;
    private readonly Task _acceptTask;
    private readonly Task _tickTask;
    private long _lastTick = Stopwatch.GetTimestamp();
    private long _revision;
    private int _nextId;
    private int _disposed;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public event Action<RoomSnapshot>? Changed;
    public event Action<Packet>? Message;

    public RoomServer(string hostName, GameMode mode, GameSettings settings, int port = Protocol.DefaultPort,
        IPAddress? bindAddress = null)
    {
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _match = new(mode, settings);
        _players.Add(new(0, CleanName(hostName), 0, true));
        _listener = new(bindAddress ?? IPAddress.Any, port);
        _listener.Start(8);
        _acceptTask = AcceptLoop(); _tickTask = TickLoop();
    }

    public RoomSnapshot Snapshot() { lock (_gate) return SnapshotLocked(); }
    private RoomSnapshot SnapshotLocked() => new(_match.Snapshot(), _players.ToArray(), _revision);
    private void AdvanceLocked()
    {
        long now = Stopwatch.GetTimestamp();
        _match.Advance(Stopwatch.GetElapsedTime(_lastTick, now)); _lastTick = now;
    }
    private void PublishLocked()
    {
        _revision++;
        var room = SnapshotLocked();
        foreach (var peer in _peers.Values) peer.Send(new() { Kind = "state", Room = room });
        Changed?.Invoke(room);
    }

    public string? ExecuteHost(Packet command)
    {
        lock (_gate)
        {
            if (_disposed != 0) return "Room is closed.";
            return ExecuteLocked(0, command);
        }
    }

    private string? ExecuteLocked(int id, Packet command)
    {
        AdvanceLocked();
        var player = _players.FirstOrDefault(p => p.Id == id);
        if (player is null) return "Player is not in this room.";
        int seat = command.Seat < 0 ? player.Seat : command.Seat;
        bool ownsSeat = seat >= 0 && seat < _match.RemainingMs.Length && SnapshotLocked().OwnerOf(seat) == id;
        switch (command.Kind)
        {
            case "ping": return null;
            case "start":
                if (!player.IsHost) return "Only the host can start a game.";
                if (_match.Phase == GamePhase.Playing) return "A game is already running.";
                if (!_players.Any(p => Chess.Team(p.Seat) == 0) || !_players.Any(p => Chess.Team(p.Seat) == 1))
                    return "At least one player on each team is required.";
                _match.Start(); break;
            case "settings":
                if (!player.IsHost || _match.Phase == GamePhase.Playing) return "Only the host can change settings between games.";
                if (command.Settings is not { IsValid: true } || command.Mode != _match.Mode) return "Invalid game settings.";
                _match.Configure(_match.Mode, command.Settings); break;
            case "seat":
                if (_match.Phase == GamePhase.Playing) return "Seats cannot change during a game.";
                if (seat < 0 || seat >= _match.RemainingMs.Length || _players.Any(p => p.Seat == seat && p.Id != id))
                    return "That seat is unavailable.";
                _players[_players.IndexOf(player)] = player with { Seat = seat }; break;
            case "switch-colors":
                if (!player.IsHost || _match.Phase == GamePhase.Playing) return "Only the host can switch colors between games.";
                for (int i = 0; i < _players.Count; i++) _players[i] = _players[i] with { Seat = _players[i].Seat ^ 1 };
                break;
            case "move":
                if (command.Move is null || command.Board < 0 || command.Board >= _match.Boards.Length) return "Invalid move request.";
                if (SnapshotLocked().OwnerOf(Chess.Seat(command.Board, _match.Boards[command.Board].Turn)) != id)
                    return "You cannot move for that player.";
                if (command.GameNumber != _match.GameNumber || command.ExpectedPly != _match.Boards[command.Board].Ply)
                    return "The position changed. Please try your move again.";
                if (!_match.TryMove(command.Board, command.Move, out var error)) return error;
                break;
            case "abort":
                if (!player.IsHost) return "Only the host can abort the game.";
                _match.Abort(); break;
            case "resign":
                if (!ownsSeat) return "You cannot resign for that player.";
                _match.Resign(seat); break;
            case "offer-draw":
                if (!ownsSeat || !_match.OfferDraw(seat)) return "Cannot offer a draw now.";
                break;
            case "cancel-draw":
                if (!ownsSeat) return "You cannot cancel that offer.";
                _match.CancelDraw(seat); break;
            case "accept-draw":
                if (!ownsSeat || !_match.AcceptDraw(seat)) return "No opposing draw offer to accept.";
                break;
            case "time-call":
                if (!ownsSeat || !_match.CallTime(seat)) return "No opponent can be flagged right now.";
                break;
            case "chat":
                var text = command.Text?.Trim();
                if (string.IsNullOrEmpty(text) || text.Length > 1000 || text.Any(c => char.IsControl(c) && c != '\n'))
                    return "Chat must be between 1 and 1,000 characters.";
                var chat = new Packet { Kind = "chat", PlayerId = id, Text = $"{player.Name}: {text}", TeamOnly = command.TeamOnly };
                foreach (var recipient in _players)
                {
                    if (command.TeamOnly && Chess.Team(recipient.Seat) != Chess.Team(player.Seat)) continue;
                    if (recipient.Id == 0) Message?.Invoke(chat);
                    else if (_peers.TryGetValue(recipient.Id, out var peer)) peer.Send(chat);
                }
                return null;
            default: return "Unknown command.";
        }
        PublishLocked(); return null;
    }

    private async Task AcceptLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                if (_connections.Count >= 16) { socket.Dispose(); continue; }
                socket.NoDelay = true;
                int id = Interlocked.Increment(ref _nextId);
                var peer = new Peer(id, socket);
                _connections[id] = peer;
                peer.Completion = HandlePeer(peer);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            if (!_stop.IsCancellationRequested) Message?.Invoke(new() { Kind = "error", Text = "The room listener stopped: " + ex.Message });
        }
    }

    private async Task HandlePeer(Peer peer)
    {
        var writer = peer.WriteLoop(_stop.Token);
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(10));
            var hello = await Protocol.ReadAsync(peer.Stream, handshake.Token);
            string? refusal = null;
            lock (_gate)
            {
                if (hello.Kind != "hello") refusal = "Expected a connection greeting.";
                else if (_match.Phase == GamePhase.Playing) refusal = "A game is already in progress.";
                else if (hello.Mode != _match.Mode) refusal = "Select the same game mode as the host.";
                else if (_players.Count >= _match.RemainingMs.Length) refusal = "This room is full.";
                else
                {
                    string name = CleanName(hello.Text ?? "");
                    if (_players.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) refusal = "That nickname is already in use.";
                    else
                    {
                        int seat = Enumerable.Range(0, _match.RemainingMs.Length).First(i => _players.All(p => p.Seat != i));
                        _players.Add(new(peer.Id, name, seat, false)); _peers[peer.Id] = peer;
                        peer.Send(new() { Kind = "welcome", PlayerId = peer.Id }); PublishLocked();
                    }
                }
            }
            if (refusal is not null)
            {
                peer.Send(new() { Kind = "error", Text = refusal }); peer.Complete();
                await writer; return;
            }
            long rateStart = Stopwatch.GetTimestamp(); int messages = 0;
            while (!_stop.IsCancellationRequested)
            {
                using var read = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                read.CancelAfter(TimeSpan.FromSeconds(20));
                var command = await Protocol.ReadAsync(peer.Stream, read.Token);
                if (Stopwatch.GetElapsedTime(rateStart).TotalSeconds >= 1) { rateStart = Stopwatch.GetTimestamp(); messages = 0; }
                if (++messages > 100) throw new InvalidDataException("Too many messages.");
                lock (_gate)
                {
                    string? error = ExecuteLocked(peer.Id, command);
                    if (error is not null) peer.Send(new() { Kind = "error", Text = error });
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or
            JsonException or InvalidDataException or ArgumentException or ObjectDisposedException) { }
        finally
        {
            peer.Close();
            try { await writer; } catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            lock (_gate)
            {
                if (_peers.Remove(peer.Id))
                {
                    var player = _players.First(p => p.Id == peer.Id); _players.Remove(player);
                    _match.Abort($"Game aborted · {player.Name} disconnected");
                    if (!_stop.IsCancellationRequested) PublishLocked();
                }
            }
            _connections.TryRemove(peer.Id, out _);
        }
    }

    private async Task TickLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        int tick = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
                lock (_gate)
                {
                    var phase = _match.Phase; AdvanceLocked();
                    if (++tick % 5 == 0 || phase != _match.Phase) PublishLocked();
                }
        }
        catch (OperationCanceledException) { }
    }

    private static string CleanName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 24 || name.Any(char.IsControl)) throw new ArgumentException("Nickname must contain 1–24 printable characters.");
        return name;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel(); _listener.Stop();
        await _acceptTask;
        var peers = _connections.Values.ToArray();
        foreach (var peer in peers) peer.Close();
        await Task.WhenAll(peers.Select(p => p.Completion).Append(_tickTask));
        _stop.Dispose();
    }

    private sealed class Peer(int id, TcpClient socket)
    {
        private readonly Channel<Packet> _outgoing = Channel.CreateBounded<Packet>(new BoundedChannelOptions(32)
        { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
        public int Id { get; } = id;
        public NetworkStream Stream { get; } = socket.GetStream();
        public Task Completion { get; set; } = Task.CompletedTask;
        public void Send(Packet packet) { if (!_outgoing.Writer.TryWrite(packet)) Close(); }
        public void Complete() => _outgoing.Writer.TryComplete();
        public void Close() { Complete(); socket.Dispose(); }
        public async Task WriteLoop(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var packet in _outgoing.Reader.ReadAllAsync(cancellationToken))
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await Protocol.WriteAsync(Stream, packet, timeout.Token);
                }
            }
            finally { socket.Dispose(); }
        }
    }
}
