using System.Buffers.Binary;
using System.Text.Json;
using NetBlitz.Core;

namespace NetBlitz.Network;

public sealed record PlayerInfo(int Id, string Name, int Seat, bool IsHost);
public sealed record RoomSnapshot(MatchSnapshot Match, PlayerInfo[] Players, long Revision)
{
    public int OwnerOf(int seat)
    {
        var direct = Players.FirstOrDefault(p => p.Seat == seat);
        if (direct is not null) return direct.Id;
        // In a two/three-player bughouse game, the lone teammate operates both boards.
        return Match.Mode == GameMode.Bughouse
            ? Players.FirstOrDefault(p => Chess.Team(p.Seat) == Chess.Team(seat))?.Id ?? -1 : -1;
    }
}

public sealed record Packet
{
    public int Version { get; init; } = 1;
    public string Kind { get; init; } = "";
    public string? Text { get; init; }
    public int Board { get; init; }
    public int Seat { get; init; } = -1;
    public int PlayerId { get; init; } = -1;
    public int GameNumber { get; init; }
    public int ExpectedPly { get; init; }
    public Move? Move { get; init; }
    public GameMode Mode { get; init; }
    public GameSettings? Settings { get; init; }
    public bool TeamOnly { get; init; }
    public RoomSnapshot? Room { get; init; }
}

/// <summary>Versioned UTF-8 JSON with a 32-bit network-order length. No MFC serialization.</summary>
public static class Protocol
{
    public const int DefaultPort = 5010;
    public const int MaxPacketBytes = 128 * 1024;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(Stream stream, Packet packet, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(packet, Json);
        if (bytes.Length > MaxPacketBytes) throw new InvalidDataException("Message exceeds the protocol limit.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    public static async Task<Packet> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 2 or > MaxPacketBytes) throw new InvalidDataException("Invalid message length.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        var packet = JsonSerializer.Deserialize<Packet>(bytes, Json) ?? throw new InvalidDataException("Empty message.");
        if (packet.Version != 1) throw new InvalidDataException("Incompatible Net Blitz protocol version.");
        return packet;
    }
}
