using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LinkView.Core.Sessions;
using LinkView.Core.Settings;
using LinkView.Protocol;
using LinkView.Windows.Services;
using LinkView.Windows.Services.Logging;
using Microsoft.Extensions.Logging;

namespace LinkView.Windows.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is not null;
        if (parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is string s && !string.IsNullOrWhiteSpace(s);
        if (parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class StringEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class StateLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ShareSessionState state ? SessionLabels.Describe(state) : "未知";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class StateBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is ShareSessionState state
            ? state switch
            {
                ShareSessionState.Sharing or ShareSessionState.Connected => "Brush.Success",
                ShareSessionState.Connecting or ShareSessionState.Negotiating or ShareSessionState.AwaitingConsent => "Brush.Accent",
                ShareSessionState.Reconnecting or ShareSessionState.Pairing => "Brush.Warning",
                ShareSessionState.Failed => "Brush.Danger",
                _ => "Brush.TextTertiary"
            }
            : "Brush.TextTertiary";
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class StateSoftBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is ShareSessionState state
            ? state switch
            {
                ShareSessionState.Sharing or ShareSessionState.Connected => "Brush.SuccessSoft",
                ShareSessionState.Connecting or ShareSessionState.Negotiating or ShareSessionState.AwaitingConsent => "Brush.AccentSoft",
                ShareSessionState.Reconnecting or ShareSessionState.Pairing => "Brush.WarningSoft",
                ShareSessionState.Failed => "Brush.DangerSoft",
                _ => "Brush.SurfaceAlt"
            }
            : "Brush.SurfaceAlt";
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        Core.Settings.ThemeMode.System => "跟随系统",
        Core.Settings.ThemeMode.Light => "浅色",
        Core.Settings.ThemeMode.Dark => "深色",
        NetworkPreference.Auto => "自动",
        NetworkPreference.Ipv4Preferred => "IPv4 优先",
        NetworkPreference.Ipv6Preferred => "IPv6 优先",
        NetworkPreference.Ipv4Only => "仅 IPv4",
        NetworkPreference.Ipv6Only => "仅 IPv6",
        NetworkPreference.LanOnly => "仅局域网",
        QualityPreset.Auto => "自动",
        QualityPreset.Smooth => "流畅",
        QualityPreset.Balanced => "均衡",
        QualityPreset.High => "高清",
        QualityPreset.Ultra => "超清",
        QualityPreset.Custom => "自定义",
        VideoCodecPreference.Auto => "自动",
        VideoCodecPreference.Vp8 => "VP8",
        VideoCodecPreference.H264 => "H.264（暂不可用）",
        ScaleMode.Fit => "按比例适应",
        ScaleMode.Fill => "填充裁切",
        ScaleMode.Original => "原始尺寸",
        AnimationLevel.Full => "完整",
        AnimationLevel.Reduced => "精简",
        AnimationLevel.None => "关闭",
        InterfaceDensity.Comfortable => "舒适",
        InterfaceDensity.Compact => "紧凑",
        IceTransportMode.All => "直连优先（可中继）",
        IceTransportMode.RelayOnly => "仅中继",
        AppLogLevel.Trace => "Trace",
        AppLogLevel.Debug => "Debug",
        AppLogLevel.Information => "Information",
        AppLogLevel.Warning => "Warning",
        AppLogLevel.Error => "Error",
        LinkView.Core.Networking.IpScope.GlobalUnicast => "全局地址",
        LinkView.Core.Networking.IpScope.Private => "内网/ULA",
        LinkView.Core.Networking.IpScope.LinkLocal => "链路本地",
        LinkView.Core.Networking.IpScope.Loopback => "回环",
        LinkView.Core.Networking.IpScope.Multicast => "多播",
        LinkView.Core.Networking.IpScope.Unspecified => "未指定",
        DeviceType.Windows => "Windows 电脑",
        DeviceType.Android => "Android 手机",
        DeviceType.Unknown => "未知设备",
        SessionRole.Host => "发起方",
        SessionRole.Guest => "加入方",
        CheckStatus.Passed => "通过",
        CheckStatus.Warning => "警告",
        CheckStatus.Failed => "失败",
        CheckStatus.Skipped => "未检查",
        CheckStatus.Running => "检查中",
        LogLevel.Trace => "Trace",
        LogLevel.Debug => "Debug",
        LogLevel.Information => "Information",
        LogLevel.Warning => "Warning",
        LogLevel.Error => "Error",
        LogLevel.Critical => "Critical",
        LinkView.Windows.Media.CaptureSourceKind.Monitor => "显示器",
        LinkView.Windows.Media.CaptureSourceKind.Window => "窗口",
        _ => value?.ToString() ?? ""
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class DeviceGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DeviceType.Android => "\uE8EA",
        DeviceType.Windows => "\uE7F4",
        _ => "\uE772"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class CheckStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            CheckStatus.Passed => "Brush.Success",
            CheckStatus.Warning => "Brush.Warning",
            CheckStatus.Failed => "Brush.Danger",
            _ => "Brush.TextTertiary"
        };
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class CheckStatusGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CheckStatus.Passed => "\uE73E",
        CheckStatus.Warning => "\uE7BA",
        CheckStatus.Failed => "\uEA39",
        CheckStatus.Running => "\uE895",
        _ => "\uE738"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class LogLevelBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            LogLevel.Warning => "Brush.Warning",
            LogLevel.Error or LogLevel.Critical => "Brush.Danger",
            LogLevel.Debug or LogLevel.Trace => "Brush.TextTertiary",
            _ => "Brush.Accent"
        };
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BytesToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var bytes = value switch
        {
            long l => l,
            int i => i,
            _ => 0L
        };
        return FormatBytes(bytes);
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        double value = bytes;
        while (value >= 1024 && order < units.Length - 1)
        {
            value /= 1024;
            order++;
        }

        return $"{value:0.#} {units[order]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class DurationToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var duration = value switch
        {
            TimeSpan t => t,
            _ => TimeSpan.Zero
        };
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset time)
        {
            return "";
        }

        var delta = DateTimeOffset.Now - time;
        if (delta.TotalSeconds < 30) return "刚刚";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} 分钟前";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} 小时前";
        if (delta.TotalDays < 30) return $"{(int)delta.TotalDays} 天前";
        return time.ToLocalTime().ToString("yyyy-MM-dd");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class AccentColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value?.ToString() ?? "#4C9AFF"));
        }
        catch (FormatException)
        {
            return Brushes.Gray;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public static class SessionLabels
{
    public static string Describe(ShareSessionState state) => state switch
    {
        ShareSessionState.Idle => "准备就绪",
        ShareSessionState.Discovering => "正在搜索设备",
        ShareSessionState.DeviceFound => "已发现设备",
        ShareSessionState.Pairing => "正在配对",
        ShareSessionState.AwaitingConsent => "等待对方确认",
        ShareSessionState.Negotiating => "正在协商",
        ShareSessionState.Connecting => "正在连接",
        ShareSessionState.Connected => "已连接",
        ShareSessionState.Sharing => "正在共享",
        ShareSessionState.Reconnecting => "正在恢复连接",
        ShareSessionState.Stopping => "正在停止",
        ShareSessionState.Disconnected => "已断开",
        ShareSessionState.Failed => "连接失败",
        _ => "未知状态"
    };
}

public sealed class IntEqualsToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int i && int.TryParse(parameter?.ToString(), out var expected) && i == expected;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && int.TryParse(parameter?.ToString(), out var expected))
        {
            return expected;
        }

        return Binding.DoNothing;
    }
}

public sealed class IntEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int i && int.TryParse(parameter?.ToString(), out var expected) && i == expected
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
