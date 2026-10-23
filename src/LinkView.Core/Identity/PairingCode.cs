using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LinkView.Core.Identity;

/// <summary>
/// 一次性配对码。配对码只用于把两台已认证设备放进同一个信令会话，
/// 不能代替 WebRTC 的 DTLS 身份验证。
/// </summary>
public static class PairingCode
{
    public const int Length = 6;

    /// <summary>使用密码学随机数生成 6 位数字配对码。</summary>
    public static string Generate()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }

    public static bool IsWellFormed(string? code) =>
        code is { Length: Length } && code.All(char.IsAsciiDigit);

    /// <summary>生成带随机盐的存储哈希，避免内存或日志中保留明文配对码。</summary>
    public static (string SaltBase64, string HashBase64) Hash(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        var salt = RandomNumberGenerator.GetBytes(16);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(ComputeHash(salt, code)));
    }

    public static bool Verify(string code, string saltBase64, string expectedHashBase64)
    {
        if (!IsWellFormed(code))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expected = Convert.FromBase64String(expectedHashBase64);
            var actual = ComputeHash(salt, code);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] ComputeHash(byte[] salt, string code)
    {
        using var hmac = new HMACSHA256(salt);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes($"LinkView-Pair-v1:{code}"));
    }
}
