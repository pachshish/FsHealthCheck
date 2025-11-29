namespace HealthCheck.Core.Models;

public sealed class HealthConfigRoot
{
    public List<ShareHealthConfig> Shares { get; set; } = new();
    public StressSettings? Stress { get; set; }
}
