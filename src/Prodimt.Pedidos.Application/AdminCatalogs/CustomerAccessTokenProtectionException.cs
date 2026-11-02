namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed class CustomerAccessTokenProtectionException()
    : Exception("No se pudo recuperar el acceso de forma segura. Revisa la configuración del servicio.");
