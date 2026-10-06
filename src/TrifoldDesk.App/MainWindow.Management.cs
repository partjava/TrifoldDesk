using System.Windows.Input;
using TrifoldDesk.Services;

namespace TrifoldDesk;
public partial class MainWindow
{
    private void CollapseClick(object sender, RoutedEventArgs e) => SetCollapsed(true);
    private void SortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || sender is not ComboBox combo || combo.SelectedIndex == 0) return;
        SortScreen(int.Parse((string)combo.Tag), combo.SelectedIndex == 2);
    }
    private void SortScreen(int screen, bool descending)
    {
        var collection = Collection(screen);
        var order = LibraryRules.SortByName(collection.Select(v => v.Item), descending);
        for (int index = 0; index < order.Count; index++)
        {
            int current = collection.IndexOf(collection.First(v => v.Item.Id == order[index].Id));
            collection.Move(current, index);
        }
        if (SaveShortcuts()) _viewModel.Status = "名称顺序已保存；右键可手动上移或下移。";
    }
    private void MenuGroup(object sender, RoutedEventArgs e)
    {
        _dialogOpen = true;
        try
        {
            var shortcut = ContextItem(sender);
            var group = TextInputDialog.Ask(this, "设置分组", shortcut.Group);
            if (group == null) return;
            shortcut.Item.Group = group; shortcut.Refresh(); SaveShortcuts();
        }
        finally { _dialogOpen = false; }
    }
    private void MenuReorder(object sender, RoutedEventArgs e)
    {
        var shortcut = ContextItem(sender); var collection = Collection(shortcut.Item.ScreenIndex);
        int old = collection.IndexOf(shortcut), next = old + int.Parse((string)((MenuItem)sender).Tag);
        if (next < 0 || next >= collection.Count) return;
        collection.Move(old, next); SaveShortcuts();
    }
    private void SelectVisibleClick(object sender, RoutedEventArgs e)
    {
        var visible = _viewModel.ManagementView.Cast<ShortcutViewModel>().ToList();
        bool select = visible.Any(v => !v.IsSelected);
        foreach (var item in visible) item.IsSelected = select;
    }
    private void ClearSelectionClick(object sender, RoutedEventArgs e)
    { foreach (var item in _viewModel.AllItems) item.IsSelected = false; }
    private List<ShortcutViewModel> SelectedItems() => _viewModel.AllItems.Where(v => v.IsSelected).ToList();
    private void BatchGroupClick(object sender, RoutedEventArgs e)
    {
        var selected = SelectedItems(); if (selected.Count == 0) { _viewModel.Status = "请先选择要编辑的入口。"; return; }
        ApplyGroup(selected, BatchGroupInput.Text);
    }
    private void ApplyGroup(IEnumerable<ShortcutViewModel> items, string group)
    {
        group = string.IsNullOrWhiteSpace(group) ? "未分组" : group.Trim();
        if (group == "全部分组") { _viewModel.Status = "全部分组是筛选项，请使用其他分组名。"; return; }
        if (group.Length > 40) { _viewModel.Status = "分组名最多 40 个字符。"; return; }
        foreach (var item in items.ToArray()) { item.Item.Group = group; item.Refresh(); }
        if (SaveShortcuts()) _viewModel.Status = "分组已保存：" + group;
    }
    private void BatchMoveClick(object sender, RoutedEventArgs e) => MoveSelected(int.Parse((string)((Button)sender).Tag));
    private void MoveSelected(int target)
    {
        int moved = 0, duplicate = 0;
        foreach (var item in SelectedItems())
        {
            if (item.Item.ScreenIndex == target) continue;
            if (!LibraryRules.CanMove(item.Item, _viewModel.AllItems.Select(v => v.Item), target)) { duplicate++; continue; }
            Collection(item.Item.ScreenIndex).Remove(item); item.Item.ScreenIndex = target; Collection(target).Add(item); item.Refresh(); moved++;
        }
        if (SaveShortcuts()) _viewModel.Status = $"已移动 {moved} 项，目标区重复跳过 {duplicate} 项。";
    }
    private void BatchRemoveClick(object sender, RoutedEventArgs e)
    {
        var selected = SelectedItems(); if (selected.Count == 0) return;
        _dialogOpen = true;
        try
        {
            if (MessageBox.Show(this, $"从面板移除所选 {selected.Count} 个入口？原文件会保留。", "批量移除", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            foreach (var item in selected) Collection(item.Item.ScreenIndex).Remove(item);
            if (SaveShortcuts()) _viewModel.Status = $"已移除 {selected.Count} 个入口，原文件保留。";
        }
        finally { _dialogOpen = false; }
    }
    private void OpenDriveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (((Button)sender).DataContext is DriveSnapshot drive && drive.CanOpen)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(drive.Name) { UseShellExecute = true });
        }
        catch (Exception ex) { Fail("无法打开磁盘", ex); }
    }
    private void SystemSettingsClick(object sender, RoutedEventArgs e)
    {
        if(sender is Button radio && radio.Tag is string uri && uri is "ms-settings:network-wifi" or "ms-settings:bluetooth"){new WirelessControlWindow{Owner=this}.ShowDialog();return;}
        try { DesktopControlService.OpenSettings((string)((Button)sender).Tag); _viewModel.Status = "已打开 Windows 系统设置。"; }
        catch (Exception ex) { Fail("无法打开系统设置", ex); }
    }
    private async void HubTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || e.Source != HubTabs) return;
        if (!HasPlugin("controls")) { _controlTimer.Stop(); return; }
        _viewModel.RefreshClock(); RefreshAudio(); _controlTimer.Start();
        if (_readingBrightness) return; _readingBrightness = true;
        try
        {
            _brightness = await Task.Run(DesktopControlService.ReadBrightness);
            if (_closing) return;
            BrightnessSlider.IsEnabled = _brightness != null;
            _syncingBrightness = true;
            try { if (_brightness != null) BrightnessSlider.Value = _brightness.Value; }
            finally { _syncingBrightness = false; _brightnessDirty = false; }
            BrightnessLabel.Text = _brightness != null ? $"{_brightness.Value}%" : "设备不支持 · 打开显示设置";
        }
        catch (Exception ex) { App.Log(ex); BrightnessSlider.IsEnabled = false; BrightnessLabel.Text = "无法读取 · 打开显示设置"; }
        finally { _readingBrightness = false; }
    }
    private void RefreshAudio()
    {
        if (_closing) return;
        _syncingControls = true;
        try
        {
            var audio = DesktopControlService.ReadAudio(); _audioMuted = audio.Muted;
            if (!VolumeSlider.IsMouseCaptureWithin && !VolumeSlider.IsKeyboardFocusWithin) VolumeSlider.Value = audio.Volume;
            VolumeSlider.IsEnabled = MuteButton.IsEnabled = true;
            VolumeLabel.Text = audio.Muted ? "已静音" : $"{audio.Volume:0}%"; MuteButton.Content = audio.Muted ? "取消静音" : "静音";
        }
        catch (Exception) { VolumeSlider.IsEnabled = MuteButton.IsEnabled = false; VolumeLabel.Text = "无可用音频设备"; }
        finally { _syncingControls = false; }
    }
    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _syncingControls || !VolumeSlider.IsEnabled) return;
        try { DesktopControlService.SetVolume(e.NewValue); VolumeLabel.Text = $"{e.NewValue:0}%"; }
        catch (Exception ex) { Fail("音量调整失败", ex); RefreshAudio(); }
    }
    private void MuteClick(object sender, RoutedEventArgs e)
    { try { DesktopControlService.SetMute(!_audioMuted); RefreshAudio(); } catch (Exception ex) { Fail("静音切换失败", ex); } }
    private async Task ApplyBrightness()
    {
        if (_brightness == null || _closing || !_brightnessDirty) return;
        var target = _brightness; double value = BrightnessSlider.Value;
        _brightnessDirty = false;
        BrightnessSlider.IsEnabled = false;
        try { await Task.Run(() => DesktopControlService.SetBrightness(target, value)); _brightness = target with { Value = (byte)value }; BrightnessLabel.Text = $"{value:0}%"; }
        catch (Exception ex) { Fail("亮度调整失败", ex); }
        finally { if (!_closing) BrightnessSlider.IsEnabled = true; }
    }
    private async void BrightnessReleased(object sender, MouseButtonEventArgs e) => await ApplyBrightness();
    private async void BrightnessKeyUp(object sender, KeyEventArgs e)
    { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) await ApplyBrightness(); }
    private void BrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (_ready && !_syncingBrightness && BrightnessSlider.IsEnabled) _brightnessDirty = true; }
    internal int TestDriveCount => _viewModel.Drives.Count;
    internal void TestGroupScreen(int screen, string group) => ApplyGroup(Collection(screen), group);
    internal void TestSortScreen(int screen, bool descending) => SortScreen(screen, descending);
    internal void TestMoveItem(int sourceScreen, int index, int targetScreen)
    { foreach (var item in _viewModel.AllItems) item.IsSelected = false; Collection(sourceScreen)[index].IsSelected = true; MoveSelected(targetScreen); }
    internal void TestSelectHubTab(int index)
    {
        HubTabs.SelectedIndex = index;
        if (index == 2) foreach (var frame in _frames.Values.Where(f => f.Model.PluginId == "controls" && f.Model.IsCollapsed)) frame.TestCollapse();
    }
    internal bool TestControlReady => HubTabs.SelectedIndex == 2 && VolumeSlider.IsLoaded && BrightnessSlider.IsLoaded;
    internal string TestGroupSelection => $"App={AppGroupCombo.SelectedItem ?? "NULL"}, Project={ProjectGroupCombo.SelectedItem ?? "NULL"}, Source={_viewModel.AppGroup}/{_viewModel.ProjectGroup}";
    internal bool TestGroupSelectionsValid => AppGroupCombo.SelectedItem is string && ProjectGroupCombo.SelectedItem is string;
}
