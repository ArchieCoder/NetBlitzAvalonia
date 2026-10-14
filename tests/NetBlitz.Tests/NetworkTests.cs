using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using NetBlitz.Core;
using NetBlitz.Network;

namespace NetBlitz.Tests;

public sealed class NetworkTests
{
    private static RoomServer Host(GameMode mode = GameMode.Chess) => new("Host", mode,
        new() { UnlimitedTime = true }, 0, IPAddress.Loopback);
    private static async Task Eventually(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    private static Packet MovePacket(RoomSnapshot state, int board, string from, string to) => new()
    {
        Kind = "move", Board = board, Move = Move.Parse(from, to), GameNumber = state.Match.GameNumber,
        ExpectedPly = state.Match.Boards[board].Ply
    };

    [Fact]
    public async Task TwoPlayerChessSynchronizesAndRejectsOutOfTurnAndStaleMoves()
    {
        await using var server = Host(); await using var client = new RoomClient();
        var errors = new ConcurrentQueue<string>(); client.Message += p => { if (p.Kind == "error") errors.Enqueue(p.Text!); };
        await client.ConnectAsync("127.0.0.1", server.Port, "Guest", GameMode.Chess);
        Assert.Null(server.ExecuteHost(new() { Kind = "start" }));
        await Eventually(() => client.Room?.Match.Phase == GamePhase.Playing);
        await client.SendAsync(MovePacket(client.Room!, 0, "e2", "e4"));
        await Eventually(() => errors.Count == 1); Assert.Equal(0, server.Snapshot().Match.Boards[0].Ply);
        Assert.Null(server.ExecuteHost(MovePacket(server.Snapshot(), 0, "e2", "e4")));
        await Eventually(() => client.Room!.Match.Boards[0].Ply == 1);
        var black = MovePacket(client.Room!, 0, "e7", "e5"); await client.SendAsync(black);
        await Eventually(() => server.Snapshot().Match.Boards[0].Ply == 2);
        Assert.Null(server.ExecuteHost(MovePacket(server.Snapshot(), 0, "g1", "f3")));
        await client.SendAsync(black);
        await Eventually(() => errors.Count == 2); Assert.Equal(3, server.Snapshot().Match.Boards[0].Ply);
    }

    [Fact]
    public async Task TwoPlayerBughouseAllowsBothTeamSeatsAndSyncsCaptureAndDrop()
    {
        await using var server = Host(GameMode.Bughouse); await using var client = new RoomClient();
        await client.ConnectAsync("127.0.0.1", server.Port, "Guest", GameMode.Bughouse);
        Assert.Equal(0, server.Snapshot().OwnerOf(3)); Assert.Equal(client.PlayerId, server.Snapshot().OwnerOf(2));
        Assert.Null(server.ExecuteHost(new() { Kind = "start" }));
        Assert.Null(server.ExecuteHost(MovePacket(server.Snapshot(), 0, "e2", "e4")));
        await Eventually(() => client.Room!.Match.Boards[0].Ply == 1);
        await client.SendAsync(MovePacket(client.Room!, 0, "d7", "d5"));
        await Eventually(() => server.Snapshot().Match.Boards[0].Ply == 2);
        Assert.Null(server.ExecuteHost(MovePacket(server.Snapshot(), 0, "e4", "d5")));
        await Eventually(() => client.Room!.Match.Boards[1].Pockets[5] == 1);
        await client.SendAsync(MovePacket(client.Room!, 1, "e2", "e4"));
        await Eventually(() => server.Snapshot().Match.Boards[1].Ply == 1);
        var state = server.Snapshot();
        Assert.Null(server.ExecuteHost(new() { Kind = "move", Board = 1, Move = Move.Place(PieceType.Pawn, "e5"),
            GameNumber = state.Match.GameNumber, ExpectedPly = 1 }));
        await Eventually(() => client.Room!.Match.Boards[1].Ply == 2);
        Assert.Equal(0, client.Room!.Match.Boards[1].Pockets[5]);
        Assert.Equal(new Piece(PieceType.Pawn, Side.Black), client.Room.Match.Boards[1].Cells[Chess.Square("e5")]);
    }

    [Fact]
    public async Task FourPlayerTeamChatOnlyReachesTeammateAndSeatOwnershipIsEnforced()
    {
        await using var server = Host(GameMode.Bughouse);
        await using var a = new RoomClient(); await using var b = new RoomClient(); await using var c = new RoomClient();
        var aMessages = new ConcurrentQueue<Packet>(); var bMessages = new ConcurrentQueue<Packet>(); var cMessages = new ConcurrentQueue<Packet>();
        a.Message += aMessages.Enqueue; b.Message += bMessages.Enqueue; c.Message += cMessages.Enqueue;
        await a.ConnectAsync("127.0.0.1", server.Port, "Black A", GameMode.Bughouse);
        await b.ConnectAsync("127.0.0.1", server.Port, "White B", GameMode.Bughouse);
        await c.ConnectAsync("127.0.0.1", server.Port, "Black B", GameMode.Bughouse);
        Assert.Equal(new[] { 0, a.PlayerId, b.PlayerId, c.PlayerId }, Enumerable.Range(0, 4).Select(i => server.Snapshot().OwnerOf(i)));
        Assert.Null(server.ExecuteHost(new() { Kind = "chat", Text = "Need a knight", TeamOnly = true }));
        Assert.Null(server.ExecuteHost(new() { Kind = "chat", Text = "Hello everyone" }));
        await Eventually(() => aMessages.Count == 1 && bMessages.Count == 1 && cMessages.Count == 2);
        Assert.DoesNotContain(aMessages, p => p.TeamOnly); Assert.DoesNotContain(bMessages, p => p.TeamOnly);
        Assert.Contains(cMessages, p => p.TeamOnly && p.Text!.Contains("Need a knight"));
        Assert.Null(server.ExecuteHost(new() { Kind = "start" }));
        Assert.NotNull(server.ExecuteHost(MovePacket(server.Snapshot(), 1, "e2", "e4")));
        await b.SendAsync(new() { Kind = "resign", Seat = 0 });
        await Eventually(() => bMessages.Count == 2); Assert.Equal(GamePhase.Playing, server.Snapshot().Match.Phase);
    }

    [Fact]
    public async Task DuplicateNamesWrongModeAndFullRoomAreRejected()
    {
        await using var server = Host();
        await using var duplicate = new RoomClient();
        var nameError = await Assert.ThrowsAsync<IOException>(() => duplicate.ConnectAsync("127.0.0.1", server.Port, "host", GameMode.Chess));
        Assert.Contains("nickname", nameError.Message);
        await using var wrongMode = new RoomClient();
        await Assert.ThrowsAsync<IOException>(() => wrongMode.ConnectAsync("127.0.0.1", server.Port, "Guest", GameMode.Bughouse));
        await using var guest = new RoomClient(); await guest.ConnectAsync("127.0.0.1", server.Port, "Guest", GameMode.Chess);
        await using var extra = new RoomClient();
        await Assert.ThrowsAsync<IOException>(() => extra.ConnectAsync("127.0.0.1", server.Port, "Extra", GameMode.Chess));
        Assert.Equal(2, server.Snapshot().Players.Length);
    }

    [Fact]
    public async Task DisconnectAbortsActiveMatchAndReleasesSeat()
    {
        await using var server = Host(); await using var client = new RoomClient();
        await client.ConnectAsync("127.0.0.1", server.Port, "Guest", GameMode.Chess);
        Assert.Null(server.ExecuteHost(new() { Kind = "start" })); await client.DisposeAsync();
        await Eventually(() => server.Snapshot().Players.Length == 1);
        Assert.Equal(GamePhase.Finished, server.Snapshot().Match.Phase);
        Assert.Contains("disconnected", server.Snapshot().Match.Result);
        Assert.Equal(0, server.Snapshot().Match.Draws);
    }

    [Fact]
    public async Task OversizedFrameIsDisconnectedWithoutAffectingRoom()
    {
        await using var server = Host(); using var malformed = new TcpClient();
        await malformed.ConnectAsync(IPAddress.Loopback, server.Port);
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, Protocol.MaxPacketBytes + 1);
        await malformed.GetStream().WriteAsync(header);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Equal(0, await malformed.GetStream().ReadAsync(new byte[1], timeout.Token));
        await using var good = new RoomClient(); await good.ConnectAsync("127.0.0.1", server.Port, "Guest", GameMode.Chess);
        Assert.Equal(2, server.Snapshot().Players.Length);
    }

    [Fact]
    public async Task ProtocolHandlesFragmentedStreamAndRejectsVersionMismatch()
    {
        using var data = new MemoryStream(); await Protocol.WriteAsync(data, new() { Kind = "chat", Text = "échecs ♞" }, default);
        data.Position = 0; using var fragment = new FragmentedStream(data.ToArray());
        Assert.Equal("échecs ♞", (await Protocol.ReadAsync(fragment, default)).Text);
        using var bad = new MemoryStream(); await Protocol.WriteAsync(bad, new() { Version = 42 }, default); bad.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => Protocol.ReadAsync(bad, default));
    }
    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
