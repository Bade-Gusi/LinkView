using System.Text.Json;
using LinkView.Protocol;

namespace LinkView.Windows.Services;

public sealed class SavedDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Note { get; set; }
    public bool IsFavorite { get; set; }
    public DeviceType DeviceType { get; set; } = DeviceType.Unknown;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastAddress { get; set; }
}

public sealed class DeviceStore
{
    private readonly AppPaths _paths;
    private readonly object _sync = new();
    private List<SavedDevice> _devices = [];

    public DeviceStore(AppPaths paths)
    {
        _paths = paths;
        Load();
    }

    public IReadOnlyList<SavedDevice> All
    {
        get
        {
            lock (_sync)
            {
                return _devices.Select(Clone).OrderByDescending(d => d.IsFavorite)
                    .ThenByDescending(d => d.LastSeenAt).ToArray();
            }
        }
    }

    public void Upsert(DeviceSummary device, string? address = null, bool preserveNote = true)
    {
        lock (_sync)
        {
            var existing = _devices.FirstOrDefault(d => d.DeviceId == device.DeviceId);
            if (existing is null)
            {
                _devices.Add(new SavedDevice
                {
                    DeviceId = device.DeviceId,
                    Name = device.DeviceName,
                    DeviceType = device.DeviceType,
                    LastSeenAt = DateTimeOffset.UtcNow,
                    LastAddress = address ?? device.ObservedAddress
                });
            }
            else
            {
                existing.Name = device.DeviceName;
                existing.DeviceType = device.DeviceType;
                existing.LastSeenAt = DateTimeOffset.UtcNow;
                existing.LastAddress = address ?? device.ObservedAddress ?? existing.LastAddress;
                if (!preserveNote)
                {
                    existing.Note = null;
                }
            }

            Save();
        }
    }

    public void ToggleFavorite(string deviceId)
    {
        lock (_sync)
        {
            var device = _devices.FirstOrDefault(d => d.DeviceId == deviceId);
            if (device is null) return;
            device.IsFavorite = !device.IsFavorite;
            Save();
        }
    }

    public void SetNote(string deviceId, string? note)
    {
        lock (_sync)
        {
            var device = _devices.FirstOrDefault(d => d.DeviceId == deviceId);
            if (device is null) return;
            device.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            Save();
        }
    }

    public void Remove(string deviceId)
    {
        lock (_sync)
        {
            _devices.RemoveAll(d => d.DeviceId == deviceId);
            Save();
        }
    }

    public void Prune(int maxDevices)
    {
        lock (_sync)
        {
            var limit = Math.Clamp(maxDevices, 1, 200);
            var favorites = _devices.Where(d => d.IsFavorite).ToList();
            var others = _devices.Where(d => !d.IsFavorite)
                .OrderByDescending(d => d.LastSeenAt)
                .Take(Math.Max(0, limit - favorites.Count))
                .ToList();
            var keep = favorites.Concat(others).ToHashSet();
            var before = _devices.Count;
            _devices = _devices.Where(d => keep.Contains(d)).ToList();
            if (_devices.Count != before)
            {
                Save();
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _devices.Clear();
            Save();
        }
    }

    private static SavedDevice Clone(SavedDevice d) => new()
    {
        DeviceId = d.DeviceId,
        Name = d.Name,
        Note = d.Note,
        IsFavorite = d.IsFavorite,
        DeviceType = d.DeviceType,
        LastSeenAt = d.LastSeenAt,
        LastAddress = d.LastAddress
    };

    private void Load()
    {
        if (!File.Exists(_paths.DevicesFile))
        {
            return;
        }

        try
        {
            _devices = JsonSerializer.Deserialize<List<SavedDevice>>(File.ReadAllText(_paths.DevicesFile))
                       ?? [];
        }
        catch (JsonException)
        {
            _devices = [];
        }
        catch (IOException)
        {
            _devices = [];
        }
    }

    private void Save()
    {
        _paths.EnsureCreated();
        File.WriteAllText(_paths.DevicesFile,
            JsonSerializer.Serialize(_devices, new JsonSerializerOptions { WriteIndented = true }));
    }
}
