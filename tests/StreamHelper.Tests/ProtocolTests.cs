using StreamHelper.Shared.Protocol;

namespace StreamHelper.Tests;

[TestClass]
public sealed class ProtocolTests
{
    [TestMethod]
    public void StatusPacket_Roundtrip_PreservesAllFields()
    {
        var original = new StatusPacket
        {
            Type = PacketType.StateChange,
            State = MicState.Muted,
            SequenceNumber = 42,
            TimestampUnixMs = 1700000000123L,
            ServerId = "server-guid-1234",
            HostName = "GamingPC",
            MicrophoneName = "Blue Yeti X"
        };

        var bytes = original.ToBytes();
        Assert.IsNotEmpty(bytes);

        var success = StatusPacket.TryParse(bytes, out var parsed);
        Assert.IsTrue(success);
        Assert.IsNotNull(parsed);

        Assert.AreEqual(ProtocolConstants.MagicHeader, parsed.Magic);
        Assert.AreEqual((byte)ProtocolConstants.ProtocolVersion, parsed.Version);
        Assert.AreEqual(PacketType.StateChange, parsed.Type);
        Assert.AreEqual(MicState.Muted, parsed.State);
        Assert.AreEqual(42UL, parsed.SequenceNumber);
        Assert.AreEqual(1700000000123L, parsed.TimestampUnixMs);
        Assert.AreEqual("server-guid-1234", parsed.ServerId);
        Assert.AreEqual("GamingPC", parsed.HostName);
        Assert.AreEqual("Blue Yeti X", parsed.MicrophoneName);
    }

    [TestMethod]
    [DataRow(MicState.Unmuted)]
    [DataRow(MicState.Muted)]
    [DataRow(MicState.Disconnected)]
    [DataRow(MicState.Paused)]
    public void StatusPacket_AllStates_SerializeAndDeserialize(MicState state)
    {
        var packet = new StatusPacket
        {
            State = state,
            SequenceNumber = 100,
            ServerId = "srv",
            HostName = "host",
            MicrophoneName = "mic"
        };

        var bytes = packet.ToBytes();
        Assert.IsTrue(StatusPacket.TryParse(bytes, out var result));
        Assert.AreEqual(state, result!.State);
    }

    [TestMethod]
    public void StatusPacket_CorruptedPayload_FailsChecksum()
    {
        var packet = new StatusPacket
        {
            State = MicState.Muted,
            SequenceNumber = 1,
            ServerId = "srv",
            HostName = "host",
            MicrophoneName = "mic"
        };

        var bytes = packet.ToBytes();
        // Corrupt one byte in the body
        bytes[10] ^= 0xFF;

        var success = StatusPacket.TryParse(bytes, out var result);
        Assert.IsFalse(success);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void StatusPacket_InvalidMagicHeader_FailsParsing()
    {
        var packet = new StatusPacket
        {
            State = MicState.Unmuted,
            SequenceNumber = 5
        };

        var bytes = packet.ToBytes();
        // Corrupt the magic header and fix the checksum so only the magic check fails
        bytes[0] = 0x00;
        // recompute checksum on corrupted payload
        var payloadLen = bytes.Length - 4;
        var newChecksum = StatusPacket.ComputeCrc32(bytes.AsSpan(0, payloadLen));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(payloadLen), newChecksum);

        var success = StatusPacket.TryParse(bytes, out var result);
        Assert.IsFalse(success);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void StatusPacket_TruncatedBuffer_FailsParsing()
    {
        var buffer = new byte[10]; // below minimum
        var success = StatusPacket.TryParse(buffer, out var result);
        Assert.IsFalse(success);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void StatusPacket_SequenceTracking_DetectsHigherSequence()
    {
        var seq1 = new StatusPacket { SequenceNumber = 10 }.ToBytes();
        var seq2 = new StatusPacket { SequenceNumber = 11 }.ToBytes();

        Assert.IsTrue(StatusPacket.TryParse(seq1, out var p1));
        Assert.IsTrue(StatusPacket.TryParse(seq2, out var p2));

        Assert.IsTrue(p2!.SequenceNumber > p1!.SequenceNumber);
    }

    [TestMethod]
    public void StatusPacket_AlertFlags_Roundtrip_PreservesAllFlags()
    {
        var alerts = AlertFlags.ObsNetworkIssue | AlertFlags.ObsRenderIssue | AlertFlags.MicMuted | AlertFlags.GameAudioNotFound;
        var packet = new StatusPacket
        {
            Type = PacketType.StateChange,
            State = MicState.Muted,
            Alerts = alerts,
            SequenceNumber = 123,
            ServerId = "server-alerts",
            HostName = "MyHost",
            MicrophoneName = "MyMic"
        };

        var bytes = packet.ToBytes();
        Assert.IsTrue(StatusPacket.TryParse(bytes, out var parsed));
        Assert.IsNotNull(parsed);
        Assert.AreEqual(alerts, parsed.Alerts);
        Assert.IsTrue(parsed.Alerts.HasFlag(AlertFlags.ObsNetworkIssue));
        Assert.IsTrue(parsed.Alerts.HasFlag(AlertFlags.ObsRenderIssue));
        Assert.IsTrue(parsed.Alerts.HasFlag(AlertFlags.MicMuted));
        Assert.IsTrue(parsed.Alerts.HasFlag(AlertFlags.GameAudioNotFound));
    }

    [TestMethod]
    public async Task UdpBroadcasterAndListener_DeliverAlertFlags()
    {
        int testPort = 19282;
        using var broadcaster = new StreamHelper.Shared.Network.UdpBroadcaster(testPort, "server-alerts-test");
        using var listener = new StreamHelper.Shared.Network.UdpListener(testPort);

        AlertFlags receivedAlerts = AlertFlags.None;
        var tcs = new TaskCompletionSource<bool>();

        listener.TargetServerAlertsChanged += alerts =>
        {
            if (alerts != AlertFlags.None)
            {
                receivedAlerts = alerts;
                tcs.TrySetResult(true);
            }
        };

        listener.Start(null, 2);
        await Task.Delay(100);

        broadcaster.Start();
        broadcaster.UpdateAlerts(AlertFlags.ObsNetworkIssue | AlertFlags.ObsRenderIssue);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.AreEqual(tcs.Task, completed, "Alerts should be received within 3 seconds");
        Assert.IsTrue(receivedAlerts.HasFlag(AlertFlags.ObsNetworkIssue));
        Assert.IsTrue(receivedAlerts.HasFlag(AlertFlags.ObsRenderIssue));
    }
}
