using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkView.Windows.Services;

namespace LinkView.Windows.ViewModels;

public sealed record HistoryRow(
    string PeerName,
    string Direction,
    string StartedText,
    string DurationText,
    string ResultText,
    string Detail,
    string DataText,
    bool Success);

public partial class HistoryViewModel : ObservableObject
{
    private readonly ConnectionHistoryService _history;
    private readonly SettingsService _settings;
    private readonly Action<string, ToastKind> _notify;

    public HistoryViewModel(
        ConnectionHistoryService history,
        SettingsService settings,
        Action<string, ToastKind> notify)
    {
        _history = history;
        _settings = settings;
        _notify = notify;
    }

    [ObservableProperty] private bool isEmpty = true;
    [ObservableProperty] private string summary = "暂无连接记录";

    public ObservableCollection<HistoryRow> Rows { get; } = [];

    public void Refresh()
    {
        _history.Prune(_settings.Current.Privacy.HistoryRetentionDays);
        Rows.Clear();
        foreach (var record in _history.All.Take(200))
        {
            var duration = record.EndedAt is { } end ? end - record.StartedAt : TimeSpan.Zero;
            var data = (record.SentBytes ?? 0) + (record.ReceivedBytes ?? 0);
            Rows.Add(new HistoryRow(
                record.PeerName,
                record.Direction == "share" ? "共享" : "接收",
                record.StartedAt.ToLocalTime().ToString("MM-dd HH:mm"),
                duration > TimeSpan.Zero ? $"{(int)duration.TotalMinutes} 分 {duration.Seconds} 秒" : "—",
                record.Result switch
                {
                    "completed" => "已完成",
                    "failed" => "失败",
                    "disconnected" => "已断开",
                    _ => record.Result
                },
                string.IsNullOrWhiteSpace(record.Detail) ? record.Codec ?? "" : record.Detail,
                data > 0 ? BytesToText(data) : "—",
                record.Result != "failed"));
        }

        IsEmpty = Rows.Count == 0;
        Summary = Rows.Count == 0 ? "暂无连接记录" : $"共 {Rows.Count} 条记录";
    }

    [RelayCommand]
    private void Clear()
    {
        _history.Clear();
        Refresh();
        _notify("连接历史已清除", ToastKind.Info);
    }

    private static string BytesToText(long bytes) =>
        Converters.BytesToTextConverter.FormatBytes(bytes);
}
