using System.Runtime.InteropServices;
using StreamHelper.Shared.Audio;

namespace StreamHelper.Tests;

[TestClass]
public sealed class AudioMonitorTests
{

    [TestMethod]
    public void WindowsAudioMonitor_Instantiation_SucceedsOnWindows()
    {
        using var monitor = new WindowsAudioMonitor();
        Assert.IsNotNull(monitor);

        var devices = monitor.GetActiveCaptureDevices();
        Assert.IsNotNull(devices);

        var defaultDevice = monitor.GetDefaultCaptureDevice();
        if (defaultDevice != null)
        {
            Assert.IsNotNull(defaultDevice.Id);
            Assert.IsNotNull(defaultDevice.Name);
        }
    }

    [TestMethod]
    public void WindowsAudioMonitor_TargetNonExistentDevice_ReportsDisconnected()
    {
        using var monitor = new WindowsAudioMonitor();
        
        var connectionEvents = new List<bool>();
        monitor.ConnectionChanged += connected => connectionEvents.Add(connected);

        monitor.StartMonitoring("non-existent-device-guid-xyz", retryTimeoutSeconds: 1);

        Assert.IsFalse(monitor.IsConnected);
    }

    [TestMethod]
    public void WindowsAudioMonitor_OnNotify_FiresMuteChangedEvent()
    {
        using var monitor = new WindowsAudioMonitor();
        bool? receivedMuteState = null;
        monitor.MuteChanged += muted => receivedMuteState = muted;

        var callback = (IAudioEndpointVolumeCallback)monitor;

        var data = new AUDIO_VOLUME_NOTIFICATION_DATA
        {
            guidEventContext = Guid.NewGuid(),
            bMuted = true,
            fMasterVolume = 0.8f,
            nChannels = 1
        };

        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<AUDIO_VOLUME_NOTIFICATION_DATA>());
        try
        {
            Marshal.StructureToPtr(data, ptr, false);
            int hr = callback.OnNotify(ptr);

            Assert.AreEqual(0, hr);
            Assert.IsTrue(monitor.IsMuted);
            Assert.IsTrue(receivedMuteState.HasValue && receivedMuteState.Value);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [TestMethod]
    public void WindowsAudioMonitor_DeviceRemoval_TriggersDisconnectedState()
    {
        using var monitor = new WindowsAudioMonitor();
        monitor.StartMonitoring("test-device-id", retryTimeoutSeconds: 1);

        var notifyClient = (IMMNotificationClient)monitor;
        int hr = notifyClient.OnDeviceRemoved("test-device-id");

        Assert.AreEqual(0, hr);
        Assert.IsFalse(monitor.IsConnected);
    }

    [TestMethod]
    public void AudioDeviceInfo_Properties_SetCorrectly()
    {
        var info = new AudioDeviceInfo("id-123", "Test Mic", true);
        Assert.AreEqual("id-123", info.Id);
        Assert.AreEqual("Test Mic", info.Name);
        Assert.IsTrue(info.IsDefault);
    }

    [TestMethod]
    public async Task WindowsAudioMonitor_DefaultDeviceChanged_DoesNotDeadlockAndTriggersDevicesChanged()
    {
        using var monitor = new WindowsAudioMonitor();
        monitor.StartMonitoring(targetDeviceId: null, retryTimeoutSeconds: 1);

        bool devicesChangedFired = false;
        monitor.DevicesChanged += () => devicesChangedFired = true;

        var notifyClient = (IMMNotificationClient)monitor;
        int hr = notifyClient.OnDefaultDeviceChanged(EDataFlow.eCapture, ERole.eCommunications, "new-default-device-id");

        Assert.AreEqual(0, hr);

        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < timeout && !devicesChangedFired)
        {
            await Task.Delay(25);
        }

        Assert.IsTrue(devicesChangedFired);
    }

    [TestMethod]
    public void AudioDeviceInfo_ExtendedProperties_SetCorrectly()
    {
        var info = new AudioDeviceInfo("id-123", "Test Mic", IsDefault: true, IsDefaultConsole: true, IsDefaultCommunications: false);
        Assert.AreEqual("id-123", info.Id);
        Assert.AreEqual("Test Mic", info.Name);
        Assert.IsTrue(info.IsDefault);
        Assert.IsTrue(info.IsDefaultConsole);
        Assert.IsFalse(info.IsDefaultCommunications);

        var commsInfo = new AudioDeviceInfo("id-456", "Comms Mic", IsDefault: false, IsDefaultConsole: false, IsDefaultCommunications: true);
        Assert.IsFalse(commsInfo.IsDefault);
        Assert.IsFalse(commsInfo.IsDefaultConsole);
        Assert.IsTrue(commsInfo.IsDefaultCommunications);
    }

    [TestMethod]
    public async Task WindowsAudioMonitor_DefaultDeviceChanged_ConsoleRole_DoesNotDeadlockAndTriggersDevicesChanged()
    {
        using var monitor = new WindowsAudioMonitor();
        monitor.StartMonitoring(targetDeviceId: null, retryTimeoutSeconds: 1);

        bool devicesChangedFired = false;
        monitor.DevicesChanged += () => devicesChangedFired = true;

        var notifyClient = (IMMNotificationClient)monitor;
        int hr = notifyClient.OnDefaultDeviceChanged(EDataFlow.eCapture, ERole.eConsole, "new-console-default-device-id");

        Assert.AreEqual(0, hr);

        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < timeout && !devicesChangedFired)
        {
            await Task.Delay(25);
        }

        Assert.IsTrue(devicesChangedFired);
    }
}
