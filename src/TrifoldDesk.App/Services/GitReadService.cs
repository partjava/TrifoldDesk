using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace TrifoldDesk.Services;

public sealed record GitReadResult(bool IsRepository, IReadOnlyList<GitFileStatus> Changes, string? Error, DateTime RetrievedUtc);
public static class GitReadService
{
    public static async Task<GitReadResult> ReadAsync(string root, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo { FileName = "git", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { "--no-optional-locks", "-c", "core.fsmonitor=false", "-C", root, "status", "--porcelain=v1", "-z", "--untracked-files=normal", "--ignore-submodules=all" }) process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0"; process.StartInfo.Environment["LC_ALL"] = "C";
        try
        {
            cancellationToken.ThrowIfCancellationRequested(); process.Start();
            var outputTask = ReadBounded(process.StandardOutput, 1024 * 1024, timeout.Token);
            var errorTask = ReadBounded(process.StandardError, 16384, timeout.Token);
            var readers = Task.WhenAll(outputTask, errorTask);
            // Stop waiting for process completion immediately when a bounded reader fails.
            var completed = await Task.WhenAny(process.WaitForExitAsync(timeout.Token), readers);
            if (completed == readers) await readers;
            await process.WaitForExitAsync(timeout.Token);
            string output = await outputTask; string error = await errorTask;
            if (process.ExitCode != 0) return new(false, [], error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase) ? "此目录不是Git仓库" : "Git状态不可用：" + error.Trim(), DateTime.UtcNow);
            return new(true, GitStatusRules.ParsePorcelain(output), null, DateTime.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, [], "Git查询超时；大型仓库请稍后刷新", DateTime.UtcNow); }
        catch (Win32Exception) { return new(false, [], "Git未安装或不可用", DateTime.UtcNow); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException) { return new(false, [], "Git状态读取失败：" + ex.Message, DateTime.UtcNow); }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, int limit, CancellationToken cancellationToken)
    {
        var result = new StringBuilder(); var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            if (result.Length + count > limit) throw new IOException("Git状态输出过大，未完整显示。");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
