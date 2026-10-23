using System.Text.Json;

namespace LinkView.Windows.Services;

public sealed class ConnectionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PeerName { get; set; } = string.Empty;
    public string PeerDeviceId { get; set; } = string.Empty;
    public string Direction { get; set; } = "receive";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public string Result { get; set; } = "completed";
    public string? Detail { get; set; }
    public string? Codec { get; set; }
    public long? SentBytes { get; set; }
    public long? ReceivedBytes { get; set; }
}

public sealed class ConnectionHistoryService
{
    private readonly AppPaths _paths;
    private readonly object _sync = new();
    private List<ConnectionRecord> _records = [];

    public ConnectionHistoryService(AppPaths paths)
    {
        _paths = paths;
        Load();
    }

    public IReadOnlyList<ConnectionRecord> All
    {
        get
        {
            lock (_sync)
            {
                return _records.OrderByDescending(r => r.StartedAt).ToArray();
            }
        }
    }

    public void Add(ConnectionRecord record)
    {
        lock (_sync)
        {
            _records.Add(record);
            Prune(30);
            Save();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _records.Clear();
            Save();
        }
    }

    public void Prune(int retentionDays)
    {
        lock (_sync)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(0, retentionDays));
            _records.RemoveAll(r => r.StartedAt < cutoff);
        }
    }

    private void Load()
    {
        if (!File.Exists(_paths.HistoryFile))
        {
            return;
        }

        try
        {
            _records = JsonSerializer.Deserialize<List<ConnectionRecord>>(File.ReadAllText(_paths.HistoryFile)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _records = [];
        }
    }

    private void Save()
    {
        _paths.EnsureCreated();
        File.WriteAllText(_paths.HistoryFile,
            JsonSerializer.Serialize(_records, new JsonSerializerOptions { WriteIndented = true }));
    }
}
