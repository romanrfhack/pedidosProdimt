namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed record AdminCustomerResponse(
    Guid Id,
    string Name,
    string PhoneNumber,
    bool IsActive,
    TimeOnly? PreferredDeliveryTime,
    TimeOnly? PreferredDeliveryWindowStart,
    TimeOnly? PreferredDeliveryWindowEnd,
    string? DeliveryNotes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertAdminCustomerRequest(
    string Name,
    string? PhoneNumber,
    TimeOnly? PreferredDeliveryTime,
    TimeOnly? PreferredDeliveryWindowStart,
    TimeOnly? PreferredDeliveryWindowEnd,
    string? DeliveryNotes);

public sealed record AdminProductResponse(Guid Id, string Name, string? Description, bool IsActive);

public sealed record UpsertAdminProductRequest(string Name, string? Description);

public sealed record AdminMachineResponse(Guid Id, int Number, string? Name, bool IsActive);

public sealed record UpsertAdminMachineRequest(int Number, string? Name);

public sealed record AdminCustomerFrequentProductResponse(
    Guid ProductId,
    string ProductName,
    decimal? DefaultQuantity,
    int SortOrder,
    bool IsActive);

public sealed record UpdateCustomerFrequentProductsRequest(
    IReadOnlyList<UpdateCustomerFrequentProductItemRequest> Items);

public sealed record UpdateCustomerFrequentProductItemRequest(
    Guid ProductId,
    decimal? DefaultQuantity,
    int SortOrder,
    bool IsActive);

public sealed record AdminCustomerMachineAssignmentResponse(
    Guid MachineId,
    int MachineNumber,
    string? MachineName,
    bool IsDefault,
    bool IsActive,
    string? Notes);

public sealed record UpdateCustomerMachineAssignmentsRequest(
    IReadOnlyList<UpdateCustomerMachineAssignmentItemRequest> Items);

public sealed record UpdateCustomerMachineAssignmentItemRequest(
    Guid MachineId,
    bool IsDefault,
    bool IsActive,
    string? Notes);

public sealed record AdminCustomerAccessTokenResponse(
    Guid TokenId,
    Guid CustomerId,
    string Description,
    DateTimeOffset? ExpiresAt,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

public sealed record CreateCustomerAccessTokenRequest(string? Description, DateTimeOffset? ExpiresAt);

public sealed record CreatedCustomerAccessTokenResponse(
    Guid TokenId,
    Guid CustomerId,
    string PlainToken,
    string Description,
    DateTimeOffset? ExpiresAt,
    bool IsActive);

public sealed record AdminUserCatalogResponse(
    Guid Id,
    string UserName,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record CreateAdminUserRequest(string UserName, string DisplayName, string Password);
