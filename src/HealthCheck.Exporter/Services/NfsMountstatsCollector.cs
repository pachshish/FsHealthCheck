using HealthCheck.Core.Models;
using Prometheus;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HealthCheck.Exporter;

public interface INfsMountstatsCollector
{
    void CollectOnce();
}

/// <summary>
/// Reads passive NFS client statistics from /proc/self/mountstats.
/// No active probe is performed and no extra traffic is generated.
///
/// Exported metrics:
/// - Read/Write throughput per mount
/// - Avg RTT per operation
/// - Avg execute latency per operation
/// - Retransmission ratio per operation
///
/// Notes:
/// Per-op line format in mountstats is commonly:
///   OP: calls transmissions major_timeouts bytes_sent bytes_received queue_ms rtt_ms execute_ms [...]
///
/// Important:
/// The second field is transmissions, not retransmissions.
/// Retransmissions are derived as:
///   retransmissions = transmissions - calls
/// </summary>
public sealed class NfsMountstatsCollector : INfsMountstatsCollector
{
    private readonly HealthConfigRoot _config;

    private static readonly Gauge NfsMountReadThroughputBytesPerSecond =
        Metrics.CreateGauge(
            "fs_nfs_mount_read_throughput_bytes_per_second",
            "NFS client read throughput per mount derived from /proc/self/mountstats deltas",
            new[] { "share", "mount" });

    private static readonly Gauge NfsMountWriteThroughputBytesPerSecond =
        Metrics.CreateGauge(
            "fs_nfs_mount_write_throughput_bytes_per_second",
            "NFS client write throughput per mount derived from /proc/self/mountstats deltas",
            new[] { "share", "mount" });

    private static readonly Gauge NfsOpAvgRttMilliseconds =
        Metrics.CreateGauge(
            "fs_nfs_op_avg_rtt_milliseconds",
            "Average RPC round trip time per NFS operation over the latest scrape interval",
            new[] { "share", "mount", "op" });

    private static readonly Gauge NfsOpAvgExecuteMilliseconds =
        Metrics.CreateGauge(
            "fs_nfs_op_avg_execute_milliseconds",
            "Average end-to-end execute time per NFS operation over the latest scrape interval",
            new[] { "share", "mount", "op" });

    private static readonly Gauge NfsOpRetransRatio =
        Metrics.CreateGauge(
            "fs_nfs_op_retrans_ratio",
            "Retransmission ratio per NFS operation over the latest scrape interval",
            new[] { "share", "mount", "op" });

    private readonly ConcurrentDictionary<string, MountBytesSnapshot> _mountBytesSnapshots = new();
    private readonly ConcurrentDictionary<string, OpSnapshot> _opSnapshots = new();

    public NfsMountstatsCollector(HealthConfigRoot config)
    {
        _config = config;
    }

