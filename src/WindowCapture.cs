// Capturing one window, even while another window covers it.
//
// On one monitor both game clients fill the same screen, so a BitBlt of the desktop only ever
// sees the one in front. Windows.Graphics.Capture reads a window's own image from the
// compositor instead -- what OBS's "Windows 10 (1903 and up)" window capture uses -- so the
// Priest's frames can be mirrored while the Warrior is played full screen on top.
//
// Frames arrive as GPU textures. The mirrored rectangle is copied into a small staging texture,
// mapped, and its rows copied into Capture's DIB, so the overlay draws it exactly as it draws a
// screen capture. Everything here is created on the UI thread and read from the capture timer;
// the frame pool is free-threaded, and only the timer ever touches the device context.

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Glass
{
    public sealed unsafe class WindowCapture : IDisposable
    {
        IntPtr device, context, staging;
        int stagingW, stagingH;
        IDirect3DDevice rtDevice;
        GraphicsCaptureItem item;
        Direct3D11CaptureFramePool pool;
        GraphicsCaptureSession session;
        SizeInt32 poolSize;
        public bool Closed { get; private set; }

        /// Start capturing `hWnd`, or say why not.
        public static WindowCapture TryStart(IntPtr hWnd, out string why)
        {
            var c = new WindowCapture();
            try
            {
                if (!GraphicsCaptureSession.IsSupported()) { why = "Windows.Graphics.Capture is not supported here"; return null; }
                c.Open(hWnd);
                why = null;
                return c;
            }
            catch (Exception e)
            {
                why = e.GetType().Name + ": " + e.Message;
                c.Dispose();
                return null;
            }
        }

        void Open(IntPtr hWnd)
        {
            // A hardware device that can hand BGRA textures to the compositor.
            const uint BGRA_SUPPORT = 0x20;
            Check(D3D11CreateDevice(IntPtr.Zero, 1 /* hardware */, IntPtr.Zero, BGRA_SUPPORT, IntPtr.Zero, 0, 7,
                                    out device, out _, out context), "D3D11CreateDevice");
            var dxgiId = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Check(Marshal.QueryInterface(device, ref dxgiId, out IntPtr dxgi), "IDXGIDevice");
            try
            {
                Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out IntPtr inspectable), "CreateDirect3D11DeviceFromDXGIDevice");
                rtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                Marshal.Release(inspectable);
            }
            finally { Marshal.Release(dxgi); }

            item = CreateItem(hWnd);
            item.Closed += (s, e) => Closed = true;           // the window went away
            poolSize = item.Size;
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(rtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, poolSize);
            session = pool.CreateCaptureSession(item);
            try { session.IsCursorCaptureEnabled = false; } catch { }       // Windows 10 2004+
            try { session.IsBorderRequired = false; } catch { }             // Windows 11 only; 10 draws a yellow border
            session.StartCapture();
        }

        static GraphicsCaptureItem CreateItem(IntPtr hWnd)
        {
            const string cls = "Windows.Graphics.Capture.GraphicsCaptureItem";
            Check(WindowsCreateString(cls, cls.Length, out IntPtr hs), "WindowsCreateString");
            try
            {
                var interopId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");     // IGraphicsCaptureItemInterop
                Check(RoGetActivationFactory(hs, ref interopId, out IntPtr interop), "RoGetActivationFactory");
                try
                {
                    var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");    // IGraphicsCaptureItem
                    IntPtr raw;
                    // IGraphicsCaptureItemInterop::CreateForWindow, the first method after IUnknown.
                    var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)(*(*(IntPtr**)interop + 3));
                    Check(create(interop, hWnd, &itemId, &raw), "CreateForWindow");
                    try { return GraphicsCaptureItem.FromAbi(raw); }
                    finally { Marshal.Release(raw); }
                }
                finally { Marshal.Release(interop); }
            }
            finally { WindowsDeleteString(hs); }
        }

        /// Copy the newest frame's `crop` (window coordinates) into a top-down BGRA buffer of
        /// crop's size. False if no new frame has arrived since the last call.
        public bool CopyLatest(Rectangle crop, IntPtr dst, int dstStride)
        {
            using (var frame = pool.TryGetNextFrame())
            {
                if (frame == null) return false;
                var content = frame.ContentSize;
                if (content.Width != poolSize.Width || content.Height != poolSize.Height)
                {
                    // The window changed size: take frames at the new size from the next one on.
                    poolSize = content;
                    pool.Recreate(rtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, poolSize);
                }

                EnsureStaging(crop.Width, crop.Height);
                var src = Rectangle.Intersect(crop, new Rectangle(0, 0, content.Width, content.Height));
                if (src.Width <= 0 || src.Height <= 0) return false;

                IntPtr texture = Texture(frame.Surface);
                try
                {
                    var box = new D3D11_BOX { left = (uint)src.X, top = (uint)src.Y, front = 0,
                                              right = (uint)src.Right, bottom = (uint)src.Bottom, back = 1 };
                    // ID3D11DeviceContext::CopySubresourceRegion, slot 46.
                    var copy = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, D3D11_BOX*, void>)
                               (*(*(IntPtr**)context + 46));
                    copy(context, staging, 0, (uint)(src.X - crop.X), (uint)(src.Y - crop.Y), 0, texture, 0, &box);
                }
                finally { Marshal.Release(texture); }
            }

            // ID3D11DeviceContext::Map (slot 14) and Unmap (slot 15).
            var map = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)(*(*(IntPtr**)context + 14));
            var unmap = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)(*(*(IntPtr**)context + 15));
            D3D11_MAPPED_SUBRESOURCE m;
            Check(map(context, staging, 0, 1 /* READ */, 0, &m), "Map");
            try
            {
                int row = crop.Width * 4;
                for (int y = 0; y < crop.Height; y++)
                    Buffer.MemoryCopy((byte*)m.pData + (long)y * m.RowPitch, (byte*)dst + (long)y * dstStride, row, row);
            }
            finally { unmap(context, staging, 0); }
            return true;
        }

        void EnsureStaging(int w, int h)
        {
            if (staging != IntPtr.Zero && stagingW == w && stagingH == h) return;
            if (staging != IntPtr.Zero) { Marshal.Release(staging); staging = IntPtr.Zero; }
            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = 87 /* B8G8R8A8_UNORM */,
                SampleCount = 1, SampleQuality = 0, Usage = 3 /* STAGING */, BindFlags = 0,
                CPUAccessFlags = 0x20000 /* READ */, MiscFlags = 0,
            };
            // ID3D11Device::CreateTexture2D, slot 5.
            var create = (delegate* unmanaged[Stdcall]<IntPtr, D3D11_TEXTURE2D_DESC*, IntPtr, IntPtr*, int>)(*(*(IntPtr**)device + 5));
            IntPtr t;
            Check(create(device, &desc, IntPtr.Zero, &t), "CreateTexture2D");
            staging = t; stagingW = w; stagingH = h;
        }

        /// The ID3D11Texture2D behind a WinRT surface.
        static IntPtr Texture(IDirect3DSurface surface)
        {
            IntPtr abi = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
            try
            {
                var accessId = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");        // IDirect3DDxgiInterfaceAccess
                Check(Marshal.QueryInterface(abi, ref accessId, out IntPtr access), "IDirect3DDxgiInterfaceAccess");
                try
                {
                    var textureId = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");  // ID3D11Texture2D
                    IntPtr texture;
                    var get = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)(*(*(IntPtr**)access + 3));
                    Check(get(access, &textureId, &texture), "GetInterface");
                    return texture;
                }
                finally { Marshal.Release(access); }
            }
            finally { Marshal.Release(abi); }
        }

        static void Check(int hr, string what)
        {
            if (hr < 0) throw new COMException(what + " failed", hr);
        }

        public void Dispose()
        {
            try { session?.Dispose(); } catch { }
            try { pool?.Dispose(); } catch { }
            session = null; pool = null; item = null;
            try { (rtDevice as IDisposable)?.Dispose(); } catch { }
            rtDevice = null;
            if (staging != IntPtr.Zero) { Marshal.Release(staging); staging = IntPtr.Zero; }
            if (context != IntPtr.Zero) { Marshal.Release(context); context = IntPtr.Zero; }
            if (device != IntPtr.Zero) { Marshal.Release(device); device = IntPtr.Zero; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct D3D11_TEXTURE2D_DESC
        {
            public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct D3D11_BOX { public uint left, top, front, right, bottom, back; }

        [StructLayout(LayoutKind.Sequential)]
        struct D3D11_MAPPED_SUBRESOURCE { public void* pData; public uint RowPitch, DepthPitch; }

        [DllImport("d3d11.dll")]
        static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr levels,
                                            uint levelCount, uint sdkVersion, out IntPtr device, out int level, out IntPtr context);
        [DllImport("d3d11.dll")]
        static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        static extern int WindowsCreateString(string source, int length, out IntPtr hstring);
        [DllImport("combase.dll")]
        static extern int WindowsDeleteString(IntPtr hstring);
        [DllImport("combase.dll")]
        static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
    }
}
