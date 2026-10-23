using System.Text.Json;
using System.Text.Json.Nodes;

namespace LinkView.Core.Settings;

public sealed record SettingsMigrationResult(AppSettings Settings, IReadOnlyList<string> AppliedMigrations, IReadOnlyList<string> Warnings);

/// <summary>
/// 配置迁移。规则：
/// 1. 缺少版本号视为版本 0；
/// 2. 只做已知的、可测试的迁移；
/// 3. 高于当前版本的配置拒绝加载，而不是猜测字段含义；
/// 4. 未知字段保留在 UnknownFields 中。
/// </summary>
public static class SettingsMigrator
{
    public static SettingsMigrationResult Migrate(JsonObject root)
    {
        var applied = new List<string>();
        var warnings = new List<string>();

        var version = root["schemaVersion"]?.GetValue<int?>() ?? 0;
        if (version > AppSettings.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"配置文件版本 {version} 高于当前支持的版本 {AppSettings.CurrentVersion}，请升级 LinkView。");
        }

        if (version < 1)
        {
            MigrateV0ToV1(root);
            applied.Add("v0 -> v1");
        }

        root["schemaVersion"] = AppSettings.CurrentVersion;

        var json = root.ToJsonString();
        var settings = JsonSerializer.Deserialize<AppSettings>(json, SettingsJson.Options)
                       ?? throw new InvalidOperationException("配置文件反序列化结果为 null。");

        // extension data 中的字段只做提示，不参与业务逻辑。
        if (root["schemaVersion"] is not null)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "schemaVersion", "general", "video", "network", "connection", "security",
                "privacy", "appearance", "performance", "diagnostics", "experimental"
            };
            foreach (var pair in root.Where(p => p.Value is not null && !known.Contains(p.Key)))
            {
                warnings.Add($"保留未知配置字段：{pair.Key}");
            }
        }

        return new SettingsMigrationResult(settings, applied, warnings);
    }

    private static void MigrateV0ToV1(JsonObject root)
    {
        // 早期原型使用扁平字段。迁移后进行删除，避免新旧字段同时生效。
        Move(root, "deviceName", "general", "deviceName");
        Move(root, "theme", "appearance", "theme");
        Move(root, "language", "appearance", "language");
        Move(root, "fps", "video", "targetFps");
        Move(root, "bitrateKbps", "video", "targetBitrateKbps");
        Move(root, "stunServer", "network", "stunServers", transform: v => new JsonArray(v?.GetValue<string>()));

        if (root["video"]?["targetFps"]?.GetValue<int?>() is int fps and > 0)
        {
            // v0 的 fps 没有上限约束，迁移时钳制到合法范围。
            root["video"]!["targetFps"] = Math.Clamp(fps, 5, 120);
        }
    }

    private static void Move(JsonObject root, string oldKey, string section, string newKey, Func<JsonNode?, JsonNode?>? transform = null)
    {
        if (root[oldKey] is not { } value)
        {
            return;
        }

        var target = root[section] as JsonObject;
        if (target is null)
        {
            target = new JsonObject();
            root[section] = target;
        }

        if (target[newKey] is null)
        {
            target[newKey] = transform is null ? value.DeepClone() : transform(value);
        }

        root.Remove(oldKey);
    }
}
