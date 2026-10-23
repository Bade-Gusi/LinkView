using System.Collections.Concurrent;

namespace LinkView.Windows.Services.Logging;

/// <summary>供“日志与诊断”页面展示的内存环形缓冲。不会持久化敏感内容。</summary>
public sealed class LogStore
{
    private const int Capacity = 2_000;
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private int _count;

    public event EventHandler<LogEntry>? EntryAdded;

    public IReadOnlyList<LogEntry> Snapshot() => _entries.ToArray();

    public void Add(LogEntry entry)
    {
        _entries.Enqueue(entry);
        while (Interlocked.Increment(ref _count) > Capacity && _entries.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);
        }

        EntryAdded?.Invoke(this, entry);
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _count, 0);
    }
}
