using LinkView.Protocol;
using LinkView.Windows.Services;

namespace LinkView.Windows.ViewModels;

public sealed record LicenseInfo(string Name, string Version, string License, string Purpose);

public sealed class AboutViewModel
{
    public AboutViewModel(IdentityService identity)
    {
        DeviceId = identity.Identity.DeviceId;
    }

    public string AppVersion => typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.9.0";
    public string Runtime => $".NET {Environment.Version}";
    public string OsVersion => Environment.OSVersion.ToString();
    public string DeviceId { get; }
    public int ProtocolVersion => ProtocolConstants.Version;

    public IReadOnlyList<LicenseInfo> Licenses { get; } =
    [
        new("SIPSorcery", "10.0.17", "BSD-3-Clause", "ICE、DTLS、SRTP 与 RTP 媒体传输"),
        new("libvpx / vpxmd", "内置 x64 构建", "BSD-3-Clause", "VP8 视频编解码"),
        new("Vortice.Windows", "3.8.3", "MIT", "Direct3D 11 采集纹理互操作"),
        new("CommunityToolkit.Mvvm", "8.4.2", "MIT", "MVVM 源生成器"),
        new("Microsoft.Extensions.*", "10.0.13", "MIT", "宿主、依赖注入与日志"),
        new("ASP.NET Core", "10.0", "MIT", "可选的轻量信令服务"),
        new("BouncyCastle.Cryptography", "2.7.0", "MIT", "DTLS 证书与加密原语"),
        new("Concentus", "2.2.2", "BSD-3-Clause", "Opus 音频（当前版本未启用音频）")
    ];
}
