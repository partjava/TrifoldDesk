using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using TrifoldDesk.Core;
using TrifoldDesk.Services;

namespace TrifoldDesk;

internal sealed class ClotheslineRope : FrameworkElement
{
    public Brush RopeBrush { get; set; } = new SolidColorBrush(Color.FromRgb(214, 190, 154));
    public Brush PinBrush { get; set; } = new SolidColorBrush(Color.FromRgb(120, 148, 162));

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth, h = ActualHeight;
        if (w < 24 || h < 8) return;
        var left = new Point(12, 10);
        var right = new Point(w - 12, 10);
        var control = new Point(w / 2, Math.Min(h - 2, 16));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(left, false, false);
            ctx.QuadraticBezierTo(control, right, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(45, 0, 0, 0)), 2.6), geometry);
        dc.DrawGeometry(null, new Pen(RopeBrush, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
        dc.DrawEllipse(PinBrush, new Pen(Brushes.WhiteSmoke, 0.8), left, 3.2, 3.2);
        dc.DrawEllipse(PinBrush, new Pen(Brushes.WhiteSmoke, 0.8), right, 3.2, 3.2);
    }
}

public sealed class PhotoClotheslineWidget : Grid
{
    private const string DragPhotoFormat = "TrifoldDesk.HangingPhoto";
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif"];
    private static readonly Queue<(string Hash, DateTime Utc)> RecentHungSignatures = new();
    private static DateTime _lastAnyHungUtc = DateTime.MinValue;

