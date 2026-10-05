namespace TrifoldDesk;
public static class DateCounterDialog
{
    public static DateCounter? Ask(Window owner,DateCounter? initial=null)
    {
        var window=new Window{Owner=owner,Title=initial==null?"添加日期":"编辑日期",Width=350,Height=245,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,Background=new SolidColorBrush(Color.FromRgb(24,32,40))};
        var root=new StackPanel{Margin=new Thickness(18)};window.Content=root;
        root.Children.Add(new TextBlock{Text="名称",FontSize=12});
        var name=new TextBox{Text=initial?.Name??"",MaxLength=40,Margin=new Thickness(0,6,0,12)};root.Children.Add(name);
        var date=new DatePicker{SelectedDate=initial?.Date??DateTime.Today,Margin=new Thickness(0,0,0,14)};root.Children.Add(date);
        var dateText=new Style(typeof(TextBlock));dateText.Setters.Add(new Setter(TextBlock.ForegroundProperty,Brushes.Black));date.Resources[typeof(TextBlock)]=dateText;
        var dateButton=new Style(typeof(Button));dateButton.Setters.Add(new Setter(Button.ForegroundProperty,Brushes.Black));date.Resources[typeof(Button)]=dateButton;
        var save=new Button{Content="保存",IsDefault=true};root.Children.Add(save);
        save.Click+=(_,_)=>{if(!string.IsNullOrWhiteSpace(name.Text)&&date.SelectedDate!=null)window.DialogResult=true;};
        window.Loaded+=(_,_)=>name.Focus();
        return window.ShowDialog()==true?new DateCounter{Id=initial?.Id??Guid.NewGuid().ToString("N"),Name=name.Text.Trim(),Date=date.SelectedDate!.Value.Date}:null;
    }
}
