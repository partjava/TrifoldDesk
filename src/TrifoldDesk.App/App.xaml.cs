using System.Diagnostics;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using TrifoldDesk.Services;

namespace TrifoldDesk;
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _exitEvent;
    private CancellationTokenSource? _listenerStop;
    private Exception? _selfTestFailure;
    public static string DataDirectory { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrifoldDesk");
    public static bool IsSelfTest {get;private set;}
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool selfTest = e.Args.Contains("--self-test");
        IsSelfTest=selfTest;
        if (selfTest)
        {
            int index = Array.IndexOf(e.Args, "--data-dir");
            if (index < 0 || index + 1 >= e.Args.Length) { Shutdown(2); return; }
            var testDirectory = Path.GetFullPath(e.Args[index + 1]).TrimEnd(Path.DirectorySeparatorChar);
            var formalDirectory = Path.GetFullPath(DataDirectory).TrimEnd(Path.DirectorySeparatorChar);
            // Tests must never reuse existing data, including the formal profile or a child of it.
            if (testDirectory.Equals(formalDirectory, StringComparison.OrdinalIgnoreCase)
                || testDirectory.StartsWith(formalDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || Directory.Exists(testDirectory) || File.Exists(testDirectory)) { Shutdown(2); return; }
            DataDirectory = testDirectory; Directory.CreateDirectory(DataDirectory);
        }
        if (e.Args.Contains("--shutdown"))
        { using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TrifoldDesk.Exit"); signal.Set(); Shutdown(); return; }
        // Self-tests never signal or replace the running user's instance.
        string suffix = selfTest ? ".Test." + Guid.NewGuid().ToString("N") : "";
        _instanceMutex = new Mutex(true, @"Local\TrifoldDesk.Instance" + suffix, out bool created);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TrifoldDesk.Show" + suffix);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TrifoldDesk.Exit" + suffix);
        if (!created) { _showEvent.Set(); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            if (selfTest) { _selfTestFailure = args.Exception; args.Handled = true; return; }
            MessageBox.Show("操作失败，详细日志保存在：" + DataDirectory + "\n" + args.Exception.Message, "TrifoldDesk");
            args.Handled = true;
        };
        try
        {
            var window = new MainWindow(); MainWindow = window; window.Show();
            if (e.Args.Contains("--show")) window.Reveal();
            if (selfTest) { _ = RunSelfTest(window); return; }
            _listenerStop = new CancellationTokenSource();
            var stop = _listenerStop.Token;
            _ = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    int signal = WaitHandle.WaitAny([_showEvent, _exitEvent, stop.WaitHandle]);
                    if (stop.IsCancellationRequested) break;
                    if (signal == 0) Dispatcher.BeginInvoke(window.Reveal);
                    if (signal == 1) { Dispatcher.BeginInvoke(window.Exit); break; }
                }
            });
        }
        catch (Exception ex) { Log(ex); MessageBox.Show(ex.Message, "TrifoldDesk 启动失败"); Shutdown(1); }
    }
    private async Task RunSelfTest(MainWindow window)
    {
        var lines = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); lines.Add("PASS: " + name); }
        try
        {
            var firstLayout = new ConfigManager(DataDirectory).Load<WidgetConfig>("widgets.json").Value;
            var normalWindow=new Window{Width=100,Height=80,ShowActivated=false,ShowInTaskbar=false,Title="layer-check"};normalWindow.Show();
            WindowDockService.KeepAtDesktop(window);window.Reveal();
            Check(WindowDockService.IsAbove(normalWindow,window),"ordinary application window remains above desktop panel after reveal");normalWindow.Close();
            Check(SystemActionService.AutoStartCommand(Environment.ProcessPath!).Contains("AutoStart.vbs",StringComparison.OrdinalIgnoreCase),"startup command uses stable launcher instead of a version-specific executable");
            Check(firstLayout.Items.Count == 3 && !firstLayout.Items.Any(i => i.PluginId == "cpu"), "first selective default layout is saved before later settings create legacy-looking profile");
            using (var rejected = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--self-test", "--data-dir", DataDirectory } })!)
            { await rejected.WaitForExitAsync(); Check(rejected.ExitCode == 2, "self-test refuses an existing data directory before creating a window"); }
            window.TestApplyWidgets(PluginRules.Defaults(true));
            await Task.Delay(2300);
            Check(window.IsVisible && !window.HandleIsVisible, "expanded panel hides the edge handle");
            Check(window.Width > 0 && window.Height > 0, "docking produces positive dimensions");
            Check(WindowDockService.FillsWorkspace(window), "three panes fill desktop workspace instead of a small sidebar");
            string document = Path.Combine(DataDirectory, "测试文档.txt");
            File.WriteAllText(document, "Self-test file; adding it must not launch any application.");
            window.TestAddPaths([document, DataDirectory], 1);
            Check(window.TestShortcutCount(1) == 2, "file and directory registration");
            window.TestAddPaths([document], 1);
            Check(window.TestShortcutCount(1) == 2, "duplicate registration is ignored");
            window.TestAddPaths([document], 0);
            Check(window.TestShortcutCount(0) == 1, "same source permitted on another panel");
            Check(new ConfigManager(DataDirectory).Load<ShortcutConfig>("shortcuts.json").Value.Items.Count == 3, "UI registration persisted to JSON");
            string linkPath = Path.Combine(DataDirectory, "带参数的快捷方式.lnk");
            object? shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell") ?? throw new Exception("WScript.Shell COM is unavailable."));
                dynamic script = shell!; link = script.CreateShortcut(linkPath); dynamic shortcut = link;
                shortcut.TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                shortcut.Arguments = "/select,\"" + document + "\"";
                shortcut.WorkingDirectory = DataDirectory; shortcut.Save();
                var parsed = ShellLinkHelper.CreateShortcut(linkPath, 0, 1);
                Check(parsed.TargetPath.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase), "IShellLink resolves target");
                Check(parsed.Arguments.Contains("/select") && parsed.Arguments.Contains(document), "IShellLink preserves arguments");
                Check(parsed.WorkingDirectory.TrimEnd('\\') == DataDirectory.TrimEnd('\\'), "IShellLink preserves working directory");
                Check(ShellLinkHelper.GetIcon(parsed) != null, "Shell icon converted into WPF image");
                window.TestAddPaths([linkPath], 0);
                Check(window.TestShortcutCount(0) == 2, "COM shortcut can be registered without execution");
                string missingTarget = Path.Combine(DataDirectory, "将删除的目标.txt"); File.WriteAllText(missingTarget, "target");
                var targetCheck = new ShortcutItem { SourcePath = linkPath, TargetPath = missingTarget };
                Check(ShellLinkHelper.IsAvailable(targetCheck), "existing local link target is available");
                File.Delete(missingTarget);
                Check(!ShellLinkHelper.IsAvailable(targetCheck), "deleted local link target is marked missing");
                targetCheck.TargetPath = "";
                Check(ShellLinkHelper.IsAvailable(targetCheck), "unresolved special link retains Shell fallback");
                object? retargetLink = null;
                try
                {
                    string oldTarget = Path.Combine(DataDirectory, "旧目标.txt"), newTarget = Path.Combine(DataDirectory, "新目标.txt");
                    File.WriteAllText(oldTarget, "old"); File.WriteAllText(newTarget, "new");
                    string editableLink = Path.Combine(DataDirectory, "外部修改.lnk");
                    retargetLink = script.CreateShortcut(editableLink); dynamic edit = retargetLink;
                    edit.TargetPath = oldTarget; edit.Save();
                    var linkView = new ShortcutViewModel(ShellLinkHelper.CreateShortcut(editableLink, 0, 0));
                    edit.TargetPath = newTarget; edit.Save(); File.Delete(oldTarget); linkView.Refresh();
                    Check(!linkView.IsMissing && linkView.Item.TargetPath == newTarget, "externally retargeted link refreshes metadata");
                    File.Delete(newTarget);
                    Check(ShellLinkHelper.SourceExists(linkView.Item) && !ShellLinkHelper.IsAvailable(linkView.Item), "missing link target retains source for Shell launch and folder location");
                }
                finally { if (retargetLink != null && Marshal.IsComObject(retargetLink)) Marshal.FinalReleaseComObject(retargetLink); }
            }
            finally
            {
                if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
            window.TestRemoveShortcut(0, 0);
            window.TestAddPaths([document], 0);
            var reordered = new ConfigManager(DataDirectory).Load<ShortcutConfig>("shortcuts.json").Value.Items.Where(i => i.ScreenIndex == 0).OrderBy(i => i.Order).ToList();
            Check(reordered[0].SourcePath == linkPath && reordered[1].SourcePath == document, "delete and add order survives reload");
            window.TestGroupScreen(1, "项目资料");
            Check(new ConfigManager(DataDirectory).Load<ShortcutConfig>("shortcuts.json").Value.Items.Where(i => i.ScreenIndex == 1).All(i => i.Group == "项目资料"), "batch group is saved from UI");
            window.TestSortScreen(0, true);
            var sorted = new ConfigManager(DataDirectory).Load<ShortcutConfig>("shortcuts.json").Value.Items.Where(i => i.ScreenIndex == 0).OrderBy(i => i.Order).Select(i => i.Name).ToArray();
            Check(sorted.SequenceEqual(sorted.OrderByDescending(name => name, StringComparer.CurrentCultureIgnoreCase)), "UI name sort persists across reload");
            var currentApps = new ConfigManager(DataDirectory).Load<ShortcutConfig>("shortcuts.json").Value.Items.Where(i => i.ScreenIndex == 0).OrderBy(i => i.Order).ToList();
            int before = window.TestShortcutCount(1); window.TestMoveItem(0, currentApps.FindIndex(i => i.SourcePath == document), 1);
            Check(window.TestShortcutCount(1) == before, "batch destination duplicate is skipped");
            var movable = new ConfigManager(DataDirectory).Load<ShortcutConfig>("shortcuts.json").Value.Items.Where(i => i.ScreenIndex == 0).OrderBy(i => i.Order).ToList();
            int linkIndex = movable.FindIndex(i => i.SourcePath == linkPath); window.TestMoveItem(0, linkIndex, 1);
            Check(window.TestShortcutCount(1) == before + 1 && window.TestShortcutCount(0) == 1, "batch move changes destination and source");
            Check(window.TestDriveCount == DriveInfo.GetDrives().Length, "every logical drive is represented on status page");
            window.TestSelectHubTab(1); await Task.Delay(120); window.ExportPreview(Path.Combine(DataDirectory, "management.png"));
            window.TestSelectHubTab(2); await Task.Delay(1400); window.ExportPreview(Path.Combine(DataDirectory, "controls.png"));
            Check(window.TestControlReady, "control page including sliders and system setting entries loads without writes");
            window.TestSelectHubTab(0); await Task.Delay(120);
            Check(window.TestGroupSelectionsValid, "group selector retains a selected value after collection edits: " + window.TestGroupSelection);
            var calendarConfig = PluginRules.Defaults(false); PluginRules.Add(calendarConfig, "calendar", 0, "paper");
            var library = new PluginLibraryWindow(calendarConfig) { Owner = window }; library.Show();
            library.TestAdd("calendar", 1, 1);
            Check(library.Result.Items.Count(i => i.PluginId == "calendar") == 3 && calendarConfig.Items.Count(i => i.PluginId == "calendar") == 2, "library actual add click selects pane and style without mutating unsaved desktop");
            Check(library.Result.Items.Last().PaneIndex == 1 && library.Result.Items.Last().Style == "paper", "library location and style controls reach instance config");
            library.UpdateLayout();
            var libraryBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)library.ActualWidth, (int)library.ActualHeight, 96, 96, PixelFormats.Pbgra32); libraryBitmap.Render(library);
            var libraryEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); libraryEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(libraryBitmap));
            using (var imageStream = File.Create(Path.Combine(DataDirectory, "plugin-library.png"))) libraryEncoder.Save(imageStream);
            library.Close();
            window.TestApplyWidgets(calendarConfig);
            Check(!window.TestSamplingEnabled, "removing all status plugins stops monitor and disk sampling");
            Check(window.TestCalendarCount == 2, "two calendar instances render across desktop panes");
            var calendar = window.TestCalendar;
            var displayed = calendar.DisplayMonth; calendar.ChangeMonth(1);
            Check(calendar.DisplayMonth == displayed.AddMonths(1) && calendar.DayCount == 42, "actual calendar changes month with six week grid");
            calendar.GoToday(); Check(calendar.SelectedDate == DateTime.Today && calendar.DisplayMonth.Month == DateTime.Today.Month, "calendar today action restores actual date");
            var selectedWidget = calendarConfig.Items.First(i => i.PluginId == "calendar");
            var referenceDate=new DateTime(2026,10,5);
            Check(new DateCounter{Date=new DateTime(2026,8,26)}.DaysFrom(referenceDate)==-40&&new DateCounter{Date=new DateTime(2026,10,6)}.DaysFrom(referenceDate)==1&&new DateCounter{Date=referenceDate.AddHours(18)}.DaysFrom(referenceDate)==0,"date counters distinguish elapsed countdown and same day without inclusive-day error");
            Check(new DateCounter{Date=new DateTime(2024,3,1)}.DaysFrom(new DateTime(2024,2,28))==2,"date counter includes leap day correctly");
            Check(window.TestCalendarDates(selectedWidget.InstanceId,[new DateCounter{Name="起始日",Date=new DateTime(2026,8,26)},new DateCounter{Name="目标日",Date=DateTime.Today.AddDays(100)}])&&window.TestCalendar.DateCounterCount==2,"calendar renders multiple saved date counters below month grid");
            Check(new ConfigManager(DataDirectory).Load<WidgetConfig>("widgets.json").Value.Items.First(i=>i.InstanceId==selectedWidget.InstanceId).Dates.Count==2,"date counter names and dates persist in calendar instance");
            window.UpdateLayout();window.ExportPreview(Path.Combine(DataDirectory,"calendar-dates.png"));
            window.TestRemoveWidget(selectedWidget.InstanceId);
            Check(window.TestCalendarCount == 1 && new ConfigManager(DataDirectory).Load<WidgetConfig>("widgets.json").Value.Items.Count(i => i.PluginId == "calendar") == 1, "remove calendar persists without removing other instance");
            Check(new ConfigManager(DataDirectory).Load<ShortcutConfig>("shortcuts.json").Value.Items.Count > 0, "removing plugin does not delete shortcut data");
            window.TestSelectHubTab(3); window.ExportPreview(Path.Combine(DataDirectory, "plugins.png"));
            window.TestApplyWidgets(PluginRules.Defaults(true));
            Check(window.TestSamplingEnabled, "adding status plugins restarts shared sampling");
            window.TestSelectHubTab(3); window.ExportPreview(Path.Combine(DataDirectory, "calendar.png"));
            var freeConfig = PluginRules.Defaults(false);
            var folderA = PluginRules.Add(freeConfig, "folder", 0); var folderB = PluginRules.Add(freeConfig, "folder", 1); var folderC = PluginRules.Add(freeConfig, "folder", 2);
            window.TestApplyWidgets(freeConfig); await Task.Delay(150);
            window.TestFolderAdd(folderA.InstanceId, [document, linkPath]); window.TestFolderAdd(folderB.InstanceId, [DataDirectory]);
            Check(window.TestFolderCount(folderA.InstanceId) == 2, "folder accepts file and COM shortcut without launching");
            Check(window.TestDesktopExit(folderA.InstanceId,1,new Point(99999,99999))&&window.TestDesktopCount==1&&window.TestFolderCount(folderA.InstanceId)==1,"dragging out moves folder entry into standalone desktop icon atomically");
            var desktopSaved=new ConfigManager(DataDirectory).Load<FolderConfig>("folders.json").Value.DesktopEntries.Single();
            var desktopPaneWidth=((Canvas)window.TestFrame(folderA.InstanceId).Parent).ActualWidth/3;
            Check(desktopSaved.PaneIndex==0&&desktopSaved.X+70<=desktopPaneWidth+.01,"standalone icon position persists wholly inside source page");
            window.TestDesktopReturn(folderA.InstanceId);
            Check(window.TestDesktopCount==0&&window.TestFolderCount(folderA.InstanceId)==2,"standalone icon can return to a folder without duplicate membership");
            var edgeFrame=window.TestFrame(folderA.InstanceId);double edgeWidth=edgeFrame.Width,edgeHeight=edgeFrame.Height;bool edgeAuto=edgeFrame.Model.FolderAutoHeight;
            window.UpdateLayout();window.TestEdgeHover(folderA.InstanceId);await Task.Delay(200);
            Check(edgeFrame.Width==edgeWidth,"brief edge crossing does not expand folder");
            await Task.Delay(400);
            Check(edgeFrame.Width>edgeWidth,"actual 500ms edge dwell timer expands folder");
            window.TestEdgeCancel();
            edgeFrame.TestEdgeExpansion(2);edgeFrame.TestEdgeExpansion(4);
            Check(edgeFrame.Width>edgeWidth&&edgeFrame.Height>edgeHeight&&!edgeFrame.Model.FolderAutoHeight,"edge dwell expands folder viewport without automatic shrink");
            edgeFrame.TestEdgeFinish(false);
            Check(edgeFrame.Width==edgeWidth&&edgeFrame.Height==edgeHeight&&edgeFrame.Model.FolderAutoHeight==edgeAuto,"cancelled edge expansion restores original viewport and sizing mode");
            int libraryCount = window.TestShortcutCount(0);
            window.TestLibraryToFolder(folderC.InstanceId, 0, 0, true);
            Check(window.TestShortcutCount(0) == libraryCount && window.TestFolderCount(folderC.InstanceId) == 1, "copying from application library to empty folder preserves library entry");
            window.TestRemoveWidget(folderC.InstanceId);
            window.TestFolderTransfer(folderA.InstanceId, folderB.InstanceId, 0, false);
            Check(window.TestFolderCount(folderA.InstanceId) == 1 && window.TestFolderCount(folderB.InstanceId) == 2, "folder drag transfer saves both memberships");
            var frame = window.TestFrame(folderA.InstanceId);
            Check(frame.Height == 142 && frame.Width == 80 && frame.Model.FolderAutoHeight, "folder fits occupied slots while preserving its intentional empty first slot");
            double initialPaneWidth=((Canvas)frame.Parent).ActualWidth/3;
            frame.Model.X=(frame.Model.PaneIndex+1)*initialPaneWidth-frame.Width;frame.Constrain(false);
            window.TestFolderDropGrid(folderA.InstanceId,true);
            Check(frame.Width>=220&&frame.Height>=146,"dragging temporarily exposes empty destination slots");
            Check(frame.Model.X+frame.Width<=(frame.Model.PaneIndex+1)*initialPaneWidth+.01,"temporary drop grid stays inside pane at right wall");
            window.TestFolderDropGrid(folderA.InstanceId,false);
            Check(frame.Width==150&&frame.Height==80,"ending drag trims unused right and bottom space while preserving empty slot");
            frame.TestDrag(72, 48); frame.TestResize(80, 64);
            var savedFrame = new ConfigManager(DataDirectory).Load<WidgetConfig>("widgets.json").Value.Items.First(i => i.InstanceId == folderA.InstanceId);
            Check(savedFrame.X >= 0 && savedFrame.Width == frame.Width && savedFrame.Height == frame.Height, "actual Thumb move and resize persist layout");
            double paneWidth = ((Canvas)frame.Parent).ActualWidth / 3;
            frame.TestDrag(paneWidth * 1.5-(frame.Model.X+frame.Width/2), 0);
            Check(frame.Model.PaneIndex == 1 && frame.Model.X >= paneWidth && frame.Model.X + frame.Width <= 2*paneWidth+.01, "actual drag transfers wholly to nearest pane");
            frame.TestResize(paneWidth * 3, 0);
            Check(frame.Width <= paneWidth+.01 && frame.Model.PaneIndex == 1 && frame.Model.X + frame.Width <= 2*paneWidth+.01, "actual resize stays within current pane");
            savedFrame = new ConfigManager(DataDirectory).Load<WidgetConfig>("widgets.json").Value.Items.First(i => i.InstanceId == folderA.InstanceId);
            Check(savedFrame.PaneIndex == 1 && savedFrame.X + savedFrame.Width <= 2*paneWidth+.01, "pane assignment and bounded dimensions persist together");
            frame.TestResize(-paneWidth*3,0);
            for (int corner=0;corner<4;corner++)
            {
                double oldLeft=frame.Model.X,oldTop=frame.Model.Y,oldRight=oldLeft+frame.Width,oldBottom=oldTop+frame.Height;
                frame.TestCornerResize(corner,12,8);
                bool left=corner is 1 or 3,top=corner is 1 or 2;
                Check(Math.Abs((left?frame.Model.X+frame.Width:frame.Model.X)-(left?oldRight:oldLeft))<.01 && Math.Abs((top?frame.Model.Y+frame.Height:frame.Model.Y)-(top?oldBottom:oldTop))<.01, "corner resize preserves opposite anchor: "+corner);
            }
            frame.TestResize(0,64-frame.Height);await Task.Delay(120);
            Check(frame.Height==64&&!frame.Model.FolderAutoHeight,"manual small viewport is retained without expanding to all icons");
            Check(window.TestFolderScroll(folderA.InstanceId)>0,"hidden folder content remains vertically scrollable in small viewport");
            frame.TestLock(); double lockedX = System.Windows.Controls.Canvas.GetLeft(frame); frame.TestDrag(64, 32);
            Check(System.Windows.Controls.Canvas.GetLeft(frame) == lockedX, "locked frame rejects dragging");
            double offGridX = frame.Model.PaneIndex*paneWidth+13;
            frame.Model.X = offGridX; frame.Model.Y = 71; frame.Constrain(false); frame.TestDrag(0, 0);
            Check(frame.Model.X == offGridX && frame.Model.Y == 71, "locked valid off-grid layout never snaps during rejected drag");
            var rejectedModel = new WidgetInstance { PluginId = "folder", X = 80, Y = 80, Width = 320, Height = 300 };
            var rejectedFrame = new WidgetFrame(rejectedModel, new TextBlock(), new Canvas(), _ => false, () => { });
            rejectedFrame.TestLock(); rejectedFrame.TestCollapse(); rejectedFrame.TestAutoCollapse();
            Check(!rejectedModel.IsLocked && !rejectedModel.IsCollapsed && !rejectedModel.AutoCollapse, "failed saving rolls back lock collapse and auto-collapse switches");
            frame.TestAutoCollapse(); window.TestFolderBusy(folderA.InstanceId, true);
            frame.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
            await Task.Delay(800);
            Check(!frame.Model.IsCollapsed, "folder auto-collapse respects active child operation");
            window.TestFolderBusy(folderA.InstanceId, false); frame.TestAutoCollapse();
            frame.TestCollapse(); Check(frame.Height == 48 && frame.Width == 180 && window.TestFolderPreview(folderA.InstanceId) == window.TestFolderCount(folderA.InstanceId), "single item compact preview removes unused grid rows");
            window.ExportPreview(Path.Combine(DataDirectory,"phone-folders.png"));
            var persistedFree = new ConfigManager(DataDirectory).Load<WidgetConfig>("widgets.json").Value;
            window.TestApplyWidgets(persistedFree);
            Check(window.TestFrame(folderA.InstanceId).Height == 48 && window.TestFrame(folderA.InstanceId).Model.IsLocked, "rebuilding desktop restores fitted compact height and lock");
            var restoredPaneFrame = window.TestFrame(folderA.InstanceId);
            Check(restoredPaneFrame.Model.PaneIndex == 1 && restoredPaneFrame.Model.X >= paneWidth && restoredPaneFrame.Model.X + restoredPaneFrame.Width <= 2*paneWidth+.01, "rebuilding desktop preserves pane ownership and compact bounds");
            window.TestFolderOpen(folderA.InstanceId);
            Check(window.TestFrame(folderA.InstanceId).Height > 48, "capsule expands to remembered height");
            Check(window.TestFolderPreview(folderA.InstanceId) <= 9, "phone folder shows at most nine preview icons");
            window.ExportPreview(Path.Combine(DataDirectory, "free-folders.png"));
            window.TestRemoveWidget(folderB.InstanceId);
            Check(window.TestFolderCount(folderB.InstanceId) == 2, "removing folder plugin preserves archived folder contents");
            window.TestApplyWidgets(PluginRules.Defaults(true)); await Task.Delay(150);
            window.ExportPreview(Path.Combine(DataDirectory, "expanded.png"));
            var suite = new WidgetConfig();
            foreach (var (id, pane, style, x, y, w, h) in new (string, int, string, double, double, double, double)[] {
                ("cpu",0,"glass",16,16,280,240), ("memory",0,"neon",312,16,280,240), ("network",0,"glass",16,272,576,230),
                ("clock",1,"paper",620,16,430,220), ("battery",1,"glass",620,252,430,220), ("gpu",1,"neon",620,488,430,250), ("calendar",2,"paper",1080,16,530,590) })
            { var item = PluginRules.Add(suite,id,pane,style); item.X=x;item.Y=y;item.Width=w;item.Height=h; }
            window.TestApplyWidgets(suite); await Task.Delay(3600);
            var clockWidget = window.TestDashboard("clock");
            Check(clockWidget.TestActive && clockWidget.TestValue.Contains(':'), "clock dashboard starts live timer with actual time");
            Check(window.TestDashboard("battery").TestValue.Length > 0, "battery dashboard displays actual power status");
            Check(DashboardWidget.BatteryValue(255, .5f) == "不可用" && DashboardWidget.BatteryValue(128, 1f) == "交流电", "unknown battery flags never imply a missing battery");
            Check(window.TestDashboard("gpu").TestValue == "—" || window.TestDashboard("gpu").TestValue.EndsWith('%'), "GPU dashboard reports unavailable or actual 3D percentage");
            window.ExportPreview(Path.Combine(DataDirectory,"dashboard-suite.png"));
            var compactSuite = new WidgetConfig(); var combined = PluginRules.Add(compactSuite, "system-summary", 0); combined.X=16; combined.Y=16; combined.Width=380; combined.Height=260;
            var compactCalendar = PluginRules.Add(compactSuite, "calendar", 1, "paper"); compactCalendar.X=420; compactCalendar.Y=16; compactCalendar.Width=350; compactCalendar.Height=480;
            window.TestApplyWidgets(compactSuite); await Task.Delay(3600);
            var summaryView = window.TestSummary(combined.InstanceId);
            Check(summaryView.Metrics.Count == 5 && summaryView.Metrics.All(m=>m.TestActive), "one compact frame contains five live metrics");
            Check(summaryView.TestDiskBars == DriveInfo.GetDrives().Count(d=>d.DriveType==DriveType.Fixed), "summary disk cell shows every local drive with its own capacity bar");
            Check(window.TestSamplingEnabled, "summary enables shared monitoring without standalone metrics");
            window.ExportPreview(Path.Combine(DataDirectory,"compact-suite.png"));
            var menu = (MenuItem)window.TestFrame(combined.InstanceId).ContextMenu.Items[0];
            var cpuChoice = (MenuItem)menu.Items[0]; cpuChoice.IsChecked=false; cpuChoice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(100);
            Check(window.TestSummary(combined.InstanceId).Metrics.Count == 4 && !summaryView.Metrics.Any(m=>m.TestActive), "module selection rebuilds summary and stops old timers");
            window.TestRemoveWidget(combined.InstanceId); await Task.Delay(100);
            Check(!window.TestSamplingEnabled, "removing the only summary stops shared sampling");
            window.TestApplyWidgets(PluginRules.Defaults(true)); await Task.Delay(100);
            Check(!clockWidget.TestActive, "removing dashboard unloads and stops its timer");
            window.TestCollapse(); Check(window.TestFoldFaces == 3, "collapse animates three separate faces"); await Task.Delay(450);
            Check(!window.IsVisible && window.HandleIsVisible, "collapsed panel is hidden, narrow handle remains");
            window.Reveal(); await Task.Delay(450);
            Check(window.IsVisible, "handle reveal restores panel");
            // Start a collapse, then reverse before completion; old completion must not hide the panel.
            window.TestCollapse(); await Task.Delay(70); window.Reveal(); await Task.Delay(450);
            Check(window.IsVisible && !window.CollapsedForTest, "interrupted animation retains final expanded state");
            Check(window.TestFoldClean, "three folding layers release snapshots after reveal");
            window.TestTogglePane(0); await Task.Delay(250);
            Check(window.TestPaneFolded(0) && !window.TestPaneFolded(1) && !window.TestPaneFolded(2), "left face folds independently from other faces");
            window.TestPaneMenuClick(0); await Task.Delay(250);
            Check(!window.TestPaneFolded(0)&&window.TestNoPaneButtons, "individual face restores from context menu without floating edge buttons");
            window.TestTogglePane(1); window.TestTogglePane(2); await Task.Delay(250);
            Check(!window.TestPaneFolded(0) && window.TestPaneFolded(1) && window.TestPaneFolded(2), "middle and right faces fold without folding left");
            window.ExportPreview(Path.Combine(DataDirectory,"independent-panes.png"));
            window.TestTogglePane(1); window.TestTogglePane(2); await Task.Delay(250);
            var desktopMenu=window.ContextMenu; desktopMenu.PlacementTarget=window; desktopMenu.IsOpen=true; await Task.Delay(120); desktopMenu.UpdateLayout();
            var menuBitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(desktopMenu.ActualWidth)),Math.Max(1,(int)Math.Ceiling(desktopMenu.ActualHeight)),96,96,PixelFormats.Pbgra32); menuBitmap.Render(desktopMenu);
            var menuEncoder=new System.Windows.Media.Imaging.PngBitmapEncoder(); menuEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(menuBitmap)); using(var menuFile=File.Create(Path.Combine(DataDirectory,"desktop-menu.png")))menuEncoder.Save(menuFile); desktopMenu.IsOpen=false;
            var detailTip=new ToolTip { Content="305实验室 AndroidStudio修改\nC:\\Users\\34970\\Desktop\\Android\\305实验室\\AndroidStudio修改.txt",PlacementTarget=window,IsOpen=true }; await Task.Delay(120); detailTip.UpdateLayout();
            var tipBitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(detailTip.ActualWidth)),Math.Max(1,(int)Math.Ceiling(detailTip.ActualHeight)),96,96,PixelFormats.Pbgra32); tipBitmap.Render(detailTip);
            var tipEncoder=new System.Windows.Media.Imaging.PngBitmapEncoder(); tipEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(tipBitmap)); using(var tipFile=File.Create(Path.Combine(DataDirectory,"file-tooltip.png")))tipEncoder.Save(tipFile); detailTip.IsOpen=false;
            Check(window.MonitorSampleReceived, "background sampling dispatches to view model");
            var installedApps=TrifoldDesk.Services.InstalledAppsService.Read();
            var duplicateSamples=InstalledAppsService.Disambiguate([new InstalledApp("Anaconda Navigator","anaconda.AnacondaNavigator.anaconda.base"),new InstalledApp("Anaconda Navigator","anaconda.AnacondaNavigator.anaconda3.base"),new InstalledApp("Unique","unique"),new InstalledApp("Unique","unique")]);
            Check(duplicateSamples.Count==3&&duplicateSamples.Any(a=>a.DisplayName=="Anaconda Navigator（anaconda）")&&duplicateSamples.Any(a=>a.DisplayName=="Anaconda Navigator（anaconda3）")&&duplicateSamples.Single(a=>a.Name=="Unique").DisplayName=="Unique","same-name installations are labeled separately while identical Shell entries are deduplicated");
            Check(installedApps.Count>0,"Windows installed application catalog includes selectable entries");
            var installedSample=installedApps.First();
            string installedLink=TrifoldDesk.Services.ShellLinkHelper.SaveShellAppLink(installedSample);
            Check(ShellLinkHelper.SaveShellAppLink(installedSample with {DisplayName=installedSample.Name+"（区分目录）"})==installedLink,"renaming picker label reuses existing shortcut without creating duplicate entry");
            Check(File.Exists(installedLink)&&TrifoldDesk.Services.ShellLinkHelper.GetShellAppIcon(installedSample.ShellPath)!=null,"installed application icon and native Shell shortcut are created");
            var plainAppIcon=(System.Windows.Media.Imaging.BitmapSource)TrifoldDesk.Services.ShellLinkHelper.GetShellAppIcon(installedSample.ShellPath)!;
            var linkAppIcon=(System.Windows.Media.Imaging.BitmapSource)TrifoldDesk.Services.ShellLinkHelper.GetIcon(TrifoldDesk.Services.ShellLinkHelper.CreateShortcut(installedLink,0,0))!;
            byte[] IconPixels(System.Windows.Media.Imaging.BitmapSource source)
            {
                var normalized=new System.Windows.Media.Imaging.FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
                var pixels=new byte[normalized.PixelWidth*normalized.PixelHeight*4];normalized.CopyPixels(pixels,normalized.PixelWidth*4,0);return pixels;
            }
            Check(plainAppIcon.PixelWidth==linkAppIcon.PixelWidth&&plainAppIcon.PixelHeight==linkAppIcon.PixelHeight&&IconPixels(plainAppIcon).SequenceEqual(IconPixels(linkAppIcon)),"shortcut displays identical target icon pixels without arrow overlay");
            var appPicker=new InstalledAppsWindow{Owner=window};appPicker.Show();await Task.Delay(150);appPicker.UpdateLayout();
            var appBitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)appPicker.ActualWidth,(int)appPicker.ActualHeight,96,96,PixelFormats.Pbgra32);appBitmap.Render(appPicker);
            var appEncoder=new System.Windows.Media.Imaging.PngBitmapEncoder();appEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(appBitmap));using(var appFile=File.Create(Path.Combine(DataDirectory,"installed-apps.png")))appEncoder.Save(appFile);appPicker.Close();
            using (var settings = new SettingsWindowForTestScope(window)) { Check(settings.Ready, "settings window XAML loads"); }
            var readableSettings = new SettingsWindow(new AppSettings()) { Owner=window }; readableSettings.Show(); await Task.Delay(150); readableSettings.UpdateLayout();
            IEnumerable<TextBlock> VisibleText(DependencyObject root)
            {
                if(root is TextBlock text) yield return text;
                for(int child=0;child<VisualTreeHelper.GetChildrenCount(root);child++)
                    foreach(var textChild in VisibleText(VisualTreeHelper.GetChild(root,child))) yield return textChild;
            }
            bool DarkText(DependencyObject root)
            {
                var labels=VisibleText(root).Where(t=>!string.IsNullOrWhiteSpace(t.Text)).ToArray();
                return labels.Length>0 && labels.All(t=>t.Foreground is SolidColorBrush brush && brush.Color.R<80 && brush.Color.G<80 && brush.Color.B<80);
            }
            var monitorChoice=(ComboBox)readableSettings.FindName("MonitorCombo"); var networkChoice=(ComboBox)readableSettings.FindName("NetworkCombo");
            Check(DarkText(monitorChoice)&&DarkText(networkChoice),"settings selected monitor and network text contrasts with light background");
            networkChoice.IsDropDownOpen=true; await Task.Delay(100); networkChoice.UpdateLayout();
            var networkItem=(ComboBoxItem)networkChoice.ItemContainerGenerator.ContainerFromIndex(0);
            Check(networkItem!=null&&DarkText(networkItem),"settings expanded network choice uses readable dark text");
            networkChoice.IsDropDownOpen=false;
            var settingsBitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)readableSettings.ActualWidth,(int)readableSettings.ActualHeight,96,96,PixelFormats.Pbgra32); settingsBitmap.Render(readableSettings);
            var settingsEncoder=new System.Windows.Media.Imaging.PngBitmapEncoder(); settingsEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(settingsBitmap)); using(var settingsFile=File.Create(Path.Combine(DataDirectory,"settings-readable.png")))settingsEncoder.Save(settingsFile);
            readableSettings.Close();
            var previewCanvas=new Canvas{Width=1260,Height=570,HorizontalAlignment=HorizontalAlignment.Left}; var preview=new Window{Width=440,Height=610,Content=previewCanvas,Background=Brushes.WhiteSmoke,Owner=window,ShowActivated=false,ShowInTaskbar=false,Title="Widget visual verification"};
            var previewClockModel=new WidgetInstance{PluginId="clock",InstanceId="preview-clock",Style="glass",Title="时钟",PaneIndex=0,X=8,Y=8,Width=408,Height=174};
            var previewStatusModel=new WidgetInstance{PluginId="system-summary",InstanceId="preview-summary",Style="glass",Title="系统概览",PaneIndex=0,X=8,Y=198,Width=408,Height=354,Modules=new List<string>{"cpu","memory","gpu","network","battery","disks"}};
            var previewClock=new DashboardWidget("clock","glass",window.TestMonitorModel);
            var previewSummary=new CombinedStatusWidget(previewStatusModel,window.TestMonitorModel);
            previewCanvas.Children.Add(new WidgetFrame(previewClockModel,previewClock,previewCanvas,_=>true,()=>{}));
            previewCanvas.Children.Add(new WidgetFrame(previewStatusModel,previewSummary,previewCanvas,_=>true,()=>{}));
            preview.Show(); preview.UpdateLayout(); foreach(var previewFrame in previewCanvas.Children.OfType<WidgetFrame>()) previewFrame.Constrain(false); await Task.Delay(1200); preview.UpdateLayout();
            Check(VisibleText(previewClock).Any(t=>t.Text.Contains(':')),"digital and analog clock layout renders real current time");
            Check(previewSummary.Metrics.Count==5,"polished summary preserves all selected metric modules");
            var ringProbe=new OrganicMetricRing { Width=28,Height=28 }; previewCanvas.Children.Add(ringProbe); ringProbe.Sample(47); preview.UpdateLayout(); await Task.Delay(80); Check(ringProbe.TestAnimating,"available visible ring animates"); ringProbe.Visibility=Visibility.Collapsed; Check(!ringProbe.TestAnimating,"hidden ring stops animation"); ringProbe.Visibility=Visibility.Visible; ringProbe.Sample(null); Check(!ringProbe.TestAnimating,"unavailable ring does not invent activity"); ringProbe.Sample(12); previewCanvas.Children.Remove(ringProbe); await Task.Delay(80); Check(!ringProbe.TestAnimating,"removed ring stops timer");
            var previewImage=new System.Windows.Media.Imaging.RenderTargetBitmap((int)preview.ActualWidth,(int)preview.ActualHeight,96,96,PixelFormats.Pbgra32);previewImage.Render(preview);
            var previewEncoder=new System.Windows.Media.Imaging.PngBitmapEncoder();previewEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(previewImage));using(var previewFile=File.Create(Path.Combine(DataDirectory,"widget-redesign.png")))previewEncoder.Save(previewFile);preview.Close();
            Check(_selfTestFailure == null, "no unhandled XAML or UI exceptions");
            lines.Add("Hardware counters are environment-dependent; unavailable CPU is reported honestly.");
            File.WriteAllLines(Path.Combine(DataDirectory, "self-test.txt"), lines);
            window.Exit();
        }
        catch (Exception ex)
        {
            Log(ex); lines.Add(ex.ToString()); File.WriteAllLines(Path.Combine(DataDirectory, "self-test.txt"), lines);
            window.Exit(); Environment.ExitCode = 1;
        }
    }
    private sealed class SettingsWindowForTestScope : IDisposable
    {
        private readonly SettingsWindow _window;
        public bool Ready => _window.IsInitialized;
        public SettingsWindowForTestScope(Window owner) { _window = new SettingsWindow(new AppSettings()) { Owner = owner }; }
        public void Dispose() => _window.Close();
    }
    public static void Log(Exception ex)
    {
        try { Directory.CreateDirectory(DataDirectory); File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), DateTime.Now + " " + ex + Environment.NewLine); }
        catch (IOException) { Debug.WriteLine(ex); }
        catch (UnauthorizedAccessException) { Debug.WriteLine(ex); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _listenerStop?.Cancel();
        // Let the background waiter finish without disposing its handle while it is waiting.
        _showEvent?.Set();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
