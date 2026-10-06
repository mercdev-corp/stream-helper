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
            consecutiveLowCorrCountRequired: 10,
            toleranceDb: 0);

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
    public void PearsonCorrelationEngine_LowCorrelationWithinTolerance_DoesNotAlert()
    {
        int size = 100;
        // Default tolerance is 20.0 dB
        var engine = new PearsonCorrelationEngine(
            windowSize: size,
            maxLagSteps: 5,
            varianceThreshold: 5.0,
            minCorrelationThreshold: 0.5,
            consecutiveLowCorrCountRequired: 10,
            toleranceDb: 20.0);

        var rnd = new Random(123);
        // Client: [-30, -10] dBFS, OBS: [-30, -10] dBFS (uncorrelated, mean diff ~6.7 dB <= 20 dB)
        for (int i = 0; i < size + 20; i++)
        {
            engine.AddSamples(-30.0 + (rnd.NextDouble() * 20.0), -30.0 + (rnd.NextDouble() * 20.0));
        }

        Assert.IsFalse(engine.IsAlertActive, "Should not alert when difference is within tolerance");
        Assert.IsTrue(engine.LastCalculatedDifference <= 20.0);
    }

    [TestMethod]
    public void PearsonCorrelationEngine_LowCorrelationBeyondTolerance_TriggersAlert()
    {
        int size = 100;
        // Tolerance set to 10 dB
        var engine = new PearsonCorrelationEngine(
            windowSize: size,
            maxLagSteps: 5,
            varianceThreshold: 5.0,
            minCorrelationThreshold: 0.5,
            consecutiveLowCorrCountRequired: 10,
            toleranceDb: 10.0);

        var rnd = new Random(123);
        // Client: [-25, -5] (mean ~-15, variance > 5 dB)
        // OBS: [-55, -45] (mean ~-50, diff ~ 35 dB > 10 dB)
        for (int i = 0; i < size; i++)
        {
            engine.AddSamples(-25.0 + (rnd.NextDouble() * 20.0), -55.0 + (rnd.NextDouble() * 10.0));
        }

        bool alertTriggered = false;
        for (int i = 0; i < 15; i++)
        {
            alertTriggered = engine.AddSamples(-25.0 + (rnd.NextDouble() * 20.0), -55.0 + (rnd.NextDouble() * 10.0));
            if (alertTriggered) break;
        }

        Assert.IsTrue(alertTriggered);
        Assert.IsTrue(engine.IsAlertActive);
        Assert.IsTrue(engine.LastCalculatedDifference > 10.0);
    }

    [TestMethod]
    public void AudioCorrelationEngine_SimpleMode_AlertsOnlyOnActiveClientAndSilentObs()
    {
        var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 3);
        var correlationEngine = new PearsonCorrelationEngine(
            windowSize: 35,
            consecutiveLowCorrCountRequired: 2,
            toleranceDb: 0.0);

        var engine = new AudioCorrelationEngine(
            silenceDetector: silenceDetector,
            correlationEngine: correlationEngine,
            toleranceDb: 0.0,
            simpleMode: true);

        // Case 1: In simple mode, active client (-20 dBFS) and non-silent OBS (-30 dBFS)
        // Even with 0 correlation and 0 tolerance, it should NOT alert!
        var rnd = new Random(42);
        for (int i = 0; i < 40; i++)
        {
            engine.IngestObsAudioMeter(-30.0 + (rnd.NextDouble() * 10.0), -25.0, DateTime.UtcNow);
            engine.ProcessClientReading(-20.0 + (rnd.NextDouble() * 10.0));
        }
        Assert.IsFalse(engine.HasSoundIssue, "Simple mode should not alert when OBS audio is present, regardless of correlation");

        // Case 2: In simple mode, active client (-20 dBFS) and silent OBS (<-60 dBFS) for 3 consecutive readings -> triggers silence alert!
        for (int i = 0; i < 3; i++)
        {
            engine.IngestObsAudioMeter(-75.0, -70.0, DateTime.UtcNow);
            engine.ProcessClientReading(-20.0);
        }
        Assert.IsTrue(engine.HasSoundIssue, "Simple mode should alert when client is active and OBS is silent");
    }

    [TestMethod]
    public void AudioCorrelationEngine_ModeToggling_ResetsState()
    {
        var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 50);
        var correlationEngine = new PearsonCorrelationEngine(
            windowSize: 40,
            consecutiveLowCorrCountRequired: 5,
            toleranceDb: 0.0);

        var engine = new AudioCorrelationEngine(
            silenceDetector: silenceDetector,
            correlationEngine: correlationEngine,
            toleranceDb: 0.0,
            simpleMode: false);

        var rnd = new Random(456);
        // Trigger correlation mismatch alert in normal mode (OBS non-silent at -30 dBFS, client active with variance)
        for (int i = 0; i < 60; i++)
        {
            engine.IngestObsAudioMeter(-30.0 + (rnd.NextDouble() * 10.0), -25.0, DateTime.UtcNow);
            engine.ProcessClientReading(-10.0 + (rnd.NextDouble() * 15.0));
        }

        Assert.IsTrue(engine.HasSoundIssue, "Correlation alert should be active in normal mode");

        // Toggle to simple mode -> resets Pearson correlation state and clears alert
        engine.Configure(20.0, simpleMode: true);
        Assert.IsFalse(engine.HasSoundIssue, "Toggling to simple mode should reset correlation state and clear alert");

        // Toggle back to normal mode -> remains clear until new samples accumulate
        engine.Configure(20.0, simpleMode: false);
        Assert.IsFalse(engine.HasSoundIssue, "Toggling back to normal mode should maintain clean state until new mismatch");
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
    public void AudioCorrelationEngine_NotInActiveScene_SuppressesSoundIssueAlert()
    {
        var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 1);
        var engine = new AudioCorrelationEngine(silenceDetector: silenceDetector);

        // Feed reading that triggers silence issue (client active -20, obs silent -80)
        engine.ProcessClientReading(-20.0);
        Assert.IsTrue(engine.HasSoundIssue);

        // Not in active scene -> suppresses sound issue!
        engine.UpdateSuppressionStates(obsDeviceDisconnected: false, obsDeviceMuted: false, isNotInActiveScene: true);
        Assert.IsFalse(engine.HasSoundIssue, "Sound issue should be suppressed when capture source is not in active scene");

        // Back in active scene -> alert becomes visible again
        engine.UpdateSuppressionStates(obsDeviceDisconnected: false, obsDeviceMuted: false, isNotInActiveScene: false);
        Assert.IsTrue(engine.HasSoundIssue, "Sound issue should restore when source returns to active scene");
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

    [TestMethod]
    public void AudioCorrelationEngine_DiagnosticMusicScenarios()
    {
        // Scenario 1: Compressed music with low variance (-18 to -20 dBFS) and OBS silent (-100 dBFS)
        var engine1 = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: false);
        for (int p = 1; p <= 10; p++)
        {
            var readings = new float[10];
            for (int i = 0; i < 10; i++) readings[i] = -19.0f + ((i % 2 == 0) ? -0.5f : 0.5f);
            engine1.IngestObsAudioMeter(-100.0, -100.0, DateTime.UtcNow);
            engine1.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Readings = readings
            });
        }
        Assert.IsTrue(engine1.HasSoundIssue, "Scenario 1: Compressed music with -100 OBS should trigger sound issue");

        // Scenario 2: Music with quiet dip below -45 dBFS every 2 seconds, and OBS silent (-100 dBFS)
        var engine2 = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: true);
        for (int p = 1; p <= 10; p++)
        {
            var readings = new float[10];
            for (int i = 0; i < 10; i++)
            {
                // Reading 9 dips to -46 dBFS (quiet moment/pause)
                readings[i] = (i == 9) ? -46.0f : -20.0f;
            }
            engine2.IngestObsAudioMeter(-100.0, -100.0, DateTime.UtcNow);
            engine2.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Readings = readings
            });
        }
        Assert.IsTrue(engine2.HasSoundIssue, "Scenario 2: Music with quiet dips in simple mode should trigger sound issue");

        // Scenario 3: Music at -20 dBFS, OBS has noise floor at -55 dBFS, simple mode
        var engine3 = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: true);
        for (int p = 1; p <= 10; p++)
        {
            var readings = new float[10];
            for (int i = 0; i < 10; i++) readings[i] = -20.0f;
            engine3.IngestObsAudioMeter(-55.0, -50.0, DateTime.UtcNow);
            engine3.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Readings = readings
            });
        }
        Assert.IsTrue(engine3.HasSoundIssue, "Scenario 3: Music active with OBS noise floor -55dBFS in simple mode should trigger sound issue");

        // Scenario 4: Music at -20 dBFS, OBS has noise floor at -55 dBFS, normal mode with tolerance 20
        var engine4 = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: false);
        var rnd = new Random(123);
        for (int p = 1; p <= 10; p++)
        {
            var readings = new float[10];
            for (int i = 0; i < 10; i++) readings[i] = (float)(-25.0 + rnd.NextDouble() * 15.0);
            engine4.IngestObsAudioMeter(-55.0, -50.0, DateTime.UtcNow);
            engine4.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Readings = readings
            });
        }
        Assert.IsTrue(engine4.HasSoundIssue, "Scenario 4: Dynamic music with OBS noise -55dBFS in normal mode should trigger sound issue");

        // Scenario 5: Realistic music with speech/pauses, OBS silent (-100 dBFS), normal mode with ANY tolerance (0 to 40)
        for (int tol = 0; tol <= 40; tol += 10)
        {
            var engine5 = new AudioCorrelationEngine(toleranceDb: tol, simpleMode: false);
            for (int p = 1; p <= 10; p++)
            {
                var readings = new float[10];
                for (int i = 0; i < 10; i++)
                {
                    // Realistic music: some active beats (-20 to -30), some quieter parts (-40 to -48)
                    readings[i] = (i % 3 == 0) ? -46.0f : (float)(-25.0 + (i % 5) * 2.0);
                }
                engine5.IngestObsAudioMeter(-100.0, -100.0, DateTime.UtcNow);
                engine5.ProcessTelemetryPacket(new AudioTelemetryPacket
                {
                    SequenceNumber = (ulong)p,
                    TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Readings = readings
                });
            }
            Assert.IsTrue(engine5.HasSoundIssue, $"Scenario 5: Realistic music with dips, -100 OBS, normal mode tol={tol} should trigger sound issue");
        }

        // Scenario 6: Mastered music with low variance (variance ~2.5), OBS silent (-100 dBFS), normal mode
        var engine6 = new AudioCorrelationEngine(toleranceDb: 0, simpleMode: false);
        for (int p = 1; p <= 10; p++)
        {
            var readings = new float[10];
            for (int i = 0; i < 10; i++) readings[i] = (float)(-20.0 + (i % 2 == 0 ? 1.0 : -1.0)); // variance = 1.0 dB
            engine6.IngestObsAudioMeter(-100.0, -100.0, DateTime.UtcNow);
            engine6.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Readings = readings
            });
        }
        Assert.IsTrue(engine6.HasSoundIssue, "Scenario 6: Low variance music, -100 OBS, normal mode tol=0 should trigger sound issue");

        // Scenario 7: Low variance music, OBS noise floor at -55 dBFS, normal mode with tol=20
        var engine7 = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: false);
        for (int p = 1; p <= 10; p++)
        {
            var readings = new float[10];
            for (int i = 0; i < 10; i++) readings[i] = (float)(-20.0 + (i % 2 == 0 ? 1.0 : -1.0));
            engine7.IngestObsAudioMeter(-55.0, -50.0, DateTime.UtcNow);
            engine7.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Readings = readings
            });
        }
        Assert.IsTrue(engine7.HasSoundIssue, "Scenario 7: Low variance music, OBS noise floor -55dBFS, normal mode tol=20 should trigger sound issue");
    }
}
