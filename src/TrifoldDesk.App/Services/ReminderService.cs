using System.Text.Json;
using System.Windows.Threading;

namespace TrifoldDesk.Services;

/// <summary>One low-frequency application timer. Delivery is persisted before invoking the notification callback.</summary>
public sealed class ReminderService : IDisposable
{
    private readonly ConfigManager _store;
    private readonly Action<CalendarOccurrence> _notify;
    private readonly Action<string>? _warning;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private DateTime? _lastCheck;
    private string? _lastWarning;
    private bool _disposed;
    public ReminderService(ConfigManager store, Action<CalendarOccurrence> notify, Action<string>? warning = null, bool start = true)
    {
        _store = store; _notify = notify; _warning = warning;
        _timer.Tick += Tick;
        if (start) _timer.Start();
    }
    private void Tick(object? sender, EventArgs e) => CheckNow(DateTime.Now);
    public void SetPaused(bool paused){if(_disposed)return;if(paused)_timer.Stop();else _timer.Start();}
    public void CheckNow(DateTime nowLocal)
    {
        if (_disposed) return;
        try
        {
            var loaded = _store.Load<EventConfig>("events.json");
            if (loaded.Warning != null) Warn(loaded.Warning);
            if (_store.ReadOnlyFiles.Contains("events.json")) return;
            var due = CalendarEventRules.DueReminders(loaded.Value, nowLocal, _lastCheck);
            if (due.Count > 0)
            {
                var next = CalendarEventRules.MarkDelivered(loaded.Value, due, nowLocal);
                _store.Save("events.json", next);
                // A write failure leaves the check cursor unchanged so a subsequent tick can retry.
                foreach (var occurrence in due)
                {
                    try { _notify(occurrence); }
                    catch (Exception ex) { Warn("日程通知显示失败：" + ex.Message); }
                }
            }
            _lastCheck = nowLocal;
            if (loaded.Warning == null) _lastWarning = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { Warn("日程提醒暂时不可用：" + ex.Message); }
    }
    private void Warn(string message)
    {
        if (_lastWarning == message) return;
        _lastWarning = message; _warning?.Invoke(message);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop(); _timer.Tick -= Tick;
    }
}
