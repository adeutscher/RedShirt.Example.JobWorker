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
    private static SqsJobSourceExceptionArbiterReport Fresh(bool isCritical, bool couldBeTransient)
    {
        return new SqsJobSourceExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private static SqsJobSourceExceptionArbiterReport Handled(bool isCritical, bool couldBeTransient)
    {
        return new SqsJobSourceExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private SqsJobSourceExceptionArbiterReport MapFromSqsArbiter(Exception exception)
    {
        var report = sqsExceptionArbiterService.GetJudgement(exception);
        return report.AlreadyHandled
            ? Handled(report.IsCritical, report.CouldBeTransient)
            : Fresh(report.IsCritical, report.CouldBeTransient);
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
            WorkerJobSourceException workerJobSource =>
                Handled(workerJobSource.IsCritical,
                    workerJobSource is {IsHandled: false, CouldBeTransient: true}),
            WorkerSqsException workerSqs =>
                Handled(workerSqs.IsCritical, workerSqs is {IsHandled: false, IsTransient: true}),
            _ => MapFromSqsArbiter(exception)
        };
    }
}