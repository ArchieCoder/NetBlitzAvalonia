using System.Net.Sockets;
using System.Text.Json;

namespace NetBlitz.Network;

public sealed class RoomClient : IAsyncDisposable
{
    private readonly TcpClient _socket = new() { NoDelay = true };
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task _readTask = Task.CompletedTask;
    private Task _pingTask = Task.CompletedTask;
    private int _disposed;
    public int PlayerId { get; private set; } = -1;
    public RoomSnapshot? Room { get; private set; }
    public event Action<RoomSnapshot>? Changed;
    public event Action<Packet>? Message;
    public event Action<string>? Disconnected;

    public async Task ConnectAsync(string host, int port, string nickname, Core.GameMode mode, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await _socket.ConnectAsync(host, port, timeout.Token);
        await SendAsync(new() { Kind = "hello", Text = nickname, Mode = mode }, timeout.Token);
        var welcome = await Protocol.ReadAsync(_socket.GetStream(), timeout.Token);
        if (welcome.Kind != "welcome") throw new IOException(welcome.Text ?? "The host rejected the connection.");
        PlayerId = welcome.PlayerId;
        var state = await Protocol.ReadAsync(_socket.GetStream(), timeout.Token);
        if (state.Kind != "state" || state.Room is null) throw new IOException("The host did not send a room state.");
        Room = state.Room; Changed?.Invoke(Room);
        _readTask = ReadLoop(); _pingTask = PingLoop();
    }

    public async Task SendAsync(Packet packet, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await _writeGate.WaitAsync(timeout.Token);
        try { await Protocol.WriteAsync(_socket.GetStream(), packet, timeout.Token); }
        finally { _writeGate.Release(); }
    }

    private async Task ReadLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var packet = await Protocol.ReadAsync(_socket.GetStream(), timeout.Token);
                if (packet is { Kind: "state", Room: { } room }) { Room = room; Changed?.Invoke(room); }
                else Message?.Invoke(packet);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or JsonException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            if (!_stop.IsCancellationRequested) Disconnected?.Invoke("Connection lost: " + ex.Message);
            _stop.Cancel(); _socket.Dispose();
        }
    }

    private async Task PingLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try { while (await timer.WaitForNextTickAsync(_stop.Token)) await SendAsync(new() { Kind = "ping" }); }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            if (!_stop.IsCancellationRequested) Disconnected?.Invoke("Connection lost: " + ex.Message);
            _stop.Cancel(); _socket.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel(); _socket.Dispose();
        await Task.WhenAll(_readTask, _pingTask);
        // The cancellation source stays alive for in-flight UI SendAsync calls completing their cleanup.
    }
}
