using System.Diagnostics;
using System.Text;
using LinkView.Core.Settings;
using Microsoft.Extensions.Logging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace LinkView.Windows.Media;

/// <summary>
/// 基于 Windows.Graphics.Capture 的屏幕采集。帧在专用 WGC 线程上到达，
/// FrameAvailable 的订阅者必须快速处理（编码管线会直接编码），不要在这里做 UI 阻塞操作。
/// </summary>
public sealed class ScreenCaptureService : IDisposable
{
    private readonly ILogger<ScreenCaptureService> _logger;
    private readonly object _sync = new();
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDirect3DDevice? _winrtDevice;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private ID3D11Texture2D? _staging;
    private byte[]? _frameBuffer;
    private int _targetWidth;
    private int _targetHeight;
    private int _sourceWidth;
    private int _sourceHeight;
    private bool _disposed;

    public ScreenCaptureService(ILogger<ScreenCaptureService> logger)
    {
        _logger = logger;
    }

    public event EventHandler<CapturedFrame>? FrameAvailable;
    public event EventHandler<string>? SourceLost;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsCapturing { get; private set; }
    public bool IsPaused { get; set; }
    public CaptureSource? ActiveSource { get; private set; }
    public int OutputWidth => _targetWidth;
    public int OutputHeight => _targetHeight;
    public long FramesCaptured => Interlocked.Read(ref _framesCaptured);
    public long FramesDropped => Interlocked.Read(ref _framesDropped);
    public double LastCopyMs { get; private set; }

    private long _framesCaptured;
    private long _framesDropped;

