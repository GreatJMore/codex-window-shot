using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;

namespace WindowShot;

public sealed record CaptureResult(BitmapSource Image, int Width, int Height, bool IsBlack);

public sealed class WindowCaptureException : Exception
{
    public WindowCaptureException(string message) : base(message)
    {
    }
}

public static class WindowCaptureService
{
    private const uint PwRenderFullContent = 0x00000002;

    public static CaptureResult Capture(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle))
        {
            throw new WindowCaptureException("窗口已关闭");
        }

        if (IsIconic(handle))
        {
            throw new WindowCaptureException("窗口已最小化");
        }

        if (!GetWindowRect(handle, out var rect))
        {
            throw new WindowCaptureException("无法获取窗口大小");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0 || width > 20000 || height > 20000)
        {
            throw new WindowCaptureException("窗口尺寸无效");
        }

        using var bitmap = new Drawing.Bitmap(width, height, DrawingImaging.PixelFormat.Format32bppRgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(Drawing.Color.Black);
            var hdc = graphics.GetHdc();
            bool printed;
            try
            {
                printed = PrintWindow(handle, hdc, PwRenderFullContent);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            if (!printed)
            {
                throw new WindowCaptureException("该窗口不支持窗口级截图");
            }
        }

        var image = ToBitmapSource(bitmap, out var isBlack);
        return new CaptureResult(image, width, height, isBlack);
    }

    private static BitmapSource ToBitmapSource(Drawing.Bitmap bitmap, out bool isBlack)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var data = bitmap.LockBits(
            new Drawing.Rectangle(0, 0, width, height),
            DrawingImaging.ImageLockMode.ReadOnly,
            DrawingImaging.PixelFormat.Format32bppRgb);

        try
        {
            var stride = data.Stride;
            var pixels = new byte[stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            isBlack = true;
            for (var i = 0; i < pixels.Length; i += 4 * 97)
            {
                if (pixels[i] > 2 || pixels[i + 1] > 2 || pixels[i + 2] > 2)
                {
                    isBlack = false;
                    break;
                }
            }

            var source = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgr32,
                null,
                pixels,
                stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
