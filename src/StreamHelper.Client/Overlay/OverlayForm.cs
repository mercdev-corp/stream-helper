using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using StreamHelper.Client.Config;
using StreamHelper.Shared.Common;
using StreamHelper.Shared.Protocol;
using StreamHelper.Shared.UI;

namespace StreamHelper.Client.Overlay;

public sealed class OverlayForm : Form
{
    private readonly ClientSettings _settings;
    private readonly OverlayAssetManager _assetManager;
    private readonly System.Windows.Forms.Timer _pulseTimer;
    private readonly Stopwatch _stopwatch = new();
    private readonly Win32Native.WinEventDelegate _winEventDelegate;
    private IntPtr _hWinEventHook = IntPtr.Zero;

    private bool _isWysiwygMode;
    private MicState _actualLiveState = MicState.Disconnected;
    private bool _isPaused;

    // Interactive Drag/Resize state for WYSIWYG mode
    private Point _dragStartCursor;
    private Point _dragStartLocation;
    private Size _dragStartSize;

    public bool IsWysiwygMode => _isWysiwygMode;
    public bool IsPulseTimerRunning => _pulseTimer.Enabled;
    public MicState ActualLiveState => _actualLiveState;
    internal IntPtr WinEventHookHandle => _hWinEventHook;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32Native.WS_EX_TOPMOST |
                          Win32Native.WS_EX_LAYERED |
                          Win32Native.WS_EX_NOACTIVATE |
                          Win32Native.WS_EX_TOOLWINDOW;

