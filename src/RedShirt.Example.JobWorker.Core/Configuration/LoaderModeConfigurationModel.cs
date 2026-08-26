namespace RedShirt.Example.JobWorker.Core.Configuration;

internal sealed class LoaderModeConfigurationModel
{
    public string? Enabled { get; init; }

    /// <summary>
    ///     Parsing of <see cref="Enabled" />. Felt the need to be a bit more flexible with this parameter, so went with an
    ///     Effective_ property.
    /// </summary>
    public bool EffectiveEnabledSetting => !string.IsNullOrWhiteSpace(Enabled) &&
                                           (
                                               (int.TryParse(Enabled, out var intResult) &&
                                                intResult > 0) ||
                                               (bool.TryParse(Enabled, out var boolResult) &&
                                                boolResult));

    /// <summary>
    ///     Minimum number of free backlog slots required before loader mode will poll the job source.
    ///     When free capacity is positive but below this value, the loader waits instead of creeping along one message at a
    ///     time.
    /// </summary>
    public int MinimumBatchSize { get; init; }

    public int EffectiveMinimumBatchSize => Math.Max(1, MinimumBatchSize);
}
