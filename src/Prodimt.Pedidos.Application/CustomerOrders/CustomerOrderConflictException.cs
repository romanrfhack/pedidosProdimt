namespace Prodimt.Pedidos.Application.CustomerOrders;

public sealed class CustomerOrderConflictException(string message) : Exception(message);
