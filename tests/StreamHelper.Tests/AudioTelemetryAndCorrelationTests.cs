using System.Net;
using System.Net.Sockets;
using StreamHelper.Shared.Audio;

namespace StreamHelper.Tests;

[TestClass]
public sealed class AudioTelemetryAndCorrelationTests
{
    [TestMethod]
    public void AudioTelemetryPacket_SerializationRoundtrip_PreservesDataAndCrc()
    {
        var original = new AudioTelemetryPacket
        {
            SequenceNumber = 42,
            TimestampUnixMs = 1700000000123L,
            Readings = new float[] { -20.5f, -18.2f, -12.0f, -45.0f, -80.0f, -15.1f, -10.2f, -5.5f, -30.0f, -22.3f }
        };

        var bytes = original.ToBytes();
        Assert.IsTrue(AudioTelemetryPacket.TryParse(bytes, out var parsed));
        Assert.IsNotNull(parsed);

        Assert.AreEqual(original.SequenceNumber, parsed.SequenceNumber);
        Assert.AreEqual(original.TimestampUnixMs, parsed.TimestampUnixMs);
        Assert.AreEqual(original.Readings.Length, parsed.Readings.Length);

        for (int i = 0; i < original.Readings.Length; i++)
        {
            Assert.AreEqual(original.Readings[i], parsed.Readings[i], 0.001f);
        }

        Assert.AreEqual(original.Checksum, parsed.Checksum);
    }

    [TestMethod]
    public void AudioTelemetryPacket_CorruptedBytes_FailsParsing()
    {
        var packet = new AudioTelemetryPacket
        {
            SequenceNumber = 1,
            Readings = new float[] { -10.0f, -20.0f }
        };
        var bytes = packet.ToBytes();

        // Corrupt one byte
        bytes[10] ^= 0xFF;
        Assert.IsFalse(AudioTelemetryPacket.TryParse(bytes, out _));
    }

    [TestMethod]
    public void ConditionalSilenceDetector_ClientQuiet_SuppressesAlert()
    {
        var detector = new ConditionalSilenceDetector(clientActiveThresholdDbfs: -45.0, obsSilenceThresholdDbfs: -60.0, consecutiveReadingsRequired: 30);

        // 50 readings where client is quiet (-50 dBFS) and OBS is silent (-80 dBFS)
        for (int i = 0; i < 50; i++)
        {
            Assert.IsFalse(detector.ProcessReading(-50.0, -80.0));
        }
        Assert.IsFalse(detector.IsAlertActive);
    }

    [TestMethod]
    public void ConditionalSilenceDetector_ClientActiveWhileObsSilent_TriggersAfter3Seconds()
    {
        // 30 readings = 3 seconds at 100ms
        var detector = new ConditionalSilenceDetector(clientActiveThresholdDbfs: -45.0, obsSilenceThresholdDbfs: -60.0, consecutiveReadingsRequired: 30);

        // 29 readings of client active (-20 dBFS) and OBS silent (-75 dBFS) -> not triggered yet
        for (int i = 0; i < 29; i++)
        {
            Assert.IsFalse(detector.ProcessReading(-20.0, -75.0), $"Should not trigger at index {i}");
        }
        Assert.IsFalse(detector.IsAlertActive);

        // 30th reading -> triggers!
        Assert.IsTrue(detector.ProcessReading(-20.0, -75.0));
        Assert.IsTrue(detector.IsAlertActive);

        // OBS audio returns -> clears immediately
        Assert.IsFalse(detector.ProcessReading(-20.0, -25.0));
        Assert.IsFalse(detector.IsAlertActive);
    }

    [TestMethod]
    public void PearsonCorrelationEngine_StaticCalculations_Accurate()
    {
        double[] x = { 1.0, 2.0, 3.0, 4.0, 5.0 };
        double[] y = { 2.0, 4.0, 6.0, 8.0, 10.0 };

        double meanX = PearsonCorrelationEngine.CalculateMean(x);
        Assert.AreEqual(3.0, meanX, 0.001);

        double varX = PearsonCorrelationEngine.CalculateVariance(x, meanX);
        Assert.AreEqual(2.0, varX, 0.001);

        double r = PearsonCorrelationEngine.CalculateLaggedCorrelation(x, y, 0);
        Assert.AreEqual(1.0, r, 0.001); // Perfect correlation
    }

