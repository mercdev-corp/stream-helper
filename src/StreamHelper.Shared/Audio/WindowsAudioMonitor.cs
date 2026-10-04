using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StreamHelper.Shared.Common;

namespace StreamHelper.Shared.Audio;

[SupportedOSPlatform("windows")]
public sealed class WindowsAudioMonitor : IAudioMonitor, IAudioEndpointVolumeCallback, IMMNotificationClient
{
    private readonly object _lock = new();
    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _currentDevice;
    private IAudioEndpointVolume? _endpointVolume;
    private System.Threading.Timer? _probeTimer;

    private string? _targetDeviceId;
    private int _retryTimeoutSeconds = 5;
    private bool _isMuted;
    private bool _isConnected;
    private string? _currentDeviceName;
    private bool _disposed;

    public bool IsMuted
    {
        get { lock (_lock) return _isMuted; }
        private set
        {
            bool changed = false;
            lock (_lock)
            {
                if (_isMuted != value)
                {
                    _isMuted = value;
                    changed = true;
                }
            }
            if (changed)
            {
                AppLogger.Info($"[AudioMonitor] Mute state changed: isMuted={value} (Device: '{CurrentDeviceName ?? "None"}').");
                MuteChanged?.Invoke(value);
            }
        }
    }

    public bool IsConnected
    {
        get { lock (_lock) return _isConnected; }
        private set
        {
            bool changed = false;
            lock (_lock)
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    changed = true;
                }
            }
            if (changed)
            {
                AppLogger.Info($"[AudioMonitor] Connection state changed: isConnected={value} (Device: '{CurrentDeviceName ?? "None"}').");
                ConnectionChanged?.Invoke(value);
            }
        }
    }

    public string? CurrentDeviceId
    {
        get { lock (_lock) return _targetDeviceId; }
    }

    public string? CurrentDeviceName
    {
        get { lock (_lock) return _currentDeviceName; }
        private set { lock (_lock) _currentDeviceName = value; }
    }

    public event Action<bool>? MuteChanged;
    public event Action<bool>? ConnectionChanged;
    public event Action? DevicesChanged;

    public WindowsAudioMonitor()
    {
        InitializeEnumerator();
    }

    private void InitializeEnumerator()
    {
        try
        {
            var comType = Type.GetTypeFromCLSID(CoreAudioConstants.CLSID_MMDeviceEnumerator);
            if (comType != null)
            {
                _deviceEnumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(comType);
                _deviceEnumerator?.RegisterEndpointNotificationCallback(this);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize MMDeviceEnumerator: {ex.Message}");
        }
    }

    public IReadOnlyList<AudioDeviceInfo> GetActiveCaptureDevices()
    {
        lock (_lock)
        {
            var list = new List<AudioDeviceInfo>();
            if (_deviceEnumerator == null) return list;

            string? defaultConsoleId = null;
            if (_deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out var consoleDev) == 0 && consoleDev != null)
            {
                try
                {
                    consoleDev.GetId(out var cId);
                    defaultConsoleId = cId;
                }
                finally
                {
                    Marshal.ReleaseComObject(consoleDev);
                }
            }

            string? defaultCommunicationsId = null;
            if (_deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out var commsDev) == 0 && commsDev != null)
            {
                try
                {
                    commsDev.GetId(out var cmId);
                    defaultCommunicationsId = cmId;
                }
                finally
                {
                    Marshal.ReleaseComObject(commsDev);
                }
            }

            var primaryDefaultId = defaultConsoleId ?? defaultCommunicationsId;
            if (primaryDefaultId == null)
            {
                primaryDefaultId = GetDefaultCaptureDevice()?.Id;
            }

            int hr = _deviceEnumerator.EnumAudioEndpoints(EDataFlow.eCapture, CoreAudioConstants.DEVICE_STATE_ACTIVE, out var collection);
            if (hr != 0 || collection == null) return list;

            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) == 0 && device != null)
                {
                    try
                    {
                        device.GetId(out string id);
                        string name = GetFriendlyName(device) ?? id;
                        bool isDefConsole = string.Equals(id, defaultConsoleId, StringComparison.OrdinalIgnoreCase);
                        bool isDefComms = string.Equals(id, defaultCommunicationsId, StringComparison.OrdinalIgnoreCase);
                        bool isDef = string.Equals(id, primaryDefaultId, StringComparison.OrdinalIgnoreCase);
                        list.Add(new AudioDeviceInfo(id, name, isDef, isDefConsole, isDefComms));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }

            Marshal.ReleaseComObject(collection);
            return list;
        }
    }

    public IReadOnlyList<AudioDeviceInfo> GetActiveRenderDevices()
    {
        lock (_lock)
        {
            var list = new List<AudioDeviceInfo>();
            if (_deviceEnumerator == null) return list;

            string? defaultConsoleId = null;
            if (_deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var consoleDev) == 0 && consoleDev != null)
            {
                try
                {
                    consoleDev.GetId(out var cId);
                    defaultConsoleId = cId;
                }
                finally
                {
                    Marshal.ReleaseComObject(consoleDev);
                }
            }

            int hr = _deviceEnumerator.EnumAudioEndpoints(EDataFlow.eRender, CoreAudioConstants.DEVICE_STATE_ACTIVE, out var collection);
            if (hr != 0 || collection == null) return list;

            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) == 0 && device != null)
                {
                    try
                    {
                        device.GetId(out var id);
                        var name = GetFriendlyName(device) ?? "Unknown Output";
                        bool isDef = string.Equals(id, defaultConsoleId, StringComparison.OrdinalIgnoreCase);
                        list.Add(new AudioDeviceInfo(id, name, isDef, isDef, false));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }

            Marshal.ReleaseComObject(collection);
            return list;
        }
    }

    public static IReadOnlyList<AudioDeviceInfo> EnumerateRenderDevices()
    {
        var monitor = new WindowsAudioMonitor();
        try
        {
            return monitor.GetActiveRenderDevices();
        }
        finally
        {
            monitor.Dispose();
        }
    }

    public AudioDeviceInfo? GetDefaultCaptureDevice()
    {
        lock (_lock)
        {
            if (_deviceEnumerator == null) return null;

            bool isConsole = false;
            bool isComms = false;

            int hr = _deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out var device);
            if (hr == 0 && device != null)
            {
                isConsole = true;
            }
            else
            {
                hr = _deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out device);
                if (hr == 0 && device != null)
                {
                    isComms = true;
                }
            }

            if (hr != 0 || device == null)
            {
                hr = _deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eMultimedia, out device);
            }

            if (hr != 0 || device == null) return null;

            try
            {
                device.GetId(out string id);
                string name = GetFriendlyName(device) ?? id;
                return new AudioDeviceInfo(id, name, IsDefault: true, IsDefaultConsole: isConsole, IsDefaultCommunications: isComms);
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
    }

    private static string? GetFriendlyName(IMMDevice device)
    {
        int hr = device.OpenPropertyStore(CoreAudioConstants.STGM_READ, out var store);
        if (hr != 0 || store == null) return null;

        try
        {
            var key = CoreAudioConstants.PKEY_Device_FriendlyName;
            hr = store.GetValue(ref key, out var pv);
            if (hr == 0)
            {
                var name = pv.GetString();
                pv.Clear();
                return name;
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }

        return null;
    }

    public void StartMonitoring(string? targetDeviceId, int retryTimeoutSeconds = 5)
    {
        lock (_lock)
        {
            _targetDeviceId = targetDeviceId;
            _retryTimeoutSeconds = Math.Max(1, retryTimeoutSeconds);
        }

        AppLogger.Info($"[AudioMonitor] StartMonitoring: targetDeviceId='{targetDeviceId ?? "Default"}', retryTimeout={_retryTimeoutSeconds}s.");
        TryConnect();
        StartProbeTimer();
    }

    public void StopMonitoring()
    {
        AppLogger.Info($"[AudioMonitor] StopMonitoring called.");
        StopProbeTimer();
        UnbindCurrentDevice();
        IsConnected = false;
    }

    private bool TryConnect()
    {
        lock (_lock)
        {
            if (_disposed || _deviceEnumerator == null) return false;

            // If already bound and healthy, check state
            if (_currentDevice != null && _endpointVolume != null)
            {
                try
                {
                    _currentDevice.GetState(out uint state);
                    if (state == CoreAudioConstants.DEVICE_STATE_ACTIVE)
                    {
                        if (!string.IsNullOrEmpty(_targetDeviceId))
                        {
                            _currentDevice.GetId(out string currentId);
                            if (string.Equals(currentId, _targetDeviceId, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                        else
                        {
                            var def = GetDefaultCaptureDevice();
                            _currentDevice.GetId(out string currentId);
                            if (def != null && string.Equals(currentId, def.Id, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            UnbindCurrentDevice();

            IMMDevice? deviceToBind = null;
            string? deviceIdToUse = _targetDeviceId;

            // If target is null or empty, resolve default device
            if (string.IsNullOrEmpty(deviceIdToUse))
            {
                var def = GetDefaultCaptureDevice();
                if (def != null)
                {
                    deviceIdToUse = def.Id;
                }
            }

            if (!string.IsNullOrEmpty(deviceIdToUse))
            {
                try
                {
                    int hr = _deviceEnumerator.GetDevice(deviceIdToUse, out var dev);
                    if (hr == 0 && dev != null)
                    {
                        dev.GetState(out uint devState);
                        if (devState == CoreAudioConstants.DEVICE_STATE_ACTIVE)
                        {
                            deviceToBind = dev;
                        }
                        else
                        {
                            Marshal.ReleaseComObject(dev);
                        }
                    }
                }
                catch
                {
                    deviceToBind = null;
                }
            }

            if (deviceToBind == null)
            {
                AppLogger.Warn($"[AudioMonitor] Audio capture device not available or inactive (target: '{_targetDeviceId ?? "Default"}').");
                CurrentDeviceName = null;
                IsConnected = false;
                return false;
            }

            try
            {
                var iid = CoreAudioConstants.IID_IAudioEndpointVolume;
                int hr = deviceToBind.Activate(ref iid, CoreAudioConstants.CLSCTX_ALL, IntPtr.Zero, out var obj);
                if (hr == 0 && obj is IAudioEndpointVolume epv)
                {
                    _currentDevice = deviceToBind;
                    _endpointVolume = epv;
                    CurrentDeviceName = GetFriendlyName(deviceToBind);

                    epv.RegisterControlChangeNotify(this);
                    epv.GetMute(out bool muted);

                    _isMuted = muted;
                    _isConnected = true;

                    AppLogger.Info($"[AudioMonitor] Bound to capture device '{CurrentDeviceName}' (ID: {deviceIdToUse}), isMuted={muted}.");

                    // Notify state outside lock
                    Task.Run(() =>
                    {
                        ConnectionChanged?.Invoke(true);
                        MuteChanged?.Invoke(muted);
                    });

                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[AudioMonitor] Failed to activate endpoint volume on device '{deviceIdToUse}'", ex);
            }

            Marshal.ReleaseComObject(deviceToBind);
            IsConnected = false;
            return false;
        }
    }

    private void UnbindCurrentDevice()
    {
        lock (_lock)
        {
            if (_endpointVolume != null || _currentDevice != null)
            {
                AppLogger.Debug($"[AudioMonitor] Unbinding current audio device '{CurrentDeviceName}'.");
            }

            if (_endpointVolume != null)
            {
                try { _endpointVolume.UnregisterControlChangeNotify(this); } catch { }
                try { Marshal.ReleaseComObject(_endpointVolume); } catch { }
                _endpointVolume = null;
            }

            if (_currentDevice != null)
            {
                try { Marshal.ReleaseComObject(_currentDevice); } catch { }
                _currentDevice = null;
            }
        }
    }

    private void StartProbeTimer()
    {
        lock (_lock)
        {
            _probeTimer?.Dispose();
            _probeTimer = new System.Threading.Timer(OnProbeTimerTick, null,
                TimeSpan.FromSeconds(_retryTimeoutSeconds),
                TimeSpan.FromSeconds(_retryTimeoutSeconds));
        }
    }

    private void StopProbeTimer()
    {
        lock (_lock)
        {
            _probeTimer?.Dispose();
            _probeTimer = null;
        }
    }

    private void OnProbeTimerTick(object? state)
    {
        if (_disposed) return;

        bool connected;
        lock (_lock) { connected = _isConnected; }

        if (!connected)
        {
            TryConnect();
        }
    }

    // IAudioEndpointVolumeCallback
    int IAudioEndpointVolumeCallback.OnNotify(IntPtr pNotifyData)
    {
        if (pNotifyData == IntPtr.Zero) return 0;

        try
        {
            // AUDIO_VOLUME_NOTIFICATION_DATA layout:
            // offset 0 (16 bytes): GUID guidEventContext
            // offset 16 (4 bytes): BOOL bMuted
            bool muted = Marshal.ReadInt32(pNotifyData, 16) != 0;
            AppLogger.Debug($"[AudioMonitor] CoreAudio OnNotify received: bMuted={muted}");
            IsMuted = muted;
        }
        catch (Exception ex)
        {
            AppLogger.Error("[AudioMonitor] OnNotify error", ex);
        }

        return 0;
    }

    // IMMNotificationClient
    int IMMNotificationClient.OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState)
    {
        AppLogger.Info($"[AudioMonitor] IMMNotificationClient.OnDeviceStateChanged: devId={pwstrDeviceId}, newState=0x{dwNewState:X}");
        Task.Run(() =>
        {
            try
            {
                lock (_lock)
                {
                    bool isTarget = string.IsNullOrEmpty(_targetDeviceId) ||
                                    string.Equals(pwstrDeviceId, _targetDeviceId, StringComparison.OrdinalIgnoreCase);

                    if (isTarget)
                    {
                        if (dwNewState != CoreAudioConstants.DEVICE_STATE_ACTIVE)
                        {
                            UnbindCurrentDevice();
                            IsConnected = false;
                        }
                        else
                        {
                            TryConnect();
                        }
                    }
                }

                DevicesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AudioMonitor] OnDeviceStateChanged background task error", ex);
            }
        });

        return 0;
    }

    int IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId)
    {
        AppLogger.Info($"[AudioMonitor] IMMNotificationClient.OnDeviceAdded: devId={pwstrDeviceId}");
        Task.Run(() =>
        {
            try
            {
                lock (_lock)
                {
                    bool isTarget = string.IsNullOrEmpty(_targetDeviceId) ||
                                    string.Equals(pwstrDeviceId, _targetDeviceId, StringComparison.OrdinalIgnoreCase);

                    if (isTarget)
                    {
                        TryConnect();
                    }
                }

                DevicesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AudioMonitor] OnDeviceAdded background task error", ex);
            }
        });

        return 0;
    }

    int IMMNotificationClient.OnDeviceRemoved(string pwstrDeviceId)
    {
        AppLogger.Info($"[AudioMonitor] IMMNotificationClient.OnDeviceRemoved: devId={pwstrDeviceId}");
        Task.Run(() =>
        {
            try
            {
                lock (_lock)
                {
                    bool isTarget = string.IsNullOrEmpty(_targetDeviceId) ||
                                    string.Equals(pwstrDeviceId, _targetDeviceId, StringComparison.OrdinalIgnoreCase);

                    if (isTarget)
                    {
                        UnbindCurrentDevice();
                        IsConnected = false;
                    }
                }

                DevicesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AudioMonitor] OnDeviceRemoved background task error", ex);
            }
        });

        return 0;
    }

    int IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId)
    {
        if (flow == EDataFlow.eCapture && (role == ERole.eCommunications || role == ERole.eConsole))
        {
            AppLogger.Info($"[AudioMonitor] IMMNotificationClient.OnDefaultDeviceChanged: flow={flow}, role={role}, defId={pwstrDefaultDeviceId}");
            Task.Run(() =>
            {
                try
                {
                    lock (_lock)
                    {
                        if (string.IsNullOrEmpty(_targetDeviceId))
                        {
                            TryConnect();
                        }
                    }

                    DevicesChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    AppLogger.Error("[AudioMonitor] OnDefaultDeviceChanged background task error", ex);
                }
            });
        }

        return 0;
    }

    int IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        return 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        StopProbeTimer();
        UnbindCurrentDevice();

        if (_deviceEnumerator != null)
        {
            try { _deviceEnumerator.UnregisterEndpointNotificationCallback(this); } catch { }
            try { Marshal.ReleaseComObject(_deviceEnumerator); } catch { }
            _deviceEnumerator = null;
        }
    }
}
