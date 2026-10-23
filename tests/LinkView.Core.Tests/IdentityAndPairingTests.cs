using System.Security.Cryptography;
using LinkView.Core.Identity;

namespace LinkView.Core.Tests;

public class IdentityAndPairingTests
{
    [Fact]
    public void DeviceId_IsStable_ForSameKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = key.ExportSubjectPublicKeyInfo();
        var id1 = DeviceIdentity.ComputeDeviceId(spki);
        var id2 = DeviceIdentity.ComputeDeviceId(spki);

        Assert.Equal(id1, id2);
        Assert.StartsWith("lv", id1);
        Assert.Equal(34, id1.Length);
    }

    [Fact]
    public void DifferentKeys_ProduceDifferentDeviceIds()
    {
        var a = DeviceIdentity.CreateEphemeral();
        var b = DeviceIdentity.CreateEphemeral();
        Assert.NotEqual(a.DeviceId, b.DeviceId);
        Assert.NotEqual(a.PublicKeyBase64, b.PublicKeyBase64);
    }

    [Fact]
    public void AuthSignature_VerifiesOnlyWithExactCanonicalText()
    {
        var identity = DeviceIdentity.CreateEphemeral();
        const string nonce = "n-123";
        const long ts = 1_760_000_000_000;
        var canonical = DeviceIdentity.BuildAuthCanonical(nonce, identity.DeviceId, ts);
        var signature = identity.Sign(canonical);
        var spki = Convert.FromBase64String(identity.PublicKeyBase64);

        Assert.True(DeviceIdentity.Verify(spki, canonical, signature));

        var tampered = DeviceIdentity.BuildAuthCanonical(nonce, identity.DeviceId, ts + 1);
        Assert.False(DeviceIdentity.Verify(spki, tampered, signature));
    }

    [Fact]
    public void PairingCode_HasSixDigits()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = PairingCode.Generate();
            Assert.True(PairingCode.IsWellFormed(code));
            Assert.Equal(6, code.Length);
        }
    }

    [Fact]
    public void PairingCode_RejectsNonDigitsAndWrongLength()
    {
        Assert.False(PairingCode.IsWellFormed("12345"));
        Assert.False(PairingCode.IsWellFormed("1234567"));
        Assert.False(PairingCode.IsWellFormed("12345a"));
        Assert.False(PairingCode.IsWellFormed(null));
        Assert.True(PairingCode.IsWellFormed("000000"));
    }

    [Fact]
    public void PairingHash_VerifiesOnlyCorrectCode()
    {
        var code = PairingCode.Generate();
        var (salt, hash) = PairingCode.Hash(code);

        Assert.True(PairingCode.Verify(code, salt, hash));
        var wrong = code == "000000" ? "000001" : "000000";
        Assert.False(PairingCode.Verify(wrong, salt, hash));
    }

    [Fact]
    public void PairingHashes_UseUniqueSalt()
    {
        var (salt1, hash1) = PairingCode.Hash("123456");
        var (salt2, hash2) = PairingCode.Hash("123456");
        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
    }
}
