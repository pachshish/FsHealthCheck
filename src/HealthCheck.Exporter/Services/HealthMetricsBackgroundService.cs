using HealteCheck.Services;
using HealthCheck.Core.Models;
using HealthCheck.Core.Services;
using Microsoft.Extensions.Hosting;

namespace HealthCheck.Exporter;

public sealed class HealthMetricsBackgroundService : BackgroundService
{
    private readonly HealthConfigRoot _config;
    private readonly IShareHealthCheckerFactory _checkerFactory;
    private readonly IShareStressRunner _stressRunner;
    private readonly IHealthMetricsUpdater _metricsUpdater;
    private readonly TimeSpan _interval;

    public HealthMetricsBackgroundService(
        HealthConfigRoot config,
        IShareHealthCheckerFactory checkerFactory,
        IShareStressRunner stressRunner,
        IHealthMetricsUpdater metricsUpdater,
        IConfiguration configuration)
    {
        _config = config;
        _checkerFactory = checkerFactory;
        _stressRunner = stressRunner;
        _metricsUpdater = metricsUpdater;

        int intervalSeconds = configuration.GetValue<int?>("MetricsIntervalSeconds") ?? 300;
        _interval = TimeSpan.FromSeconds(intervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var shareCfg in _config.Shares)
            {
                string label = shareCfg.ShareName;

                try
                {
                    if (_config.Stress?.Enabled == true)
                    {
                        Console.WriteLine($"[Exporter] (timer) Stress for share '{label}'...");
                        await _stressRunner.RunStressAsync(shareCfg, _config.Stress, stoppingToken);
                    }

                    var checker = _checkerFactory.Create(shareCfg);
                    var res = await checker.RunChecksAsync();

                    _metricsUpdater.UpdateMetrics(res);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Exporter] (timer) ERROR share '{label}': {ex}");
                }
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
