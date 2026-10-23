using System.Security.Cryptography;
using LinkView.Core.Identity;
using Microsoft.Extensions.Logging;

namespace LinkView.Windows.Services;

/// <summary>
/// 管理本机设备身份。私钥使用 DPAPI(CurrentUser) 保护后写入磁盘，
/// 任何设置文件或日志中都不会出现私钥。
/// </summary>
public sealed class IdentityService
{
    private static readonly byte[] Entropy = "LinkView.Identity.v1"u8.ToArray();
    private readonly AppPaths _paths;
    private readonly ILogger<IdentityService> _logger;
    private readonly Lazy<DeviceIdentity> _identity;

    public IdentityService(AppPaths paths, ILogger<IdentityService> logger)
    {
        _paths = paths;
        _logger = logger;
        _identity = new Lazy<DeviceIdentity>(LoadOrCreate, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public DeviceIdentity Identity => _identity.Value;

    private DeviceIdentity LoadOrCreate()
    {
        _paths.EnsureCreated();
        if (File.Exists(_paths.IdentityFile))
        {
            try
            {
                var protectedBytes = File.ReadAllBytes(_paths.IdentityFile);
                var pkcs8 = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                var key = ECDsa.Create();
                key.ImportPkcs8PrivateKey(pkcs8, out _);
                CryptographicOperations.ZeroMemory(pkcs8);
                var identity = new DeviceIdentity(key);
                _logger.LogInformation("device_identity_loaded {DeviceId}", identity.DeviceId);
                return identity;
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or ArgumentException)
            {
                // 身份文件损坏时不静默覆盖旧文件，先备份，方便用户恢复或重新配对。
                var backup = _paths.IdentityFile + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
                try
                {
                    File.Move(_paths.IdentityFile, backup, overwrite: false);
                }
                catch (IOException)
                {
                }

                _logger.LogError(ex, "device_identity_corrupt backup={Backup}", backup);
            }
        }

        var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var newIdentity = new DeviceIdentity(newKey);
        Save(newKey);
        _logger.LogInformation("device_identity_created {DeviceId}", newIdentity.DeviceId);
        return newIdentity;
    }

    private void Save(ECDsa key)
    {
        var pkcs8 = key.ExportPkcs8PrivateKey();
        try
        {
            var protectedBytes = ProtectedData.Protect(pkcs8, Entropy, DataProtectionScope.CurrentUser);
            var temp = _paths.IdentityFile + ".tmp";
            File.WriteAllBytes(temp, protectedBytes);
            File.Move(temp, _paths.IdentityFile, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }
}
