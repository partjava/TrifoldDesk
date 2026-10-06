using System.Runtime.InteropServices;
using TrifoldDesk.Services;
namespace TrifoldDesk;
public partial class MainWindow
{
 private DesktopHostStatus? _desktopHost;
 private static readonly uint ExplorerRestartMessage=RegisterWindowMessage("TaskbarCreated");
 private void ApplyDesktopHost()
 {
  if(App.IsSelfTest)return;
  _desktopHost=_settings.DesktopEmbedded?DesktopHostService.EnsureAttached(this):DesktopHostService.Detach(this);
  if(_desktopHost.IsFallback)_viewModel.Status=_desktopHost.Message;
 }
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string message);
}
