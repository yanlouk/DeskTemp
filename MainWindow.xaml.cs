using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DeskTemp.Services;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace DeskTemp;

public partial class MainWindow : Window
{
    private const int HistoryLength = 60;

    private static readonly SolidColorBrush CoolBrush = Frozen("#6EE7B7");
    private static readonly SolidColorBrush WarmBrush = Frozen("#FCD34D");
    private static readonly SolidColorBrush HotBrush = Frozen("#FB7185");
    private static readonly SolidColorBrush UnknownBrush = Frozen("#5F6A7C");
    private static readonly SolidColorBrush WarnBrush = Frozen("#FBBF24");
    private static readonly SolidColorBrush TipPrimary = Frozen("#EEF2F8");
    private static readonly SolidColorBrush TipMuted = Frozen("#9AA6B8");
    private static readonly SolidColorBrush TextPrimaryBrush = Frozen("#EEF2F8");
    private static readonly SolidColorBrush TextMutedBrush = Frozen("#7C8798");

    private readonly bool _preview;
    private readonly bool _isAdmin;
    private readonly AppSettings _settings;

    private readonly List<double> _cpuHistory = new();
    private readonly List<double> _gpuHistory = new();

    private SensorService? _sensor;
    private WinForms.NotifyIcon? _tray;
    private WinForms.ToolStripMenuItem? _trayShowItem;
    private WinForms.ToolStripMenuItem? _trayTopItem;
    private bool _locked;
    private bool _closing;

    private TextBlock? _cpuFanTipContent;
    private TextBlock? _gpuFanTipContent;
    private string _cpuFanTipCache = string.Empty;
    private string _gpuFanTipCache = string.Empty;
    private TextBlock? _memTipContent;
    private string _memTipCache = string.Empty;

    private IReadOnlyList<StorageReading> _lastDisks = Array.Empty<StorageReading>();
    private bool _lastStorageDenied;
    private bool _storageLoaded;
    private string _storageCacheKey = string.Empty;

    public MainWindow(bool preview, bool isAdmin)
    {
        _preview = preview;
        _isAdmin = isAdmin;
        _settings = AppSettings.Load();

        InitializeComponent();
        InitializeFanTooltips();
        InitializeMemoryTooltip();

        ShowStorageMenuItem.IsChecked = _settings.ShowStorage;
        StorageSection.Visibility = _settings.ShowStorage ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.ShowStorage)
            RenderStorage(Array.Empty<StorageReading>(), false);

        _locked = _settings.Locked;
        Topmost = _settings.Topmost;
        PlaceWindow();
        CreateTrayIcon();

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(OpacityProperty, fade);

        var slide = new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        CardOffset.BeginAnimation(TranslateTransform.YProperty, slide);

