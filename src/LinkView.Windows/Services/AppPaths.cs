namespace LinkView.Windows.Services;

public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinkView");
        SettingsFile = Path.Combine(Root, "settings.json");
        IdentityFile = Path.Combine(Root, "identity.key");
        SecretsFile = Path.Combine(Root, "secrets.dat");
        HistoryFile = Path.Combine(Root, "history.json");
        DevicesFile = Path.Combine(Root, "devices.json");
        LogsDirectory = Path.Combine(Root, "logs");
        DiagnosticsDirectory = Path.Combine(Root, "diagnostics");
    }

    public string Root { get; }
    public string SettingsFile { get; }
    public string IdentityFile { get; }
    public string SecretsFile { get; }
    public string HistoryFile { get; }
    public string DevicesFile { get; }
    public string LogsDirectory { get; }
    public string DiagnosticsDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);
    }
}
