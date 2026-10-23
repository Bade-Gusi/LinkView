using System.Text.Json.Nodes;
using System.Text.Json;
using LinkView.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LinkView.Windows.Services;

public sealed class SettingsService
{
    private readonly AppPaths _paths;
    private readonly ILogger<SettingsService> _logger;
    private readonly object _sync = new();
    private AppSettings _current = new();

    public SettingsService(AppPaths paths, ILogger<SettingsService> logger)
    {
        _paths = paths;
        _logger = logger;
        Load();
    }

    public AppSettings Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public event EventHandler<AppSettings>? Changed;

    public IReadOnlyList<SettingsIssue> Validate(AppSettings settings) => SettingsValidator.Validate(settings);

    public bool Save(AppSettings settings, out IReadOnlyList<SettingsIssue> issues)
    {
        issues = SettingsValidator.Validate(settings);
        if (issues.Any(i => i.Severity == SettingsIssueSeverity.Error))
        {
            return false;
        }

        lock (_sync)
        {
            _paths.EnsureCreated();
            SettingsJson.SaveAtomic(_paths.SettingsFile, settings);
            _current = settings;
        }

        Changed?.Invoke(this, settings);
        _logger.LogInformation("settings_saved");
        return true;
    }

    public void ResetToDefaults()
    {
        Save(new AppSettings(), out _);
    }

    public (bool Ok, string? Error) Import(string path)
    {
        try
        {
            var settings = SettingsJson.Load(path);
            if (!Save(settings, out var issues))
            {
                var first = issues.First(i => i.Severity == SettingsIssueSeverity.Error);
                return (false, $"{first.Field}：{first.Message}");
            }

            return (true, null);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return (false, ex.Message);
        }
    }

    public string Export(string path)
    {
        File.WriteAllText(path, SettingsJson.Serialize(Current));
        return path;
    }

    private void Load()
    {
        _paths.EnsureCreated();
        if (!File.Exists(_paths.SettingsFile))
        {
            _current = new AppSettings();
            return;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_paths.SettingsFile)) as JsonObject
                       ?? throw new InvalidOperationException("settings root must be an object");
            var result = SettingsMigrator.Migrate(root);
            _current = result.Settings;

            var errors = SettingsValidator.Validate(_current)
                .Where(i => i.Severity == SettingsIssueSeverity.Error)
                .ToList();
            if (errors.Count > 0)
            {
                _logger.LogWarning("settings_invalid falling back to defaults: {Issues}",
                    string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}")));
                var backup = _paths.SettingsFile + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmss}";
                File.Copy(_paths.SettingsFile, backup, overwrite: true);
                _current = new AppSettings();
            }
            else if (result.AppliedMigrations.Count > 0)
            {
                SettingsJson.SaveAtomic(_paths.SettingsFile, _current);
                _logger.LogInformation("settings_migrated {Migrations}", string.Join(",", result.AppliedMigrations));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            _logger.LogError(ex, "settings_load_failed");
            _current = new AppSettings();
        }
    }
}
