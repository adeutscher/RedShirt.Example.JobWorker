using RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Models;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services.Resilience;

internal interface ISsmExceptionArbiterService
{
    SsmExceptionArbiterReport GetJudgement(Exception exception);
}

/// <summary>
///     SSM-oriented arbiter that delegates common AWS classification to
///     <see cref="IAwsExceptionArbiterService" />.
/// </summary>
internal class SsmExceptionArbiterService(IAwsExceptionArbiterService awsExceptionArbiterService)
    : ISsmExceptionArbiterService
{
    private static SsmExceptionArbiterReport Fresh(bool isCritical, bool couldBeTransient)
    {
        return new SsmExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private static SsmExceptionArbiterReport Handled(bool isCritical, bool couldBeTransient)
    {
        return new SsmExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private SsmExceptionArbiterReport MapFromAws(Exception exception)
    {
        var report = awsExceptionArbiterService.GetJudgement(exception);
        return Fresh(report.IsCritical, report.CouldBeTransient);
    }

    public SsmExceptionArbiterReport GetJudgement(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException!;
        }

        return exception switch
        {
            // Already classified/wrapped by an earlier secret-manager layer — do not wrap again.
            // Only allow further retry when the prior wrapper has not already exhausted retries.
            WorkerSecretManagerException workerSecretManager =>
                Handled(workerSecretManager.IsCritical,
                    workerSecretManager is {IsHandled: false, IsTransient: true}),
            _ => MapFromAws(exception)
        };
    }
}