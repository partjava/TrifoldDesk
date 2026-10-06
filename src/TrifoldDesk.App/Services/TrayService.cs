using Forms = System.Windows.Forms;

namespace TrifoldDesk.Services;
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _appIcon;
    public TrayService(Action reveal, Action settings, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("展开三折桌面", null, (_, _) => reveal());
        menu.Items.Add("设置", null, (_, _) => settings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico"))!.Stream;
        _appIcon=new System.Drawing.Icon(stream,32,32);
        _icon = new Forms.NotifyIcon { Text = "TrifoldDesk · 三折桌面", Icon = _appIcon, ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => reveal();
    }
    public void Notify(string title,string message)=>_icon.ShowBalloonTip(10000,title,message,Forms.ToolTipIcon.Info);
    public void Dispose() { _icon.Visible = false; _icon.ContextMenuStrip?.Dispose(); _icon.Dispose(); _appIcon.Dispose(); }
}
