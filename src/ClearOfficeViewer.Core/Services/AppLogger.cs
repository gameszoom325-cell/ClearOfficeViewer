using System.IO;
using System.Text;

namespace ClearOfficeViewer.Core.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public sealed class AppLogger
{
    private static readonly Lazy<AppLogger> _instance = new(() => new AppLogger());
    public static AppLogger Instance => _instance.Value;

    private readonly object _sync = new();
    private readonly string _logDirectory;
    private readonly List<string> _recentLines = [];
    private const int MaxBufferedLines = 500;

    public string LogDirectory => _logDirectory;

    private AppLogger()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logDirectory = Path.Combine(localAppData, "ClearOfficeViewer", "logs");
        try
        {
            Directory.CreateDirectory(_logDirectory);
        }
        catch
        {
            // Ignore directory creation failure in restricted environments.
        }
    }

    public void Info(string category, string message) => Write(LogLevel.Info, category, message, null);
    public void Debug(string category, string message) => Write(LogLevel.Debug, category, message, null);
    public void Warn(string category, string message) => Write(LogLevel.Warning, category, message, null);
    public void Error(string category, string message, Exception? ex = null) => Write(LogLevel.Error, category, message, ex);

    public IReadOnlyList<string> GetRecentEntries()
    {
        lock (_sync)
        {
            return _recentLines.ToArray();
        }
    }

    private void Write(LogLevel level, string category, string message, Exception? ex)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var sb = new StringBuilder();
        sb.Append('[').Append(timestamp).Append("] [").Append(level.ToString().ToUpperInvariant()).Append("] [")
          .Append(category).Append("] ").Append(message);

        if (ex != null)
        {
            sb.AppendLine();
            sb.Append("  Exception: ").Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                sb.AppendLine();
                sb.Append(ex.StackTrace);
            }
        }

        string line = sb.ToString();

        lock (_sync)
        {
            _recentLines.Add(line);
            if (_recentLines.Count > MaxBufferedLines)
                _recentLines.RemoveAt(0);

            try
            {
                string filePath = Path.Combine(_logDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Avoid throwing from diagnostic logging.
            }
        }
    }
}
