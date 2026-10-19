using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WindowShot;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 1 && e.Args[0].Equals("--list", StringComparison.OrdinalIgnoreCase))
        {
            var outputPath = e.Args.Length >= 2 ? e.Args[1] : null;
            Shutdown(RunCliList(outputPath));
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0].Equals("--capture-title", StringComparison.OrdinalIgnoreCase))
        {
            var query = e.Args[1];
            var target = WindowEnumerator.Enumerate()
                .Where(w => !w.IsMinimized)
                .Where(w =>
                    w.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    w.ProcessName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(w => w.ProcessName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();

            Shutdown(target is null ? 3 : RunCliCapture(target.Handle.ToString("X"), e.Args[2]));
            return;
        }

        if (e.Args.Length >= 3 && e.Args[0].Equals("--capture", StringComparison.OrdinalIgnoreCase))
        {
            Shutdown(RunCliCapture(e.Args[1], e.Args[2]));
            return;
        }

        if (e.Args.Length >= 1 && e.Args[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
        {
            WriteConsole(
                "WindowShot\r\n" +
                "  WindowShot.exe                            打开窗口选择界面\r\n" +
                "  WindowShot.exe --select <文字>            打开界面并预选窗口\r\n" +
                "  WindowShot.exe --list <输出.json>         列出可截图窗口\r\n" +
                "  WindowShot.exe --capture <句柄> <图.png>  按窗口句柄截图\r\n" +
                "  WindowShot.exe --capture-title <文字> <图.png>  按标题截图\r\n");
            Shutdown(0);
            return;
        }

        var initialSelection = e.Args.Length >= 2 &&
                               e.Args[0].Equals("--select", StringComparison.OrdinalIgnoreCase)
            ? e.Args[1]
            : null;
        new MainWindow(initialSelection).Show();
    }

    private static int RunCliCapture(string handleText, string outputPath)
    {
        try
        {
            var normalized = handleText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? handleText[2..]
                : handleText;
            var handle = new IntPtr(Convert.ToInt64(normalized, 16));
            var result = WindowCaptureService.Capture(handle);
            var path = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(result.Image));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int RunCliList(string? outputPath)
    {
        var windows = WindowEnumerator.Enumerate()
            .Select(w => new
            {
                handle = $"0x{w.Handle.ToInt64():X}",
                title = w.Title,
                process = w.ProcessName,
                width = w.Width,
                height = w.Height,
                minimized = w.IsMinimized
            })
            .ToArray();

        var json = JsonSerializer.Serialize(
            new
            {
                count = windows.Length,
                windows
            },
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            try
            {
                var path = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json, new UTF8Encoding(false));
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        WriteConsole(json);
        return 0;
    }

    private static void WriteConsole(string text)
    {
        try
        {
            AttachConsole(-1);
            var stream = Console.OpenStandardOutput();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            writer.Write(text);
        }
        catch
        {
            // GUI-subsystem processes may not have a parent console.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);
}
