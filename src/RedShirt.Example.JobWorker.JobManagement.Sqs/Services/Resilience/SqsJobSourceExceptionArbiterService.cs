using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Sqs.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Services.Resilience;

internal interface ISqsJobSourceExceptionArbiterService
{
    SqsJobSourceExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     Job-source SQS arbiter. Delegates unclassified failures to
///     <see cref="ISqsExceptionArbiterService" />.
/// </summary>
internal class SqsJobSourceExceptionArbiterService(ISqsExceptionArbiterService sqsExceptionArbiterService)
    : ISqsJobSourceExceptionArbiterService
{
    private static SqsJobSourceExceptionArbiterReport Fresh(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new SqsJobSourceExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static SqsJobSourceExceptionArbiterReport Handled(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new SqsJobSourceExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private SqsJobSourceExceptionArbiterReport MapFromSqsArbiter(Exception exception)
    {
        var report = sqsExceptionArbiterService.GetReport(exception);
        return report.AlreadyHandled
            ? Handled(report.IsExpected, report.CouldBeTransient, report.CouldBeExternallySolvable)
            : Fresh(report.IsExpected, report.CouldBeTransient, report.CouldBeExternallySolvable);
    }

    public SqsJobSourceExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException!;
        }

        return exception switch
        {
            // Propagate the inner wrapper's own externally-solvable classification.
            WorkerJobSourceException workerJobSource =>
                Handled(
                    true,
                    workerJobSource is {IsHandled: false, CouldBeTransient: true},
                    workerJobSource.CouldBeExternallySolvable),
            WorkerSqsException workerSqs =>
                Handled(
                    true,
                    workerSqs is {IsHandled: false, CouldBeTransient: true},
                    workerSqs.CouldBeExternallySolvable),
            _ => MapFromSqsArbiter(exception)
        };
    }
}