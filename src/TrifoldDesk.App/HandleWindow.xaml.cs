using System.Windows.Input;
using TrifoldDesk.Services;

namespace TrifoldDesk;
public partial class HandleWindow : Window
{
    public event Action? ToggleRequested, RevealRequested, SettingsRequested, ExitRequested, Entered, LeftHandle;
    public bool MenuIsOpen { get; private set; }
    public HandleWindow() { InitializeComponent(); }
    protected override void OnSourceInitialized(EventArgs e) { base.OnSourceInitialized(e); WindowDockService.ApplyToolWindow(this);System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(ProtectOrder); }
    private IntPtr ProtectOrder(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled) { if(msg==0x0046&&!Topmost)WindowDockService.ProtectDesktopOrder(l);return IntPtr.Zero; }
    public void SetCollapsed(bool collapsed) => Arrow.Text = collapsed ? "‹" : "›";
    private void HandleClick(object sender, MouseButtonEventArgs e) => ToggleRequested?.Invoke();
    private void HandleEnter(object sender, MouseEventArgs e) => Entered?.Invoke();
    private void HandleLeave(object sender, MouseEventArgs e) => LeftHandle?.Invoke();
    private void RevealClick(object sender, RoutedEventArgs e) => RevealRequested?.Invoke();
    private void SettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void ExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
    private void MenuOpened(object sender, RoutedEventArgs e) => MenuIsOpen = true;
    private void MenuClosed(object sender, RoutedEventArgs e) => MenuIsOpen = false;
}
