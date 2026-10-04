using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace StreamHelper.Shared.Protocol;

public sealed class StatusPacket
{
    public uint Magic { get; init; } = ProtocolConstants.MagicHeader;
    public byte Version { get; init; } = (byte)ProtocolConstants.ProtocolVersion;
    public PacketType Type { get; init; } = PacketType.Heartbeat;
    public MicState State { get; init; } = MicState.Unmuted;
    public AlertFlags Alerts { get; init; } = AlertFlags.None;
    public ulong SequenceNumber { get; init; }
    public long TimestampUnixMs { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public string ServerId { get; init; } = string.Empty;
    public string HostName { get; init; } = string.Empty;
    public string MicrophoneName { get; init; } = string.Empty;
    public uint Checksum { get; private set; }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write((byte)Type);
        writer.Write((byte)State);
        writer.Write(SequenceNumber);
        writer.Write(TimestampUnixMs);
        writer.Write(ServerId ?? string.Empty);
        writer.Write(HostName ?? string.Empty);
        writer.Write(MicrophoneName ?? string.Empty);
        writer.Write((uint)Alerts);

        writer.Flush();
        var payloadBytes = ms.ToArray();
        var checksum = ComputeCrc32(payloadBytes);

        var finalBytes = new byte[payloadBytes.Length + sizeof(uint)];
        Buffer.BlockCopy(payloadBytes, 0, finalBytes, 0, payloadBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(finalBytes.AsSpan(payloadBytes.Length), checksum);

        Checksum = checksum;
        return finalBytes;
    }

    public static bool TryParse(ReadOnlySpan<byte> buffer, [NotNullWhen(true)] out StatusPacket? packet)
    {
        packet = null;
        // Minimum packet length: Magic(4) + Version(1) + Type(1) + State(1) + Seq(8) + Time(8) + ServerId(1) + HostName(1) + Mic(1) + Checksum(4) = 30 bytes
        if (buffer.Length < 30)
        {
            return false;
        }

        var payloadLength = buffer.Length - sizeof(uint);
        var expectedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(payloadLength));
        var actualChecksum = ComputeCrc32(buffer.Slice(0, payloadLength));

        if (expectedChecksum != actualChecksum)
        {
            return false;
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        if (magic != ProtocolConstants.MagicHeader)
        {
            return false;
        }

        try
        {
            using var ms = new MemoryStream(buffer.Slice(0, payloadLength).ToArray());
            using var reader = new BinaryReader(ms, Encoding.UTF8);

            var readMagic = reader.ReadUInt32();
            var version = reader.ReadByte();
            var packetType = (PacketType)reader.ReadByte();
            var state = (MicState)reader.ReadByte();
            var seq = reader.ReadUInt64();
            var timestamp = reader.ReadInt64();
            var serverId = reader.ReadString();
            var hostName = reader.ReadString();
            var micName = reader.ReadString();
            AlertFlags alerts = AlertFlags.None;
            if (ms.Position + sizeof(uint) <= ms.Length)
            {
                alerts = (AlertFlags)reader.ReadUInt32();
            }
            else
            {
                if (state == MicState.Muted) alerts |= AlertFlags.MicMuted;
                else if (state == MicState.Disconnected) alerts |= AlertFlags.MicDisconnected;
            }

            packet = new StatusPacket
            {
                Magic = readMagic,
                Version = version,
                Type = packetType,
                State = state,
                Alerts = alerts,
                SequenceNumber = seq,
                TimestampUnixMs = timestamp,
                ServerId = serverId,
                HostName = hostName,
                MicrophoneName = micName,
                Checksum = actualChecksum
            };

            return true;
        }
        catch
        {
            packet = null;
            return false;
        }
    }

    public static StatusPacket Parse(byte[] bytes)
    {
        if (!TryParse(bytes, out var packet))
        {
            throw new InvalidOperationException("Failed to parse status packet: invalid header, corrupted data, or failed checksum.");
        }
        return packet;
    }

    public static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        const uint polynomial = 0xEDB88320;
        uint crc = 0xFFFFFFFF;

        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            crc ^= b;
            for (int j = 0; j < 8; j++)
            {
                var mask = (uint)-(int)(crc & 1);
                crc = (crc >> 1) ^ (polynomial & mask);
            }
        }

        return ~crc;
    }
}
