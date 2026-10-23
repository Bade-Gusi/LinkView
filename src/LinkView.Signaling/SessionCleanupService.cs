using Microsoft.Extensions.Options;

namespace LinkView.Signaling;

/// <summary>周期性清理过期配对、僵尸会话和心跳超时连接。</summary>
public sealed class SessionCleanupService : BackgroundService
{
    private readonly SignalingServer _server;
    private readonly SignalingOptions _options;
    private readonly ILogger<SessionCleanupService> _logger;

    public SessionCleanupService(
        SignalingServer server,
        IOptions<SignalingOptions> options,
        ILogger<SessionCleanupService> logger)
    {
        _server = server;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.CleanupIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await _server.CleanupAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "signaling_cleanup_failed");
            }
        }
    }
}
