namespace HealthCheck.Core.Models;

public sealed class ShareHealthResult
{
    public string ShareName { get; set; } = "";
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public long? TotalBytes { get; set; }
    public long? FreeBytes { get; set; }
    public double? FreeRatio { get; set; }

    public int TestFileSizeMb { get; set; }
    public double? WriteDurationSeconds { get; set; }
    public double? WriteThroughputBytesPerSecond { get; set; }

    public double? ReadDurationSeconds { get; set; }
    public double? ReadThroughputBytesPerSecond { get; set; }

    public double? ReadUnbufferedDurationSeconds { get; set; }
    public double? ReadUnbufferedThroughputBytesPerSecond { get; set; }

    public int SmallFilesCount { get; set; }
    public double? SmallCreateDurationSeconds { get; set; }
    public double? SmallCreateOpsPerSecond { get; set; }
    public double? SmallDeleteDurationSeconds { get; set; }
    public double? SmallDeleteOpsPerSecond { get; set; }

    public double? SmallWriteLatencyMs { get; set; }
    public double? SmallReadLatencyMs { get; set; }
    public double? DirectoryListDurationSeconds { get; set; }
    public int IoErrorCount { get; set; }
    public double? ConnectionOpenLatencyMs { get; set; }
}
