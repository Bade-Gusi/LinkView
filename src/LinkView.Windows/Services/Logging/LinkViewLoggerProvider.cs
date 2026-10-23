using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace LinkView.Windows.Services.Logging;

public sealed class RuntimeLogLevel
{
    private volatile int _level = (int)LogLevel.Information;

    public LogLevel Level
    {
        get => (LogLevel)_level;
        set => _level = (int)value;
    }

    public int RetentionDays { get; set; } = 7;
}

public sealed partial class LinkViewLoggerProvider : ILoggerProvider
{
    private readonly AppPaths _paths;
    private readonly LogStore _store;
    private readonly RuntimeLogLevel _level;
    private readonly object _fileLock = new();
    private bool _disposed;

    public LinkViewLoggerProvider(AppPaths paths, LogStore store, RuntimeLogLevel level)
    {
        _paths = paths;
        _store = store;
        _level = level;
        _paths.EnsureCreated();
        CleanupOldLogs();
    }

    public ILogger CreateLogger(string categoryName) => new LinkViewLogger(this, categoryName);

    private void Write(LogEntry entry)
    {
        _store.Add(entry);
        if (entry.Level < _level.Level)
        {
            return;
        }

        var line = new StringBuilder(256)
            .Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(' ')
            .Append(entry.LevelLabel.PadRight(5))
            .Append(' ')
            .Append(entry.CategoryShort)
            .Append(" | ")
            .Append(entry.Message);

        if (!string.IsNullOrWhiteSpace(entry.Exception))
        {
            line.AppendLine().Append(entry.Exception);
        }

        lock (_fileLock)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var path = Path.Combine(_paths.LogsDirectory, $"linkview-{entry.Timestamp:yyyy-MM-dd}.log");
                File.AppendAllText(path, line.AppendLine().ToString(), Encoding.UTF8);
            }
            catch (IOException)
            {
                // 日志写入失败不能反过来影响业务；内存日志仍在。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(_level.RetentionDays, 1, 365));
            foreach (var file in Directory.EnumerateFiles(_paths.LogsDirectory, "linkview-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        lock (_fileLock)
        {
            _disposed = true;
        }
    }

    private sealed class LinkViewLogger : ILogger
    {
        private readonly LinkViewLoggerProvider _provider;
        private readonly string _category;

        public LinkViewLogger(LinkViewLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _provider._level.Level;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = LogSanitizer.Sanitize(formatter(state, exception));
            var exceptionText = exception is null
                ? null
                : LogSanitizer.Sanitize(exception.ToString());
            if (exceptionText is { Length: > 4000 })
            {
                exceptionText = exceptionText[..4000] + "…";
            }

            _provider.Write(new LogEntry(DateTimeOffset.UtcNow, logLevel, _category, message, exceptionText));
        }
    }
}

public static partial class LogSanitizer
{
    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = CredentialRegex().Replace(input, "$1=***");
        result = PairingCodeRegex().Replace(result, "\"pairingCode\":\"***\"");
        return result;
    }

    [GeneratedRegex("(credential|password|secret|token)\\s*[=:]\\s*[^\\s,;]+", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialRegex();

    [GeneratedRegex("\"pairingCode\"\\s*:\\s*\"[^\"]*\"", RegexOptions.IgnoreCase)]
    private static partial Regex PairingCodeRegex();
}
