namespace RedShirt.Example.JobWorker.Configuration;

public sealed class HealthConfigurationModel
{
    public const string SectionName = "Health";

    public bool Enabled { get; init; } = true;

    public int Port { get; init; } = 8080;
}