            if (!_isWysiwygMode)
            {
                cp.ExStyle |= Win32Native.WS_EX_TRANSPARENT;
            }
            return cp;
        }
    }

    public OverlayForm(ClientSettings settings, OverlayAssetManager assetManager)
    {
        _settings = settings;
        _assetManager = assetManager;
        _winEventDelegate = OnForegroundWindowChanged;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        DoubleBuffered = true;

        _pulseTimer = new System.Windows.Forms.Timer
        {
            Interval = 16 // ~60 FPS
        };
        _pulseTimer.Tick += OnPulseTimerTick;

        UpdateGeometryFromSettings();
        _assetManager.PreRenderAndCache(_settings.OverlayWidth, _settings.OverlayHeight);
    }

    public void UpdateGeometryFromSettings()
    {
        int x = _settings.OverlayCenterX - (_settings.OverlayWidth / 2);
        int y = _settings.OverlayCenterY - (_settings.OverlayHeight / 2);

        Location = new Point(x, y);
        Size = new Size(_settings.OverlayWidth, _settings.OverlayHeight);
    }

    private AlertFlags _actualLiveAlerts = AlertFlags.None;
    private AlertFlags _currentDisplayAlerts = AlertFlags.None;

    public AlertFlags ActualLiveAlerts => _actualLiveAlerts;

    public void SetLiveAlerts(AlertFlags alerts, bool isPaused)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetLiveAlerts(alerts, isPaused)));
            return;
        }

        bool alertChanged = (_actualLiveAlerts != alerts) || (_isPaused != isPaused);
        _actualLiveAlerts = alerts;
        _isPaused = isPaused;

        if (alertChanged)
        {
            AppLogger.Debug($"[Overlay] SetLiveAlerts: alerts={alerts}, isPaused={isPaused}.");
        }

        if (alerts.HasFlag(AlertFlags.MicMuted)) _actualLiveState = MicState.Muted;
        else if (alerts.HasFlag(AlertFlags.ServerDisconnected) || alerts.HasFlag(AlertFlags.MicDisconnected)) _actualLiveState = MicState.Disconnected;
        else if (isPaused) _actualLiveState = MicState.Paused;
        else _actualLiveState = MicState.Unmuted;

        if (!_isWysiwygMode)
        {
            ApplyLiveAlerts(alerts);
        }
    }

    public void SetLiveState(MicState state, bool isPaused)
    {
        var alerts = state switch
        {
            MicState.Muted => AlertFlags.MicMuted,
            MicState.Disconnected => AlertFlags.ServerDisconnected,
            _ => AlertFlags.None
        };
        SetLiveAlerts(alerts, isPaused);
    }

    private void ApplyLiveAlerts(AlertFlags alerts)
    {
        if (_isPaused || alerts == AlertFlags.None)
        {
            // Zero-CPU idle: completely stop animation timer and hide
            StopPulsingAndHide();
            return;
        }

        _currentDisplayAlerts = alerts;
        StartPulsing();
    }

    private void ApplyLiveState(MicState state)
    {
        var alerts = state switch
        {
            MicState.Muted => AlertFlags.MicMuted,
            MicState.Disconnected => AlertFlags.ServerDisconnected,
            _ => AlertFlags.None
        };
        ApplyLiveAlerts(alerts);
    }

    public void SetWysiwygMode(bool enabled)
    {
        if (_isWysiwygMode == enabled) return;
        _isWysiwygMode = enabled;

        AppLogger.Info($"[Overlay] WYSIWYG edit mode {(enabled ? "entered" : "exited")}.");

        int exStyle = Win32Native.GetWindowLong(Handle, Win32Native.GWL_EXSTYLE);
        if (enabled)
        {
            exStyle &= ~Win32Native.WS_EX_TRANSPARENT;
            Win32Native.SetWindowLong(Handle, Win32Native.GWL_EXSTYLE, exStyle);

            _currentDisplayAlerts = AlertFlags.MicMuted;
            StartPulsing();
        }
        else
        {
            exStyle |= Win32Native.WS_EX_TRANSPARENT;
            Win32Native.SetWindowLong(Handle, Win32Native.GWL_EXSTYLE, exStyle);

            // Re-render and cache assets on exit of settings WYSIWYG
            _assetManager.PreRenderAndCache(_settings.OverlayWidth, _settings.OverlayHeight);
            ApplyLiveAlerts(_actualLiveAlerts);
        }
    }

    public void UpdateAnimationSettings(int maxOpacity, double frequencySeconds)
    {
        UpdateAnimationSettings(maxOpacity, frequencySeconds, _settings.AnimationCycles);
    }

    public void UpdateAnimationSettings(int maxOpacity, double frequencySeconds, int animationCycles)
    {
        _settings.Opacity = Math.Clamp(maxOpacity, 0, 100);
        _settings.PulseFrequency = Math.Clamp(frequencySeconds, 0.1, 5.0);
        _settings.AnimationCycles = Math.Clamp(animationCycles, 1, 10);
    }

    public void ReassertTopmost()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(ReassertTopmost);
            }
            catch (ObjectDisposedException)
            {
            }
            return;
        }

        Win32Native.SetWindowPos(
            Handle,
            Win32Native.HWND_TOPMOST,
            0, 0, 0, 0,
            Win32Native.SWP_NOMOVE | Win32Native.SWP_NOSIZE | Win32Native.SWP_NOACTIVATE);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            ReassertTopmost();
        }
    }

    private void StartPulsing()
    {
        if (!Visible)
        {
            Visible = true;
        }
        ReassertTopmost();
        if (!_pulseTimer.Enabled)
        {
            AppLogger.Debug($"[Overlay] Pulsing started for display alerts: {_currentDisplayAlerts}.");
            _stopwatch.Restart();
            _pulseTimer.Start();
        }
        RenderFrame();
    }

    private void StopPulsingAndHide()
    {
        if (_pulseTimer.Enabled || Visible)
        {
            AppLogger.Debug($"[Overlay] Overlay animation stopped and hidden.");
        }
        _pulseTimer.Stop();
        _stopwatch.Stop();
        Visible = false;
    }

    public event Action<int>? OverlayResized;

    private enum DragMode
    {
        None,
        Move,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight
    }

    private DragMode _currentDragMode = DragMode.None;

    public void ApplyNewSize(int newSize)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ApplyNewSize(newSize)));
            return;
        }

        newSize = Math.Clamp(newSize, 32, 1024);
        if (newSize == Width && newSize == Height) return;

        int centerX = Left + (Width / 2);
        int centerY = Top + (Height / 2);

        int newLeft = centerX - (newSize / 2);
        int newTop = centerY - (newSize / 2);

        SetBounds(newLeft, newTop, newSize, newSize);

        _settings.OverlayWidth = newSize;
        _settings.OverlayHeight = newSize;
        _settings.OverlayCenterX = centerX;
        _settings.OverlayCenterY = centerY;
        _settings.Save();

        AppLogger.Info($"[Overlay] Applied new size: {newSize}x{newSize} (Center: {centerX}, {centerY}).");

        _assetManager.PreRenderAndCache(newSize, newSize);
        RenderFrame();

        OverlayResized?.Invoke(newSize);
    }

    private void OnPulseTimerTick(object? sender, EventArgs e)
    {
        if (IsHandleCreated && Visible)
        {
            var prevWindow = Win32Native.GetWindow(Handle, Win32Native.GW_HWNDPREV);
            if (prevWindow != IntPtr.Zero)
            {
                ReassertTopmost();
            }
        }

        RenderFrame();
    }

    public AlertFlags CurrentDisplayAlerts => _currentDisplayAlerts;

    public AlertFlags GetDisplayedAlertAt(double elapsedSeconds)
    {
        var activeList = AlertDisplayInfo.GetActiveAlertList(_currentDisplayAlerts);
        if (activeList.Count == 0) return AlertFlags.None;
        double period = Math.Max(0.1, _settings.PulseFrequency);
        int totalCycles = (int)(elapsedSeconds / period);
        int cyclesPerAlert = Math.Clamp(_settings.AnimationCycles, 1, 10);
        return activeList[(totalCycles / cyclesPerAlert) % activeList.Count];
    }

    private void RenderFrame()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        if (!_isWysiwygMode && (_isPaused || _currentDisplayAlerts == AlertFlags.None))
        {
            return;
        }

        Bitmap? bmp;
        if (_isWysiwygMode)
        {
            bmp = _assetManager.GetAlertBitmap(AlertFlags.MicMuted);
            if (bmp == null) return;

            using var frame = CreateWysiwygFrame(bmp);
            SetLayeredBitmap(frame, 255);
            return;
        }

        var activeList = AlertDisplayInfo.GetActiveAlertList(_currentDisplayAlerts);
        if (activeList.Count == 0)
        {
            return;
        }

        double elapsed = _stopwatch.Elapsed.TotalSeconds;
        double period = Math.Max(0.1, _settings.PulseFrequency);
        int totalCycles = (int)(elapsed / period);
        int cyclesPerAlert = Math.Clamp(_settings.AnimationCycles, 1, 10);
        var currentAlert = activeList[(totalCycles / cyclesPerAlert) % activeList.Count];

        bmp = _assetManager.GetAlertBitmap(currentAlert);
        if (bmp == null) return;

        double phase = (elapsed % period) / period;

        // Smooth sine-wave oscillating between 0.0 and 1.0
        double sine = 0.5 * (1.0 - Math.Cos(phase * 2.0 * Math.PI));
        double maxAlpha = Math.Clamp(_settings.Opacity / 100.0, 0.0, 1.0);
        byte alpha = (byte)Math.Clamp((int)(sine * maxAlpha * 255.0), 0, 255);

        SetLayeredBitmap(bmp, alpha);
    }

    private Bitmap CreateWysiwygFrame(Bitmap micBitmap)
    {
        var frame = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(frame);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // 1. Semi-transparent canvas background so the entire window is hit-testable by Windows
        using var canvasBrush = new SolidBrush(Color.FromArgb(45, 0, 120, 215));
        g.FillRectangle(canvasBrush, 0, 0, Width, Height);

        // 2. Draw microphone icon inside
        int pad = 8;
        int innerW = Math.Max(16, Width - (pad * 2));
        int innerH = Math.Max(16, Height - (pad * 2));
        g.DrawImage(micBitmap, new Rectangle(pad, pad, innerW, innerH), 0, 0, micBitmap.Width, micBitmap.Height, GraphicsUnit.Pixel);

        // 3. Crisp dashed bounding border
        using var borderPen = new Pen(Color.FromArgb(230, 0, 160, 255), 2f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(borderPen, 1, 1, Width - 2, Height - 2);

        // 4. Corner resize handles (10-14px white squares with blue borders)
        int handleSize = Math.Clamp(Width / 8, 8, 14);
        using var handleBrush = new SolidBrush(Color.White);
        using var handlePen = new Pen(Color.FromArgb(0, 120, 215), 1.5f);

        void DrawHandle(int hx, int hy)
        {
            g.FillRectangle(handleBrush, hx, hy, handleSize, handleSize);
            g.DrawRectangle(handlePen, hx, hy, handleSize, handleSize);
        }

        DrawHandle(0, 0); // Top-Left
        DrawHandle(Width - handleSize - 1, 0); // Top-Right
        DrawHandle(0, Height - handleSize - 1); // Bottom-Left
        DrawHandle(Width - handleSize - 1, Height - handleSize - 1); // Bottom-Right

        // 5. Diagonal grip ridges in bottom-right corner
        using var gripPen = new Pen(Color.FromArgb(0, 90, 190), 2f);
        int brX = Width - 2;
        int brY = Height - 2;
        g.DrawLine(gripPen, brX - 5, brY, brX, brY - 5);
        g.DrawLine(gripPen, brX - 10, brY, brX, brY - 10);
        g.DrawLine(gripPen, brX - 15, brY, brX, brY - 15);

        // 6. Size badge (e.g. "128×128")
        if (Width >= 64 && Height >= 48)
        {
            string badgeText = $"{Width}×{Height}";
            using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            var textSize = g.MeasureString(badgeText, font);
            int badgeW = (int)textSize.Width + 8;
            int badgeH = (int)textSize.Height + 4;
            int badgeX = (Width - badgeW) / 2;
            int badgeY = Height - badgeH - 4;

            using var badgeBg = new SolidBrush(Color.FromArgb(200, 20, 20, 20));
            g.FillRectangle(badgeBg, badgeX, badgeY, badgeW, badgeH);
            using var badgeBorder = new Pen(Color.FromArgb(120, 255, 255, 255), 1f);
            g.DrawRectangle(badgeBorder, badgeX, badgeY, badgeW, badgeH);
            using var textBrush = new SolidBrush(Color.White);
            g.DrawString(badgeText, font, textBrush, badgeX + 4, badgeY + 2);
        }

        return frame;
    }

    private void SetLayeredBitmap(Bitmap bitmap, byte alpha)
    {
        var screenDc = Win32Native.GetDC(IntPtr.Zero);
        var memDc = Win32Native.CreateCompatibleDC(screenDc);
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = Win32Native.SelectObject(memDc, hBitmap);

        try
        {
            var dstPoint = new Win32Native.POINT(Left, Top);
            var size = new Win32Native.SIZE(Width, Height);
            var srcPoint = new Win32Native.POINT(0, 0);

            var blend = new Win32Native.BLENDFUNCTION
            {
                BlendOp = Win32Native.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = alpha,
                AlphaFormat = Win32Native.AC_SRC_ALPHA
            };

            Win32Native.UpdateLayeredWindow(
                Handle,
                screenDc,
                ref dstPoint,
                ref size,
                memDc,
                ref srcPoint,
                0,
                ref blend,
                Win32Native.ULW_ALPHA);
        }
        finally
        {
            Win32Native.SelectObject(memDc, oldBitmap);
            Win32Native.DeleteObject(hBitmap);
            Win32Native.DeleteDC(memDc);
            Win32Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // --- WYSIWYG Drag & Resize Handling ---

    private DragMode GetDragModeAtPoint(int x, int y)
    {
        int cornerHitSize = Math.Clamp(Width / 4, 16, 28);

        bool isLeft = x <= cornerHitSize;
        bool isRight = x >= Width - cornerHitSize;
        bool isTop = y <= cornerHitSize;
        bool isBottom = y >= Height - cornerHitSize;

        if (isTop && isLeft) return DragMode.ResizeTopLeft;
        if (isTop && isRight) return DragMode.ResizeTopRight;
        if (isBottom && isLeft) return DragMode.ResizeBottomLeft;
        if (isBottom && isRight) return DragMode.ResizeBottomRight;

        return DragMode.Move;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_isWysiwygMode) return;

        int step = e.Delta > 0 ? 8 : -8;
        ApplyNewSize(Width + step);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_isWysiwygMode || e.Button != MouseButtons.Left) return;

        _currentDragMode = GetDragModeAtPoint(e.X, e.Y);
        _dragStartCursor = Cursor.Position;
        _dragStartLocation = Location;
        _dragStartSize = Size;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isWysiwygMode) return;

        if (_currentDragMode == DragMode.Move)
        {
            int deltaX = Cursor.Position.X - _dragStartCursor.X;
            int deltaY = Cursor.Position.Y - _dragStartCursor.Y;

            Location = new Point(_dragStartLocation.X + deltaX, _dragStartLocation.Y + deltaY);

            _settings.OverlayCenterX = Location.X + (Width / 2);
            _settings.OverlayCenterY = Location.Y + (Height / 2);
            _settings.Save();
            RenderFrame();
        }
        else if (_currentDragMode != DragMode.None)
        {
            int deltaX = Cursor.Position.X - _dragStartCursor.X;
            int deltaY = Cursor.Position.Y - _dragStartCursor.Y;

            int newWidth = _dragStartSize.Width;
            int newLeft = _dragStartLocation.X;
            int newTop = _dragStartLocation.Y;

            switch (_currentDragMode)
            {
                case DragMode.ResizeBottomRight:
                {
                    int delta = Math.Max(deltaX, deltaY);
                    newWidth = Math.Clamp(_dragStartSize.Width + delta, 32, 1024);
                    break;
                }
                case DragMode.ResizeBottomLeft:
                {
                    int delta = Math.Max(-deltaX, deltaY);
                    newWidth = Math.Clamp(_dragStartSize.Width + delta, 32, 1024);
                    newLeft = _dragStartLocation.X + (_dragStartSize.Width - newWidth);
                    break;
                }
                case DragMode.ResizeTopRight:
                {
                    int delta = Math.Max(deltaX, -deltaY);
                    newWidth = Math.Clamp(_dragStartSize.Width + delta, 32, 1024);
                    newTop = _dragStartLocation.Y + (_dragStartSize.Height - newWidth);
                    break;
                }
                case DragMode.ResizeTopLeft:
                {
                    int delta = Math.Max(-deltaX, -deltaY);
                    newWidth = Math.Clamp(_dragStartSize.Width + delta, 32, 1024);
                    newLeft = _dragStartLocation.X + (_dragStartSize.Width - newWidth);
                    newTop = _dragStartLocation.Y + (_dragStartSize.Height - newWidth);
                    break;
                }
            }

            SetBounds(newLeft, newTop, newWidth, newWidth);
            _settings.OverlayWidth = newWidth;
            _settings.OverlayHeight = newWidth;
            _settings.OverlayCenterX = newLeft + (newWidth / 2);
            _settings.OverlayCenterY = newTop + (newWidth / 2);
            _settings.Save();

            _assetManager.PreRenderAndCache(newWidth, newWidth);
            RenderFrame();

            OverlayResized?.Invoke(newWidth);
        }
        else
        {
            var mode = GetDragModeAtPoint(e.X, e.Y);
            Cursor = mode switch
            {
                DragMode.ResizeTopLeft or DragMode.ResizeBottomRight => Cursors.SizeNWSE,
                DragMode.ResizeTopRight or DragMode.ResizeBottomLeft => Cursors.SizeNESW,
                _ => Cursors.SizeAll
            };
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_isWysiwygMode) return;

        if (_currentDragMode != DragMode.None)
        {
            _currentDragMode = DragMode.None;
            Cursor = Cursors.Default;
            _settings.Save();
            AppLogger.Info($"[Overlay] Interaction complete: Center=({_settings.OverlayCenterX}, {_settings.OverlayCenterY}), Size={_settings.OverlayWidth}x{_settings.OverlayHeight}.");
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterForegroundHook();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterForegroundHook();
        base.OnHandleDestroyed(e);
    }

    private void RegisterForegroundHook()
    {
        if (_hWinEventHook == IntPtr.Zero)
        {
            _hWinEventHook = Win32Native.SetWinEventHook(
                Win32Native.EVENT_SYSTEM_FOREGROUND,
                Win32Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                Win32Native.WINEVENT_OUTOFCONTEXT | Win32Native.WINEVENT_SKIPOWNPROCESS);
        }
    }

    private void UnregisterForegroundHook()
    {
        if (_hWinEventHook != IntPtr.Zero)
        {
            Win32Native.UnhookWinEvent(_hWinEventHook);
            _hWinEventHook = IntPtr.Zero;
        }
    }

    private void OnForegroundWindowChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (IsDisposed || !IsHandleCreated || !Visible)
        {
            return;
        }

        ReassertTopmost();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pulseTimer.Dispose();
            _stopwatch.Stop();
        }
        UnregisterForegroundHook();
        base.Dispose(disposing);
    }
}
