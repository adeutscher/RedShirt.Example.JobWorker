using RedShirt.Example.JobWorker.Connectors.Bar.Core.Models;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Core.Services;

/// <summary>
///     Opaque connector for the Bar dependency.
///     Bar is a stand-in for an OAuth-backed API client; see <c>docs/bar-connector.md</c> for last-mile instructions.
/// </summary>
public interface IBarConnector
{
    Task<CreateBarConnectorResponse> CreateAsync(CreateBarConnectorRequest request,
        CancellationToken cancellationToken = default);

    Task<GetBarConnectorResponse> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}