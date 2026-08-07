using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Health.Models;
using RedShirt.Example.JobWorker.Core.Enums;

namespace RedShirt.Example.JobWorker.Core.Services.Health;

/// <summary>
///     Collects and exposes Core worker lifetime statistics for health reporting.
/// </summary>
public interface ICoreStatisticsService
{
    /// <summary>
    ///     Snapshot of lifetime and recent-window statistics and current uptime.
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
    private readonly int _bucketCount;
    private readonly long _bucketDurationTicks;
    private readonly TimeSpan _recentWindow;
    private readonly Lock _recentGate = new();
    private readonly WindowBucket[] _recentBuckets;
    private readonly DateTimeOffset _startedAt;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _timingsGate = new();

    private long _cancelledLifetimeTally;
    private long _failedLifetimeTally;
    private long _invalidDataLifetimeTally;

    private long _receivedLifetimeTally;

    private ulong _successfulLifetimeDurationTicksSum;
    private long _successfulLifetimeMaxTicks = long.MinValue;
    private long _successfulLifetimeMinTicks = long.MaxValue;
    private long _successfulLifetimeTally;

    public CoreStatisticsService(IOptions<ConfigurationModel> options)
        : this(options, TimeProvider.System)
    {
    }

    internal CoreStatisticsService(IOptions<ConfigurationModel> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
        _startedAt = timeProvider.GetUtcNow();
        _recentWindow = options.Value.EffectiveRecentWindow;
        var bucketDuration = options.Value.EffectiveBucketDuration;
        _bucketDurationTicks = bucketDuration.Ticks;
        _bucketCount = options.Value.EffectiveBucketCount;
        _recentBuckets = new WindowBucket[_bucketCount];
        for (var i = 0; i < _bucketCount; i++)
        {
            _recentBuckets[i] = new WindowBucket();
        }
    }

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
                    newSum = _successfulLifetimeDurationTicksSum + (ulong) ticks;
                    newTally = _successfulLifetimeTally + 1;
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

        RecordRecentSuccess(ticks);
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

    private long CurrentBucketEpoch()
    {
        return _timeProvider.GetUtcNow().UtcTicks / _bucketDurationTicks;
    }

    private WindowBucket GetOrResetCurrentBucketNoLock()
    {
        var epoch = CurrentBucketEpoch();
        var index = (int) (epoch % _bucketCount);
        // Mod of negative epochs is avoided: UtcTicks are non-negative.
        var bucket = _recentBuckets[index];
        if (bucket.Epoch != epoch)
        {
            bucket.Reset(epoch);
        }

        return bucket;
    }

    private void RecordRecentReceived()
    {
        lock (_recentGate)
        {
            TryIncrementTally(ref GetOrResetCurrentBucketNoLock().Received);
        }
    }

