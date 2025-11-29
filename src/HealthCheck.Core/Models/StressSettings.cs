namespace HealthCheck.Core.Models;

public sealed class StressSettings
{
    public bool Enabled { get; set; } = false;
    public int DurationSeconds { get; set; } = 60;
    public int ParallelWorkers { get; set; } = 4;
    public int WriteFileSizeMb { get; set; } = 100;
    public int SmallFilesPerBatch { get; set; } = 200;
}
