using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LinkView.Core.Settings;

public static class SettingsJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) }
    };

    public static AppSettings Load(string path)
    {
        var json = File.ReadAllText(path);
        var root = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidOperationException("配置文件根节点必须是 JSON 对象。");
        return SettingsMigrator.Migrate(root).Settings;
    }

    public static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(settings, Options);

    public static void SaveAtomic(string path, AppSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(settings));
        if (File.Exists(path))
        {
            File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
