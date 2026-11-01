using System.Windows.Threading;
using TrifoldDesk.Services;
namespace TrifoldDesk;
public sealed class HardwareDetailsWindow : Window
{
 private readonly HardwareSensorService _service=new();private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};private readonly ListBox _values=new(){Background=new SolidColorBrush(Color.FromRgb(14,22,30)),Foreground=Brushes.WhiteSmoke,BorderBrush=new SolidColorBrush(Color.FromRgb(45,65,82))};private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(5)};private bool _busy,_closed;
 public HardwareDetailsWindow()
 {
  Title="硬件传感器";Width=680;Height=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(22,36,48));
  var root=new DockPanel{Margin=new Thickness(20)};Content=root;DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);root.Children.Add(_values);
  Loaded+=async(_,_)=>{await Refresh();if(!_closed)_timer.Start();};_timer.Tick+=async(_,_)=>await Refresh();Closed+=(_,_)=>{_closed=true;_timer.Stop();_service.Dispose();};
 }
 private async Task Refresh()
 {
  if(_busy)return;_busy=true;try
  {
   var sample=await _service.RefreshAsync();if(!IsVisible)return;
   _values.ItemsSource=sample.Sensors.Select(s=>$"{s.HardwareName} · {s.Name}    {(s.Value is double v?$"{v:0.#} {s.Unit}":"不可用 · "+s.Status)}").ToArray();
   _status.Text="采样："+sample.SampledUtc.ToLocalTime().ToString("HH:mm:ss")+"\n"+string.Join("\n",sample.Warnings);
  }
  catch(Exception ex){_status.Text=ex.Message;}finally{_busy=false;}
 }
}
