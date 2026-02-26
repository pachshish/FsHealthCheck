using Microsoft.Extensions.Hosting;

namespace HealthCheck.Exporter;

/// <summary>
/// Collects passive (client-side) NFS metrics from Linux /proc.
/// This does NOT perform active I/O against the share.
///
/// Config:
///   NfsPassiveMetricsIntervalSeconds (default: 15)
/// </summary>
public sealed class NfsPassiveMetricsBackgroundService : BackgroundService
{
    private readonly INfsPassiveMetricsCollector _collector;
    private readonly TimeSpan _interval;

    public NfsPassiveMetricsBackgroundService(
        INfsPassiveMetricsCollector collector,
        IConfiguration configuration)
    {
        _collector = collector;
        int seconds = configuration.GetValue<int?>("NfsPassiveMetricsIntervalSeconds") ?? 15;
        _interval = TimeSpan.FromSeconds(Math.Max(1, seconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _collector.CollectOnce();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Exporter] NFS passive metrics ERROR: {ex}");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
