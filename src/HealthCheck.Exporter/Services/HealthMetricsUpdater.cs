using HealthCheck.Core.Models;
using Prometheus;

namespace HealthCheck.Exporter;

public interface IHealthMetricsUpdater
{
    void UpdateMetrics(ShareHealthResult res);
}

public sealed class HealthMetricsUpdater : IHealthMetricsUpdater
{
    private static readonly Gauge FreeRatio =
        Metrics.CreateGauge("fs_share_free_ratio", "Free space ratio (0-1) per share", new[] { "share" });

    private static readonly Gauge FreeBytes =
        Metrics.CreateGauge("fs_share_free_bytes", "Free bytes per share", new[] { "share" });

    private static readonly Gauge TotalBytes =
        Metrics.CreateGauge("fs_share_total_bytes", "Total bytes per share", new[] { "share" });

    private static readonly Gauge WriteThroughput =
        Metrics.CreateGauge("fs_share_write_throughput_bytes", "Write throughput (bytes/sec) per share", new[] { "share" });

    private static readonly Gauge ReadUnbufferedThroughput =
        Metrics.CreateGauge("fs_share_read_unbuffered_throughput_bytes", "Unbuffered read throughput (bytes/sec) per share", new[] { "share" });

    private static readonly Gauge SmallCreateOps =
        Metrics.CreateGauge("fs_share_smallfiles_create_ops_per_sec", "Small files create ops/sec per share", new[] { "share" });

    private static readonly Gauge SmallDeleteOps =
        Metrics.CreateGauge("fs_share_smallfiles_delete_ops_per_sec", "Small files delete ops/sec per share", new[] { "share" });

    public void UpdateMetrics(ShareHealthResult res)
    {
        var label = res.ShareName;

        if (!res.Success)
        {
            // אפשר בעתיד להוסיף metric לשגיאה, כרגע לא עושים כלום
            return;
        }

        if (res.FreeRatio.HasValue)
            FreeRatio.WithLabels(label).Set(res.FreeRatio.Value);

        if (res.FreeBytes.HasValue)
            FreeBytes.WithLabels(label).Set(res.FreeBytes.Value);

        if (res.TotalBytes.HasValue)
            TotalBytes.WithLabels(label).Set(res.TotalBytes.Value);

        if (res.WriteThroughputBytesPerSecond.HasValue)
            WriteThroughput.WithLabels(label).Set(res.WriteThroughputBytesPerSecond.Value);

        if (res.ReadUnbufferedThroughputBytesPerSecond.HasValue)
            ReadUnbufferedThroughput.WithLabels(label).Set(res.ReadUnbufferedThroughputBytesPerSecond.Value);

        if (res.SmallCreateOpsPerSecond.HasValue)
            SmallCreateOps.WithLabels(label).Set(res.SmallCreateOpsPerSecond.Value);

        if (res.SmallDeleteOpsPerSecond.HasValue)
            SmallDeleteOps.WithLabels(label).Set(res.SmallDeleteOpsPerSecond.Value);
    }
}
