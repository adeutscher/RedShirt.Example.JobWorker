using Amazon.Runtime;
using Amazon.SimpleSystemsManagement.Model;
using RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Models;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services.Resilience;

internal interface ISsmExceptionArbiterService
{
    SsmExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     SSM-oriented arbiter. Classifies Parameter Store / SSM-specific failures, then delegates
///     remaining <see cref="AmazonServiceException" /> instances to <see cref="IAwsExceptionArbiterService" />.
///     Unrecognized exception types are marked unexpected so callers surface them raw.
/// </summary>
internal sealed class SsmExceptionArbiterService(IAwsExceptionArbiterService awsExceptionArbiterService)
    : ISsmExceptionArbiterService
{
    private static SsmExceptionArbiterReport Fresh(bool isExpected, bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new SsmExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static SsmExceptionArbiterReport Handled(bool isExpected, bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new SsmExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private SsmExceptionArbiterReport MapFromAws(AmazonServiceException exception)
    {
        var report = awsExceptionArbiterService.GetReport(exception);
        return Fresh(report.IsExpected, report.CouldBeTransient, report.CouldBeExternallySolvable);
    }

    public SsmExceptionArbiterReport GetReport(Exception exception)
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
            // Propagate the inner wrapper's own externally-solvable classification.
            WorkerSecretManagerException workerSecretManager =>
                Handled(
                    true,
                    workerSecretManager is {IsHandled: false, CouldBeTransient: true},
                    workerSecretManager.CouldBeExternallySolvable),

            /*
             * Strictly speaking, mapping all of these Amazon.SimpleSystemsManagement.Model
             * exceptions isn't absolutely necessary — the general AmazonServiceException arbiter
             * *should* deduce many of them via status codes. Explicit arms still offer certainty
             * for Parameter Store / GetParameter(s) failure modes this worker uses.
             * Overall though, it's largely for a sense of security.
             */

            // --- SSM: transient ---
            InternalServerErrorException => Fresh(true, true, true),
            ThrottlingException
                or TooManyUpdatesException => Fresh(true, true, true),

            // --- SSM: permanent / caller must recover without blind retry ---
            // Missing parameter — ops can create it externally without a worker restart.
            ParameterNotFoundException
                or ParameterVersionNotFoundException
                or ResourceNotFoundException => Fresh(true, false, true),
            // Access / KMS — ops can grant IAM or fix the CMK without a worker restart.
            AccessDeniedException
                or InvalidKeyIdException => Fresh(true, false, true),
            // Hierarchy / naming conflicts that ops or config can address without a process restart.
            HierarchyLevelLimitExceededException
                or HierarchyTypeMismatchException
                or ParameterAlreadyExistsException
                or ParameterLimitExceededException
                or ParameterMaxVersionLimitExceededException
                or ParameterVersionLabelLimitExceededException => Fresh(true, false, true),
            // Client request shape / configured parameter type mismatches — not recoverable by
            // creating infra for the same request; fix the calling config or code.
            InvalidFilterException
                or InvalidFilterKeyException
                or InvalidFilterOptionException
                or InvalidFilterValueException
                or InvalidParametersException
                or InvalidNextTokenException
                or ParameterPatternMismatchException
                or UnsupportedParameterTypeException
                or IdempotentParameterMismatchException
                or ValidationException => Fresh(true, false, false),

            // Remaining AWS service exceptions (including other SSM types) — shared AWS heuristics.
            AmazonServiceException amazonService => MapFromAws(amazonService),

            // Unrecognized exception type — unexpected so callers surface the raw failure.
            _ => Fresh(false, false, false)
        };
    }
}