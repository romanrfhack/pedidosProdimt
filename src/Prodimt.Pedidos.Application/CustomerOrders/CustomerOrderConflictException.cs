namespace Prodimt.Pedidos.Application.CustomerOrders;

public sealed class CustomerOrderConflictException(string message, bool retrySameOperation = false) : Exception(message)
{
    // A receipt collision or concurrent write must retain its operation key.
    // The default preserves existing business-conflict callers and HTTP contracts.
    public bool RetrySameOperation { get; } = retrySameOperation;
}
