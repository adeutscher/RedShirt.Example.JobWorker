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

    private long _cancelledLifetimeTally;
    private long _failedLifetimeTally;
    private long _invalidDataLifetimeTally;

    private long _receivedLifetimeTally;

    private ulong _successfulLifetimeDurationTicksSum;
    private long _successfulLifetimeMaxTicks = long.MinValue;
    private long _successfulLifetimeMinTicks = long.MaxValue;
    private long _successfulLifetimeTally;

    private void RecordSuccessful(TimeSpan duration)
    {
        var ticks = Math.Max(0, duration.Ticks);

        lock (_timingsGate)
        {
            // Either the ulong duration sum (~1.84e19 ticks ≈ 58,000 years of
            // aggregated successful work) or the long success tally (~9.22e18) would need to overflow.
            // At 1,000 one-second jobs/s the sum saturates on the order of decades-to-millennia
            // of wall clock only if every sample is huge; the tally alone would need ~300 million
            // years at 1,000 increments/s.
            ulong newSum;
            long newTally;
            checked
            {
                newSum = _successfulLifetimeDurationTicksSum + (ulong) ticks;
                newTally = _successfulLifetimeTally + 1;
            }

            _successfulLifetimeDurationTicksSum = newSum;
            _successfulLifetimeTally = newTally;

            if (ticks < _successfulLifetimeMinTicks)
            {
                _successfulLifetimeMinTicks = ticks;
            }

            if (ticks > _successfulLifetimeMaxTicks)
            {
                _successfulLifetimeMaxTicks = ticks;
            }
        }
    }

    /// <summary>
    ///     Atomically increments <paramref name="tally" /> under <see langword="checked" /> arithmetic.
    ///     <see cref="Interlocked.Increment(ref long)" /> is not used because it wraps on overflow
    ///     instead of throwing <see cref="OverflowException" />.
    ///     Overflow would require long.MaxValue (~9.22e18) increments; at a sustained 1,000 jobs/s,
    ///     that would take on the order of 300 million years to reach.
    ///     Suffice it to say, this is not considered to be on the table.
    ///     This application is only supported for runtimes up to 30 million years.
    /// </summary>
    private static void TryIncrementTally(ref long tally)
    {
        do
        {
            var current = Volatile.Read(ref tally);
            long next;
            checked
            {
                next = current + 1;
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
            successful = _successfulLifetimeTally;
            if (successful == 0)
            {
                average = TimeSpan.Zero;
                min = TimeSpan.Zero;
                max = TimeSpan.Zero;
            }
            else
            {
                average = TimeSpan.FromTicks((long) (_successfulLifetimeDurationTicksSum / (ulong) successful));
                min = TimeSpan.FromTicks(_successfulLifetimeMinTicks);
                max = TimeSpan.FromTicks(_successfulLifetimeMaxTicks);
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
                    Received = Interlocked.Read(ref _receivedLifetimeTally),
                    Successful = successful,
                    Cancelled = Interlocked.Read(ref _cancelledLifetimeTally),
                    Failed = Interlocked.Read(ref _failedLifetimeTally),
                    InvalidData = Interlocked.Read(ref _invalidDataLifetimeTally)
                }
            }
        };
    }

    public void RecordReceived()
    {
        TryIncrementTally(ref _receivedLifetimeTally);
    }

    public void RecordResult(CoreJobResult result, TimeSpan duration = default)
    {
        switch (result)
        {
            case CoreJobResult.Success:
                RecordSuccessful(duration);
                break;
            case CoreJobResult.Failure:
                TryIncrementTally(ref _failedLifetimeTally);
                break;
            case CoreJobResult.Cancelled:
                TryIncrementTally(ref _cancelledLifetimeTally);
                break;
            case CoreJobResult.Empty:
            case CoreJobResult.Parsing:
            case CoreJobResult.InvalidData:
                TryIncrementTally(ref _invalidDataLifetimeTally);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, null);
        }
    }
}