using HealteCheck.Services;
using HealthCheck.Core.Models;
using HealthCheck.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace HealthCheck.Exporter.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthCheckController : ControllerBase
{
    private readonly HealthConfigRoot _config;
    private readonly IShareHealthCheckerFactory _checkerFactory;
    private readonly IShareStressRunner _stressRunner;
    private readonly IHealthMetricsUpdater _metricsUpdater;

    public HealthCheckController(
        HealthConfigRoot config,
        IShareHealthCheckerFactory checkerFactory,
        IShareStressRunner stressRunner,
        IHealthMetricsUpdater metricsUpdater)
    {
        _config = config;
        _checkerFactory = checkerFactory;
        _stressRunner = stressRunner;
        _metricsUpdater = metricsUpdater;
    }


    [HttpPost("run")]
    public async Task<IActionResult> RunNow([FromQuery] bool withStress = false, CancellationToken ct = default)
    {
        var results = new List<object>();

        foreach (var shareCfg in _config.Shares)
        {
            var label = shareCfg.ShareName;

            try
            {
                if (withStress && _config.Stress?.Enabled == true)
                {
                    Console.WriteLine($"[Exporter] (manual) Stress for share '{label}'...");
                    await _stressRunner.RunStressAsync(shareCfg, _config.Stress, ct);
                }

                var checker = _checkerFactory.Create(shareCfg);
                var res = await checker.RunChecksAsync();

                _metricsUpdater.UpdateMetrics(res);

                results.Add(new
                {
                    res.ShareName,
                    res.Success,
                    res.ErrorMessage,
                    FreeRatio = res.FreeRatio,
                    FreeBytes = res.FreeBytes,
                    TotalBytes = res.TotalBytes,

                    WriteMbps = res.WriteThroughputBytesPerSecond.HasValue
        ? (double?)(res.WriteThroughputBytesPerSecond.Value / 1024d / 1024d)
        : null,

                    ReadUnbufferedMbps = res.ReadUnbufferedThroughputBytesPerSecond.HasValue
        ? (double?)(res.ReadUnbufferedThroughputBytesPerSecond.Value / 1024d / 1024d)
        : null,

                    SmallFilesCreateOpsPerSec = res.SmallCreateOpsPerSecond,
                    SmallFilesDeleteOpsPerSec = res.SmallDeleteOpsPerSecond
                });

            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    ShareName = shareCfg.ShareName,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        return Ok(new
        {
            RanAt = DateTimeOffset.Now,
            WithStress = withStress,
            SharesCount = _config.Shares.Count,
            Results = results
        });
    }
}
