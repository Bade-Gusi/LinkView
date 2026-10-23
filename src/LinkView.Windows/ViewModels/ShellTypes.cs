using CommunityToolkit.Mvvm.ComponentModel;
using LinkView.Windows.Services;

namespace LinkView.Windows.ViewModels;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record NavItem(string Key, string Label, string Description, string Glyph, object Page);

public sealed partial class ToastMessage : ObservableObject
{
    public ToastMessage(string text, ToastKind kind)
    {
        Text = text;
        Kind = kind;
    }

    public string Text { get; }
    public ToastKind Kind { get; }
}

public interface IFileDialogService
{
    string? PickOpenFile(string filter, string title);
    string? PickSaveFile(string filter, string title, string defaultName);
    bool Confirm(string title, string message);
}

public sealed class FileDialogService : IFileDialogService
{
    public string? PickOpenFile(string filter, string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, Title = title };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string filter, string title, string defaultName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            Title = title,
            FileName = defaultName
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string message) =>
        System.Windows.MessageBox.Show(message, title,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
}

public static class Formatters
{
    public static string ShortDeviceId(string id) =>
        id.Length <= 12 ? id : $"{id[..6]}…{id[^6..]}";

    public static string Addresses(IEnumerable<string> addresses, int max = 3)
    {
        var list = addresses.Distinct().Take(max).ToArray();
        return list.Length == 0 ? "未获取到地址" : string.Join(" · ", list);
    }

    public static string StateDescription(Core.Sessions.ShareSessionState state) => state switch
    {
        Core.Sessions.ShareSessionState.Idle => "选择一项操作开始",
        Core.Sessions.ShareSessionState.Pairing => "使用一次性配对码建立安全的临时会话",
        Core.Sessions.ShareSessionState.AwaitingConsent => "等待对方确认本次连接",
        Core.Sessions.ShareSessionState.Negotiating => "正在交换媒体协商信息",
        Core.Sessions.ShareSessionState.Connecting => "正在建立加密的点对点通道",
        Core.Sessions.ShareSessionState.Connected => "连接已建立，可以开始共享屏幕",
        Core.Sessions.ShareSessionState.Sharing => "屏幕画面正在实时发送",
        Core.Sessions.ShareSessionState.Reconnecting => "网络波动，正在尝试恢复会话",
        Core.Sessions.ShareSessionState.Disconnected => "会话已结束",
        Core.Sessions.ShareSessionState.Failed => "连接未建立，请查看错误详情",
        _ => ""
    };
}
