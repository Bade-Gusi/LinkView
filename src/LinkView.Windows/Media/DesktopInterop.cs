using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Capture;
using WinRT;

namespace LinkView.Windows.Media;

internal static class DesktopInterop
{
    private static readonly Guid GraphicsCaptureItemInteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid GraphicsCaptureItemInterfaceIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid DxgiInterfaceAccessIid = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    public const uint MonitorDefaultToPrimary = 1;
    public const uint MonitorDefaultToNearest = 2;
    private const int DwmwaCloaked = 14;
    private const int GwlExstyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    public static bool IsCloaked(IntPtr hwnd)
    {
        return DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    public static bool IsToolWindow(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
        return (style & (WsExToolWindow | WsExNoActivate)) != 0;
    }

    public static unsafe GraphicsCaptureItem CreateItemForMonitor(IntPtr monitor)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = QueryInterface(factory.ThisPtr, GraphicsCaptureItemInteropIid);
        try
        {
            var vtbl = *(IntPtr**)interop;
            var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtbl[4];
            var iid = GraphicsCaptureItemInterfaceIid;
            IntPtr item;
            var hr = create(interop, monitor, &iid, &item);
            Marshal.ThrowExceptionForHR(hr);
            return GraphicsCaptureItem.FromAbi(item);
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    public static unsafe GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = QueryInterface(factory.ThisPtr, GraphicsCaptureItemInteropIid);
        try
        {
            var vtbl = *(IntPtr**)interop;
            var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtbl[3];
            var iid = GraphicsCaptureItemInterfaceIid;
            IntPtr item;
            var hr = create(interop, hwnd, &iid, &item);
            Marshal.ThrowExceptionForHR(hr);
            return GraphicsCaptureItem.FromAbi(item);
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    public static unsafe IntPtr GetDxgiInterface(IntPtr dxgiAccess, Guid iid)
    {
        var vtbl = *(IntPtr**)dxgiAccess;
        var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtbl[3];
        IntPtr result;
        var hr = getInterface(dxgiAccess, &iid, &result);
        Marshal.ThrowExceptionForHR(hr);
        return result;
    }

    public static IntPtr QueryInterface(IntPtr unknown, Guid iid)
    {
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, ref iid, out var ptr));
        return ptr;
    }

    public static IntPtr GetSurfaceNativePointer(object surface)
    {
        return ((IWinRTObject)surface).NativeObject.ThisPtr;
    }

    public static IntPtr GetDxgiAccessForSurface(object surface)
    {
        var abi = GetSurfaceNativePointer(surface);
        return QueryInterface(abi, DxgiInterfaceAccessIid);
    }
}
