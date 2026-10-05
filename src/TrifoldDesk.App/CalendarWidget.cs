using System.Windows.Controls.Primitives;
using System.Windows.Threading;
namespace TrifoldDesk;
public sealed class CalendarWidget : Border
{
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly UniformGrid _days = new() { Columns = 7, Rows = 6, Height = 228 };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly bool _paper;
    private DateTime _today = DateTime.Today;
    private readonly StackPanel _counters=new();
    private readonly Func<IReadOnlyList<DateCounter>> _readDates;
    private readonly Func<List<DateCounter>,bool> _saveDates;
    public DateTime DisplayMonth { get; private set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public DateTime SelectedDate { get; private set; } = DateTime.Today;
    public CalendarWidget(string style,Func<IReadOnlyList<DateCounter>>? readDates=null,Func<List<DateCounter>,bool>? saveDates=null)
    {
        _readDates=readDates??(()=>Array.Empty<DateCounter>());_saveDates=saveDates??(_=>false);
        _paper = style == "paper";
        Background = Brushes.Transparent; Padding = new Thickness(4); BorderThickness = new Thickness(0);
        var root = new StackPanel(); Child = root;
        var top = new Grid { Margin = new Thickness(0,0,0,12) };
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); top.ColumnDefinitions.Add(new() { Width = new GridLength(28) }); top.ColumnDefinitions.Add(new()); top.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        var today = ActionButton("今天", GoToday); today.ToolTip = "回到今天"; top.Children.Add(today);
        var prev = ActionButton("‹", () => ChangeMonth(-1)); prev.ToolTip = "上个月"; Grid.SetColumn(prev,1); top.Children.Add(prev);
        Grid.SetColumn(_title,2); top.Children.Add(_title);
        var next = ActionButton("›", () => ChangeMonth(1)); next.ToolTip = "下个月"; Grid.SetColumn(next,3); top.Children.Add(next); root.Children.Add(top);
        var week = new UniformGrid { Columns = 7, Margin = new Thickness(0,0,0,4) };
        foreach (var day in new[] { "一", "二", "三", "四", "五", "六", "日" }) week.Children.Add(Label(day,12,day is "六" or "日" ? "#D28B91" : "#A6ABB5"));
        root.Children.Add(week); root.Children.Add(_days);
        root.Children.Add(_counters);
        _clock.Tick += (_, _) => { if (_today != DateTime.Today) { _today = DateTime.Today; Render(); } };
        Loaded += (_, _) => { _today = DateTime.Today; Render(); _clock.Start(); }; Unloaded += (_, _) => _clock.Stop(); Render();
    }
    private static SolidColorBrush Fill(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private TextBlock Label(string text,double size,string color) => new() { Text=text,FontSize=size,Foreground=Fill(_paper && color != "#D28B91" ? "#554D3E" : color),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
    private Button ActionButton(string text,Action action)
    {
        var button=new Button { Content=Label(text,12,"#CFD2D9"),Padding=new Thickness(4),Background=Brushes.Transparent,BorderThickness=new Thickness(0) };
        button.Click+=(_,_)=>action(); return button;
    }
    public void ChangeMonth(int offset)
    {
        var next=DisplayMonth.AddMonths(offset); if(next.Year is <1902 or >2099)return; DisplayMonth=next; Render();
    }
    public void GoToday() { SelectedDate=_today=DateTime.Today; DisplayMonth=new(_today.Year,_today.Month,1); Render(); }
    private void Render()
    {
        RenderCounters();
        _title.Text=DisplayMonth.ToString("yyyy年M月"); _title.Foreground=Fill(_paper ? "#393D38" : "#F0F7FA"); _days.Children.Clear();
        foreach(var day in CalendarMonth.Days(DisplayMonth))
        {
            bool weekend=day.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var button=new Button { Content=Label(day.Date.Day.ToString(),13,weekend ? "#D28B91" : "#D7DBE2"),Background=Brushes.Transparent,BorderBrush=Brushes.Transparent,BorderThickness=new Thickness(0),Margin=new Thickness(2),Padding=new Thickness(0),ToolTip=day.Date.ToString("yyyy-MM-dd dddd")+" · "+CalendarMonth.LunarText(day.Date),Opacity=day.InMonth?1:.25 };
            if(day.Date==SelectedDate)button.Background=Fill(_paper ? "#509DBBB1" : "#50288095");
            if(day.Date==_today) { var cell=new Grid(); var label=(UIElement)button.Content; button.Content=null; cell.Children.Add(label); cell.Children.Add(new Border { Width=4,Height=4,CornerRadius=new CornerRadius(2),Background=Fill("#6BD2AA"),VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,3),HorizontalAlignment=HorizontalAlignment.Center }); button.Content=cell; }
            button.Click+=(_,_)=>{SelectedDate=day.Date;DisplayMonth=new(day.Date.Year,day.Date.Month,1);Render();}; _days.Children.Add(button);
        }
    }
    private void EditCounter(DateCounter? item)
    {
        var changed=DateCounterDialog.Ask(Window.GetWindow(this)!,item);if(changed==null)return;
        var next=_readDates().ToList();int index=next.FindIndex(d=>d.Id==changed.Id);if(index<0)next.Add(changed);else next[index]=changed;
        if(_saveDates(next))RenderCounters();
    }
    public void AddDateCounter()=>EditCounter(null);
    private void RenderCounters()
    {
        _counters.Children.Clear();
        foreach(var item in _readDates())
        {
            int days=item.DaysFrom(_today);
            var row=new Grid{Margin=new Thickness(0,8,0,2)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            row.Children.Add(new TextBlock{Text=item.Name,FontSize=12,Foreground=Fill(_paper?"#554D3E":"#D7DBE2"),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,0,8,0)});
            var count=new TextBlock{Text=days==0?"今天":(days>0?"还有 ":"已过 ")+Math.Abs(days)+" 天",FontSize=18,Foreground=Fill(_paper?"#393D38":"#F0F7FA"),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(count,1);row.Children.Add(count);
            var button=new Button{Content=row,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(2),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip=item.Date.ToString("yyyy年M月d日")};button.Click+=(_,_)=>EditCounter(item);
            var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Stretch);
            button.Template=new ControlTemplate(typeof(Button)){VisualTree=presenter};
            var menu=new ContextMenu();var edit=new MenuItem{Header="编辑"};edit.Click+=(_,_)=>EditCounter(item);menu.Items.Add(edit);
            var remove=new MenuItem{Header="删除"};remove.Click+=(_,_)=>{if(_saveDates(_readDates().Where(d=>d.Id!=item.Id).ToList()))RenderCounters();};menu.Items.Add(remove);button.ContextMenu=menu;_counters.Children.Add(button);
        }
    }
    internal int DateCounterCount=>_counters.Children.Count;
    internal int DayCount=>_days.Children.Count;
}
