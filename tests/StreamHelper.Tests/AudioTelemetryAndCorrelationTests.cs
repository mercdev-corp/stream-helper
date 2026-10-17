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
    public void ConditionalSilenceDetector_DefaultThresholds_DetectsAboveMinus60dBInOneSecond()
    {
        var detector = new ConditionalSilenceDetector();
        Assert.AreEqual(-60.0, detector.ClientActiveThresholdDbfs, 0.001);
        Assert.AreEqual(-60.0, detector.ObsSilenceThresholdDbfs, 0.001);
        Assert.AreEqual(10, detector.ConsecutiveReadingsRequired);

        // Low-volume scene: client active at -52.0 dBFS, OBS also capturing at -52.0 dBFS
        // Since -52.0 >= -60.0 dBFS, it should not trigger silence alert despite client being active
        for (int i = 0; i < 20; i++)
        {
            Assert.IsFalse(detector.ProcessReading(-52.0, -52.0));
        }
        Assert.IsFalse(detector.IsAlertActive);

        // True silence in OBS during quiet footsteps (-52.0 dBFS > -60.0 dBFS, OBS at -65.0 dBFS < -60.0 dBFS)
        // Triggers on the 10th reading (1.0 second)
        for (int i = 0; i < 9; i++)
        {
            Assert.IsFalse(detector.ProcessReading(-52.0, -65.0));
        }
        Assert.IsTrue(detector.ProcessReading(-52.0, -65.0));
        Assert.IsTrue(detector.IsAlertActive);
    }

    [TestMethod]
    public void ConditionalSilenceDetector_ObsCapturesAudioWhileClientQuiet_ClearsAlertImmediately()
    {
        var detector = new ConditionalSilenceDetector(clientActiveThresholdDbfs: -60.0, obsSilenceThresholdDbfs: -60.0, consecutiveReadingsRequired: 10);

        // Trigger silence alert: 10 readings of active client (-20 dBFS) and silent OBS (-75 dBFS)
        for (int i = 0; i < 10; i++)
        {
            detector.ProcessReading(-20.0, -75.0);
        }
        Assert.IsTrue(detector.IsAlertActive);
        Assert.IsTrue(detector.IsActivelyConfirmingIssue);

        // Post-fight quiet client (-65 dBFS) while OBS captures audio (-36 dBFS >= -60 dBFS)
        // Must clear alert and reset immediately on reading 1 without requiring decay readings
        bool alertStillActive = detector.ProcessReading(-65.0, -36.0);
        Assert.IsFalse(alertStillActive, "Alert should clear immediately when OBS captures sound even during quiet client audio");
        Assert.IsFalse(detector.IsAlertActive);
        Assert.IsFalse(detector.IsActivelyConfirmingIssue);
    }

    [TestMethod]
    public void ConditionalSilenceDetector_IsActivelyConfirmingIssue_QuietReadingsDecayByTwoAndSingleSpikeDoesNotRelatch()
    {
        var detector = new ConditionalSilenceDetector(clientActiveThresholdDbfs: -60.0, obsSilenceThresholdDbfs: -60.0, consecutiveReadingsRequired: 10);

        // 10 readings -> triggers alert and sets IsActivelyConfirmingIssue
        for (int i = 0; i < 10; i++)
        {
            detector.ProcessReading(-48.0, -75.0);
        }
        Assert.IsTrue(detector.IsAlertActive);
        Assert.IsTrue(detector.IsActivelyConfirmingIssue);

        // Quiet reading (<= -60.0 dBFS, e.g. -65 dBFS, OBS -75 dBFS) decays count by 2 (10 -> 8)
        detector.ProcessReading(-65.0, -75.0);
        Assert.IsTrue(detector.IsAlertActive, "Alert should remain latched during initial decay");
        Assert.IsFalse(detector.IsActivelyConfirmingIssue, "Quiet reading must not report active issue confirmation");

        // Single active reading spike after quiet reading (-48 dBFS, OBS -75 dBFS) increments 8 -> 9 (< 10)
        detector.ProcessReading(-48.0, -75.0);
        Assert.IsTrue(detector.IsAlertActive);
        Assert.IsFalse(detector.IsActivelyConfirmingIssue, "Single active reading spike after quiet period must not re-latch active confirmation");

        // Second consecutive active reading (9 -> 10) restores full threshold and re-confirms active issue
        detector.ProcessReading(-48.0, -75.0);
        Assert.IsTrue(detector.IsActivelyConfirmingIssue, "Continuous active audio after brief micro-gap should re-confirm once threshold is reached");
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

        // Scenario 3: Music at -20 dBFS, OBS has noise floor at -65 dBFS, simple mode
        var engine3 = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: true);
        for (int p = 1; p <= 10; p++)
        {
            var readings = new float[10];
            for (int i = 0; i < 10; i++) readings[i] = -20.0f;
            engine3.IngestObsAudioMeter(-65.0, -60.0, DateTime.UtcNow);
            engine3.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Readings = readings
            });
        }
        Assert.IsTrue(engine3.HasSoundIssue, "Scenario 3: Music active with OBS noise floor -65dBFS in simple mode should trigger sound issue");

        // Scenario 4: Music at -20 dBFS, OBS has noise floor at -65 dBFS, normal mode with tolerance 20
        var engine4 = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: false);
        var rnd = new Random(123);
        for (int p = 1; p <= 10; p++)
        {
            var readings = new float[10];
            for (int i = 0; i < 10; i++) readings[i] = (float)(-25.0 + rnd.NextDouble() * 15.0);
            engine4.IngestObsAudioMeter(-65.0, -60.0, DateTime.UtcNow);
            engine4.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Readings = readings
            });
        }
        Assert.IsTrue(engine4.HasSoundIssue, "Scenario 4: Dynamic music with OBS noise -65dBFS in normal mode should trigger sound issue");

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

        // Scenario 7: Low variance music, OBS noise floor at -65 dBFS, normal mode with tol=20
        var engine7 = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: false);
        for (int p = 1; p <= 10; p++)
        {
            var readings = new float[10];
            for (int i = 0; i < 10; i++) readings[i] = (float)(-20.0 + (i % 2 == 0 ? 1.0 : -1.0));
            engine7.IngestObsAudioMeter(-65.0, -60.0, DateTime.UtcNow);
            engine7.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Readings = readings
            });
        }
        Assert.IsTrue(engine7.HasSoundIssue, "Scenario 7: Low variance music, OBS noise floor -65dBFS, normal mode tol=20 should trigger sound issue");
    }

    [TestMethod]
    [DataRow(500)]
    [DataRow(1000)]
    [DataRow(1500)]
    public void AudioCorrelationEngine_LagCompensation_DelaysBetween500msAnd1500ms_DualPc_SuppressesFalseSilenceAlerts(int delayMs)
    {
        // Lag compensation window configured to 2.0s
        var engine = new AudioCorrelationEngine(toleranceDb: 10, simpleMode: true, lagWindowSeconds: 2.0);
        var baseUtc = DateTime.UtcNow;
        long baseTimeMs = new DateTimeOffset(baseUtc).ToUnixTimeMilliseconds();

        // At time T, OBS has silence (-100 dBFS); delayed active audio (-25 dBFS) arrives at T + delayMs
        for (int step = 0; step < 60; step++)
        {
            engine.IngestObsAudioMeter(-100.0, -100.0, baseUtc.AddMilliseconds(step * 100));
            engine.IngestObsAudioMeter(-25.0, -20.0, baseUtc.AddMilliseconds(step * 100 + delayMs));
        }

        // Process 40 client telemetry packets (4 seconds of continuous active audio at -25.0 dBFS)
        for (int p = 1; p <= 40; p++)
        {
            var readings = new float[] { -25.0f };
            long packetTimestamp = baseTimeMs + (p * 100);

            engine.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = packetTimestamp,
                Readings = readings
            });

            Assert.IsFalse(engine.HasSoundIssue, $"Should not trigger sound issue at packet {p} with delay {delayMs}ms");
        }
    }

    [TestMethod]
    [DataRow(500)]
    [DataRow(1000)]
    [DataRow(1500)]
    public void AudioCorrelationEngine_LagCompensation_DelaysBetween500msAnd1500ms_SinglePc_SuppressesFalseSilenceAlerts(int delayMs)
    {
        // Single-PC direct reading feed with lag compensation window 2.0s
        var engine = new AudioCorrelationEngine(toleranceDb: 10, simpleMode: true, lagWindowSeconds: 2.0);
        var baseUtc = DateTime.UtcNow;
        long baseTimeMs = new DateTimeOffset(baseUtc).ToUnixTimeMilliseconds();

        // At time T, OBS has silence (-100 dBFS); delayed active audio arrives at T + delayMs
        for (int step = 0; step < 60; step++)
        {
            engine.IngestObsAudioMeter(-100.0, -100.0, baseUtc.AddMilliseconds(step * 100));
            engine.IngestObsAudioMeter(-25.0, -20.0, baseUtc.AddMilliseconds(step * 100 + delayMs));
        }

        // Process 40 client readings (4 seconds)
        for (int step = 1; step <= 40; step++)
        {
            long sampleTime = baseTimeMs + (step * 100);
            engine.ProcessClientReading(-25.0, sampleTime);
            Assert.IsFalse(engine.HasSoundIssue, $"Should not trigger sound issue at step {step} with delay {delayMs}ms");
        }
    }

    [TestMethod]
    public void AudioCorrelationEngine_WithoutLagCompensation_DelayedObsAudio_TriggersSilenceAlert()
    {
        // When lag compensation window is 0.0s (disabled), delayed OBS audio does not match client time T
        var engine = new AudioCorrelationEngine(toleranceDb: 10, simpleMode: true, lagWindowSeconds: 0.0);
        var baseUtc = DateTime.UtcNow;
        long baseTimeMs = new DateTimeOffset(baseUtc).ToUnixTimeMilliseconds();

        // At time T, OBS is silent (-100 dBFS); delayed active audio arrives at T + 1000ms
        for (int step = 0; step < 60; step++)
        {
            engine.IngestObsAudioMeter(-100.0, -100.0, baseUtc.AddMilliseconds(step * 100));
            engine.IngestObsAudioMeter(-25.0, -20.0, baseUtc.AddMilliseconds(step * 100 + 1000));
        }

        // Process 40 client readings (4 seconds)
        for (int step = 1; step <= 40; step++)
        {
            long sampleTime = baseTimeMs + (step * 100);
            engine.ProcessClientReading(-25.0, sampleTime);
        }

        // Without lag window, OBS at time T was silent (-100 dBFS), so silence detector triggered
        Assert.IsTrue(engine.HasSoundIssue, "Without lag compensation, delayed OBS audio should trigger silence alert");
    }

    [TestMethod]
    public void AudioCorrelationEngine_LowVolumeScene_ClientActiveVsObsAttenuated_NoFalseAlerts()
    {
        // Low-volume scene: client active at -46.0 dBFS (above -48.0 dBFS threshold),
        // OBS attenuated at -50.0 dBFS (above -60.0 dBFS silence threshold)
        // Simple mode test:
        var simpleEngine = new AudioCorrelationEngine(toleranceDb: 10, simpleMode: true, lagWindowSeconds: 1.0);
        var now = DateTime.UtcNow;
        long baseMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        for (int step = 0; step < 40; step++)
        {
            simpleEngine.IngestObsAudioMeter(-50.0, -45.0, now.AddMilliseconds(step * 100));
            simpleEngine.ProcessClientReading(-46.0, baseMs + (step * 100));
        }

        Assert.IsFalse(simpleEngine.HasSoundIssue, "Low-volume scene (-46 dBFS client vs -50 dBFS OBS) should not trigger silence alert in simple mode");

        // Normal mode test with toleranceDb = 10 (difference is 4 dB <= 10 dB):
        var normalEngine = new AudioCorrelationEngine(toleranceDb: 10, simpleMode: false, lagWindowSeconds: 1.0);
        for (int p = 1; p <= 40; p++)
        {
            normalEngine.IngestObsAudioMeter(-50.0, -45.0, DateTime.UtcNow);
            normalEngine.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Readings = new float[] { -46.0f }
            });
        }

        Assert.IsFalse(normalEngine.HasSoundIssue, "Low-volume scene (-46 dBFS client vs -50 dBFS OBS) should not trigger alert in normal mode");
    }

    [TestMethod]
    public void AudioCorrelationEngine_RetryTimeout_PropertyUpdatesProperly()
    {
        using var defaultEngine = new AudioCorrelationEngine();
        Assert.AreEqual(5, defaultEngine.RetryTimeoutSeconds);

        using var customEngine = new AudioCorrelationEngine(retryTimeoutSeconds: 12);
        Assert.AreEqual(12, customEngine.RetryTimeoutSeconds);

        customEngine.SetRetryTimeout(25);
        Assert.AreEqual(25, customEngine.RetryTimeoutSeconds);

        // Clamp checks
        customEngine.SetRetryTimeout(-5);
        Assert.AreEqual(1, customEngine.RetryTimeoutSeconds);

        customEngine.SetRetryTimeout(999);
        Assert.AreEqual(300, customEngine.RetryTimeoutSeconds);

        // Configure overload checks
        customEngine.Configure(15.0, simpleMode: true, lagWindowSeconds: 2.0, retryTimeoutSeconds: 8);
        Assert.AreEqual(8, customEngine.RetryTimeoutSeconds);
        Assert.AreEqual(15.0, customEngine.ToleranceDb);
        Assert.IsTrue(customEngine.SimpleDetection);
        Assert.AreEqual(2.0, customEngine.LagWindowSeconds);
    }

    [TestMethod]
    public async Task AudioCorrelationEngine_SoundIssue_AutoExpiresAfterTimeoutDuringSilence()
    {
        var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 1);
        using var engine = new AudioCorrelationEngine(silenceDetector: silenceDetector, retryTimeoutSeconds: 1);

        bool? lastEventVal = null;
        engine.SoundIssueChanged += val => lastEventVal = val;

        // Feed reading that triggers sound issue
        engine.ProcessClientReading(-20.0);
        Assert.IsTrue(engine.HasSoundIssue);
        Assert.IsTrue(lastEventVal == true);
        Assert.IsNotNull(engine.LastAudioIssueTime);

        // Without any further packets or audio, wait for retry timeout (1s) to elapse
        await Task.Delay(1300);

        Assert.IsFalse(engine.HasSoundIssue, "Sound issue should auto-expire after retry timeout when unconfirmed during silence");
        Assert.IsTrue(lastEventVal == false, "SoundIssueChanged event should fire with false on expiration");
    }

    [TestMethod]
    public async Task AudioCorrelationEngine_SoundIssue_AutoExpiresAfterTimeoutDuringSilence_DualPcPacket()
    {
        var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 30);
        using var engine = new AudioCorrelationEngine(silenceDetector: silenceDetector, retryTimeoutSeconds: 1);

        // Send 3 packets (30 active readings) to trigger alert
        for (ulong p = 1; p <= 3; p++)
        {
            engine.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = p,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Readings = new float[] { -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f, -20f }
            });
        }

        Assert.IsTrue(engine.HasSoundIssue, "Sound issue should trigger after 30 active readings with silent OBS");
        Assert.IsNotNull(engine.LastAudioIssueTime);

        // Silence ensues: no further telemetry packets arrive (e.g. WASAPI loopback produces no packets during silence)
        await Task.Delay(1300);

        Assert.IsFalse(engine.HasSoundIssue, "Dual-PC sound issue should auto-expire after retry timeout when packets cease");
    }

    [TestMethod]
    public async Task AudioCorrelationEngine_RepeatedIssueDetections_RefreshTimestampAndSustainAlert()
    {
        var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 1);
        using var engine = new AudioCorrelationEngine(silenceDetector: silenceDetector, retryTimeoutSeconds: 1);

        // Initial trigger
        engine.ProcessClientReading(-20.0);
        Assert.IsTrue(engine.HasSoundIssue);
        DateTime? initialTime = engine.LastAudioIssueTime;
        Assert.IsNotNull(initialTime);

        // Repeat detections every 400ms across 1.6 seconds (exceeding initial 1.0s timeout)
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(400);
            engine.ProcessClientReading(-20.0);
            Assert.IsTrue(engine.HasSoundIssue, $"Alert should remain sustained at iteration {i}");
            Assert.IsTrue(engine.LastAudioIssueTime >= initialTime, "LastAudioIssueTime should be refreshed");
            initialTime = engine.LastAudioIssueTime;
        }

        // Now stop feeding readings (source silence)
        await Task.Delay(1300);
        Assert.IsFalse(engine.HasSoundIssue, "Alert should expire and clear after feeding stops and timeout elapses");
    }

    [TestMethod]
    public void AudioCorrelationEngine_SoundIssue_ClearedImmediatelyWhenObsAudioDetectedBeforeTimeout()
    {
        var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 1);
        using var engine = new AudioCorrelationEngine(silenceDetector: silenceDetector, retryTimeoutSeconds: 10);

        bool? lastEventVal = null;
        engine.SoundIssueChanged += val => lastEventVal = val;

        // Trigger sound issue
        engine.ProcessClientReading(-20.0);
        Assert.IsTrue(engine.HasSoundIssue);
        Assert.IsTrue(lastEventVal == true);

        // Immediately OBS captures matching valid audio (-20 dBFS)
        var now = DateTime.UtcNow;
        engine.IngestObsAudioMeter(-20.0, -15.0, now);
        engine.ProcessClientReading(-20.0);

        Assert.IsFalse(engine.HasSoundIssue, "Sound issue should clear immediately when OBS audio is detected before retry timeout");
        Assert.IsTrue(lastEventVal == false, "SoundIssueChanged event should fire immediately with false");
        Assert.IsNull(engine.LastAudioIssueTime);
    }

    [TestMethod]
    public void AudioCorrelationEngine_SoundIssue_ClearedImmediatelyOnSuppressionBeforeTimeout()
    {
        var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 1);
        using var engine = new AudioCorrelationEngine(silenceDetector: silenceDetector, retryTimeoutSeconds: 10);

        // Trigger sound issue
        engine.ProcessClientReading(-20.0);
        Assert.IsTrue(engine.HasSoundIssue);

        // Mute OBS device -> suppresses sound issue immediately
        engine.UpdateSuppressionStates(obsDeviceDisconnected: false, obsDeviceMuted: true);
        Assert.IsFalse(engine.HasSoundIssue, "Sound issue should clear immediately when suppression activates");

        // Unmute OBS device within timeout -> alert restores
        engine.UpdateSuppressionStates(obsDeviceDisconnected: false, obsDeviceMuted: false);
        Assert.IsTrue(engine.HasSoundIssue, "Sound issue should restore upon unmuting within retry timeout");
    }

    [TestMethod]
    public async Task AudioCorrelationEngine_RuntimeAdjustmentOfRetryTimeout_AppliesImmediately()
    {
        var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 1);
        using var engine = new AudioCorrelationEngine(silenceDetector: silenceDetector, retryTimeoutSeconds: 10);

        // Trigger sound issue with 10-second timeout
        engine.ProcessClientReading(-20.0);
        Assert.IsTrue(engine.HasSoundIssue);

        // Wait 1.1s (well below 10s timeout, alert is still active)
        await Task.Delay(1100);
        Assert.IsTrue(engine.HasSoundIssue, "Alert should still be active under 10s timeout");

        // User reduces timeout in settings to 1s -> should expire immediately since >1.1s has elapsed
        engine.SetRetryTimeout(1);
        Assert.AreEqual(1, engine.RetryTimeoutSeconds);

        Assert.IsFalse(engine.HasSoundIssue, "Alert should immediately clear upon adjusting timeout to a duration already exceeded");
        Assert.IsNull(engine.LastAudioIssueTime);
    }

    [TestMethod]
    public void AudioCorrelationEngine_EscapeFromTarkov_CombatAndPostCombatLooting_DoesNotFalseAlarm_FullMode_DualPc()
    {
        // Full mode with default tolerance 20 dB, lag window 1.0s, and simulated 2.5s clock skew
        var engine = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: false, lagWindowSeconds: 1.0);
        long clientClockOffsetMs = 2500;
        var baseUtc = DateTime.UtcNow;
        long serverBaseMs = new DateTimeOffset(baseUtc).ToUnixTimeMilliseconds();
        var rnd = new Random(789);

        // Phase 1: Moving through bushes (+20 dB gain offset: PC -34 dBFS, OBS -14 dBFS)
        for (int p = 1; p <= 5; p++)
        {
            var readings = new float[10];
            for (int i = 0; i < 10; i++)
            {
                readings[i] = (float)(-34.0 + (rnd.NextDouble() * 3.0 - 1.5));
                long meterTimeMs = serverBaseMs + ((p - 1) * 1000) + (i * 100);
                engine.IngestObsAudioMeter(readings[i] + 20.0, readings[i] + 25.0, DateTimeOffset.FromUnixTimeMilliseconds(meterTimeMs).UtcDateTime);
            }

            long clientPacketTs = serverBaseMs + (p * 1000) - clientClockOffsetMs;
            engine.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = clientPacketTs,
                Readings = readings
            });

            Assert.IsFalse(engine.HasSoundIssue, $"Phase 1 (Bushes): Packet {p} should not trigger sound issue");
        }

        // Phase 2: Loud gunfire burst with OBS Limiter filter clamping at -8.4 dBFS
        // Client: -14 dBFS with high variance; OBS: -8.4 dBFS (limiter ceiling)
        for (int p = 6; p <= 12; p++)
        {
            var readings = new float[10];
            for (int i = 0; i < 10; i++)
            {
                readings[i] = (float)(-14.0 + (rnd.NextDouble() * 5.0 - 2.5));
                long meterTimeMs = serverBaseMs + ((p - 1) * 1000) + (i * 100);
                // OBS limiter clamps all gunfire peaks to -8.4 dBFS
                engine.IngestObsAudioMeter(-8.4, -8.0, DateTimeOffset.FromUnixTimeMilliseconds(meterTimeMs).UtcDateTime);
            }

            long clientPacketTs = serverBaseMs + (p * 1000) - clientClockOffsetMs;
            engine.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = clientPacketTs,
                Readings = readings
            });

            Assert.IsFalse(engine.HasSoundIssue, $"Phase 2 (Gunfire Limiter): Packet {p} should not trigger sound issue");
        }

        // Phase 3: Post-combat quiet: Footsteps at -48 dBFS and looting UI at -56 dBFS
        // Client audio drops below active threshold, OBS captures at -36 dBFS (due to +12..+20dB gain)
        for (int p = 13; p <= 18; p++)
        {
            var readings = new float[10];
            for (int i = 0; i < 10; i++)
            {
                readings[i] = (i % 2 == 0) ? -48.0f : -56.0f;
                long meterTimeMs = serverBaseMs + ((p - 1) * 1000) + (i * 100);
                engine.IngestObsAudioMeter(-36.0, -32.0, DateTimeOffset.FromUnixTimeMilliseconds(meterTimeMs).UtcDateTime);
            }

            long clientPacketTs = serverBaseMs + (p * 1000) - clientClockOffsetMs;
            engine.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = (ulong)p,
                TimestampUnixMs = clientPacketTs,
                Readings = readings
            });

            Assert.IsFalse(engine.HasSoundIssue, $"Phase 3 (Post-combat quiet): Packet {p} should not trigger sound issue");
        }
    }

    [TestMethod]
    public void AudioCorrelationEngine_EscapeFromTarkov_CombatAndPostCombatLooting_DoesNotFalseAlarm_SimpleMode_SinglePc()
    {
        // Simple mode direct Single-PC readings with lag window 1.0s
        var engine = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: true, lagWindowSeconds: 1.0);
        var baseUtc = DateTime.UtcNow;
        var rnd = new Random(101);

        // Sequence: UI (-56 PC / -36 OBS), Footsteps (-48 PC / -36 OBS), Bushes (-34 PC / -14 OBS), Gunshots (-14 PC / -8.4 OBS limiter)
        for (int i = 0; i < 60; i++)
        {
            double pcRms;
            double obsRms;

            if (i < 15)
            {
                // UI & Footsteps
                pcRms = (i % 2 == 0) ? -48.0 : -56.0;
                obsRms = -36.0;
            }
            else if (i < 30)
            {
                // Bushes
                pcRms = -34.0 + (rnd.NextDouble() * 2.0 - 1.0);
                obsRms = -14.0;
            }
            else
            {
                // Gunshots clamped by limiter
                pcRms = -14.0 + (rnd.NextDouble() * 4.0 - 2.0);
                obsRms = -8.4;
            }

            var time = baseUtc.AddMilliseconds(i * 100);
            engine.IngestObsAudioMeter(obsRms, obsRms + 3.0, time);
            engine.ProcessClientReading(pcRms, new DateTimeOffset(time).ToUnixTimeMilliseconds());

            Assert.IsFalse(engine.HasSoundIssue, $"Step {i} should not trigger sound issue in simple mode");
        }
    }

    [TestMethod]
    public void AudioCorrelationEngine_EscapeFromTarkov_TrueCaptureFailure_TriggersAndRecoversImmediately()
    {
        // Full mode: Gunfire occurs on PC (-14 dBFS), but OBS audio capture drops completely silent (-100 dBFS)
        var engine = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: false, lagWindowSeconds: 1.0);
        var baseUtc = DateTime.UtcNow;
        long baseMs = new DateTimeOffset(baseUtc).ToUnixTimeMilliseconds();

        // 3 seconds (30 readings) of active gunfire with silent OBS
        for (int i = 0; i < 30; i++)
        {
            var time = baseUtc.AddMilliseconds(i * 100);
            long timeMs = baseMs + (i * 100);
            engine.IngestObsAudioMeter(-100.0, -100.0, time);
            engine.ProcessClientReading(-14.0, timeMs);
        }

        Assert.IsTrue(engine.HasSoundIssue, "Sound issue should trigger after 30 readings of active gunfire and silent OBS");

        // Now OBS audio recovers, capturing footsteps/UI (-36 dBFS) while PC is quiet (-48 dBFS)
        var recoverTime = baseUtc.AddMilliseconds(3100);
        long recoverTimeMs = baseMs + 3100;
        engine.IngestObsAudioMeter(-36.0, -30.0, recoverTime);
        engine.ProcessClientReading(-48.0, recoverTimeMs);

        Assert.IsFalse(engine.HasSoundIssue, "Sound issue should immediately clear when OBS captures audio, even during quiet client audio");
    }

    [TestMethod]
    public void AudioCorrelationEngine_DualPc_CrossMachineClockSkew_1500msTo3000ms_AlignsCorrectly()
    {
        // Test cross-PC clock skew ranging from 1500ms to 3000ms
        foreach (int skewMs in new[] { 1500, 2000, 3000, -1500 })
        {
            var engine = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: false, lagWindowSeconds: 1.0);
            var baseUtc = DateTime.UtcNow;
            long serverBaseMs = new DateTimeOffset(baseUtc).ToUnixTimeMilliseconds();
            var rnd = new Random(42);

            for (int p = 1; p <= 6; p++)
            {
                var readings = new float[10];
                for (int i = 0; i < 10; i++)
                {
                    readings[i] = (float)(-25.0 + rnd.NextDouble() * 10.0);
                    long meterTimeMs = serverBaseMs + ((p - 1) * 1000) + (i * 100);
                    engine.IngestObsAudioMeter(readings[i] - 2.0, readings[i], DateTimeOffset.FromUnixTimeMilliseconds(meterTimeMs).UtcDateTime);
                }

                // Packet timestamp has the clock skew relative to server clock
                long packetTs = serverBaseMs + (p * 1000) - skewMs;
                engine.ProcessTelemetryPacket(new AudioTelemetryPacket
                {
                    SequenceNumber = (ulong)p,
                    TimestampUnixMs = packetTs,
                    Readings = readings
                });

                Assert.IsFalse(engine.HasSoundIssue, $"Clock skew {skewMs}ms: Packet {p} should not trigger sound issue");
            }
        }
    }

    [TestMethod]
    public void AudioCorrelationEngine_FootstepsLowerThanMinus30dB_AboveMinus60dB_NoObsCapture_DetectsAfterOneSecond_SinglePc_BothModes()
    {
        foreach (bool simpleMode in new[] { false, true })
        {
            foreach (int toleranceDb in new[] { 20, 30 })
            {
                var engine = new AudioCorrelationEngine(toleranceDb: toleranceDb, simpleMode: simpleMode, lagWindowSeconds: 1.0);
                var baseUtc = DateTime.UtcNow;
                long baseMs = new DateTimeOffset(baseUtc).ToUnixTimeMilliseconds();

                // Running footsteps on Gaming PC at -48 dBFS to -56 dBFS (well below -30 dBFS, but above -60 dBFS)
                // OBS captures nothing (-100 dBFS)
                for (int i = 0; i < 9; i++)
                {
                    var time = baseUtc.AddMilliseconds(i * 100);
                    long timeMs = baseMs + (i * 100);
                    double pcFootstepsDb = -48.0 - (i % 4) * 2.0; // -48, -50, -52, -54 dBFS
                    engine.IngestObsAudioMeter(-100.0, -100.0, time);
                    engine.ProcessClientReading(pcFootstepsDb, timeMs);
                    Assert.IsFalse(engine.HasSoundIssue, $"SimpleMode={simpleMode}, Tolerance={toleranceDb}: Should not trigger before 1.0s (reading {i + 1})");
                }

                // 10th reading (1.0 second continuous footsteps with no OBS capture) -> MUST trigger!
                var triggerTime = baseUtc.AddMilliseconds(900);
                engine.IngestObsAudioMeter(-100.0, -100.0, triggerTime);
                engine.ProcessClientReading(-52.0, baseMs + 900);
                Assert.IsTrue(engine.HasSoundIssue, $"SimpleMode={simpleMode}, Tolerance={toleranceDb}: Footsteps at -48..-54 dBFS with no OBS capture must trigger after 1.0s (10 readings)");
            }
        }
    }

    [TestMethod]
    public void AudioCorrelationEngine_FootstepsLowerThanMinus30dB_AboveMinus60dB_NoObsCapture_DetectsInOneSecondPacket_DualPc_BothModes()
    {
        foreach (bool simpleMode in new[] { false, true })
        {
            var engine = new AudioCorrelationEngine(toleranceDb: 30, simpleMode: simpleMode, lagWindowSeconds: 1.0);
            var baseUtc = DateTime.UtcNow;
            long serverBaseMs = new DateTimeOffset(baseUtc).ToUnixTimeMilliseconds();

            // 1 second UDP packet (10 readings at 100ms) of running footsteps (-48 to -55 dBFS) while OBS is silent (-75 dBFS)
            var readings = new float[10];
            for (int i = 0; i < 10; i++)
            {
                readings[i] = -48.0f - (i % 4) * 2.0f; // -48, -50, -52, -54 dBFS
                long meterTimeMs = serverBaseMs + (i * 100);
                engine.IngestObsAudioMeter(-75.0, -72.0, DateTimeOffset.FromUnixTimeMilliseconds(meterTimeMs).UtcDateTime);
            }

            engine.ProcessTelemetryPacket(new AudioTelemetryPacket
            {
                SequenceNumber = 1,
                TimestampUnixMs = serverBaseMs + 1000,
                Readings = readings
            });

            Assert.IsTrue(engine.HasSoundIssue, $"SimpleMode={simpleMode}: Dual-PC 1.0s packet of footsteps (-48..-54 dBFS) with silent OBS must trigger sound issue");
        }
    }

    [TestMethod]
    public void AudioCorrelationEngine_SoundsBelowMinus60dB_DoNotTriggerFalseSoundIssue()
    {
        foreach (bool simpleMode in new[] { false, true })
        {
            var engine = new AudioCorrelationEngine(toleranceDb: 20, simpleMode: simpleMode, lagWindowSeconds: 1.0);
            var baseUtc = DateTime.UtcNow;
            long baseMs = new DateTimeOffset(baseUtc).ToUnixTimeMilliseconds();

            // Sounds at or below -60 dBFS (-60.0 to -75.0 dBFS) for 5 seconds with silent OBS (-100 dBFS)
            for (int i = 0; i < 50; i++)
            {
                var time = baseUtc.AddMilliseconds(i * 100);
                long timeMs = baseMs + (i * 100);
                double quietPcDb = -60.0 - (i % 5) * 3.0;
                engine.IngestObsAudioMeter(-100.0, -100.0, time);
                engine.ProcessClientReading(quietPcDb, timeMs);
                Assert.IsFalse(engine.HasSoundIssue, $"SimpleMode={simpleMode}: Client audio <= -60 dBFS must not trigger sound issue at step {i}");
            }
        }
    }
}
