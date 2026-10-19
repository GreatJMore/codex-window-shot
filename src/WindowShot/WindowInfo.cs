using System;
using System.Windows.Media;

namespace WindowShot;

public sealed class WindowInfo
{
    public required IntPtr Handle { get; init; }
    public required string Title { get; init; }
    public required string ProcessName { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public bool IsMinimized { get; init; }
    public ImageSource? Icon { get; init; }

    public string Subtitle => IsMinimized
        ? $"{ProcessName} · 已最小化"
        : ProcessName;
}
