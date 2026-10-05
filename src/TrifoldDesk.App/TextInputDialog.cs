namespace TrifoldDesk;
public static class TextInputDialog
{
    public static string? Ask(Window owner, string title, string initial)
    {
        var window = new Window { Owner = owner, Title = title, Width = 360, Height = 165, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(24, 38, 51)), ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(18) }; var input = new TextBox { Text = initial, MaxLength = 80, Padding = new Thickness(8) };
        var button = new Button { Content = "保存", Margin = new Thickness(0, 14, 0, 0), IsDefault = true };
        button.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) window.DialogResult = true; };
        panel.Children.Add(input); panel.Children.Add(button); window.Content = panel;
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }
}
