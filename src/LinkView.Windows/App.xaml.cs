using System.Windows;
using System.Windows.Threading;
using LinkView.Core.Settings;
using LinkView.Windows.Services;
using LinkView.Windows.Services.Logging;
using LinkView.Windows.ViewModels;
using LinkView.Windows.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LinkView.Windows;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new AppPaths();
        paths.EnsureCreated();
        var logStore = new LogStore();
        var levelSwitch = new RuntimeLogLevel();

        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddProvider(new LinkViewLoggerProvider(paths, logStore, levelSwitch));
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton(paths);
                services.AddSingleton(logStore);
                services.AddSingleton(levelSwitch);
                services.AddSingleton<IdentityService>();
                services.AddSingleton<SettingsService>();
                services.AddSingleton<SecretStore>();
                services.AddSingleton<NetworkInfoService>();
                services.AddSingleton<DeviceStore>();
                services.AddSingleton<ConnectionHistoryService>();
                services.AddSingleton<DiscoveryService>();
                services.AddSingleton<Media.ScreenCaptureService>();
                services.AddSingleton<StunProbe>();
                services.AddSingleton<SessionCoordinator>();
                services.AddSingleton<DiagnosticsService>();
                services.AddSingleton<ThemeService>();
                services.AddSingleton<IFileDialogService, FileDialogService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        await _host.StartAsync();
        var settings = _host.Services.GetRequiredService<SettingsService>();
        levelSwitch.Level = MapLevel(settings.Current.Diagnostics.LogLevel);
        levelSwitch.RetentionDays = settings.Current.Diagnostics.LogRetentionDays;

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    private static LogLevel MapLevel(AppLogLevel level) => level switch
    {
        AppLogLevel.Trace => LogLevel.Trace,
        AppLogLevel.Debug => LogLevel.Debug,
        AppLogLevel.Information => LogLevel.Information,
        AppLogLevel.Warning => LogLevel.Warning,
        AppLogLevel.Error => LogLevel.Error,
        _ => LogLevel.Information
    };

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "ui_unhandled_exception");
        MessageBox.Show(
            "LinkView 遇到未处理的界面错误。详细信息已写入诊断日志。\n\n" + e.Exception.Message,
            "LinkView", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _host?.Services.GetService<ILogger<App>>()?.LogCritical(ex, "process_unhandled_exception");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "unobserved_task_exception");
        e.SetObserved();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                var coordinator = _host.Services.GetService<SessionCoordinator>();
                if (coordinator is not null)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await coordinator.DisposeAsync().AsTask().WaitAsync(cts.Token);
                }

                var discovery = _host.Services.GetService<DiscoveryService>();
                if (discovery is not null)
                {
                    await discovery.StopAsync();
                }

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _host.StopAsync(timeout.Token);
            }
            catch (Exception)
            {
            }
            finally
            {
                _host.Dispose();
            }
        }

        base.OnExit(e);
    }
}
