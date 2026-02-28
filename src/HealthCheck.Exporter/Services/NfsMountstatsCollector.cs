using HealthCheck.Core.Models;
using Prometheus;
using System.Text.RegularExpressions;

namespace HealthCheck.Exporter;

public interface INfsMountstatsCollector
{
    void CollectOnce();
}

/// <summary>
/// Reads per-mount NFS client stats from /proc/self/mountstats.
/// No network traffic is generated.
/// </summary>
public sealed class NfsMountstatsCollector : INfsMountstatsCollector
{
    private readonly HealthConfigRoot _config;

    private static readonly Gauge NfsMountReadBytesTotal =
        Metrics.CreateGauge(
            "fs_nfs_mount_read_bytes_total",
            "NFS client read bytes per mount (from /proc/self/mountstats, since boot)",
            new[] { "share", "mount" });

    private static readonly Gauge NfsMountWriteBytesTotal =
        Metrics.CreateGauge(
            "fs_nfs_mount_write_bytes_total",
            "NFS client write bytes per mount (from /proc/self/mountstats, since boot)",
            new[] { "share", "mount" });

    private static readonly Gauge NfsOpCallsTotal =
        Metrics.CreateGauge(
            "fs_nfs_op_calls_total",
            "NFS op calls per mount (from /proc/self/mountstats, since boot)",
            new[] { "share", "mount", "op" });

    private static readonly Gauge NfsOpRetransTotal =
        Metrics.CreateGauge(
            "fs_nfs_op_retrans_total",
            "NFS op retrans per mount (from /proc/self/mountstats, since boot)",
            new[] { "share", "mount", "op" });

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

        foreach (var block in SplitIntoBlocks(text))
        {
            if (!block.Header.Contains(" fstype nfs", StringComparison.OrdinalIgnoreCase) &&
                !block.Header.Contains(" fstype nfs4", StringComparison.OrdinalIgnoreCase))
                continue;

            var mount = block.MountPoint;
            if (string.IsNullOrWhiteSpace(mount))
                continue;

            var shareName = ResolveShareName(mount) ?? "unknown";

            if (TryParseBytesLine(block.Lines, out var readBytes, out var writeBytes))
            {
                NfsMountReadBytesTotal.WithLabels(shareName, mount).Set(readBytes);
                NfsMountWriteBytesTotal.WithLabels(shareName, mount).Set(writeBytes);
            }

            foreach (var (op, calls, retrans) in ParseOpLines(block.Lines))
            {
                NfsOpCallsTotal.WithLabels(shareName, mount, op).Set(calls);
                NfsOpRetransTotal.WithLabels(shareName, mount, op).Set(retrans);
            }
        }
    }

    private string? ResolveShareName(string mountPoint)
    {
        string? best = null;
        int bestLen = -1;

        foreach (var s in _config.Shares)
        {
            if (string.IsNullOrWhiteSpace(s.SharePath))
                continue;

            var p = s.SharePath.TrimEnd('/');
            if (mountPoint.Equals(p, StringComparison.Ordinal) ||
                mountPoint.StartsWith(p + "/", StringComparison.Ordinal))
            {
                if (p.Length > bestLen)
                {
                    bestLen = p.Length;
                    best = s.ShareName;
                }
            }
        }

        return best;
    }

    private static bool TryParseBytesLine(List<string> lines, out double readBytes, out double writeBytes)
    {
        readBytes = 0;
        writeBytes = 0;

        var line = lines.FirstOrDefault(l => l.TrimStart().StartsWith("bytes:", StringComparison.OrdinalIgnoreCase));
        if (line is null) return false;

        var parts = Regex.Split(line.Trim(), @"\s+");
        if (parts.Length < 3) return false;

        return double.TryParse(parts[1], out readBytes) && double.TryParse(parts[2], out writeBytes);
    }

    private static IEnumerable<(string op, double calls, double retrans)> ParseOpLines(List<string> lines)
    {
        // Common format: "READ: <calls> <retrans> ..."
        foreach (var l in lines)
        {
            var t = l.Trim();
            if (!t.Contains(':')) continue;

            if (t.StartsWith("device ", StringComparison.OrdinalIgnoreCase)) continue;
            if (t.StartsWith("bytes:", StringComparison.OrdinalIgnoreCase)) continue;
            if (t.StartsWith("events:", StringComparison.OrdinalIgnoreCase)) continue;
            if (t.StartsWith("xprt:", StringComparison.OrdinalIgnoreCase)) continue;

            var idx = t.IndexOf(':');
            if (idx <= 0) continue;

            var op = t[..idx].Trim();
            if (op.Length == 0) continue;

            var parts = Regex.Split(t[(idx + 1)..].Trim(), @"\s+");
            if (parts.Length < 2) continue;

            if (double.TryParse(parts[0], out var calls) && double.TryParse(parts[1], out var retrans))
                yield return (op, calls, retrans);
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
        var m = Regex.Match(header, @"mounted on\s+(?<m>.+?)\s+with fstype", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["m"].Value.Trim() : "";
    }

    private sealed record MountBlock(string Header, string MountPoint, List<string> Lines);
}
