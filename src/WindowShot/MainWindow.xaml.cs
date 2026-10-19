using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WindowShot;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x5753;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkS = 0x53;

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly string? _initialSelection;

    private IReadOnlyList<WindowInfo> _allWindows = Array.Empty<WindowInfo>();
    private WindowInfo? _selected;
    private BitmapSource? _lastImage;
    private HwndSource? _source;
    private bool _suppressSelection;
    private bool _closed;
    private bool _hotkeyRegistered;
    private IntPtr _blackWarningHandle;

    public MainWindow(string? initialSelection = null)
    {
        InitializeComponent();

        _initialSelection = initialSelection;
        _settings = AppSettings.Load();
        Topmost = _settings.Topmost;
        UpdatePinVisual();

        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _previewTimer.Tick += PreviewTimer_Tick;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += (_, _) => RefreshWindows(updateStatus: false);

        _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.6)
        };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.Visibility = Visibility.Collapsed;
        };

        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;

        UpdateSaveFolderUi();
        UpdateEmptyState("选择一个窗口");
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshWindows(updateStatus: true);
        _refreshTimer.Start();

        if (!_hotkeyRegistered)
        {
            StatusText.Text = "全局快捷键未注册，可能已被其他程序占用";
        }

        if (!string.IsNullOrWhiteSpace(_initialSelection))
        {
            var match = _allWindows.FirstOrDefault(w =>
                w.Title.Contains(_initialSelection, StringComparison.CurrentCultureIgnoreCase) ||
                w.ProcessName.Contains(_initialSelection, StringComparison.CurrentCultureIgnoreCase));
            if (match is not null)
            {
                WindowList.SelectedItem = match;
                WindowList.ScrollIntoView(match);
            }
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        _hotkeyRegistered = RegisterHotKey(
            handle,
            HotkeyId,
            ModControl | ModAlt | ModShift | ModNoRepeat,
            VkS);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _previewTimer.Stop();
        _refreshTimer.Stop();
        _toastTimer.Stop();

        if (_source is not null)
        {
            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HotkeyId);
            _source.RemoveHook(WndProc);
        }

        _captureGate.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _ = CaptureFromHotkeyAsync();
        }

        return IntPtr.Zero;
    }

    private async Task CaptureFromHotkeyAsync()
    {
        if (_selected is null)
        {
            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            StatusText.Text = "请选择一个窗口";
            return;
        }

        await CaptureAsync(copyOnly: false);
    }

    private void RefreshWindows(bool updateStatus)
    {
        var selectedHandle = _selected?.Handle ?? IntPtr.Zero;
        _allWindows = WindowEnumerator.Enumerate();
        ApplyFilter(selectedHandle);

        if (updateStatus)
        {
            StatusText.Text = $"{_allWindows.Count} 个窗口";
        }
    }

    private void ApplyFilter(IntPtr preferredHandle)
    {
        var query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allWindows.ToList()
            : _allWindows
                .Where(w =>
                    w.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    w.ProcessName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        _suppressSelection = true;
        WindowList.ItemsSource = filtered;
        WindowCountText.Text = string.IsNullOrWhiteSpace(query)
            ? $"{filtered.Count}"
            : $"{filtered.Count}/{_allWindows.Count}";

        WindowInfo? match = null;
        if (preferredHandle != IntPtr.Zero)
        {
            match = filtered.FirstOrDefault(w => w.Handle == preferredHandle);
        }

        if (match is null && WindowList.SelectedItem is WindowInfo current)
        {
            match = filtered.FirstOrDefault(w => w.Handle == current.Handle);
        }

        WindowList.SelectedItem = match;
        if (match is not null)
        {
            WindowList.ScrollIntoView(match);
        }

        _suppressSelection = false;
        SetSelected(match);
    }

    private void WindowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
        {
            return;
        }

        SetSelected(WindowList.SelectedItem as WindowInfo);
    }

    private void SetSelected(WindowInfo? info)
    {
        if (_closed)
        {
            return;
        }

        var sameWindow = info is not null && _selected?.Handle == info.Handle;
        _selected = info;
        if (!sameWindow)
        {
            PreviewImage.Source = null;
            _lastImage = null;
        }

        if (info is null)
        {
            _previewTimer.Stop();
            SelectedTitle.Text = "未选择窗口";
            SelectedMeta.Text = string.Empty;
            CaptureButton.IsEnabled = false;
            CopyButton.IsEnabled = false;
            UpdateEmptyState("选择一个窗口");
            return;
        }

        SelectedTitle.Text = info.Title;
        SelectedMeta.Text = info.IsMinimized
            ? $"{info.ProcessName} · 已最小化"
            : $"{info.ProcessName} · {info.Width} × {info.Height}";

        CaptureButton.IsEnabled = !info.IsMinimized && sameWindow && _lastImage is not null;
        CopyButton.IsEnabled = sameWindow && _lastImage is not null;
        if (!sameWindow)
        {
            UpdateEmptyState(info.IsMinimized ? "窗口已最小化" : "正在读取窗口…");
        }

        _previewTimer.Start();
        if (!sameWindow)
        {
            _ = RefreshPreviewAsync();
        }
    }

    private async void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshPreviewAsync();
    }

    private async Task RefreshPreviewAsync()
    {
        var target = _selected;
        if (target is null || _closed || !await _captureGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var result = await Task.Run(() => WindowCaptureService.Capture(target.Handle));
            if (_closed || _selected?.Handle != target.Handle)
            {
                return;
            }

            PreviewImage.Source = result.Image;
            _lastImage = result.Image;
            EmptyState.Visibility = Visibility.Collapsed;
            SelectedMeta.Text = $"{target.ProcessName} · {result.Width} × {result.Height}";
            CaptureButton.IsEnabled = true;
            CopyButton.IsEnabled = true;

            if (result.IsBlack && _blackWarningHandle != target.Handle)
            {
                _blackWarningHandle = target.Handle;
                StatusText.Text = "该窗口画面为全黑，可能不支持窗口级截图";
            }
        }
        catch (WindowCaptureException ex)
        {
            if (_closed || _selected?.Handle != target.Handle)
            {
                return;
            }

            PreviewImage.Source = null;
            _lastImage = null;
            CaptureButton.IsEnabled = false;
            CopyButton.IsEnabled = false;
            UpdateEmptyState(ex.Message);

            if (ex.Message == "窗口已关闭")
            {
                _previewTimer.Stop();
            }
        }
        catch
        {
            if (_closed || _selected?.Handle != target.Handle)
            {
                return;
            }

            PreviewImage.Source = null;
            _lastImage = null;
            CaptureButton.IsEnabled = false;
            CopyButton.IsEnabled = false;
            UpdateEmptyState("无法读取该窗口画面");
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private void UpdateEmptyState(string message)
    {
        EmptyText.Text = message;
        EmptyState.Visibility = PreviewImage.Source is null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        await CaptureAsync(copyOnly: false);
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        await CaptureAsync(copyOnly: true);
    }

    private async void WindowList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_selected is not null && !_selected.IsMinimized)
        {
            await CaptureAsync(copyOnly: false);
        }
    }

    private async Task CaptureAsync(bool copyOnly)
    {
        var target = _selected;
        if (target is null || _closed)
        {
            return;
        }

        if (!await _captureGate.WaitAsync(0))
        {
            ShowToast("正在读取窗口，请稍候");
            return;
        }

        CaptureButton.IsEnabled = false;
        CopyButton.IsEnabled = false;

        try
        {
            var result = await Task.Run(() => WindowCaptureService.Capture(target.Handle));
            if (_closed)
            {
                return;
            }

            _lastImage = result.Image;
            PreviewImage.Source = result.Image;
            EmptyState.Visibility = Visibility.Collapsed;
            SelectedMeta.Text = $"{target.ProcessName} · {result.Width} × {result.Height}";

            if (copyOnly)
            {
                CopyImage(result.Image);
                ShowToast("已复制到剪贴板");
                StatusText.Text = "已复制到剪贴板";
                return;
            }

            var path = await Task.Run(() => SaveImage(result.Image, target));
            CopyImage(result.Image);
            ShowToast("已保存并复制");
            StatusText.Text = $"已保存 · {path}";
            StatusText.ToolTip = path;
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                ShowToast(ex.Message, isError: true);
                StatusText.Text = ex.Message;
            }
        }
        finally
        {
            _captureGate.Release();
            if (!_closed && _selected is not null)
            {
                var minimized = IsIconic(_selected.Handle);
                CaptureButton.IsEnabled = !minimized;
                CopyButton.IsEnabled = !minimized;
            }
        }
    }

    private string SaveImage(BitmapSource image, WindowInfo target)
    {
        var folder = _settings.ResolveSaveFolder();
        Directory.CreateDirectory(folder);

        var baseName = BuildFileName(target);
        var path = Path.Combine(folder, baseName + ".png");
        var index = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(folder, $"{baseName}_{index}.png");
            index++;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static string BuildFileName(WindowInfo target)
    {
        var raw = $"{target.ProcessName}_{target.Title}";
        var builder = new StringBuilder(raw.Length);
        var lastWasSeparator = false;
        foreach (var character in raw)
        {
            if (Path.GetInvalidFileNameChars().Contains(character) || char.IsWhiteSpace(character))
            {
                if (!lastWasSeparator)
                {
                    builder.Append('_');
                    lastWasSeparator = true;
                }

                continue;
            }

            builder.Append(character);
            lastWasSeparator = false;
        }

        var cleaned = builder.ToString().Trim('_', '.', ' ');
        if (cleaned.Length == 0)
        {
            cleaned = "Window";
        }

        if (cleaned.Length > 60)
        {
            cleaned = cleaned[..60].TrimEnd('_', '.', ' ');
        }

        return $"{cleaned}_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    private static void CopyImage(BitmapSource image)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetImage(image);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(80);
            }
        }

        throw new InvalidOperationException("剪贴板正被其他程序占用", last);
    }

    private void ShowToast(string message, bool isError = false)
    {
        ToastText.Text = message;
        ToastGlyph.Text = isError ? "\uE783" : "\uE73E";
        ToastGlyph.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(isError ? "#FCA5A5" : "#6EE7B7"));
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyFilter(_selected?.Handle ?? IntPtr.Zero);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshWindows(updateStatus: true);
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _settings.Topmost = Topmost;
        _settings.Save();
        UpdatePinVisual();
    }

    private void UpdatePinVisual()
    {
        PinGlyph.Foreground = Topmost
            ? (Brush)FindResource("Accent")
            : (Brush)FindResource("TextSecondary");
        PinButton.ToolTip = Topmost ? "取消置顶" : "置顶窗口";
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = _settings.ResolveSaveFolder();
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void ChangeFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = _settings.ResolveSaveFolder();
        var dialog = new OpenFolderDialog
        {
            Title = "选择保存文件夹",
            InitialDirectory = Directory.Exists(folder) ? folder : string.Empty
        };

        if (dialog.ShowDialog(this) == true)
        {
            _settings.SaveFolder = dialog.FolderName;
            _settings.Save();
            UpdateSaveFolderUi();
        }
    }

    private void UpdateSaveFolderUi()
    {
        var folder = _settings.ResolveSaveFolder();
        SaveFolderText.Text = folder;
        SaveFolderText.ToolTip = folder;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);
}