        _sensor = new SensorService(_preview) { StorageEnabled = _settings.ShowStorage };
        _sensor.Updated += OnSensorUpdated;
        _sensor.Start();
    }

    private void OnSensorUpdated(SensorSnapshot snapshot)
    {
        if (_closing)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (!_closing)
                Apply(snapshot);
        });
    }

    private void Apply(SensorSnapshot s)
    {
        CpuNameText.Text = s.CpuName;
        GpuNameText.Text = s.GpuName;
        CpuNameText.ToolTip = s.CpuName;
        GpuNameText.ToolTip = s.GpuName;

        CpuTempText.Text = FormatTemp(s.CpuTemp);
        CpuTempText.Foreground = TempBrush(s.CpuTemp);

        GpuTempText.Text = FormatTemp(s.GpuTemp);
        GpuTempText.Foreground = TempBrush(s.GpuTemp);

        CpuLoadText.Text = FormatLoad(s.CpuLoad);
        GpuLoadText.Text = FormatLoad(s.GpuLoad);

        SetBar(CpuBarTrack, CpuBarFill, s.CpuLoad);
        SetBar(GpuBarTrack, GpuBarFill, s.GpuLoad);

        if (s.CpuTemp == null && !_isAdmin)
        {
            CpuDetailText.Text = "需管理员";
            CpuDetailText.Foreground = WarnBrush;
        }
        else
        {
            CpuDetailText.Text = s.CpuClockMhz is double mhz ? $"{mhz / 1000.0:0.0} GHz" : "— GHz";
            CpuDetailText.Foreground = TextMutedBrush;
        }

        GpuDetailText.Text = s.GpuPower is double watts ? $"{watts:0} W" : "— W";

        if (s.MemoryLoad is double memoryLoad)
        {
            CpuMemText.Text = $"{memoryLoad:0}%";
            CpuMemText.Foreground = memoryLoad >= 90 ? HotBrush
                : memoryLoad >= 80 ? WarnBrush
                : TextPrimaryBrush;
        }
        else
        {
            CpuMemText.Text = "--%";
            CpuMemText.Foreground = TextMutedBrush;
        }

        UpdateMemoryTooltip(s);

        GpuMemText.Text = s.GpuMemoryUsedMb is double usedMem &&
                          s.GpuMemoryTotalMb is double totalMem && totalMem > 0
            ? $"显存 {usedMem / 1024:0.0}/{totalMem / 1024:0.#} GB"
            : "显存 --";

        UpdateFanTooltips(s.Fans);
        _lastDisks = s.Disks;
        _lastStorageDenied = s.StorageAccessDenied;
        _storageLoaded = s.StorageLoaded;
        RenderStorage(s.Disks, s.StorageAccessDenied);

        AppendHistory(_cpuHistory, s.CpuTemp);
        AppendHistory(_gpuHistory, s.GpuTemp);
        CpuSpark.Values = _cpuHistory.ToArray();
        GpuSpark.Values = _gpuHistory.ToArray();
    }

    private void InitializeFanTooltips()
    {
        _cpuFanTipContent = NewFanTipContent();
        _gpuFanTipContent = NewFanTipContent();
        CpuLeftPanel.ToolTip = NewFanToolTip(_cpuFanTipContent);
        GpuLeftPanel.ToolTip = NewFanToolTip(_gpuFanTipContent);

        ToolTipService.SetInitialShowDelay(CpuLeftPanel, 200);
        ToolTipService.SetBetweenShowDelay(CpuLeftPanel, 80);
        ToolTipService.SetShowDuration(CpuLeftPanel, 60000);
        ToolTipService.SetInitialShowDelay(GpuLeftPanel, 200);
        ToolTipService.SetBetweenShowDelay(GpuLeftPanel, 80);
        ToolTipService.SetShowDuration(GpuLeftPanel, 60000);

        _cpuFanTipContent.Text = "正在读取风扇…";
        _gpuFanTipContent.Text = "正在读取风扇…";
    }

    private void InitializeMemoryTooltip()
    {
        _memTipContent = NewFanTipContent();
        CpuMemPanel.ToolTip = NewFanToolTip(_memTipContent);
        ToolTipService.SetInitialShowDelay(CpuMemPanel, 200);
        ToolTipService.SetBetweenShowDelay(CpuMemPanel, 80);
        ToolTipService.SetShowDuration(CpuMemPanel, 60000);
    }

    private void UpdateMemoryTooltip(SensorSnapshot s)
    {
        if (_memTipContent is null)
            return;

        string key = $"{s.MemoryLoad:0}\u0001{s.MemoryUsedGb:0.0}\u0001{s.MemoryTotalGb:0.#}";
        if (key == _memTipCache)
            return;

        _memTipCache = key;
        _memTipContent.Inlines.Clear();

        if (s.MemoryUsedGb is not double usedGb || s.MemoryTotalGb is not double totalGb)
        {
            _memTipContent.Inlines.Add(new Run("内存占用")
            {
                FontSize = 10.5,
                Foreground = TipMuted,
            });
            return;
        }

        _memTipContent.Inlines.Add(new Run("已用")
        {
            FontSize = 10.5,
            Foreground = TipMuted,
        });
        _memTipContent.Inlines.Add(new Run("  ")
        {
            FontSize = 10.5,
        });
        _memTipContent.Inlines.Add(new Run($"{usedGb:0.0}")
        {
            FontSize = 11,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = TipPrimary,
        });
        _memTipContent.Inlines.Add(new Run($" / {totalGb:0.#} GB")
        {
            FontSize = 8.5,
            Foreground = TipMuted,
        });
    }

    private static TextBlock NewFanTipContent() => new()
    {
        FontSize = 10.5,
        Foreground = TipPrimary,
        LineHeight = 17,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 240,
    };

    private static ToolTip NewFanToolTip(TextBlock content) => new()
    {
        Content = content,
        Background = new SolidColorBrush(Color.FromRgb(23, 27, 35)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(10, 7, 10, 7),
        HasDropShadow = false,
    };

    private void UpdateFanTooltips(IReadOnlyList<FanReading> fans)
    {
        if (_cpuFanTipContent is null || _gpuFanTipContent is null)
            return;

        var board = fans.Where(f => !f.IsGpu).ToList();
        var gpu = fans.Where(f => f.IsGpu).ToList();

        RenderFanTip(_cpuFanTipContent, BuildBoardFanLines(board), ref _cpuFanTipCache);
        RenderFanTip(_gpuFanTipContent, BuildGpuFanLines(gpu), ref _gpuFanTipCache);
    }

    private void RenderStorage(IReadOnlyList<StorageReading> disks, bool denied)
    {
        if (!_settings.ShowStorage)
        {
            ContentGrid.Margin = new Thickness(16, 13, 16, 14);
            StorageSection.Visibility = Visibility.Collapsed;
            _storageCacheKey = string.Empty;
            return;
        }

        ContentGrid.Margin = new Thickness(16, 13, 16, 7);
        StorageSection.Visibility = Visibility.Visible;
        bool accessDenied = denied || (!_isAdmin && disks.Count == 0);
        bool loading = !accessDenied && disks.Count == 0 && !_storageLoaded;
        string key = accessDenied
            ? "denied"
            : loading
                ? "loading"
                : disks.Count == 0
                    ? "none"
                    : string.Join("|", disks.Select(d => $"{d.Name}\u0001{d.Capacity}\u0001{d.Temperature:0}"));

        if (key == _storageCacheKey)
            return;
        _storageCacheKey = key;

        StoragePanel.Children.Clear();
        Brush muted = TextMutedBrush;
        Brush primary = TextPrimaryBrush;

        StoragePanel.Children.Add(new TextBlock
        {
            Text = "存储",
            FontSize = 8,
            Foreground = muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 2),
        });

        if (accessDenied)
        {
            StoragePanel.Children.Add(new TextBlock
            {
                Text = "需管理员",
                FontSize = 8,
                Foreground = WarnBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            });
            return;
        }

        if (disks.Count == 0)
        {
            StoragePanel.Children.Add(new TextBlock
            {
                Text = loading ? "读取中…" : "未检测到",
                FontSize = 8,
                Foreground = muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            });
            return;
        }

        for (int i = 0; i < disks.Count; i++)
        {
            if (i > 0)
            {
                StoragePanel.Children.Add(new TextBlock
                {
                    Text = "·",
                    FontSize = 9,
                    Foreground = muted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 2),
                });
            }

            var item = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
                ToolTip = $"{disks[i].FullName} · {disks[i].SizeGb:0} GB · {disks[i].Temperature:0}°C",
            };
            item.Inlines.Add(new Run(disks[i].Name) { FontSize = 8, Foreground = muted });
            if (disks[i].Capacity.Length > 0)
                item.Inlines.Add(new Run($" ({disks[i].Capacity})") { FontSize = 8, Foreground = primary });
            item.Inlines.Add(new Run("  ") { FontSize = 8 });
            item.Inlines.Add(new Run($"{disks[i].Temperature:0}°")
            {
                FontSize = 8.5,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = primary,
            });
            StoragePanel.Children.Add(item);
        }
    }

    private static List<FanTipLine> BuildBoardFanLines(List<FanReading> board)
    {
        var lines = new List<FanTipLine>();
        if (board.Count == 0)
        {
            lines.Add(new FanTipLine("未检测到主板风扇", null));
            return lines;
        }

        var pump = FindPump(board);
        if (pump is not null)
            lines.Add(new FanTipLine("水泵", pump.Rpm));

        var rest = board.Where(f => !ReferenceEquals(f, pump)).ToList();
        for (int i = 0; i < rest.Count; i++)
        {
            string sensor = rest[i].SensorName;
            string label;
            if (sensor.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                label = "CPU风扇";
            else if (sensor.Contains("Chassis", StringComparison.OrdinalIgnoreCase))
                label = rest.Count > 1 ? $"机箱风扇{i + 1}" : "机箱风扇";
            else
                label = rest.Count > 1 ? $"风扇{i + 1}" : "风扇";

            lines.Add(new FanTipLine(label, rest[i].Rpm));
        }

        return lines;
    }

    private static FanReading? FindPump(List<FanReading> board)
    {
        var explicitPump = board.FirstOrDefault(f =>
            f.Label == "水泵" ||
            f.SensorName.Contains("Pump", StringComparison.OrdinalIgnoreCase));
        if (explicitPump is not null)
            return explicitPump;

        // Many boards expose an AIO pump as a plain "Fan #n" channel. A pump
        // normally spins much faster than case fans, so use that as a fallback
        // instead of inventing a channel number in the UI.
        var fastest = board.OrderByDescending(f => f.Rpm).FirstOrDefault();
        if (fastest is null)
            return null;

        var second = board
            .Where(f => !ReferenceEquals(f, fastest))
            .OrderByDescending(f => f.Rpm)
            .FirstOrDefault();

        if (fastest.Rpm >= 3000)
            return fastest;
        if (second is not null && fastest.Rpm >= 2500 && fastest.Rpm >= second.Rpm * 1.8)
            return fastest;

        return null;
    }

    private static List<FanTipLine> BuildGpuFanLines(List<FanReading> gpu)
    {
        if (gpu.Count == 0)
            return new List<FanTipLine> { new("未检测到显卡风扇", null) };
        if (gpu.Count == 1)
            return new List<FanTipLine> { new("显卡风扇", gpu[0].Rpm) };

        return gpu.Select(f => new FanTipLine($"{f.Label}风扇", f.Rpm)).ToList();
    }

    private static void RenderFanTip(TextBlock block, List<FanTipLine> lines, ref string cache)
    {
        string key = string.Join("\n", lines.Select(l => $"{l.Label}\u0001{l.Rpm?.ToString("0") ?? "-"}"));
        if (key == cache)
            return;

        cache = key;
        block.Inlines.Clear();

        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                block.Inlines.Add(new LineBreak());

            block.Inlines.Add(new Run(lines[i].Label)
            {
                FontSize = 10.5,
                Foreground = TipMuted,
            });

            if (lines[i].Rpm is not double rpm)
                continue;

            block.Inlines.Add(new Run("  ")
            {
                FontSize = 10.5,
            });
            block.Inlines.Add(new Run($"{rpm:0}")
            {
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = TipPrimary,
            });
            block.Inlines.Add(new Run(" RPM")
            {
                FontSize = 8.5,
                Foreground = TipMuted,
            });
        }
    }

    private readonly record struct FanTipLine(string Label, double? Rpm);

    private static void SetBar(FrameworkElement track, FrameworkElement fill, double? percent)
    {
        double width = track.ActualWidth * Math.Clamp(percent ?? 0, 0, 100) / 100.0;
        fill.Width = Math.Max(0, width);
        fill.Opacity = percent.HasValue ? 1 : 0.25;
    }

    private static void AppendHistory(List<double> history, double? value)
    {
        if (value is not double v)
            return;

        if (history.Count == 0)
        {
            for (int i = 0; i < HistoryLength; i++)
                history.Add(v);
            return;
        }

        history.Add(v);
        if (history.Count > HistoryLength)
            history.RemoveAt(0);
    }

    private static string FormatTemp(double? value) => value is double v ? v.ToString("0") : "--";

    private static string FormatLoad(double? value) => value is double v ? $"负载 {v:0}%" : "负载 --";

    private static Brush TempBrush(double? value)
    {
        if (value is not double v)
            return UnknownBrush;
        if (v >= 80)
            return HotBrush;
        if (v >= 60)
            return WarmBrush;
        return CoolBrush;
    }

    private void PlaceWindow()
    {
        var work = SystemParameters.WorkArea;
        double defaultLeft = work.Right - Width - 24;
        double defaultTop = work.Top + 64;

        bool restored = false;
        if (_settings.Left is double left && _settings.Top is double top)
        {
            bool visible = left + 60 > SystemParameters.VirtualScreenLeft &&
                           left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
                           top + 40 > SystemParameters.VirtualScreenTop &&
                           top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
            if (visible)
            {
                Left = left;
                Top = top;
                restored = true;
            }
        }

        if (!restored)
        {
            Left = defaultLeft;
            Top = defaultTop;
        }
    }

    private void CreateTrayIcon()
    {
        Drawing.Icon icon;
        try
        {
            var info = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
            using var stream = info.Stream;
            icon = new Drawing.Icon(stream);
        }
        catch
        {
            icon = Drawing.SystemIcons.Application;
        }

        var menu = new WinForms.ContextMenuStrip();
        _trayShowItem = new WinForms.ToolStripMenuItem("隐藏窗口");
        _trayShowItem.Click += (_, _) => ToggleVisibility();

        _trayTopItem = new WinForms.ToolStripMenuItem("始终置顶")
        {
            CheckOnClick = true,
        };
        _trayTopItem.Click += (_, _) => SetTopmost(_trayTopItem.Checked);

        var exitItem = new WinForms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Close();

        menu.Items.Add(_trayShowItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_trayTopItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitItem);
        menu.Opening += (_, _) =>
        {
            _trayShowItem.Text = IsVisible ? "隐藏窗口" : "显示窗口";
            _trayTopItem.Checked = Topmost;
        };

        _tray = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "DeskTemp · 温度监控",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ToggleVisibility();
        _tray.Icon = icon;
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
            if (_sensor != null)
                _sensor.Paused = true;
        }
        else
        {
            Show();
            Activate();
            if (_sensor != null)
                _sensor.Paused = false;
        }
    }

    private void SetTopmost(bool topmost)
    {
        Topmost = topmost;
        _settings.Topmost = topmost;
        _settings.Save();
        TopmostMenuItem.IsChecked = topmost;
        if (_trayTopItem != null)
            _trayTopItem.Checked = topmost;
    }

    private void OpenMenu()
    {
        TopmostMenuItem.IsChecked = Topmost;
        LockMenuItem.IsChecked = _locked;
        ShowStorageMenuItem.IsChecked = _settings.ShowStorage;

        var menu = ContextMenu;
        if (menu == null)
            return;

        menu.PlacementTarget = MoreButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.HorizontalOffset = -8;
        menu.IsOpen = true;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e) => OpenMenu();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
        => SetTopmost(TopmostMenuItem.IsChecked);

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _locked = LockMenuItem.IsChecked;
        _settings.Locked = _locked;
        _settings.Save();
    }

    private void ShowStorageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowStorage = ShowStorageMenuItem.IsChecked;
        _settings.Save();
        _storageCacheKey = string.Empty;

        if (_settings.ShowStorage)
        {
            StorageSection.Visibility = Visibility.Visible;
            _storageLoaded = false;
            RenderStorage(Array.Empty<StorageReading>(), false);
        }
        else
        {
            StorageSection.Visibility = Visibility.Collapsed;
            _storageLoaded = false;
        }

        if (_sensor != null)
        {
            _sensor.StorageEnabled = _settings.ShowStorage;
            if (_settings.ShowStorage)
                _sensor.InvalidateStorage();
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_locked || e.ButtonState != MouseButtonState.Pressed)
            return;
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
            return;

        try
        {
            DragMove();
            _settings.Left = Left;
            _settings.Top = Top;
            _settings.Save();
        }
        catch
        {
            // DragMove can throw if the mouse button was released early.
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;
            try
            {
                current = VisualTreeHelper.GetParent(current);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _closing = true;
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Topmost = Topmost;
        _settings.Locked = _locked;
        _settings.Save();

        _sensor?.Dispose();
        _sensor = null;

        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
