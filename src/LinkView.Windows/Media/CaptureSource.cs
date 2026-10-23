namespace LinkView.Windows.Media;

public enum CaptureSourceKind
{
    Monitor,
    Window
}

public sealed record CaptureSource(
    CaptureSourceKind Kind,
    string Id,
    string DisplayName,
    string Detail,
    nint Handle,
    int Width,
    int Height,
    bool IsPrimary)
{
    public string KindLabel => Kind == CaptureSourceKind.Monitor ? "显示器" : "窗口";
}

public sealed record CapturedFrame(
    byte[] Bgra,
    int Width,
    int Height,
    long TimestampTicks,
    int SourceWidth,
    int SourceHeight);

public sealed class CaptureException : Exception
{
    public CaptureException(string code, string message, string? detail = null) : base(message)
    {
        Code = code;
        Detail = detail;
    }

    public string Code { get; }
    public string? Detail { get; }
}
