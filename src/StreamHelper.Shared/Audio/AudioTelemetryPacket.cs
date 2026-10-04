using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using StreamHelper.Shared.Protocol;

namespace StreamHelper.Shared.Audio;

public sealed class AudioTelemetryPacket
{
    public const uint MagicHeader = 0x54454C4D; // "TLMT"

    public ulong SequenceNumber { get; init; }
    public long TimestampUnixMs { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public float[] Readings { get; init; } = Array.Empty<float>();
    public uint Checksum { get; private set; }

    public byte[] ToBytes()
    {
        var count = (ushort)Readings.Length;
        var payloadLength = 4 + 8 + 8 + 2 + (count * sizeof(float));
        var totalLength = payloadLength + sizeof(uint);

        var buffer = new byte[totalLength];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), MagicHeader);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(4, 8), SequenceNumber);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(12, 8), TimestampUnixMs);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(20, 2), count);

        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(span.Slice(22 + (i * sizeof(float)), sizeof(float)), Readings[i]);
        }

        var checksum = StatusPacket.ComputeCrc32(span.Slice(0, payloadLength));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(payloadLength, sizeof(uint)), checksum);
        Checksum = checksum;

        return buffer;
    }

    public static bool TryParse(ReadOnlySpan<byte> buffer, [NotNullWhen(true)] out AudioTelemetryPacket? packet)
    {
        packet = null;
        // Minimum: Magic(4) + Seq(8) + Time(8) + Count(2) + Checksum(4) = 26 bytes
        if (buffer.Length < 26) return false;

        var payloadLength = buffer.Length - sizeof(uint);
        var expectedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(payloadLength));
        var actualChecksum = StatusPacket.ComputeCrc32(buffer.Slice(0, payloadLength));
        if (expectedChecksum != actualChecksum) return false;

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4));
        if (magic != MagicHeader) return false;

        var seq = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(4, 8));
        var time = BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(12, 8));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(20, 2));

        if (payloadLength < 22 + (count * sizeof(float))) return false;

        var readings = new float[count];
        for (int i = 0; i < count; i++)
        {
            readings[i] = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(22 + (i * sizeof(float)), sizeof(float)));
        }

        packet = new AudioTelemetryPacket
        {
            SequenceNumber = seq,
            TimestampUnixMs = time,
            Readings = readings,
            Checksum = actualChecksum
        };

        return true;
    }
}