    public static IReadOnlyList<CaptureSource> EnumerateSources()
    {
        var sources = new List<CaptureSource>();
        DesktopInterop.EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new DesktopInterop.MonitorInfoEx
            {
                Size = System.Runtime.InteropServices.Marshal.SizeOf<DesktopInterop.MonitorInfoEx>(),
                DeviceName = string.Empty
            };

            if (DesktopInterop.GetMonitorInfo(monitor, ref info))
            {
                var primary = (info.Flags & 1) != 0;
                const string prefix = @"\\.\DISPLAY";
                var friendly = info.DeviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? "显示器 " + info.DeviceName[prefix.Length..]
                    : info.DeviceName;
                var width = info.Monitor.Width;
                var height = info.Monitor.Height;
                sources.Add(new CaptureSource(
                    CaptureSourceKind.Monitor,
                    $"monitor:0x{monitor.ToInt64():X}",
                    $"{friendly}{(primary ? " · 主显示器" : string.Empty)}",
                    $"{width} × {height}",
                    monitor,
                    width,
                    height,
                    primary));
            }

            return true;
        }, 0);

        var ownPid = (uint)Environment.ProcessId;
        DesktopInterop.EnumWindows((hwnd, _) =>
        {
            if (!DesktopInterop.IsWindowVisible(hwnd) || DesktopInterop.IsIconic(hwnd) ||
                DesktopInterop.IsCloaked(hwnd) || DesktopInterop.IsToolWindow(hwnd))
            {
                return true;
            }

            var length = DesktopInterop.GetWindowTextLength(hwnd);
            if (length <= 0 || length > 512)
            {
                return true;
            }

            var title = new StringBuilder(length + 1);
            DesktopInterop.GetWindowText(hwnd, title, title.Capacity);
            if (title.Length == 0)
            {
                return true;
            }

            DesktopInterop.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == ownPid)
            {
                return true;
            }

            if (!DesktopInterop.GetWindowRect(hwnd, out var rect) || rect.Width < 160 || rect.Height < 120)
            {
                return true;
            }

            var className = new StringBuilder(256);
            DesktopInterop.GetClassName(hwnd, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")
            {
                return true;
            }

            string processName = string.Empty;
            try
            {
                processName = Process.GetProcessById((int)pid).ProcessName;
            }
            catch (ArgumentException)
            {
            }

            sources.Add(new CaptureSource(
                CaptureSourceKind.Window,
                $"window:0x{hwnd.ToInt64():X}",
                title.ToString(),
                string.IsNullOrEmpty(processName) ? $"{rect.Width} × {rect.Height}" : $"{processName} · {rect.Width} × {rect.Height}",
                hwnd,
                rect.Width,
                rect.Height,
                false));
            return true;
        }, 0);

        return sources
            .OrderBy(s => s.Kind)
            .ThenByDescending(s => s.IsPrimary)
            .ThenBy(s => s.DisplayName)
            .ToArray();
    }

    public async Task StartAsync(CaptureSource source, VideoSettings video, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await StopAsync().ConfigureAwait(false);

        EnsureDevice();

        GraphicsCaptureItem item;
        try
        {
            item = source.Kind == CaptureSourceKind.Monitor
                ? DesktopInterop.CreateItemForMonitor(source.Handle)
                : DesktopInterop.CreateItemForWindow(source.Handle);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("capture_item_failed {Source} {Error}", source.Id, ex.Message);
            throw new CaptureException("CAPTURE_ITEM_FAILED", "无法创建采集项，窗口或显示器可能已关闭。", ex.Message);
        }

        var srcWidth = item.Size.Width > 0 ? item.Size.Width : Math.Max(2, source.Width);
        var srcHeight = item.Size.Height > 0 ? item.Size.Height : Math.Max(2, source.Height);
        var (targetWidth, targetHeight) = FitTarget(srcWidth, srcHeight, video.MaxWidth, video.MaxHeight);

        try
        {
            _item = item;
            _sourceWidth = srcWidth;
            _sourceHeight = srcHeight;
            _targetWidth = targetWidth;
            _targetHeight = targetHeight;
            _frameBuffer = new byte[targetWidth * targetHeight * 4];

            _staging = _device!.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)targetWidth,
                Height = (uint)targetHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None
            });

            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                new global::Windows.Graphics.SizeInt32(targetWidth, targetHeight));
            _session = _pool.CreateCaptureSession(item);
            _session.IsCursorCaptureEnabled = true;
            _pool.FrameArrived += OnFrameArrived;
            item.Closed += OnItemClosed;

            _session.StartCapture();
            ActiveSource = source;
            IsCapturing = true;
            IsPaused = false;
            Interlocked.Exchange(ref _framesCaptured, 0);
            Interlocked.Exchange(ref _framesDropped, 0);
            _logger.LogInformation(
                "capture_started {Kind} {Source} src={SrcW}x{SrcH} out={OutW}x{OutH}",
                source.Kind, source.Id, srcWidth, srcHeight, targetWidth, targetHeight);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "capture_start_failed {Source}", source.Id);
            await StopAsync().ConfigureAwait(false);
            throw new CaptureException("CAPTURE_START_FAILED", "开始屏幕采集失败。", ex.Message);
        }

        await Task.CompletedTask;
    }

    private void EnsureDevice()
    {
        if (_device is not null)
        {
            return;
        }

        _device = D3D11.D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0]);
        _context = _device.ImmediateContext;

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        var hr = DesktopInterop.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable);
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(hr);
        _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
    }

    private unsafe void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (_disposed || !IsCapturing)
        {
            return;
        }

        var start = Stopwatch.GetTimestamp();
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            if (IsPaused)
            {
                return;
            }

            var accessPtr = DesktopInterop.GetDxgiAccessForSurface(frame.Surface);
            IntPtr texturePtr;
            try
            {
                texturePtr = DesktopInterop.GetDxgiInterface(accessPtr, typeof(ID3D11Texture2D).GUID);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.Release(accessPtr);
            }

            using var texture = new ID3D11Texture2D(texturePtr);
            lock (_sync)
            {
                if (_staging is null || _frameBuffer is null || _disposed)
                {
                    return;
                }

                _context!.CopyResource(_staging, texture);
                var mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var buffer = _frameBuffer;
                    var rowBytes = _targetWidth * 4;
                    var src = (byte*)mapped.DataPointer;
                    for (var y = 0; y < _targetHeight; y++)
                    {
                        new ReadOnlySpan<byte>(src + y * (int)mapped.RowPitch, rowBytes)
                            .CopyTo(buffer.AsSpan(y * rowBytes, rowBytes));
                    }
                }
                finally
                {
                    _context.Unmap(_staging, 0);
                }

                var frameCopy = new CapturedFrame(
                    _frameBuffer,
                    _targetWidth,
                    _targetHeight,
                    Stopwatch.GetTimestamp(),
                    _sourceWidth,
                    _sourceHeight);

                Interlocked.Increment(ref _framesCaptured);
                if (IsPaused)
                {
                    return;
                }

                FrameAvailable?.Invoke(this, frameCopy);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _framesDropped);
            _logger.LogWarning("capture_frame_failed {Error}", ex.Message);
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            LastCopyMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        _logger.LogInformation("capture_source_closed");
        SourceLost?.Invoke(this, "采集源已关闭或不可用");
    }

    private static (int Width, int Height) FitTarget(int sourceWidth, int sourceHeight, int maxWidth, int maxHeight)
    {
        var scale = Math.Min(1.0, Math.Min(
            maxWidth / (double)Math.Max(1, sourceWidth),
            maxHeight / (double)Math.Max(1, sourceHeight)));
        var width = Math.Max(2, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(2, (int)Math.Round(sourceHeight * scale));
        // VP8 使用 4:2:0 色度采样，宽高必须是偶数。
        width &= ~1;
        height &= ~1;
        return (width, height);
    }

    public async Task StopAsync()
    {
        IsCapturing = false;
        GraphicsCaptureSession? session;
        Direct3D11CaptureFramePool? pool;
        GraphicsCaptureItem? item;
        ID3D11Texture2D? staging;
        lock (_sync)
        {
            session = _session;
            pool = _pool;
            item = _item;
            staging = _staging;
            _session = null;
            _pool = null;
            _item = null;
            _staging = null;
            _frameBuffer = null;
        }

        if (pool is not null)
        {
            pool.FrameArrived -= OnFrameArrived;
        }

        if (item is not null)
        {
            item.Closed -= OnItemClosed;
        }

        session?.Dispose();
        pool?.Dispose();
        staging?.Dispose();
        ActiveSource = null;
        if (session is not null)
        {
            _logger.LogInformation("capture_stopped");
        }

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
        }

        _staging?.Dispose();
        _staging = null;
        _pool?.Dispose();
        _pool = null;
        _session?.Dispose();
        _session = null;
        _winrtDevice = null;
        _context?.Dispose();
        _context = null;
        _device?.Dispose();
        _device = null;
    }
}
