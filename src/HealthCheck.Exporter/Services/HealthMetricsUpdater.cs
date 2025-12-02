using HealthCheck.Core.Models;
using Prometheus;
using System.Reflection.Emit;

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

    private static readonly Gauge SmallReadLatencyMs =
        Metrics.CreateGauge(
            "fs_share_small_read_latency_ms",
            "Small 4KB read latency (ms) per share",
            new GaugeConfiguration { LabelNames = new[] { "share" } });

    private static readonly Gauge SmallWriteLatencyMs =
        Metrics.CreateGauge(
            "fs_share_small_write_latency_ms",
            "Small 4KB write latency (ms) per share",
            new GaugeConfiguration { LabelNames = new[] { "share" } });

    private static readonly Gauge DirectoryListDurationSeconds =
        Metrics.CreateGauge(
            "fs_share_dirlist_duration_seconds",
            "Directory listing duration (seconds) for representative directory per share",
            new GaugeConfiguration { LabelNames = new[] { "share" } });

    private static readonly Counter IoErrorsTotal =
        Metrics.CreateCounter(
            "fs_share_io_errors_total",
            "Total I/O errors encountered during health checks per share",
            new CounterConfiguration { LabelNames = new[] { "share" } });
    private static readonly Gauge ConnectionOpenLatencyGauge =
    Metrics.CreateGauge(
        "fs_share_connection_open_latency_ms",
        "Connection open latency (ms) per share",
        "share");


    public void UpdateMetrics(ShareHealthResult res)
    {
        var labels = new[] { res.ShareName };

        if (!res.Success)
        {
            // אפשר בעתיד להוסיף metric לשגיאה, כרגע לא עושים כלום
            return;
        }

        if (res.FreeRatio.HasValue)
            FreeRatio.WithLabels(labels).Set(res.FreeRatio.Value);

        if (res.FreeBytes.HasValue)
            FreeBytes.WithLabels(labels).Set(res.FreeBytes.Value);

        if (res.TotalBytes.HasValue)
            TotalBytes.WithLabels(labels).Set(res.TotalBytes.Value);

        if (res.WriteThroughputBytesPerSecond.HasValue)
            WriteThroughput.WithLabels(labels).Set(res.WriteThroughputBytesPerSecond.Value);

        if (res.ReadUnbufferedThroughputBytesPerSecond.HasValue)
            ReadUnbufferedThroughput.WithLabels(labels).Set(res.ReadUnbufferedThroughputBytesPerSecond.Value);

        if (res.SmallCreateOpsPerSecond.HasValue)
            SmallCreateOps.WithLabels(labels).Set(res.SmallCreateOpsPerSecond.Value);

        if (res.SmallDeleteOpsPerSecond.HasValue)
            SmallDeleteOps.WithLabels(labels).Set(res.SmallDeleteOpsPerSecond.Value);

        if (res.SmallReadLatencyMs.HasValue)
            SmallReadLatencyMs.WithLabels(labels).Set(res.SmallReadLatencyMs.Value);

        if (res.SmallWriteLatencyMs.HasValue)
            SmallWriteLatencyMs.WithLabels(labels).Set(res.SmallWriteLatencyMs.Value);

        if (res.DirectoryListDurationSeconds.HasValue)
            DirectoryListDurationSeconds.WithLabels(labels).Set(res.DirectoryListDurationSeconds.Value);

        // ✅ חדשים – errors: counter → מוסיפים את הכמות של הריצה הזו
        if (res.IoErrorCount > 0)
            IoErrorsTotal.WithLabels(labels).Inc(res.IoErrorCount);

        if (res.ConnectionOpenLatencyMs.HasValue)
        {
            ConnectionOpenLatencyGauge
                .WithLabels(res.ShareName)
                .Set(res.ConnectionOpenLatencyMs.Value);
        }
        else
        {
            ConnectionOpenLatencyGauge
                .WithLabels(res.ShareName)
                .Set(double.NaN);
        }

    }
}
