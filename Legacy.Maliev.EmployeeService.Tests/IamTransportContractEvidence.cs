using System.Collections.Concurrent;

namespace Legacy.Maliev.EmployeeService.Tests;

internal sealed class IamTransportContractEvidence
{
    private readonly ConcurrentQueue<Exception> failures = new();

    internal int FailureCount => failures.Count;

    internal async Task<HttpResponseMessage> ObserveAsync(
        Func<Task<HttpResponseMessage>> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            failures.Enqueue(exception);
            throw;
        }
    }

    internal void AssertHealthy() => Assert.Empty(failures);
}
