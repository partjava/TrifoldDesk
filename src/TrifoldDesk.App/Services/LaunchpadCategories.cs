namespace TrifoldDesk.Services;

internal static class LaunchpadCategories
{
    private static readonly (string Label, string[] Keywords)[] Groups =
    [
        ("办公工具", [
            "微信", "wechat", "wecom", "qq", "腾讯会议", "wemeet", "wps", "microsoft 365", "office", "word", "excel", "powerpoint", "outlook", "onenote", "飞书", "lark", "钉钉", "dingtalk", "teams", "zoom", "便签", "notion", "日历",
            "chrome", "firefox", "edge", "浏览器", "browser", "网盘", "云盘", "onedrive", "dropbox", "icloud", "夸克",
            "剪映", "capcut", "clipchamp", "photoshop", "illustrator", "premiere", "blender", "obs studio", "davinci", "音乐", "媒体播放器", "录音机", "spotify", "vlc", "爱奇艺", "抖音", "bilibili", "哔哩哔哩", "画图",
            "steam", "epic games", "xbox", "game bar", "游戏", "riot", "valorant", "minecraft", "战网"]),
        ("开发软件", [
            "visual studio", "vscode", "visualstudiocode", "git", "node.js", "python", "pycharm", "intellij", "android studio", "androidstudio", "sdk", "docker", "vmware", "virtualbox", "sql", "mongo", "redis", "navicat", "postman", "wireshark", "jupyter", "nsight", "odbc",
            "anaconda navigator", "spyder", "application verifier", "debuggable package", "dev home",
            "chatgpt", "claude", "codex", "copilot", "gemini", "antigravity", "cursor", "windsurf", "deepseek", "豆包", "通义", "kimi", "ollama", "lm studio", "cc switch", "trae", "continue", "open webui", "cherry studio", "cline", "aider", "智谱", "文心一言", "元宝"]),
        ("开发终端", [
            "ubuntu", "debian", "kali linux", "opensuse", "fedora", "arch linux", "wsl", "windows subsystem for linux",
            "anaconda prompt", "anaconda powershell", "miniconda prompt", "miniforge prompt", "developer command", "developer powershell", "native tools command", "cross tools command", "powershell", "terminal", "command prompt", "命令提示符", "终端", "命令行", "git bash", "git cmd", "msys", "mingw", "cygwin", "winscp", "putty", "mobaxterm", "finalshell", "tabby", "termius", "wezterm", "alacritty", "hyper terminal"]),
        ("系统与实用工具", ["7-zip", "winrar", "bandizip", "计算器", "记事本", "截图工具", "控制面板", "资源管理器", "任务管理器", "设备管理器", "管理工具", "计算机管理", "配置工具", "磁盘", "恢复驱动器", "防火墙", "windows 安全", "windows 工具", "服务", "设置", "联想电脑管家", "nvidia control panel"])
    ];

    public static IEnumerable<string> Labels => Groups.Select(group => group.Label).Append("其他");

    public static int GetIndex(InstalledApp app)
    {
        string name = app.DisplayName;
        // Terminal names take priority over product names such as Visual Studio or Git.
        int terminalIndex = 2;
        if (Groups[terminalIndex].Keywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            return terminalIndex;

        for (int i = 0; i < Groups.Length; i++)
        {
            if (i == terminalIndex) continue;
            foreach (string keyword in Groups[i].Keywords)
            {
                if (keyword == "qq")
                {
                    if (name.Equals("QQ", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("QQ ", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("腾讯QQ", StringComparison.OrdinalIgnoreCase))
                        return i;
                }
                else if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }
        return Groups.Length;
    }
}
