using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Security;

public static class SecurityDefaults
{
    public static readonly Guid AdministratorRoleId = Guid.Parse("18180000-0000-0000-0000-000000000001");
    public static readonly Guid InitialRevision = Guid.Parse("18180000-0000-0000-0000-000000000002");
    public static IReadOnlyList<SecurityPermission> Permissions => Definitions.Select((x, i) => new SecurityPermission
    {
        Id = Guid.Parse($"18180000-0000-0000-0001-{i + 1:000000000000}"), Key = x.Key,
        Name = x.Name, Module = x.Module, Description = x.Name, IsActive = true, IsSystem = true
    }).ToArray();
    private static readonly (string Key, string Name, string Module)[] Definitions =
    [
        ("orders.read", "Consultar pedidos", "Pedidos"),
        ("orders.capture", "Capturar y corregir pedidos", "Pedidos"),
        ("orders.review", "Revisar pedidos", "Pedidos"),
        ("customers.read", "Consultar clientes", "Clientes"),
        ("customers.manage", "Administrar clientes y perfiles", "Clientes"),
        ("products.read", "Consultar productos", "Productos"),
        ("products.manage", "Administrar productos", "Productos"),
        ("machines.read", "Consultar máquinas", "Máquinas"),
        ("machines.manage", "Administrar máquinas", "Máquinas"),
        ("pricing.manage", "Administrar precios", "Precios"),
        ("tokens.manage", "Administrar accesos de clientes", "Accesos"),
        ("imports.manage", "Importar catálogos y pedidos", "Importación"),
        ("operations.read", "Consultar operación y avisos", "Operación"),
        ("operations.acknowledge", "Reconocer avisos", "Operación"),
        ("security.manage", "Administrar usuarios, roles y permisos", "Seguridad")
    ];
}
