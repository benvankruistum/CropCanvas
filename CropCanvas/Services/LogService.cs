using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace CropCanvas.Services;

public record LogEntry(string Timestamp, string Message, LogLevel Level);

public enum LogLevel { Info, Warning, Error, Success }

/// <summary>
/// Application-wide logging service. Maintains an observable collection
/// of log entries that can be bound to the UI.
/// </summary>
public class LogService
{
    private static readonly Lazy<LogService> _instance = new(() => new LogService());
    public static LogService Instance => _instance.Value;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    private readonly Dispatcher? _dispatcher;

    private LogService()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher;
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry(DateTime.Now.ToString("HH:mm:ss"), message, level);

        if (_dispatcher != null && !_dispatcher.CheckAccess())
            _dispatcher.Invoke(() => Entries.Add(entry));
        else
            Entries.Add(entry);
    }

    public void Info(string message) => Log(message, LogLevel.Info);
    public void Warn(string message) => Log(message, LogLevel.Warning);
    public void Error(string message) => Log(message, LogLevel.Error);
    public void Success(string message) => Log(message, LogLevel.Success);

    public void Clear() => Entries.Clear();

    /// <summary>
    /// Creates an IProgress&lt;string&gt; that logs each report as Info.
    /// </summary>
    public IProgress<string> CreateProgress()
    {
        return new Progress<string>(msg =>
        {
            Info(msg);
        });
    }
}