    [TestMethod]
    public void PearsonCorrelationEngine_LaggedMatch_DetectsCorrelation()
    {
        // y is x shifted by 2 steps (200ms)
        int size = 100;
        double[] x = new double[size];
        double[] y = new double[size];
        var rnd = new Random(42);

        for (int i = 0; i < size; i++)
        {
            x[i] = -30.0 + (rnd.NextDouble() * 20.0); // dynamic signal
        }
        for (int i = 2; i < size; i++)
        {
            y[i] = x[i - 2];
        }

        var engine = new PearsonCorrelationEngine(windowSize: 100, maxLagSteps: 5, varianceThreshold: 5.0, minCorrelationThreshold: 0.5);

        for (int i = 0; i < size; i++)
        {
            engine.AddSamples(x[i], y[i]);
        }

        Assert.IsGreaterThanOrEqualTo(0.9, engine.LastCalculatedMaxCorrelation);
        Assert.IsFalse(engine.IsAlertActive);
    }

    [TestMethod]
    public void PearsonCorrelationEngine_HdmiDelayMatch_DetectsCorrelation()
    {
        // y is x shifted by 15 steps (1500ms delay - typical for Realtek Stereo Mix + HDMI capture cards)
        int size = 120;
        double[] x = new double[size];
        double[] y = new double[size];
        var rnd = new Random(42);

        for (int i = 0; i < size; i++)
        {
            x[i] = -30.0 + (rnd.NextDouble() * 20.0);
        }
        for (int i = 15; i < size; i++)
        {
            y[i] = x[i - 15];
        }

        var engine = new PearsonCorrelationEngine(windowSize: 120, maxLagSteps: 25, varianceThreshold: 5.0, minCorrelationThreshold: 0.5);

        for (int i = 0; i < size; i++)
        {
            engine.AddSamples(x[i], y[i]);
        }

        Assert.IsGreaterThanOrEqualTo(0.9, engine.LastCalculatedMaxCorrelation);
        Assert.IsFalse(engine.IsAlertActive);
    }

    [TestMethod]
    public void PearsonCorrelationEngine_NegativeLagMatch_DetectsCorrelation()
    {
        // x is y shifted by 3 steps (-300ms, client slightly lagged or network lead)
        int size = 120;
        double[] x = new double[size];
        double[] y = new double[size];
        var rnd = new Random(42);

        for (int i = 0; i < size; i++)
        {
            y[i] = -30.0 + (rnd.NextDouble() * 20.0);
        }
        for (int i = 3; i < size; i++)
        {
            x[i] = y[i - 3];
        }

        var engine = new PearsonCorrelationEngine(windowSize: 120, maxLagSteps: 25, varianceThreshold: 5.0, minCorrelationThreshold: 0.5);

        for (int i = 0; i < size; i++)
        {
            engine.AddSamples(x[i], y[i]);
        }

        Assert.IsGreaterThanOrEqualTo(0.9, engine.LastCalculatedMaxCorrelation);
        Assert.IsFalse(engine.IsAlertActive);
    }

    [TestMethod]
    public void PearsonCorrelationEngine_UncorrelatedAudio_TriggersAlertAfterThreshold()
    {
        int size = 100;
        var engine = new PearsonCorrelationEngine(
            windowSize: size,
            maxLagSteps: 5,
            varianceThreshold: 5.0,
            minCorrelationThreshold: 0.5,
            consecutiveLowCorrCountRequired: 10);

        var rnd = new Random(123);
        // Fill window with uncorrelated noise
        for (int i = 0; i < size; i++)
        {
            engine.AddSamples(-30.0 + (rnd.NextDouble() * 20.0), -30.0 + (rnd.NextDouble() * 20.0));
        }

        // Add 10 more uncorrelated samples
        bool alertTriggered = false;
        for (int i = 0; i < 15; i++)
        {
            alertTriggered = engine.AddSamples(-30.0 + (rnd.NextDouble() * 20.0), -30.0 + (rnd.NextDouble() * 20.0));
            if (alertTriggered) break;
        }

        Assert.IsTrue(alertTriggered);
        Assert.IsTrue(engine.IsAlertActive);
    }

    [TestMethod]
    public void AudioCorrelationEngine_AlertSuppressionHierarchy_Enforced()
    {
        var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 1);
        var engine = new AudioCorrelationEngine(silenceDetector: silenceDetector);

        // Feed reading that triggers silence issue (client active -20, obs silent -80)
        engine.ProcessClientReading(-20.0);
        Assert.IsTrue(engine.HasSoundIssue);

