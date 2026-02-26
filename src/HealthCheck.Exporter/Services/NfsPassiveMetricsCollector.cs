using HealthCheck.Core.Models;

namespace HealthCheck.Exporter;

public interface INfsPassiveMetricsCollector
{
    void CollectOnce();
}

/// <summary>
/// Orchestrates multiple passive NFS collectors.
/// Kept in Exporter layer to avoid coupling Core to OS-specific /proc and Prometheus.
/// </summary>
public sealed class NfsPassiveMetricsCollector : INfsPassiveMetricsCollector
{
    private readonly INfsMountstatsCollector _mountstats;
    private readonly INfsRpcStatsCollector _rpc;

    public NfsPassiveMetricsCollector(HealthConfigRoot config)
    {
        _mountstats = new NfsMountstatsCollector(config);
        _rpc = new NfsRpcStatsCollector();
    }

    public void CollectOnce()
    {
        if (!OperatingSystem.IsLinux())
            return;

        _mountstats.CollectOnce();
        _rpc.CollectOnce();
    }
}
