using HealteCheck.Services;
using HealthCheck.Core.Models;
using HealthCheck.Core.Services;
using System.Text.Json;

namespace HealthCheck.Cli.App;

public sealed class HealthRunner
{
    public async Task<int> RunOnceAsync(string configPath)
    {
        if (!File.Exists(configPath))
        {
            Log($"ERROR: Config file '{configPath}' not found.");
            return 1;
        }

        string json = await File.ReadAllTextAsync(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var root = JsonSerializer.Deserialize<HealthConfigRoot>(json, options);

        if (root == null || root.Shares == null || root.Shares.Count == 0)
        {
            Log($"ERROR: No shares defined in config '{configPath}'.");
            return 1;
        }

        var stressSettings = root.Stress ?? new StressSettings();
        var stressRunner = new ShareStressRunner();
        var checkerFactory = new ShareHealthCheckerFactory();

        Log("=== FILESYSTEM HEALTH CHECK START ===");
        Log($"Config: {configPath}, Shares: {root.Shares.Count}");

        foreach (var cfg in root.Shares)
        {
            if (stressSettings.Enabled)
            {
                Log($"STRESS: Starting stress for share '{cfg.ShareName}'...");
                await stressRunner.RunStressAsync(cfg, stressSettings);
                Log($"STRESS: Finished stress for share '{cfg.ShareName}'.");
            }

            var checker = checkerFactory.Create(cfg);
            var res = await checker.RunChecksAsync();
            PrintResult(res);
        }

        Log("=== FILESYSTEM HEALTH CHECK END ===");
        return 0;
    }

    private static string Now() =>
        DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private static void Log(string message) =>
        Console.WriteLine($"[{Now()}] {message}");

    private static void PrintResult(ShareHealthResult res)
    {
        Console.WriteLine();
        Log($"--- SHARE '{res.ShareName}' ---");

        if (!res.Success)
        {
            Log("STATUS: FAIL");
            if (!string.IsNullOrEmpty(res.ErrorMessage))
            {
                Log("ERROR:");
                Console.WriteLine(res.ErrorMessage);
            }
            return;
        }

        Log("STATUS: OK");

        // Capacity
        if (res.TotalBytes.HasValue && res.FreeBytes.HasValue && res.FreeRatio.HasValue)
        {
            double totalGb = res.TotalBytes.Value / 1024d / 1024d / 1024d;
            double freeGb = res.FreeBytes.Value / 1024d / 1024d / 1024d;
            double percent = res.FreeRatio.Value * 100.0;

            Log($"Capacity: total={totalGb:F2} GB, free={freeGb:F2} GB ({percent:F1}%).");
        }
        else
        {
            Log("Capacity: N/A");
        }

        // Write
        if (res.WriteDurationSeconds.HasValue && res.WriteThroughputBytesPerSecond.HasValue)
        {
            double mbps = res.WriteThroughputBytesPerSecond.Value / 1024d / 1024d;
            Log($"Write: size={res.TestFileSizeMb} MB, duration={res.WriteDurationSeconds.Value:F2}s, throughput={mbps:F2} MB/s.");
        }
        else
        {
            Log("Write: N/A");
        }
        // Connection open latency
        if (res.ConnectionOpenLatencyMs.HasValue)
        {
            Log($"Connection open latency: {res.ConnectionOpenLatencyMs.Value:F2} ms.");
        }
        else
        {
            Log("Connection open latency: N/A");
        }

        // Read – המדידה היחידה (ב-Windows unbuffered, ב-Linux best-effort)
        if (res.ReadUnbufferedDurationSeconds.HasValue &&
            res.ReadUnbufferedThroughputBytesPerSecond.HasValue)
        {
            double mbpsUnbuffered = res.ReadUnbufferedThroughputBytesPerSecond.Value / 1024d / 1024d;
            Log($"Read: duration={res.ReadUnbufferedDurationSeconds.Value:F4}s, throughput={mbpsUnbuffered:F2} MB/s.");
        }
        else
        {
            Log("Read: N/A");
        }

        // Small files
        if (res.SmallFilesCount > 0 &&
            res.SmallCreateDurationSeconds.HasValue &&
            res.SmallCreateOpsPerSecond.HasValue &&
            res.SmallDeleteDurationSeconds.HasValue &&
            res.SmallDeleteOpsPerSecond.HasValue)
        {
            Log(
                $"Small files: count={res.SmallFilesCount}, " +
                $"create={res.SmallCreateDurationSeconds.Value:F2}s ({res.SmallCreateOpsPerSecond.Value:F1} ops/s), " +
                $"delete={res.SmallDeleteDurationSeconds.Value:F2}s ({res.SmallDeleteOpsPerSecond.Value:F1} ops/s).");
        }
        else
        {
            Log("Small files: N/A");
        }
    }
}
