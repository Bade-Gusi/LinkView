using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace LinkView.CaptureProbe;

/// <summary>
/// Windows.Graphics.Capture 最小验证：捕获主显示器，缩放到目标尺寸，读取真实桌面像素。
/// </summary>
internal static class Program
{
    private const int TargetWidth = 1280;
    private const int TargetHeight = 720;

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private static readonly Guid GraphicsCaptureItemInteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid GraphicsCaptureItemInterfaceIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid DxgiInterfaceAccessIid = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    /// <summary>
    /// 直接调用 COM vtable。CsWinRT 的激活工厂与 WinRT 对象不是经典 COM RCW，
    /// 依赖 CLR 的 ComImport 转换在 .NET 10 上并不可靠，因此显式做 QueryInterface + vtable 调用。
    /// </summary>
    private static unsafe IntPtr CreateCaptureItemForMonitor(IntPtr interopFactory, IntPtr monitor)
    {
        var vtbl = *(IntPtr**)interopFactory;
        var createForMonitor = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtbl[4];
        var iid = GraphicsCaptureItemInterfaceIid;
        IntPtr result;
        var hr = createForMonitor(interopFactory, monitor, &iid, &result);
        Marshal.ThrowExceptionForHR(hr);
        return result;
    }

    private static unsafe IntPtr GetDxgiInterface(IntPtr dxgiAccess, Guid iid)
    {
        var vtbl = *(IntPtr**)dxgiAccess;
        var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtbl[3];
        IntPtr result;
        var hr = getInterface(dxgiAccess, &iid, &result);
        Marshal.ThrowExceptionForHR(hr);
        return result;
    }

    private static IntPtr QueryInterface(IntPtr unknown, Guid iid)
    {
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, ref iid, out var ptr));
        return ptr;
    }

    private static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("LinkView Windows.Graphics.Capture probe");

        using var device = D3D11.D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0]);

        Console.WriteLine("D3D11 device created: " + device.FeatureLevel);
        var context = device.ImmediateContext;

        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectablePtr));
        var winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectablePtr);

        var activationFactory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interopFactory = QueryInterface(activationFactory.ThisPtr, GraphicsCaptureItemInteropIid);
        IntPtr itemPtr;
        try
        {
            var monitor = MonitorFromPoint(new Point { X = 0, Y = 0 }, 1 /* MONITOR_DEFAULTTOPRIMARY */);
            Console.WriteLine($"Primary monitor handle: 0x{monitor:X}");
            itemPtr = CreateCaptureItemForMonitor(interopFactory, monitor);
        }
        finally
        {
            Marshal.Release(interopFactory);
        }

        if (itemPtr == IntPtr.Zero)
        {
            Console.Error.WriteLine("CreateForMonitor returned null.");
            return 2;
        }

        var item = GraphicsCaptureItem.FromAbi(itemPtr);
        Console.WriteLine($"Capture item: {item.DisplayName} size={item.Size.Width}x{item.Size.Height}");

        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            winrtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            new Windows.Graphics.SizeInt32(TargetWidth, TargetHeight));

        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;

        var staging = device.CreateTexture2D(new Texture2DDescription
        {
            Width = TargetWidth,
            Height = TargetHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        });

        var frameCount = 0;
        var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameTimes = new List<double>();
        var sw = Stopwatch.StartNew();
        double lastMs = 0;
        byte[]? firstFrame = null;
        var lockObj = new object();

        pool.FrameArrived += (_, _) =>
        {
            using var frame = pool.TryGetNextFrame();
            if (frame is null) return;

            try
            {
                var surfaceAbi = ((IWinRTObject)frame.Surface).NativeObject.ThisPtr;
                var accessPtr = QueryInterface(surfaceAbi, DxgiInterfaceAccessIid);
                IntPtr texturePtr;
                try
                {
                    texturePtr = GetDxgiInterface(accessPtr, typeof(ID3D11Texture2D).GUID);
                }
                finally
                {
                    Marshal.Release(accessPtr);
                }

                using var texture = new ID3D11Texture2D(texturePtr);

                context.CopyResource(staging, texture);
                var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var buffer = new byte[TargetWidth * TargetHeight * 4];
                    unsafe
                    {
                        var src = (byte*)mapped.DataPointer;
                        for (var y = 0; y < TargetHeight; y++)
                        {
                            var dst = buffer.AsSpan(y * TargetWidth * 4, TargetWidth * 4);
                            new ReadOnlySpan<byte>(src + y * (int)mapped.RowPitch, TargetWidth * 4).CopyTo(dst);
                        }
                    }

                    lock (lockObj)
                    {
                        firstFrame ??= buffer;
                        frameCount++;
                        var nowMs = sw.Elapsed.TotalMilliseconds;
                        frameTimes.Add(nowMs - lastMs);
                        lastMs = nowMs;
                        if (frameCount >= 90) stop.TrySetResult(true);
                    }
                }
                finally
                {
                    context.Unmap(staging, 0);
                }
            }
            catch (Exception ex)
            {
                stop.TrySetException(ex);
            }
        };

        session.StartCapture();
        Console.WriteLine("Capture started; waiting for 90 frames...");

        try
        {
            await stop.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Capture failed: " + ex.Message);
            return 3;
        }

        session.Dispose();
        pool.Dispose();

        if (frameCount > 1)
        {
            frameTimes.RemoveAt(0);
            frameTimes.Sort();
            var avg = frameTimes.Average();
            var p95 = frameTimes[(int)(frameTimes.Count * 0.95)];
            Console.WriteLine($"frames={frameCount} avgInterval={avg:F2}ms p95={p95:F2}ms effectiveFps={1000 / avg:F1}");
        }

        if (firstFrame is not null)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "capture-probe.ppm");
            WritePpm(path, firstFrame, TargetWidth, TargetHeight);
            Console.WriteLine("Saved " + path);

            // 简单校验：画面不应该是全黑或全白。
            long sum = 0;
            for (var i = 0; i < firstFrame.Length; i += 4)
            {
                sum += firstFrame[i] + firstFrame[i + 1] + firstFrame[i + 2];
            }

            var avgBrightness = sum / (double)(TargetWidth * TargetHeight * 3);
            Console.WriteLine($"avgBrightness={avgBrightness:F1}");
            if (avgBrightness is < 0.5 or > 254.5)
            {
                Console.Error.WriteLine("Captured image is uniform; capture may be a blank surface.");
                return 4;
            }
        }

        Console.WriteLine("RESULT: PASS");
        return 0;
    }

    private static void WritePpm(string path, byte[] bgra, int width, int height)
    {
        using var fs = File.Create(path);
        using var writer = new StreamWriter(fs, Encoding.ASCII, 1024, leaveOpen: true);
        writer.Write($"P6\n{width} {height}\n255\n");
        writer.Flush();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                fs.WriteByte(bgra[i + 2]);
                fs.WriteByte(bgra[i + 1]);
                fs.WriteByte(bgra[i + 0]);
            }
        }
    }
}
