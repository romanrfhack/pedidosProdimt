namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed class CatalogConflictException(string message) : Exception(message);
