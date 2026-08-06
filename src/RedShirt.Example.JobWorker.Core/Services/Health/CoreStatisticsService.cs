using RedShirt.Example.JobWorker.Common.Health.Models;
using RedShirt.Example.JobWorker.Core.Enums;

namespace RedShirt.Example.JobWorker.Core.Services.Health;

/// <summary>
///     Collects and exposes Core worker lifetime statistics for health reporting.
/// </summary>
public interface ICoreStatisticsService
{
    /// <summary>
    ///     Snapshot of lifetime statistics and current uptime.
    /// </summary>
    StatisticsModel GetStatistics();

    /// <summary>
    ///     Record that a job (or message) was received for processing.
    /// </summary>
    void RecordReceived();

    /// <summary>
    ///     Record a job outcome. When <paramref name="result" /> is
    ///     <see cref="CoreJobResult.Success" />, <paramref name="duration" /> updates successful timings.
    /// </summary>
    void RecordResult(CoreJobResult result, TimeSpan duration = default);
}

/// <summary>
///     Thread-safe in-memory implementation of <see cref="ICoreStatisticsService" />.
/// </summary>
public sealed class CoreStatisticsService : ICoreStatisticsService
{
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private readonly Lock _timingsGate = new();
    private long _cancelledTally;
    private long _failedTally;
    private long _invalidDataTally;

    private long _receivedTally;

    private ulong _successfulDurationTicksSum;
    private long _successfulMaxTicks = long.MinValue;
    private long _successfulMinTicks = long.MaxValue;
    private long _successfulTally;

    private void RecordSuccessful(TimeSpan duration)
    {
        var ticks = Math.Max(0, duration.Ticks);

        lock (_timingsGate)
        {
            ulong newSum;
            long newTally;
            try
            {
                checked
                {
                    newSum = _successfulDurationTicksSum + (ulong) ticks;
                    newTally = _successfulTally + 1;
                }
            }
            catch (OverflowException)
            {
                // Precaution: either the ulong duration sum (~1.84e19 ticks ≈ 58,000 years of
                // aggregated successful work) or the long success tally (~9.22e18) cannot grow.
                // At 1,000 one-second jobs/s the sum saturates on the order of decades-to-millennia
                // of wall clock only if every sample is huge; the tally alone would need ~300 million
                // years at 1,000 increments/s. Skip this sample so sum and tally stay consistent.
                return;
            }

            _successfulDurationTicksSum = newSum;
            _successfulTally = newTally;

            if (ticks < _successfulMinTicks)
            {
                _successfulMinTicks = ticks;
            }

            if (ticks > _successfulMaxTicks)
            {
                _successfulMaxTicks = ticks;
            }
        }
    }

    /// <summary>
    ///     Atomically increments <paramref name="tally" /> under <see langword="checked" /> arithmetic.
    ///     <see cref="Interlocked.Increment(ref long)" /> is not used because it wraps on overflow
    ///     instead of throwing <see cref="OverflowException" />.
    /// </summary>
    private static void TryIncrementTally(ref long tally)
    {
        do
        {
            var current = Volatile.Read(ref tally);
            long next;
            try
            {
                checked
                {
                    next = current + 1;
                }
            }
            catch (OverflowException)
            {
                // Precaution: long.MaxValue (~9.22e18) increments.
                // At a sustained 1,000 jobs/s, this would take on the order of 300 million years to reach.
                return;
            }

            // CompareExchange publishes next only if no other thread changed tally since we read
            // current. A failed CAS means we lost a race; retry with a fresh read so checked +1
            // still applies to the latest value instead of blindly overwriting a concurrent update.
            if (Interlocked.CompareExchange(ref tally, next, current) == current)
            {
                // Successfully wrote, abort out of method (and therefore the do-while loop)
                return;
            }
            // Keep looping until CompareExchange returns true.
        } while (true);
    }

    public StatisticsModel GetStatistics()
    {
        long successful;
        TimeSpan average;
        TimeSpan min;
        TimeSpan max;

        lock (_timingsGate)
        {
            successful = _successfulTally;
            if (successful == 0)
            {
                average = TimeSpan.Zero;
                min = TimeSpan.Zero;
                max = TimeSpan.Zero;
            }
            else
            {
                average = TimeSpan.FromTicks((long) (_successfulDurationTicksSum / (ulong) successful));
                min = TimeSpan.FromTicks(_successfulMinTicks);
                max = TimeSpan.FromTicks(_successfulMaxTicks);
            }
        }

        return new StatisticsModel
        {
            Uptime = DateTime.UtcNow - _startedAtUtc,
            Lifetime = new JobStatisticsModel
            {
                SuccessfulTimings = new SuccessfulTimingsModel
                {
                    Average = average,
                    Min = min,
                    Max = max
                },
                Totals = new LifetimeTotalsModel
                {
                    Received = Interlocked.Read(ref _receivedTally),
                    Successful = successful,
                    Cancelled = Interlocked.Read(ref _cancelledTally),
                    Failed = Interlocked.Read(ref _failedTally),
                    InvalidData = Interlocked.Read(ref _invalidDataTally)
                }
            }
        };
    }

    public void RecordReceived()
    {
        TryIncrementTally(ref _receivedTally);
    }

    public void RecordResult(CoreJobResult result, TimeSpan duration = default)
    {
        switch (result)
        {
            case CoreJobResult.Success:
                RecordSuccessful(duration);
                break;
            case CoreJobResult.Failure:
                TryIncrementTally(ref _failedTally);
                break;
            case CoreJobResult.Cancelled:
                TryIncrementTally(ref _cancelledTally);
                break;
            case CoreJobResult.Empty:
            case CoreJobResult.Parsing:
            case CoreJobResult.InvalidData:
                TryIncrementTally(ref _invalidDataTally);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, null);
        }
    }
}