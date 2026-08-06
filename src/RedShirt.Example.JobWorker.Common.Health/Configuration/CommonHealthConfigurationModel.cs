namespace RedShirt.Example.JobWorker.Common.Health.Configuration;

public sealed class CommonHealthConfigurationModel
{
    public const string SectionName = "Health";

    public bool Enabled { get; init; } = true;
}