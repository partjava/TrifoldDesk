using System.Windows.Controls.Primitives;
namespace TrifoldDesk;

// Pure visual samples: no widget lifecycle, timers, services, file paths or commands.
internal static class PluginPreview
{
    private static readonly Brush Accent = Fill("#74E1D1");
    internal static SolidColorBrush Fill(string color) => new((Color)ColorConverter.ConvertFromString(color));
    internal static FrameworkElement Create(string id)
    {
        var root = new Grid { Height = 144, IsHitTestVisible = false, ClipToBounds = true };
        root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        FrameworkElement content = id switch
        {
            "system-summary" => Summary(), "folder" => Folder(), "calendar" => Calendar(), "clock" => Clock(), "controls" => Controls(),
            "workbench" => SampleLines("今天的计划", "✓ 整理资料", "○ 完成一套练习", "便签 / 待办"),
            "weather" => SampleLines("北京 · 晴", "24°", "湿度 48% · 风速 8 km/h", "更新时间 15:00"),
            "music" => SampleLines("♫", "正在播放的歌曲", "艺术家 · 系统媒体", "右键控制播放"),
            "project-browser" => SampleLines("▸ 我的项目", "  ▸ src", "  README.md", "Git · main · 2 项变更"),
            _ => Label("预览", 14)
        };
        root.Children.Add(new Border { Background = Fill("#192731"), CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 7, 10, 7), Child = content });
        var caption = Label("示例数据", 10, "#B8C9D2"); caption.Margin = new Thickness(0, 5, 0, 0); caption.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(caption, 1); root.Children.Add(caption); return root;
    }
    private static FrameworkElement SampleLines(params string[] lines){var stack=new StackPanel();foreach(var line in lines)stack.Children.Add(Label(line,14));return stack;}
    private static TextBlock Label(string text, double size, string color = "#F0F7FA") => new()
    {
        Text = text, FontSize = size, Foreground = Fill(color), VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private static FrameworkElement Summary()
    {
        var tiles = new UniformGrid { Columns = 2, Rows = 2 };
        tiles.Children.Add(Metric("cpu", "CPU", "24%", 24));
        tiles.Children.Add(Metric("memory", "内存", "52%", 52));
        tiles.Children.Add(Metric("gpu", "GPU · 3D", "18%", 18));
        tiles.Children.Add(Metric("network", "网络", "↓ 1.2 MB/s", 12));
        return tiles;
    }
    private static FrameworkElement Metric(string kind, string name, string value, double amount)
    {
        var stack = new StackPanel { Margin = new Thickness(4, 1, 8, 4) };
        var header = new DockPanel();
        var glyph = new HardwareGlyph { Kind = kind, Width = 15, Height = 15, Ink = Accent }; DockPanel.SetDock(glyph, Dock.Right); header.Children.Add(glyph);
        header.Children.Add(Label(name, 9, "#A8BDC8")); stack.Children.Add(header); stack.Children.Add(Label(value, kind == "network" ? 13 : 19));
        if (kind == "memory") { var gauge = new CapacityGauge { Height = 5, Segmented = true, Accent = Accent }; gauge.UpdateValue(amount); stack.Children.Add(gauge); }
        else
        {
            var plot = new MetricPlot { Height = 10, Accent = Accent, Percent = kind != "network" };
            foreach (double factor in new[] { .8, .9, .6, 1.3, 1.05, .7, 1.1, 1 }) plot.Add(amount * factor);
            stack.Children.Add(plot);
        }
        return stack;
    }
    private static FrameworkElement Folder()
    {
        var icons = new UniformGrid { Columns = 3, Rows = 2, Margin = new Thickness(4) };
        string[] names = ["浏览器", "终端", "编辑器", "文档", "图片", "工具"];
        string[] symbols = ["◎", ">_", "{ }", "▤", "▧", "⚙"];
        string[] colors = ["#66B9EC", "#74E1D1", "#B395FF", "#F3CB7A", "#EEA4BE", "#9ABBCB"];
        for (int i = 0; i < names.Length; i++)
        {
            var item = new StackPanel { Margin = new Thickness(3, 0, 3, 3) };
            var icon = Label(symbols[i], 21, colors[i]); icon.HorizontalAlignment = HorizontalAlignment.Center; item.Children.Add(icon);
            var name = Label(names[i], 10); name.HorizontalAlignment = HorizontalAlignment.Center; item.Children.Add(name); icons.Children.Add(item);
        }
        return icons;
    }
    private static FrameworkElement Calendar()
    {
        var stack = new StackPanel();
        var title = Label("2026年10月", 11); title.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(title);
        var days = new UniformGrid { Columns = 7, Rows = 7, Height = 90, Margin = new Thickness(0, 4, 0, 0) };
        foreach (string day in new[] { "一", "二", "三", "四", "五", "六", "日" })
        { var label = Label(day, 9, day is "六" or "日" ? "#D28B91" : "#A6ABB5"); label.HorizontalAlignment = HorizontalAlignment.Center; days.Children.Add(label); }
        var sampleDate = new DateTime(2026, 10, 6);
        foreach (var day in CalendarMonth.Days(sampleDate))
        {
            var label = Label(day.Date.Day.ToString(), 9, day.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "#D28B91" : "#D7DBE2");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            days.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = day.Date == sampleDate ? Fill("#50288095") : Brushes.Transparent, Opacity = day.InMonth ? 1 : .25, Child = label });
        }
        stack.Children.Add(days); return stack;
    }
    private static FrameworkElement Clock()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        stack.Children.Add(Label("10:08", 42)); stack.Children.Add(Label("10月6日  星期二", 11, "#A8BDC8"));
        stack.Children.Add(Label(CalendarMonth.LunarText(new DateTime(2026, 10, 6)), 10, "#A8BDC8")); return stack;
    }
    private static FrameworkElement Controls()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) };
        foreach (var (name, amount) in new[] { ("音量", 65d), ("亮度", 80d) })
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(42) }); row.ColumnDefinitions.Add(new());
            row.Children.Add(Label(name, 11, "#A8BDC8"));
            var gauge = new CapacityGauge { Height = 5, Accent = Accent, VerticalAlignment = VerticalAlignment.Center }; gauge.UpdateValue(amount);
            Grid.SetColumn(gauge, 1); row.Children.Add(gauge); stack.Children.Add(row);
        }
        var actions = new UniformGrid { Columns = 2 };
        foreach (string name in new[] { "常亮", "设置" }) actions.Children.Add(new Border { Background = Fill("#12FFFFFF"), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 0), Child = Label(name, 11) });
        stack.Children.Add(actions); return stack;
    }
}