    private readonly string _id;
    private readonly ConfigManager _store;
    private readonly string _style;
    private PhotoLineData _data = new();
    private readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        CanContentScroll = false,
        Focusable = false,
        Background = Brushes.Transparent,
        Margin = new Thickness(12, 0, 12, 0)
    };
    private readonly TextBlock _status = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _emptyHint = new() { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 14, 12, 14), Margin = new Thickness(8, 18, 8, 8), Cursor = Cursors.Hand };
    private readonly Dictionary<string, (RotateTransform Transform, double BaseTilt, double Amplitude, long StartTick)> _swaying = [];
    private readonly DispatcherTimer _swayTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _clipboardWatcherTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private FileSystemWatcher? _watcher;
    private Point _panStartPoint;
    private double _panStartOffset;
    private bool _isPanningStage;
    private bool _hasPanned;
    private uint _lastClipboardSeq;
    private Button? _autoBtn;

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    public PhotoClotheslineWidget(string instanceId, ConfigManager store, string style = "glass")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(store);
        _id = instanceId;
        _store = store;
        _style = style;
        Focusable = true;
        AllowDrop = true;
        Background = Brushes.Transparent;

        bool darkText = TextContrast.IsDarkTextStyle(style);
        var ink = new SolidColorBrush((Color)ColorConverter.ConvertFromString(darkText ? "#1E353D" : "#FFFFFF"));
        var muted = new SolidColorBrush((Color)ColorConverter.ConvertFromString(darkText ? "#48606A" : "#D0DFE8"));
        _status.Foreground = muted;
        if (style == "glass") _status.Effect = TextContrast.Shadow;

        try
        {
            var config = _store.Load<PhotoLineConfig>("optional-photos.json").Value;
            if (config.Widgets != null && config.Widgets.TryGetValue(_id, out var saved) && saved != null)
                _data = PhotoLineRules.Clone(saved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FutureSchemaException)
        {
            _status.Text = "挂绳记录暂不可读";
        }
        _data.Items ??= [];

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());

        var topBar = new Grid { Margin = new Thickness(4, 0, 4, 4) };
        topBar.ColumnDefinitions.Add(new ColumnDefinition());
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBar.Children.Add(_status);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(actions, 1);

        _autoBtn = QuickButton(
            _data.AutoWatchScreenshots ? "⚡ 自动上绳: 开" : "⚡ 自动上绳",
            "自动监听新截图或剪贴板图片挂到绳子上",
            darkText,
            _data.AutoWatchScreenshots
        );
        _autoBtn.Click += (_, _) => ToggleAutoWatch();

        var pasteBtn = QuickButton("📋 粘贴", "将剪贴板中的截图直接挂到绳子上 (Ctrl+V)", darkText);
        pasteBtn.Click += (_, _) => HangFromClipboard();

        var pickBtn = QuickButton("＋ 选图", "选择本地图片挂到绳子上", darkText);
        pickBtn.Click += (_, _) => ChooseImages();

        var prevBtn = QuickButton("‹", "向左滑动挂绳", darkText);
        prevBtn.Click += (_, _) => _scroll.ScrollToHorizontalOffset(Math.Max(0, _scroll.HorizontalOffset - 150));

        var nextBtn = QuickButton("›", "向右滑动挂绳", darkText);
        nextBtn.Click += (_, _) => _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset + 150);

        var clearBtn = QuickButton("🧹 清空", "一键摘下绳上所有截图（保留原图不删除）", darkText);
        clearBtn.Click += (_, _) => ClearAllPhotos();

        actions.Children.Add(_autoBtn);
        actions.Children.Add(pasteBtn);
        actions.Children.Add(pickBtn);
        actions.Children.Add(prevBtn);
        actions.Children.Add(nextBtn);
        actions.Children.Add(clearBtn);
        topBar.Children.Add(actions);
        Children.Add(topBar);

        var stage = new Grid { ClipToBounds = true };
        Grid.SetRow(stage, 1);

        var rope = new ClotheslineRope
        {
            Height = 22,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 0, 0),
            IsHitTestVisible = false,
            RopeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(darkText ? "#8C7355" : "#DFC9A6"))
        };
        stage.Children.Add(rope);

        var hintStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var hintTitle = new TextBlock
        {
            Text = "✂ 截图后自动挂绳，或点「📋 粘贴」",
            FontSize = 12,
            Foreground = ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = style == "glass" ? TextContrast.Shadow : null
        };
        var hintSub = new TextBlock
        {
            Text = "滚轮左右滑动 · 按住鼠标拖动 · Ctrl+滚轮缩放",
            FontSize = 10,
            Foreground = muted,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = style == "glass" ? TextContrast.Shadow : null
        };
        hintStack.Children.Add(hintTitle);
        hintStack.Children.Add(hintSub);
        _emptyHint.BorderBrush = new SolidColorBrush(darkText ? Color.FromArgb(70, 40, 65, 80) : Color.FromArgb(65, 255, 255, 255));
        _emptyHint.Background = new SolidColorBrush(darkText ? Color.FromArgb(18, 30, 50, 60) : Color.FromArgb(18, 255, 255, 255));
        _emptyHint.Child = hintStack;
        _emptyHint.MouseLeftButtonUp += (_, _) => HangFromClipboard();
        stage.Children.Add(_emptyHint);

        _scroll.Content = _line;

        // Stage pan dragging: mouse drag anywhere on the stage slides smoothly
        _scroll.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _panStartPoint = e.GetPosition(_scroll);
            _panStartOffset = _scroll.HorizontalOffset;
            _isPanningStage = true;
            _hasPanned = false;
        };
        _scroll.PreviewMouseMove += (_, e) =>
        {
            if (!_isPanningStage || e.LeftButton != MouseButtonState.Pressed) return;
            var current = e.GetPosition(_scroll);
            double dx = current.X - _panStartPoint.X;
            if (!_hasPanned && Math.Abs(dx) > 4)
            {
                _hasPanned = true;
                _scroll.CaptureMouse();
            }
            if (_hasPanned)
            {
                _scroll.ScrollToHorizontalOffset(_panStartOffset - dx);
                e.Handled = true;
            }
        };
        _scroll.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_isPanningStage)
            {
                _isPanningStage = false;
                if (_hasPanned)
                {
                    _scroll.ReleaseMouseCapture();
                    e.Handled = true;
                }
            }
        };

        stage.Children.Add(_scroll);
        Children.Add(stage);

        // Entire widget wheel scrolls the clothesline horizontally
        PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            if (_scroll.ScrollableWidth > 0)
            {
                _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset - (e.Delta > 0 ? 110 : -110));
                e.Handled = true;
            }
        };

        var menu = new ContextMenu();
        var menuPaste = new MenuItem { Header = "挂起剪贴板截图 (Ctrl+V)" };
        menuPaste.Click += (_, _) => HangFromClipboard();
        var menuPick = new MenuItem { Header = "从文件选择图片挂起…" };
        menuPick.Click += (_, _) => ChooseImages();
        var menuWatch = new MenuItem { Header = "⚡ 自动上绳 (剪贴板 + 截图目录)", IsCheckable = true, IsChecked = _data.AutoWatchScreenshots };
        menuWatch.Click += (_, _) => ToggleAutoWatch();
        var menuOpenDir = new MenuItem { Header = "📂 打开历史截图文件夹" };
        menuOpenDir.Click += (_, _) => OpenScreenshotsFolder();
        var menuClear = new MenuItem { Header = "🧹 清空挂绳上的全部截图（保留文件）" };
        menuClear.Click += (_, _) => ClearAllPhotos();

        menu.Items.Add(menuPaste);
        menu.Items.Add(menuPick);
        menu.Items.Add(menuWatch);
        menu.Items.Add(new Separator());
        menu.Items.Add(menuOpenDir);
        menu.Items.Add(menuClear);
        ContextMenu = menu;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                HangFromClipboard();
                e.Handled = true;
            }
        };

        DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(DragPhotoFormat) || HasImageDrop(e.Data))
                e.Effects = DragDropEffects.Copy | DragDropEffects.Move;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                HangFiles(files);
                e.Handled = true;
            }
        };

        _swayTimer.Tick += SwayTick;
        _clipboardWatcherTimer.Tick += ClipboardWatcherTick;

        Loaded += (_, _) =>
        {
            UpdateAutoWatchState();
            Render();
        };
        Unloaded += (_, _) =>
        {
            _swayTimer.Stop();
            _swaying.Clear();
            _clipboardWatcherTimer.Stop();
            DisposeWatcher();
        };
        Render();
    }

    private static Button QuickButton(string text, string tip, bool darkText, bool highlighted = false) => new()
    {
        Content = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                highlighted ? (darkText ? "#0F524A" : "#3EE0C8") : (darkText ? "#1E353D" : "#F0F7FA")))
        },
        ToolTip = tip,
        Padding = new Thickness(6, 3, 6, 3),
        Margin = new Thickness(3, 0, 0, 0),
        Background = new SolidColorBrush(highlighted
            ? (darkText ? Color.FromArgb(45, 30, 120, 110) : Color.FromArgb(50, 40, 200, 180))
            : (darkText ? Color.FromArgb(24, 30, 50, 60) : Color.FromArgb(24, 255, 255, 255))),
        BorderBrush = new SolidColorBrush(highlighted
            ? (darkText ? Color.FromArgb(120, 30, 120, 110) : Color.FromArgb(120, 40, 200, 180))
            : (darkText ? Color.FromArgb(65, 30, 50, 60) : Color.FromArgb(55, 255, 255, 255)))
    };

    private static bool HasImageDrop(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] files &&
        files.Any(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

    private string ScreenshotsStoreDir()
    {
        string dir = Path.Combine(App.DataDirectory, "assets", "screenshots");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void OpenScreenshotsFolder()
    {
        try
        {
            string dir = ScreenshotsStoreDir();
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void ToggleAutoWatch()
    {
        var next = PhotoLineRules.Clone(_data);
        next.AutoWatchScreenshots = !next.AutoWatchScreenshots;
        next.AutoWatchClipboard = next.AutoWatchScreenshots;
        if (Save(next))
        {
            UpdateAutoWatchState();
            _status.Text = next.AutoWatchScreenshots ? "已开启自动上绳（监听截图与剪贴板）" : "已关闭自动上绳";
        }
    }

    private void UpdateAutoWatchState()
    {
        bool on = _data.AutoWatchScreenshots;
        if (_autoBtn != null)
        {
            _autoBtn.Content = new TextBlock
            {
                Text = on ? "⚡ 自动上绳: 开" : "⚡ 自动上绳",
                FontSize = 10,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    on ? (TextContrast.IsDarkTextStyle(_style) ? "#0F524A" : "#3EE0C8") : (TextContrast.IsDarkTextStyle(_style) ? "#1E353D" : "#F0F7FA")))
            };
        }
        if (ContextMenu is ContextMenu menu)
        {
            foreach (var item in menu.Items.OfType<MenuItem>().Where(m => m.Header?.ToString()?.StartsWith("⚡ 自动上绳") == true))
                item.IsChecked = on;
        }

        if (on)
        {
            ConfigureScreenshotWatcher();
            try
            {
                _lastClipboardSeq = GetClipboardSequenceNumber();
                if (!_clipboardWatcherTimer.IsEnabled) _clipboardWatcherTimer.Start();
            }
            catch { }
        }
        else
        {
            DisposeWatcher();
            _clipboardWatcherTimer.Stop();
        }
    }

    private static bool IsDuplicateAutoScreenshot(BitmapSource bmp)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastAnyHungUtc).TotalMilliseconds < 1200)
            return true;

        string hash = ComputeImageSignature(bmp);
        var cutoff = now.AddSeconds(-6);
        while (RecentHungSignatures.Count > 0 && RecentHungSignatures.Peek().Utc < cutoff)
            RecentHungSignatures.Dequeue();

        if (RecentHungSignatures.Any(r => r.Hash == hash))
            return true;

        RecentHungSignatures.Enqueue((hash, now));
        _lastAnyHungUtc = now;
        return false;
    }

    private void ClipboardWatcherTick(object? sender, EventArgs e)
    {
        if (!_data.AutoWatchScreenshots) return;
        try
        {
            uint seq = GetClipboardSequenceNumber();
            if (seq == _lastClipboardSeq || seq == 0) return;
            _lastClipboardSeq = seq;
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource bmp)
            {
                if (IsDuplicateAutoScreenshot(bmp)) return;
                HangBitmap(bmp, DateTime.Now.ToString("HH:mm 自动截图"), isAuto: true);
            }
        }
        catch { }
    }

    private static string ComputeImageSignature(BitmapSource bmp)
    {
        try
        {
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            var formatted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int sampleW = Math.Min(32, w);
            int sampleH = Math.Min(32, h);
            var cropped = new CroppedBitmap(formatted, new Int32Rect(0, 0, sampleW, sampleH));
            int stride = sampleW * 4;
            byte[] pixels = new byte[stride * sampleH];
            cropped.CopyPixels(pixels, stride, 0);
            using var md5 = MD5.Create();
            return $"{w}x{h}_" + Convert.ToHexString(md5.ComputeHash(pixels));
        }
        catch
        {
            return $"{bmp.PixelWidth}x{bmp.PixelHeight}";
        }
    }

    public bool HangBitmap(BitmapSource bitmap, string? title = null, bool isAuto = false)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (isAuto && IsDuplicateAutoScreenshot(bitmap)) return false;

        string itemId = Guid.NewGuid().ToString("N");
        string file = Path.Combine(ScreenshotsStoreDir(), itemId + ".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(file)) encoder.Save(stream);
        var next = PhotoLineRules.AddPhoto(_data, file, title ?? DateTime.Now.ToString("HH:mm 截图"), itemId);
        if (!Save(next)) return false;
        Render(itemId);
        _scroll.ScrollToLeftEnd();
        return true;
    }

    public void HangFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource image)
            {
                _lastAnyHungUtc = DateTime.UtcNow;
                HangBitmap(image, DateTime.Now.ToString("HH:mm 剪贴板截图"));
                return;
            }
            if (Clipboard.ContainsFileDropList())
            {
                var list = Clipboard.GetFileDropList().Cast<string>().Where(f => !string.IsNullOrWhiteSpace(f)).ToArray();
                if (list.Length > 0)
                {
                    HangFiles(list);
                    return;
                }
            }
            _status.Text = "剪贴板里暂无截图，可按 Win+Shift+S 截图";
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _status.Text = "读取剪贴板失败，请重试";
        }
    }

    private void ChooseImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要挂到绳上的截图或图片",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif|全部文件 (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            HangFiles(dialog.FileNames);
    }

    public void HangFiles(IEnumerable<string> paths, bool isAuto = false)
    {
        string? newestId = null;
        var next = PhotoLineRules.Clone(_data);
        foreach (var source in paths)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) continue;
                string ext = Path.GetExtension(source);
                if (!ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;

                if (isAuto)
                {
                    var bmp = LoadBitmap(source);
                    if (bmp != null && IsDuplicateAutoScreenshot(bmp)) continue;
                }

                string id = Guid.NewGuid().ToString("N");
                string dest = Path.Combine(ScreenshotsStoreDir(), id + ext.ToLowerInvariant());
                File.Copy(source, dest, true);
                string title = Path.GetFileNameWithoutExtension(source);
                next = PhotoLineRules.AddPhoto(next, dest, title, id);
                newestId = id;
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
        }
        if (newestId != null && Save(next))
        {
            Render(newestId);
            _scroll.ScrollToLeftEnd();
        }
    }

    private void RemovePhoto(string id)
    {
        var next = PhotoLineRules.RemovePhoto(_data, id);
        if (!Save(next)) return;
        Render();
    }

    private void ClearAllPhotos()
    {
        if (_data.Items.Count == 0) return;
        var next = PhotoLineRules.ClearPhotos(_data);
        if (!Save(next)) return;
        Render();
    }

    private bool Save(PhotoLineData next)
    {
        try
        {
            var config = _store.Load<PhotoLineConfig>("optional-photos.json").Value;
            config.Widgets ??= [];
            config.Widgets[_id] = PhotoLineRules.Clone(next);
            _store.Save("optional-photos.json", config);
            _data = next;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FutureSchemaException)
        {
            _status.Text = "截图挂绳保存失败：" + ex.Message;
            return false;
        }
    }

    private void ConfigureScreenshotWatcher()
    {
        DisposeWatcher();
        if (!_data.AutoWatchScreenshots) return;
        try
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures)) return;
            string target = Path.Combine(pictures, "Screenshots");
            if (!Directory.Exists(target))
            {
                string cn = Path.Combine(pictures, "屏幕截图");
                if (Directory.Exists(cn)) target = cn;
                else return;
            }
            _watcher = new FileSystemWatcher(target) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime, EnableRaisingEvents = true };
            _watcher.Created += (_, e) =>
            {
                if (!ImageExtensions.Contains(Path.GetExtension(e.FullPath), StringComparer.OrdinalIgnoreCase)) return;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(400);
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        if (IsLoaded && _data.AutoWatchScreenshots)
                            HangFiles([e.FullPath], isAuto: true);
                    });
                });
            };
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void DisposeWatcher()
    {
        try { _watcher?.Dispose(); } catch { }
        _watcher = null;
    }

    private void Render(string? newlyHungId = null)
    {
        _line.Children.Clear();
        _swaying.Clear();
        _data.Items ??= [];
        _emptyHint.Visibility = _data.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = _data.Items.Count == 0 ? "挂绳空置中" : $"已挂 {_data.Items.Count} 张截图 · 滚轮左右滑";

        for (int i = 0; i < _data.Items.Count; i++)
        {
            var item = _data.Items[i];
            var card = BuildPhotoCard(item, i, out var rotate);
            _line.Children.Add(card);
            if (item.Id == newlyHungId)
                TriggerSway(item.Id, rotate, item.TiltDegrees, 5.5);
        }
    }

    private FrameworkElement BuildPhotoCard(HangingPhotoItem item, int index, out RotateTransform rotate)
    {
        rotate = new RotateTransform(item.TiltDegrees);
        var transformRef = rotate;

        var bmp = LoadBitmap(item.ImagePath, 900);
        double scale = item.Scale > 0 ? item.Scale : 1.0;
        double cardHeight = Math.Clamp(125 * scale, 75, 230);
        double aspect = (bmp != null && bmp.PixelHeight > 0) ? (double)bmp.PixelWidth / bmp.PixelHeight : 1.33;
        double cardWidth = Math.Clamp(cardHeight * aspect, 60 * scale, 270 * scale);

        var container = new Grid
        {
            Width = cardWidth + 10,
            Height = cardHeight + 18,
            Margin = new Thickness(4, 2, 4, 4),
            RenderTransformOrigin = new Point(0.5, 0.05),
            RenderTransform = rotate,
            Cursor = Cursors.Hand,
            ToolTip = $"{item.Title}\n单击：置顶放大参考\n滚轮：左右滑动挂绳\nCtrl+滚轮：缩放大小 ({(int)(scale * 100)}%)\n右键：更多操作"
        };

        bool darkText = TextContrast.IsDarkTextStyle(_style);

        var cardFrame = new Border
        {
            Margin = new Thickness(2, 8, 2, 0),
            Width = cardWidth,
            Height = cardHeight,
            Background = new SolidColorBrush(darkText ? Color.FromArgb(16, 30, 50, 60) : Color.FromArgb(20, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(darkText ? Color.FromArgb(70, 30, 50, 60) : Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            ClipToBounds = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 8,
                ShadowDepth = 2,
                Opacity = 0.38
            }
        };

        var contentGrid = new Grid();
        if (bmp != null)
        {
            var img = new Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            contentGrid.Children.Add(img);
        }
        else
        {
            contentGrid.Children.Add(new TextBlock
            {
                Text = "图片缺失",
                FontSize = 10,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var captionBar = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(155, 12, 22, 28)),
            Padding = new Thickness(4, 2, 4, 2),
            CornerRadius = new CornerRadius(0, 0, 4, 4),
            Opacity = 0.85
        };
        var caption = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.Title) ? "截图" : item.Title,
            FontSize = 9.5,
            Foreground = Brushes.WhiteSmoke,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        captionBar.Child = caption;
        contentGrid.Children.Add(captionBar);
        cardFrame.Child = contentGrid;
        container.Children.Add(cardFrame);

        var clip = new Grid { Width = 10, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        clip.Children.Add(new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(222, 176, 118), Color.FromRgb(186, 136, 78), 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(138, 96, 48)),
            BorderThickness = new Thickness(0.8),
            CornerRadius = new CornerRadius(2)
        });
        clip.Children.Add(new Border
        {
            Height = 2.5,
            Margin = new Thickness(1, 5.5, 1, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromRgb(130, 146, 156)),
            CornerRadius = new CornerRadius(1)
        });
        container.Children.Add(clip);

        container.MouseEnter += (_, _) =>
        {
            TriggerSway(item.Id, transformRef, item.TiltDegrees, 2.8);
            captionBar.Opacity = 1.0;
        };
        container.MouseLeave += (_, _) => captionBar.Opacity = 0.85;

        container.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                double cur = item.Scale > 0 ? item.Scale : 1.0;
                double next = Math.Clamp(cur + (e.Delta > 0 ? 0.15 : -0.15), 0.6, 2.5);
                item.Scale = next;
                Save(_data);
                Render();
                e.Handled = true;
            }
        };

        Point cardPressPoint = default;
        container.PreviewMouseLeftButtonDown += (_, e) =>
        {
            cardPressPoint = e.GetPosition(this);
        };
        container.MouseLeftButtonUp += (_, e) =>
        {
            if (_hasPanned) return;
            var upPoint = e.GetPosition(this);
            if (Math.Abs(upPoint.X - cardPressPoint.X) < 6 && Math.Abs(upPoint.Y - cardPressPoint.Y) < 6)
            {
                OpenViewer(item, false);
                e.Handled = true;
            }
        };

        var cardMenu = new ContextMenu();
        var viewItem = new MenuItem { Header = "放大查看…" };
        viewItem.Click += (_, _) => OpenViewer(item, false);
        var pinItem = new MenuItem { Header = "📌 钉在桌面最前（置顶参考）" };
        pinItem.Click += (_, _) => OpenViewer(item, true);
        var copyItem = new MenuItem { Header = "复制图片到剪贴板" };
        copyItem.Click += (_, _) => CopyPhotoToClipboard(item);
        var renameItem = new MenuItem { Header = "修改备注文字…" };
        renameItem.Click += (_, _) => RenamePhoto(item);

        var moveLeft = new MenuItem { Header = "向左移一位", IsEnabled = index > 0 };
        moveLeft.Click += (_, _) =>
        {
            if (index > 0)
            {
                var next = PhotoLineRules.MovePhoto(_data, item.Id, _data.Items[index - 1].Id);
                if (Save(next)) Render(item.Id);
            }
        };
        var moveRight = new MenuItem { Header = "向右移一位", IsEnabled = index < _data.Items.Count - 1 };
        moveRight.Click += (_, _) =>
        {
            if (index < _data.Items.Count - 1)
            {
                var next = PhotoLineRules.MovePhoto(_data, item.Id, _data.Items[index + 1].Id);
                if (Save(next)) Render(item.Id);
            }
        };

        var zoomIn = new MenuItem { Header = "放大卡片 (Ctrl+滚轮上)" };
        zoomIn.Click += (_, _) =>
        {
            item.Scale = Math.Clamp((item.Scale > 0 ? item.Scale : 1.0) + 0.2, 0.6, 2.5);
            Save(_data);
            Render();
        };
        var zoomOut = new MenuItem { Header = "缩小卡片 (Ctrl+滚轮下)" };
        zoomOut.Click += (_, _) =>
        {
            item.Scale = Math.Clamp((item.Scale > 0 ? item.Scale : 1.0) - 0.2, 0.6, 2.5);
            Save(_data);
            Render();
        };
        var zoomReset = new MenuItem { Header = "恢复默认尺寸" };
        zoomReset.Click += (_, _) =>
        {
            item.Scale = 1.0;
            Save(_data);
            Render();
        };

        var folderItem = new MenuItem { Header = "打开图片所在位置" };
        folderItem.Click += (_, _) =>
        {
            if (File.Exists(item.ImagePath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(item.ImagePath)}\"") { UseShellExecute = true });
        };
        var removeItem = new MenuItem { Header = "从绳上取下（保留原图）" };
        removeItem.Click += (_, _) => RemovePhoto(item.Id);

        cardMenu.Items.Add(viewItem);
        cardMenu.Items.Add(pinItem);
        cardMenu.Items.Add(copyItem);
        cardMenu.Items.Add(renameItem);
        cardMenu.Items.Add(new Separator());
        cardMenu.Items.Add(moveLeft);
        cardMenu.Items.Add(moveRight);
        cardMenu.Items.Add(zoomIn);
        cardMenu.Items.Add(zoomOut);
        cardMenu.Items.Add(zoomReset);
        cardMenu.Items.Add(new Separator());
        cardMenu.Items.Add(folderItem);
        cardMenu.Items.Add(removeItem);
        container.ContextMenu = cardMenu;

        return container;
    }

    private void TriggerSway(string id, RotateTransform transform, double baseTilt, double amplitude)
    {
        if (!RenderBudget.AnimationsAllowed || !RenderBudget.CanDraw(this))
        {
            transform.Angle = baseTilt;
            return;
        }
        _swaying[id] = (transform, baseTilt, amplitude, Environment.TickCount64);
        if (!_swayTimer.IsEnabled) _swayTimer.Start();
    }

    private void SwayTick(object? sender, EventArgs e)
    {
        if (!RenderBudget.AnimationsAllowed || !RenderBudget.CanDraw(this) || _swaying.Count == 0)
        {
            foreach (var entry in _swaying.Values) entry.Transform.Angle = entry.BaseTilt;
            _swaying.Clear();
            _swayTimer.Stop();
            return;
        }
        long now = Environment.TickCount64;
        foreach (var (id, state) in _swaying.ToArray())
        {
            double elapsed = (now - state.StartTick) / 1000.0;
            if (elapsed >= 0.9)
            {
                state.Transform.Angle = state.BaseTilt;
                _swaying.Remove(id);
            }
            else
            {
                double damping = Math.Exp(-3.4 * elapsed);
                state.Transform.Angle = state.BaseTilt + state.Amplitude * damping * Math.Cos(elapsed * 14.0);
            }
        }
        if (_swaying.Count == 0) _swayTimer.Stop();
    }

    private void CopyPhotoToClipboard(HangingPhotoItem item)
    {
        try
        {
            var bmp = LoadBitmap(item.ImagePath);
            if (bmp != null)
            {
                Clipboard.SetImage(bmp);
                _status.Text = "已复制图片到剪贴板";
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _status.Text = "复制失败，剪贴板正忙";
        }
    }

    private void RenamePhoto(HangingPhotoItem item)
    {
        var owner = Window.GetWindow(this);
        if (owner == null) return;
        var nextTitle = TextInputDialog.Ask(owner, "截图备注", item.Title);
        if (nextTitle == null) return;
        var next = PhotoLineRules.RenamePhoto(_data, item.Id, nextTitle);
        if (Save(next)) Render();
    }

    private void OpenViewer(HangingPhotoItem item, bool pinned)
    {
        var bmp = LoadBitmap(item.ImagePath);
        if (bmp == null)
        {
            _status.Text = "原图文件已不存在";
            return;
        }
        var viewer = new PhotoPinWindow(item, bmp, pinned, () => CopyPhotoToClipboard(item), () => RenamePhoto(item), () => RemovePhoto(item.Id));
        if (!pinned) viewer.Owner = Window.GetWindow(this);
        viewer.Show();
    }

    internal static BitmapSource? LoadBitmap(string path, int? decodeWidth = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth is int w && w > 0) image.DecodePixelWidth = w;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    internal int TestPhotoCount => _data.Items.Count;
    internal IReadOnlyList<HangingPhotoItem> TestPhotos => _data.Items;
}

