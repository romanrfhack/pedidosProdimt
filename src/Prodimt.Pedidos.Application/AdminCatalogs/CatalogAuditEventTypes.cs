namespace Prodimt.Pedidos.Application.AdminCatalogs;

internal static class CatalogAuditEventTypes
{
    public const string CustomerCreated = nameof(CustomerCreated);
    public const string CustomerUpdated = nameof(CustomerUpdated);
    public const string CustomerActivated = nameof(CustomerActivated);
    public const string CustomerDeactivated = nameof(CustomerDeactivated);
    public const string ProductCreated = nameof(ProductCreated);
    public const string ProductUpdated = nameof(ProductUpdated);
    public const string ProductActivated = nameof(ProductActivated);
    public const string ProductDeactivated = nameof(ProductDeactivated);
    public const string CustomerFrequentProductsUpdated = nameof(CustomerFrequentProductsUpdated);
    public const string MachineCreated = nameof(MachineCreated);
    public const string MachineUpdated = nameof(MachineUpdated);
    public const string MachineActivated = nameof(MachineActivated);
    public const string MachineDeactivated = nameof(MachineDeactivated);
    public const string CustomerMachineAssignmentsUpdated = nameof(CustomerMachineAssignmentsUpdated);
    public const string CustomerAccessTokenCreated = nameof(CustomerAccessTokenCreated);
    public const string CustomerAccessTokenRevoked = nameof(CustomerAccessTokenRevoked);
    public const string AdminUserCreated = nameof(AdminUserCreated);
    public const string AdminUserActivated = nameof(AdminUserActivated);
    public const string AdminUserDeactivated = nameof(AdminUserDeactivated);
}