    private void RecordRecentOutcome(CoreJobResult result)
    {
        lock (_recentGate)
        {
            var bucket = GetOrResetCurrentBucketNoLock();
            switch (result)
            {
                case CoreJobResult.Failure:
                    TryIncrementTally(ref bucket.Failed);
                    break;
                case CoreJobResult.Cancelled:
                    TryIncrementTally(ref bucket.Cancelled);
                    break;
                case CoreJobResult.Empty:
                case CoreJobResult.Parsing:
                case CoreJobResult.InvalidData:
                    TryIncrementTally(ref bucket.InvalidData);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result), result, null);
            }
        }
    }

    private void RecordRecentSuccess(long ticks)
    {
        lock (_recentGate)
        {
            var bucket = GetOrResetCurrentBucketNoLock();
            ulong newSum;
            long newTally;
            try
            {
                checked
                {
                    newSum = bucket.SuccessfulDurationTicksSum + (ulong) ticks;
                    newTally = bucket.Successful + 1;
                }
            }
            catch (OverflowException)
            {
                return;
            }

            bucket.SuccessfulDurationTicksSum = newSum;
            bucket.Successful = newTally;

            if (ticks < bucket.SuccessfulMinTicks)
            {
                bucket.SuccessfulMinTicks = ticks;
            }

            if (ticks > bucket.SuccessfulMaxTicks)
            {
                bucket.SuccessfulMaxTicks = ticks;
            }
        }
    }

    private JobStatisticsModel BuildRecentStatistics()
    {
        lock (_recentGate)
        {
            var nowEpoch = CurrentBucketEpoch();
            var minEpoch = nowEpoch - _bucketCount + 1;

            long received = 0;
            long successful = 0;
            long cancelled = 0;
            long failed = 0;
            long invalidData = 0;
            ulong durationSum = 0;
            var minTicks = long.MaxValue;
            var maxTicks = long.MinValue;

            foreach (var bucket in _recentBuckets)
            {
                if (bucket.Epoch < minEpoch || bucket.Epoch > nowEpoch)
                {
                    continue;
                }

                received += bucket.Received;
                successful += bucket.Successful;
                cancelled += bucket.Cancelled;
                failed += bucket.Failed;
                invalidData += bucket.InvalidData;

                if (bucket.Successful <= 0)
                {
                    continue;
                }

                durationSum += bucket.SuccessfulDurationTicksSum;
                if (bucket.SuccessfulMinTicks < minTicks)
                {
                    minTicks = bucket.SuccessfulMinTicks;
                }

                if (bucket.SuccessfulMaxTicks > maxTicks)
                {
                    maxTicks = bucket.SuccessfulMaxTicks;
                }
            }

            TimeSpan average;
            TimeSpan min;
            TimeSpan max;
            if (successful == 0)
            {
                average = TimeSpan.Zero;
                min = TimeSpan.Zero;
                max = TimeSpan.Zero;
            }
            else
            {
                average = TimeSpan.FromTicks((long) (durationSum / (ulong) successful));
                min = TimeSpan.FromTicks(minTicks);
                max = TimeSpan.FromTicks(maxTicks);
            }

            return new JobStatisticsModel
            {
                SuccessfulTimings = new SuccessfulTimingsModel
                {
                    Average = average,
                    Min = min,
                    Max = max
                },
                Totals = new LifetimeTotalsModel
                {
                    Received = received,
                    Successful = successful,
                    Cancelled = cancelled,
                    Failed = failed,
                    InvalidData = invalidData
                }
            };
        }
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
            // Manually suppressing warning: We trust time provider to be threadsafe.
            // ReSharper disable once InconsistentlySynchronizedField
            Uptime = _timeProvider.GetUtcNow() - _startedAt,
            RecentWindow = _recentWindow,
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
            },
            Recent = BuildRecentStatistics()
        };
    }

    public void RecordReceived()
    {
        TryIncrementTally(ref _receivedLifetimeTally);
        RecordRecentReceived();
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
                RecordRecentOutcome(result);
                break;
            case CoreJobResult.Cancelled:
                TryIncrementTally(ref _cancelledLifetimeTally);
                RecordRecentOutcome(result);
                break;
            case CoreJobResult.Empty:
            case CoreJobResult.Parsing:
            case CoreJobResult.InvalidData:
                TryIncrementTally(ref _invalidDataLifetimeTally);
                RecordRecentOutcome(result);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, null);
        }
    }

    public sealed class ConfigurationModel
    {
        private const int DefaultRecentWindowSeconds = 300;
        private const int DefaultBucketDurationSeconds = 5;

        /// <summary>
        ///     Sliding window used for <see cref="StatisticsModel.Recent" />.
        /// </summary>
        public int? RecentWindowSeconds { get; init; }

        /// <summary>
        ///     Size of each fixed bucket inside the recent window.
        /// </summary>
        public int? RecentBucketDurationSeconds { get; init; }

        public TimeSpan EffectiveRecentWindow =>
            TimeSpan.FromSeconds(Math.Max(1, RecentWindowSeconds ?? DefaultRecentWindowSeconds));

        public TimeSpan EffectiveBucketDuration =>
            TimeSpan.FromSeconds(Math.Max(1, RecentBucketDurationSeconds ?? DefaultBucketDurationSeconds));

        public int EffectiveBucketCount
        {
            get
            {
                var windowSeconds = Math.Max(1, RecentWindowSeconds ?? DefaultRecentWindowSeconds);
                var bucketSeconds = Math.Max(1, RecentBucketDurationSeconds ?? DefaultBucketDurationSeconds);
                return Math.Max(1, (int) Math.Ceiling(windowSeconds / (double) bucketSeconds));
            }
        }
    }

    private sealed class WindowBucket
    {
        public long Epoch = -1;
        public long Received;
        public long Successful;
        public long Cancelled;
        public long Failed;
        public long InvalidData;
        public ulong SuccessfulDurationTicksSum;
        public long SuccessfulMinTicks = long.MaxValue;
        public long SuccessfulMaxTicks = long.MinValue;

        public void Reset(long epoch)
        {
            Epoch = epoch;
            Received = 0;
            Successful = 0;
            Cancelled = 0;
            Failed = 0;
            InvalidData = 0;
            SuccessfulDurationTicksSum = 0;
            SuccessfulMinTicks = long.MaxValue;
            SuccessfulMaxTicks = long.MinValue;
        }
    }
}
