using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowShot;

public static class WindowEnumerator
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const int DwmwaCloaked = 14;

    private static readonly ConcurrentDictionary<int, (string Name, ImageSource? Icon)> ProcessCache = new();

    public static IReadOnlyList<WindowInfo> Enumerate()
    {
        var ownProcessId = Environment.ProcessId;
        var windows = new List<WindowInfo>();

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) == 0)
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == ownProcessId)
            {
                return true;
            }

            var exStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            if ((exStyle & WsExToolWindow) != 0)
            {
                return true;
            }

            if (DwmGetWindowAttribute(handle, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            var className = GetClassNameText(handle);
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return true;
            }

            if (!GetWindowRect(handle, out var rect))
            {
                return true;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 1 || height <= 1)
            {
                return true;
            }

            var title = GetWindowTextText(handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            var process = GetProcessInfo((int)processId);
            windows.Add(new WindowInfo
            {
                Handle = handle,
                Title = title.Trim(),
                ProcessName = process.Name,
                Width = width,
                Height = height,
                IsMinimized = IsIconic(handle),
                Icon = process.Icon
            });

            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(w => w.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static (string Name, ImageSource? Icon) GetProcessInfo(int processId)
    {
        return ProcessCache.GetOrAdd(processId, id =>
        {
            try
            {
                using var process = Process.GetProcessById(id);
                var name = process.ProcessName;
                ImageSource? icon = null;

                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        using var associated = Icon.ExtractAssociatedIcon(path);
                        if (associated is not null)
                        {
                            icon = Imaging.CreateBitmapSourceFromHIcon(
                                associated.Handle,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            icon.Freeze();
                        }
                    }
                }
                catch
                {
                    // Some system processes do not expose an icon or module path.
                }

                return (name, icon);
            }
            catch
            {
                return ("未知进程", null);
            }
        });
    }

    private static string GetWindowTextText(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder(length + 1);
        GetWindowText(handle, text, text.Capacity);
        return text.ToString();
    }

    private static string GetClassNameText(IntPtr handle)
    {
        var text = new StringBuilder(256);
        GetClassName(handle, text, text.Capacity);
        return text.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder text, int count);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr handle,
        int attribute,
        out int value,
        int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