    public void CollectOnce()
    {
        const string mountstatsPath = "/proc/self/mountstats";

        if (!File.Exists(mountstatsPath))
            return;

        string text;
        try
        {
            text = File.ReadAllText(mountstatsPath);
        }
        catch
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;

        foreach (var block in SplitIntoBlocks(text))
        {
            if (!block.Header.Contains(" fstype nfs", StringComparison.OrdinalIgnoreCase) &&
                !block.Header.Contains(" fstype nfs4", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var mount = block.MountPoint;
            if (string.IsNullOrWhiteSpace(mount))
                continue;

            var shareName = ResolveShareName(mount) ?? "unknown";
            var mountKey = $"{shareName}|{mount}";

            if (TryParseBytesLine(block.Lines, out var readBytes, out var writeBytes))
            {
                UpdateMountThroughput(
                    shareName,
                    mount,
                    mountKey,
                    nowUtc,
                    readBytes,
                    writeBytes);
            }

            foreach (var stat in ParseOpLines(block.Lines))
            {
                UpdateDerivedOpMetrics(
                    shareName,
                    mount,
                    mountKey,
                    stat,
                    nowUtc);
            }
        }
    }

    private void UpdateMountThroughput(
        string shareName,
        string mount,
        string mountKey,
        DateTimeOffset nowUtc,
        double readBytes,
        double writeBytes)
    {
        var current = new MountBytesSnapshot(nowUtc, readBytes, writeBytes);

        if (_mountBytesSnapshots.TryGetValue(mountKey, out var previous))
        {
            var seconds = (current.TimestampUtc - previous.TimestampUtc).TotalSeconds;
            if (seconds > 0)
            {
                var readDelta = Math.Max(0, current.ReadBytes - previous.ReadBytes);
                var writeDelta = Math.Max(0, current.WriteBytes - previous.WriteBytes);

                NfsMountReadThroughputBytesPerSecond
                    .WithLabels(shareName, mount)
                    .Set(readDelta / seconds);

                NfsMountWriteThroughputBytesPerSecond
                    .WithLabels(shareName, mount)
                    .Set(writeDelta / seconds);
            }
        }

        _mountBytesSnapshots[mountKey] = current;
    }

    private void UpdateDerivedOpMetrics(
        string shareName,
        string mount,
        string mountKey,
        OpStat current,
        DateTimeOffset nowUtc)
    {
        var snapshotKey = $"{mountKey}|{current.Operation}";
        var currentSnapshot = new OpSnapshot(nowUtc, current);

        if (_opSnapshots.TryGetValue(snapshotKey, out var previous))
        {
            var callsDelta = current.Calls - previous.Stat.Calls;
            if (callsDelta > 0)
            {
                var rttDelta = Math.Max(0, current.RttMilliseconds - previous.Stat.RttMilliseconds);
                var executeDelta = Math.Max(0, current.ExecuteMilliseconds - previous.Stat.ExecuteMilliseconds);

                var currentRetrans = Math.Max(0, current.Transmissions - current.Calls);
                var previousRetrans = Math.Max(0, previous.Stat.Transmissions - previous.Stat.Calls);
                var retransDelta = Math.Max(0, currentRetrans - previousRetrans);

                NfsOpAvgRttMilliseconds
                    .WithLabels(shareName, mount, current.Operation)
                    .Set(rttDelta / callsDelta);

                NfsOpAvgExecuteMilliseconds
                    .WithLabels(shareName, mount, current.Operation)
                    .Set(executeDelta / callsDelta);

                NfsOpRetransRatio
                    .WithLabels(shareName, mount, current.Operation)
                    .Set(retransDelta / callsDelta);
            }
        }

        _opSnapshots[snapshotKey] = currentSnapshot;
    }

    private string? ResolveShareName(string mountPoint)
    {
        string? best = null;
        int bestLen = -1;

        foreach (var share in _config.Shares)
        {
            if (string.IsNullOrWhiteSpace(share.SharePath))
                continue;

            var configuredPath = share.SharePath.TrimEnd('/');

            if (mountPoint.Equals(configuredPath, StringComparison.Ordinal) ||
                mountPoint.StartsWith(configuredPath + "/", StringComparison.Ordinal))
            {
                if (configuredPath.Length > bestLen)
                {
                    bestLen = configuredPath.Length;
                    best = share.ShareName;
                }
            }
        }

        return best;
    }

    private static bool TryParseBytesLine(List<string> lines, out double readBytes, out double writeBytes)
    {
        readBytes = 0;
        writeBytes = 0;

        var line = lines.FirstOrDefault(l =>
            l.TrimStart().StartsWith("bytes:", StringComparison.OrdinalIgnoreCase));

        if (line is null)
            return false;

        var parts = Regex.Split(line.Trim(), @"\s+");
        if (parts.Length < 3)
            return false;

        return TryParseDouble(parts[1], out readBytes) &&
               TryParseDouble(parts[2], out writeBytes);
    }

    private static IEnumerable<OpStat> ParseOpLines(List<string> lines)
    {
        bool inPerOpSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("per-op statistics", StringComparison.OrdinalIgnoreCase))
            {
                inPerOpSection = true;
                continue;
            }

            if (!inPerOpSection)
                continue;

            if (!line.Contains(':'))
                continue;

            var idx = line.IndexOf(':');
            if (idx <= 0)
                continue;

            var op = line[..idx].Trim();
            if (op.Length == 0)
                continue;

            var values = Regex.Split(line[(idx + 1)..].Trim(), @"\s+");
            if (values.Length < 8)
                continue;

            if (!TryParseDouble(values[0], out var calls) ||
                !TryParseDouble(values[1], out var transmissions) ||
                !TryParseDouble(values[2], out var majorTimeouts) ||
                !TryParseDouble(values[3], out var bytesSent) ||
                !TryParseDouble(values[4], out var bytesReceived) ||
                !TryParseDouble(values[5], out var queueMs) ||
                !TryParseDouble(values[6], out var rttMs) ||
                !TryParseDouble(values[7], out var executeMs))
            {
                continue;
            }

            yield return new OpStat(
                Operation: op,
                Calls: calls,
                Transmissions: transmissions,
                MajorTimeouts: majorTimeouts,
                BytesSent: bytesSent,
                BytesReceived: bytesReceived,
                QueueMilliseconds: queueMs,
                RttMilliseconds: rttMs,
                ExecuteMilliseconds: executeMs);
        }
    }

    private static IEnumerable<MountBlock> SplitIntoBlocks(string text)
    {
        var lines = text.Split('\n');
        MountBlock? current = null;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');

            if (line.StartsWith("device ", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null)
                    yield return current;

                current = new MountBlock(
                    Header: line,
                    MountPoint: ExtractMountPoint(line),
                    Lines: new List<string>());
            }

            current?.Lines.Add(line);
        }

        if (current is not null)
            yield return current;
    }

    private static string ExtractMountPoint(string header)
    {
        var match = Regex.Match(
            header,
            @"mounted on\s+(?<m>.+?)\s+with fstype",
            RegexOptions.IgnoreCase);

        return match.Success
            ? match.Groups["m"].Value.Trim()
            : string.Empty;
    }

    private static bool TryParseDouble(string value, out double result)
    {
        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    private sealed record MountBlock(string Header, string MountPoint, List<string> Lines);

    private sealed record MountBytesSnapshot(
        DateTimeOffset TimestampUtc,
        double ReadBytes,
        double WriteBytes);

    private sealed record OpSnapshot(
        DateTimeOffset TimestampUtc,
        OpStat Stat);

    private sealed record OpStat(
        string Operation,
        double Calls,
        double Transmissions,
        double MajorTimeouts,
        double BytesSent,
        double BytesReceived,
        double QueueMilliseconds,
        double RttMilliseconds,
        double ExecuteMilliseconds);
}