using System.Security.Cryptography;
using System.Text.Json;

namespace LinkView.Windows.Services;

/// <summary>TURN 凭据等敏感数据使用 DPAPI 加密保存，不进入普通配置文件。</summary>
public sealed class SecretStore
{
    private static readonly byte[] Entropy = "LinkView.Secrets.v1"u8.ToArray();
    private readonly AppPaths _paths;
    private readonly object _sync = new();
    private Dictionary<string, string>? _cache;

    public SecretStore(AppPaths paths) => _paths = paths;

    public string? Get(string key)
    {
        lock (_sync)
        {
            return Load().TryGetValue(key, out var value) ? value : null;
        }
    }

    public void Set(string key, string? value)
    {
        lock (_sync)
        {
            var data = Load();
            if (string.IsNullOrEmpty(value))
            {
                if (!data.Remove(key))
                {
                    return;
                }
            }
            else
            {
                data[key] = value;
            }

            Save(data);
        }
    }

    private Dictionary<string, string> Load()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_paths.SecretsFile))
        {
            return _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_paths.SecretsFile);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(plain)
                         ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            // 凭据无法解密时不应导致应用崩溃；用户重新输入即可。
            _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return _cache;
    }

    private void Save(Dictionary<string, string> data)
    {
        _paths.EnsureCreated();
        var plain = JsonSerializer.SerializeToUtf8Bytes(data);
        try
        {
            var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_paths.SecretsFile, protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
