using Prometheus;
using System.Text.RegularExpressions;

namespace HealthCheck.Exporter;

public interface INfsRpcStatsCollector
{
    void CollectOnce();
}

/// <summary>
/// Reads global NFS RPC client stats from /proc/net/rpc/nfs.
/// This does NOT perform active I/O against the share.
/// </summary>
public sealed class NfsRpcStatsCollector : INfsRpcStatsCollector
{
    private static readonly Gauge RpcCallsTotal =
        Metrics.CreateGauge(
            "fs_nfs_rpc_calls_total",
            "NFS client RPC calls total (from /proc/net/rpc/nfs, since boot)");

    private static readonly Gauge RpcRetransTotal =
        Metrics.CreateGauge(
            "fs_nfs_rpc_retrans_total",
            "NFS client RPC retransmissions total (from /proc/net/rpc/nfs, since boot)");

    private static readonly Gauge RpcAuthRefreshTotal =
        Metrics.CreateGauge(
            "fs_nfs_rpc_auth_refresh_total",
            "NFS client RPC auth refresh total (from /proc/net/rpc/nfs, since boot)");

    public void CollectOnce()
    {
        const string rpcPath = "/proc/net/rpc/nfs";
        if (!File.Exists(rpcPath))
            return;

        string text;
        try
        {
            text = File.ReadAllText(rpcPath);
        }
        catch
        {
            return;
        }

        // Client format commonly includes a line:
        // rpc <calls> <retrans> <authrefresh>
        // (We intentionally export only these 3 stable counters.)
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (!t.StartsWith("rpc ", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = Regex.Split(t, @"\s+");
            if (parts.Length < 4)
                continue;

            if (double.TryParse(parts[1], out var calls))
                RpcCallsTotal.Set(calls);

            if (double.TryParse(parts[2], out var retrans))
                RpcRetransTotal.Set(retrans);

            if (double.TryParse(parts[3], out var auth))
                RpcAuthRefreshTotal.Set(auth);

            break;
        }
    }
}