internal sealed class PhotoPinWindow : Window
{
    public PhotoPinWindow(HangingPhotoItem item, BitmapSource bitmap, bool startPinned, Action copyAction, Action renameAction, Action removeAction)
    {
        Title = string.IsNullOrWhiteSpace(item.Title) ? "截图查看" : item.Title;
        Width = Math.Clamp(bitmap.Width + 48, 380, SystemParameters.WorkArea.Width * 0.85);
        Height = Math.Clamp(bitmap.Height + 96, 280, SystemParameters.WorkArea.Height * 0.85);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(20, 30, 38));
        Topmost = startPinned;
        double zoom = 1.0;

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(toolbar, Dock.Top);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Right);

        var pinBtn = new Button { Content = Topmost ? "📌 已钉在最前" : "📌 钉在最前", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0) };
        pinBtn.Click += (_, _) =>
        {
            Topmost = !Topmost;
            if (Topmost) Owner = null;
            pinBtn.Content = Topmost ? "📌 已钉在最前" : "📌 钉在最前";
        };
        var copyBtn = new Button { Content = "📋 复制", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0) };
        copyBtn.Click += (_, _) => copyAction();
        var noteBtn = new Button { Content = "✎ 备注", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0) };
        noteBtn.Click += (_, _) => renameAction();
        var delBtn = new Button { Content = "🗑 取下", Padding = new Thickness(10, 5, 10, 5) };
        delBtn.Click += (_, _) => { removeAction(); Close(); };

        actions.Children.Add(pinBtn);
        actions.Children.Add(copyBtn);
        actions.Children.Add(noteBtn);
        actions.Children.Add(delBtn);
        toolbar.Children.Add(actions);

        var titleText = new TextBlock
        {
            Text = $"{Title} · 滚轮缩放 (100%) · Ctrl+滚轮调透明度 (100%)",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.LightGray,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        toolbar.Children.Add(titleText);
        root.Children.Add(toolbar);

        var scale = new ScaleTransform(1, 1);
        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            LayoutTransform = scale
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var viewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(14, 22, 28)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Child = image
            }
        };

        PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.08 : -0.08), 0.20, 1.0);
                titleText.Text = $"{Title} · 滚轮缩放 ({zoom * 100:0}%) · Ctrl+滚轮调透明度 ({(int)(Opacity * 100)}%)";
                e.Handled = true;
            }
        };

        viewer.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            zoom = Math.Clamp(zoom + (e.Delta > 0 ? 0.15 : -0.15), 0.25, 4.0);
            scale.ScaleX = scale.ScaleY = zoom;
            titleText.Text = $"{Title} · 滚轮缩放 ({zoom * 100:0}%) · Ctrl+滚轮调透明度 ({(int)(Opacity * 100)}%)";
            e.Handled = true;
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        root.Children.Add(viewer);
    }
}
