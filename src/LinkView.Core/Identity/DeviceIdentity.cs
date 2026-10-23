using System.Security.Cryptography;
using System.Text;
using LinkView.Protocol;

namespace LinkView.Core.Identity;

/// <summary>
/// 设备身份。身份由 ECDSA P-256 公私钥对定义，DeviceId 是公钥指纹，
/// 而不是用户可随意修改的设备名称。
/// </summary>
public sealed class DeviceIdentity
{
    public const string AlgorithmName = "ECDSA_P256_SHA256";

    private readonly ECDsa _key;

    public DeviceIdentity(ECDsa key)
    {
        _key = key;
        var spki = _key.ExportSubjectPublicKeyInfo();
        PublicKeyBase64 = Convert.ToBase64String(spki);
        DeviceId = ComputeDeviceId(spki);
    }

    public string DeviceId { get; }

    public string PublicKeyBase64 { get; }

    /// <summary>用身份私钥对认证挑战进行签名。</summary>
    public byte[] Sign(ReadOnlySpan<byte> data) => _key.SignData(data, HashAlgorithmName.SHA256);

    /// <summary>
    /// 规范化待签名文本。服务端和客户端必须使用完全相同的格式，
    /// 否则客户端时间戳或 nonce 的编码差异会导致认证失败。
    /// </summary>
    public static byte[] BuildAuthCanonical(string nonce, string deviceId, long clientTimestampUnixMs) =>
        Encoding.UTF8.GetBytes(
            $"{ProtocolConstants.AuthCanonicalPrefix}\n{nonce}\n{deviceId}\n{clientTimestampUnixMs}");

    /// <summary>从公钥计算稳定设备 ID：SHA-256(SPKI) 前 16 字节。</summary>
    public static string ComputeDeviceId(ReadOnlySpan<byte> spki)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(spki, hash);
        return "lv" + Convert.ToHexStringLower(hash[..16]);
    }

    /// <summary>验证他人签名。</summary>
    public static bool Verify(ReadOnlySpan<byte> spki, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(spki, out _);
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    // 便于测试：允许从受信任的导入实现构造。
    public static DeviceIdentity CreateEphemeral() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));
}
