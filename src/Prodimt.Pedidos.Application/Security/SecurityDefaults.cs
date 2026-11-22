using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.Security;

public static class SecurityDefaults
{
    public static readonly Guid AdministratorRoleId = Guid.Parse("18180000-0000-0000-0000-000000000001");
    public static readonly Guid DriverVanRoleId = Guid.Parse("22220000-0000-0000-0000-000000000001");
    public static readonly Guid MachineOperatorRoleId = Guid.Parse("24240000-0000-0000-0000-000000000001");
    public static readonly Guid CounterCashierRoleId = Guid.Parse("25250000-0000-0000-0000-000000000001");
    public const string CounterCashierRoleName = "Administración - Mostrador";
    public const string MachineOperatorRoleName = "Responsable de Máquina";
    public const string MachineOperatorRoleDescription = "Captura producción y organiza el equipo de las máquinas asignadas.";
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
        ("security.manage", "Administrar usuarios, roles y permisos", "Seguridad"),
        ("operations.assign", "Asignar máquinas a pedidos", "Operación"),
        ("operations.produce", "Registrar kilos producidos", "Operación"),
        ("operations.allocate", "Asignar y revertir kilos de pedidos", "Operación"),
        ("delivery.read.own", "Consultar mi reparto", "Reparto"),
        ("delivery.operate.own", "Registrar mi conteo, carga y entregas", "Reparto"),
        ("delivery.manage", "Administrar rutas y reparto", "Reparto"),
        ("operations.machine.work", "Operar las máquinas asignadas", "Operación"),
        ("operations.machine.manage", "Administrar responsables y personal de máquinas", "Operación"),
        ("operations.collections.work", "Registrar y consultar la cobranza de mi reparto", "Cobranza"),
        ("operations.collections.receive", "Recibir la cobranza de las rutas en Mostrador", "Cobranza"),
        ("operations.collections.read", "Consultar adeudos y liquidaciones de cobranza", "Cobranza")
    ];
}
