using System.Text.Json.Nodes;
using LinkView.Core.Settings;

namespace LinkView.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        var issues = SettingsValidator.Validate(new AppSettings());
        Assert.DoesNotContain(issues, i => i.Severity == SettingsIssueSeverity.Error);
    }

    [Fact]
    public void InvalidBitrateOrder_IsRejected()
    {
        var settings = new AppSettings
        {
            Video = new VideoSettings
            {
                MinBitrateKbps = 5000,
                TargetBitrateKbps = 3000,
                MaxBitrateKbps = 2000
            }
        };

        var issues = SettingsValidator.Validate(settings);
        Assert.Contains(issues, i => i.Field == "Video.TargetBitrateKbps");
        Assert.Contains(issues, i => i.Field == "Video.MaxBitrateKbps");
        Assert.False(SettingsValidator.IsValid(settings));
    }

    [Fact]
    public void RelayOnly_WithoutTurn_IsError()
    {
        var settings = new AppSettings
        {
            Network = new NetworkSettings { IceTransport = IceTransportMode.RelayOnly }
        };

        var issues = SettingsValidator.Validate(settings);
        Assert.Contains(issues, i => i.Field == "Network.IceTransport" && i.Severity == SettingsIssueSeverity.Error);
    }

    [Fact]
    public void InvalidAccentColor_IsRejected()
    {
        var settings = new AppSettings { Appearance = new AppearanceSettings { AccentColor = "blue" } };
        Assert.Contains(SettingsValidator.Validate(settings), i => i.Field == "Appearance.AccentColor");
    }

    [Fact]
    public void Migration_V0ToV1_MovesLegacyFields()
    {
        var root = JsonNode.Parse("""
        {
          "deviceName": "Old PC",
          "theme": "dark",
          "fps": 200,
          "bitrateKbps": 2500,
          "stunServer": "stun:example.org:3478",
          "customVendorField": "keep-me"
        }
        """)!.AsObject();

        var result = SettingsMigrator.Migrate(root);

        Assert.Contains("v0 -> v1", result.AppliedMigrations);
        Assert.Equal("Old PC", result.Settings.General.DeviceName);
        Assert.Equal(ThemeMode.Dark, result.Settings.Appearance.Theme);
        Assert.Equal(120, result.Settings.Video.TargetFps);
        Assert.Equal(2500, result.Settings.Video.TargetBitrateKbps);
        Assert.Contains("stun:example.org:3478", result.Settings.Network.StunServers);
        Assert.Contains(result.Warnings, w => w.Contains("customVendorField"));
        Assert.Null(root["deviceName"]);
    }

    [Fact]
    public void Migration_RejectsFutureSchemaVersion()
    {
        var root = JsonNode.Parse("""{ "schemaVersion": 99 }""")!.AsObject();
        var ex = Assert.Throws<InvalidOperationException>(() => SettingsMigrator.Migrate(root));
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void SerializeRoundTrip_PreservesSections()
    {
        var original = new AppSettings
        {
            General = new GeneralSettings { DeviceName = "书房电脑" },
            Video = new VideoSettings { TargetFps = 60, MaxHeight = 1440 },
            Appearance = new AppearanceSettings { Theme = ThemeMode.Light }
        };

        var json = SettingsJson.Serialize(original);
        var loaded = SettingsMigrator.Migrate(JsonNode.Parse(json)!.AsObject()).Settings;

        Assert.Equal("书房电脑", loaded.General.DeviceName);
        Assert.Equal(60, loaded.Video.TargetFps);
        Assert.Equal(ThemeMode.Light, loaded.Appearance.Theme);
    }
}
