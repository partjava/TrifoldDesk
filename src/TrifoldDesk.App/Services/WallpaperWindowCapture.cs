using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Drawing = System.Drawing;

namespace TrifoldDesk.Services;

// Capture the wallpaper's GPU surface, rather than asking it to implement WM_PRINT.
internal sealed class WallpaperWindowCapture : IDisposable
{
    private readonly IDirect3DDevice _device;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly object _gate = new();
    private Drawing.Bitmap? _latest;
    private long _requested;
    private int _copying;
    private bool _disposed;

    public WallpaperWindowCapture(nint window)
    {
        _device = CreateDevice();
        try
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var iid = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
            nint itemPointer = interop.CreateForWindow(window, ref iid);
            GraphicsCaptureItem item;
            try { item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer); }
            finally { Marshal.Release(itemPointer); }
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            _session = _pool.CreateCaptureSession(item);
            _session.IsCursorCaptureEnabled = false;
            _pool.FrameArrived += OnFrameArrived;
            _session.StartCapture();
        }
        catch
        {
            if (_pool is not null) _pool.FrameArrived -= OnFrameArrived;
            _session?.Dispose();
            _pool?.Dispose();
            _device.Dispose();
            throw;
        }
    }

    public Drawing.Bitmap? CopyLatest()
    {
        lock (_gate)
            return !_disposed && _latest is not null
                ? (Drawing.Bitmap)_latest.Clone() : null;
    }

    private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (Interlocked.Exchange(ref _copying, 1) != 0) return;
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame is null || Environment.TickCount64 - _requested < 250) return;
            _requested = Environment.TickCount64;
            using var software = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore);
            int width = software.PixelWidth, height = software.PixelHeight;
            if (width <= 0 || height <= 0 || (long)width * height > 34_000_000) return;
            var pixels = new byte[checked(width * height * 4)];
            software.CopyToBuffer(pixels.AsBuffer());
            var bitmap = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppRgb);
            var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, width, height),
                Drawing.Imaging.ImageLockMode.WriteOnly, Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * width * 4, data.Scan0 + y * data.Stride, width * 4);
            }
            finally { bitmap.UnlockBits(data); }
            lock (_gate)
            {
                if (_disposed) bitmap.Dispose();
                else { _latest?.Dispose(); _latest = bitmap; }
            }
        }
        catch (Exception error) when (error is ExternalException or InvalidOperationException or ArgumentException or OperationCanceledException)
        { System.Diagnostics.Debug.WriteLine(error); }
        finally { Volatile.Write(ref _copying, 0); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _latest?.Dispose();
            _latest = null;
        }
        _pool.FrameArrived -= OnFrameArrived;
        _session.Dispose();
        _pool.Dispose();
        _device.Dispose();
    }

    private static IDirect3DDevice CreateDevice()
    {
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7,
            out nint device, out _, out nint context));
        nint dxgi = 0, inspectable = 0;
        try
        {
            var iid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, ref iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != 0) Marshal.Release(inspectable);
            if (dxgi != 0) Marshal.Release(dxgi);
            if (context != 0) Marshal.Release(context);
            if (device != 0) Marshal.Release(device);
        }
    }

    [ComImport, Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, ref Guid iid);
        nint CreateForMonitor(nint monitor, ref Guid iid);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags,
        nint featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int level, out nint context);
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint graphicsDevice);
}