        // Mute OBS device -> suppresses sound issue!
        engine.UpdateSuppressionStates(obsDeviceDisconnected: false, obsDeviceMuted: true);
        Assert.IsFalse(engine.HasSoundIssue, "Sound issue should be suppressed when OBS device is muted");

        // Unmute OBS device -> alert becomes visible again
        engine.UpdateSuppressionStates(obsDeviceDisconnected: false, obsDeviceMuted: false);
        Assert.IsTrue(engine.HasSoundIssue);

        // Disconnect OBS device -> suppresses sound issue!
        engine.UpdateSuppressionStates(obsDeviceDisconnected: true, obsDeviceMuted: false);
        Assert.IsFalse(engine.HasSoundIssue, "Sound issue should be suppressed when OBS device is disconnected");

        // Reconnect OBS device -> alert active
        engine.UpdateSuppressionStates(obsDeviceDisconnected: false, obsDeviceMuted: false);
        Assert.IsTrue(engine.HasSoundIssue);

        // Physical microphone disconnect/mute does NOT suppress game audio correlation!
        engine.UpdateSuppressionStates(micDisconnected: true, micMuted: true, obsDeviceDisconnected: false, obsDeviceMuted: false);
        Assert.IsTrue(engine.HasSoundIssue, "Sound issue should not be suppressed by voice mic disconnection/mute");
    }

    [TestMethod]
    public void AudioCorrelationEngine_StaleObsMeters_ExpiresToSilence()
    {
        var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 1);
        var engine = new AudioCorrelationEngine(silenceDetector: silenceDetector);

        // Ingest loud OBS meter from 2 seconds ago (stale > 500ms)
        engine.IngestObsAudioMeter(-15.0, -10.0, DateTime.UtcNow.AddSeconds(-2));

        // Client is playing audio (-20 dBFS)
        engine.ProcessClientReading(-20.0);

        // Stale OBS meter should be treated as silence (-100 dBFS), triggering silence alert!
        Assert.IsTrue(engine.HasSoundIssue, "Stale OBS meter should be treated as silent, triggering sound issue");
    }

    [TestMethod]
    public void AudioCorrelationEngine_DualPcPacket_TriggersSilenceAndClearsOnReturn()
    {
        // 3 consecutive packets of 10 readings = 30 readings (3 seconds)
        var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 30);
        var engine = new AudioCorrelationEngine(silenceDetector: silenceDetector);

        var packet = new AudioTelemetryPacket
        {
            SequenceNumber = 1,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Readings = new float[] { -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f }
        };

        // Send 2 packets (20 readings) -> not triggered yet
        engine.ProcessTelemetryPacket(packet);
        Assert.IsFalse(engine.HasSoundIssue);

        engine.ProcessTelemetryPacket(new AudioTelemetryPacket
        {
            SequenceNumber = 2,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Readings = new float[] { -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f }
        });
        Assert.IsFalse(engine.HasSoundIssue);

        // 3rd packet (30 readings) -> triggers alert!
        engine.ProcessTelemetryPacket(new AudioTelemetryPacket
        {
            SequenceNumber = 3,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Readings = new float[] { -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f }
        });
        Assert.IsTrue(engine.HasSoundIssue, "Sound issue should trigger after 3 seconds of active client audio and silent OBS");

        // OBS audio returns
        var now = DateTime.UtcNow;
        engine.IngestObsAudioMeter(-20.0, -15.0, now);

        engine.ProcessTelemetryPacket(new AudioTelemetryPacket
        {
            SequenceNumber = 4,
            TimestampUnixMs = new DateTimeOffset(now).ToUnixTimeMilliseconds(),
            Readings = new float[] { -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f }
        });
        Assert.IsFalse(engine.HasSoundIssue, "Sound issue should clear when OBS audio returns");
    }

    [TestMethod]
    public void WasapiLoopbackCapture_CalculateDbfs_CalculatesAccurately()
    {
        Assert.AreEqual(-100.0, WasapiLoopbackCapture.CalculateDbfs(0.0));
        Assert.AreEqual(0.0, WasapiLoopbackCapture.CalculateDbfs(1.0), 0.001);
        Assert.AreEqual(-6.02, WasapiLoopbackCapture.CalculateDbfs(0.5), 0.01);
        Assert.AreEqual(-20.0, WasapiLoopbackCapture.CalculateDbfs(0.1), 0.01);
    }
}
