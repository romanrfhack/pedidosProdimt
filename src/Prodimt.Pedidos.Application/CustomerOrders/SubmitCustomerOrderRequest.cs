namespace Prodimt.Pedidos.Application.CustomerOrders;

public sealed record SubmitCustomerOrderRequest(IReadOnlyList<SubmitCustomerOrderLineRequest> Lines);

public sealed record SubmitCustomerOrderLineRequest(Guid ProductId, decimal Quantity, string? Notes);
