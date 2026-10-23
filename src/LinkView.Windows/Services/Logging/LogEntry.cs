using Microsoft.Extensions.Logging;

namespace LinkView.Windows.Services.Logging;

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception)
{
    public string LevelLabel => Level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT",
        _ => "LOG"
    };

    public string CategoryShort
    {
        get
        {
            var index = Category.LastIndexOf('.');
            return index >= 0 ? Category[(index + 1)..] : Category;
        }
    }
}
