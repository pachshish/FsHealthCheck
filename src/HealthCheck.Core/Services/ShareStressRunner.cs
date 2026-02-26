using HealthCheck.Core.Models;
namespace HealteCheck.Services;

public sealed class ShareStressRunner : IShareStressRunner
{
    public async Task RunStressAsync(ShareHealthConfig cfg, StressSettings settings, CancellationToken ct = default)
    {
        if (!settings.Enabled)
            return;

        var start = DateTimeOffset.UtcNow;
        var end = start.AddSeconds(settings.DurationSeconds);
        var tasks = new List<Task>();

        for (int i = 0; i < settings.ParallelWorkers; i++)
        {
            int workerId = i;
            tasks.Add(Task.Run(() => WorkerLoop(cfg, settings, workerId, end, ct), ct));
        }

        Console.WriteLine($"[{Now()}] STRESS: Share '{cfg.ShareName}' for {settings.DurationSeconds}s, workers={settings.ParallelWorkers}.");
        await Task.WhenAll(tasks);
        Console.WriteLine($"[{Now()}] STRESS: Share '{cfg.ShareName}' finished.");
    }

    private void WorkerLoop(ShareHealthConfig cfg, StressSettings settings, int workerId, DateTimeOffset end, CancellationToken ct)
    {
        string workerDir = Path.Combine(cfg.HealthDirectory, "_stress", $"worker_{workerId}");
        Directory.CreateDirectory(workerDir);

        var rnd = new Random(workerId * 17 + 42);
        var buffer = new byte[1024 * 1024]; // 1MB
        rnd.NextBytes(buffer);

        while (DateTimeOffset.UtcNow < end && !ct.IsCancellationRequested)
        {
            // 1. Write file
            string filePath = Path.Combine(workerDir, $"stress_{Guid.NewGuid():N}.bin");
            long fileSizeBytes = (long)settings.WriteFileSizeMb * 1024L * 1024L;
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.SequentialScan))
            {
                long written = 0;
                while (written < fileSizeBytes)
                {
                    int toWrite = (int)Math.Min(buffer.Length, fileSizeBytes - written);
                    fs.Write(buffer, 0, toWrite);
                    written += toWrite;
                }
            }

            // אופציונלי: קריאה של אותו קובץ
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan))
            {
                while (fs.Read(buffer, 0, buffer.Length) > 0) { }
            }

            // מחיקה כדי לא למלא את הדיסק
            File.Delete(filePath);

            // 2. small files batch
            int n = settings.SmallFilesPerBatch;
            for (int i = 0; i < n; i++)
            {
                string p = Path.Combine(workerDir, $"small_{Guid.NewGuid():N}.tmp");
                using (File.Create(p)) { }
            }
            foreach (var f in Directory.EnumerateFiles(workerDir, "small_*.tmp"))
            {
                File.Delete(f);
            }
        }
    }

    private static string Now() =>
        DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
}

