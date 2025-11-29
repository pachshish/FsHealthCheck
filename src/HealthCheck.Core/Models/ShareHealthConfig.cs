namespace HealthCheck.Core.Models;

public sealed class ShareHealthConfig
{
    public string ShareName { get; set; } = "";
    public string SharePath { get; set; } = "";
    public string HealthDirectory { get; set; } = "";
    public int TestFileSizeMb { get; set; } = 100;
    public int SmallFilesCount { get; set; } = 500;
}
