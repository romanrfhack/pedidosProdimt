namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed record AdminCustomerResponse(
    Guid Id,
    string Name,
    string? ExternalCode,
    string PhoneNumber,
    bool IsActive,
    TimeOnly? PreferredDeliveryTime,
    TimeOnly? PreferredDeliveryWindowStart,
    TimeOnly? PreferredDeliveryWindowEnd,
    string? DeliveryNotes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? RouteCode = null,
    int? RouteStopOrder = null,
    bool? HasStoreKey = null,
    string? PaymentTermCode = null,
    string? InternalDeliveryInstructions = null,
    int OrderingProfileVersion = 0,
    bool IsOrderProfileApproved = false);

public sealed record UpsertAdminCustomerRequest(
    string Name,
    string? PhoneNumber,
    TimeOnly? PreferredDeliveryTime,
    TimeOnly? PreferredDeliveryWindowStart,
    TimeOnly? PreferredDeliveryWindowEnd,
    string? DeliveryNotes,
    string? RouteCode = null,
    int? RouteStopOrder = null,
    bool? HasStoreKey = null,
    string? PaymentTermCode = null,
    string? InternalDeliveryInstructions = null);

public sealed record AdminProductResponse(Guid Id, string Name, string? ExternalCode, string? Description, bool IsActive, string? UnitCode = null);

public sealed record UpsertAdminProductRequest(string Name, string? Description, string? UnitCode = null);

public sealed record AdminMachineResponse(Guid Id, int Number, string? ExternalCode, string? Name, bool IsActive);

public sealed record UpsertAdminMachineRequest(int Number, string? Name);

public sealed record AdminCustomerFrequentProductResponse(
    Guid ProductId,
    string ProductName,
    decimal? DefaultQuantity,
    int SortOrder,
    bool IsActive,
    Guid? PreferredMachineId = null,
    int? InterpretationPosition = null,
    string? UnitCode = null,
    bool ProductIsActive = true);

public sealed record UpdateCustomerFrequentProductsRequest(
    IReadOnlyList<UpdateCustomerFrequentProductItemRequest> Items,
    bool ApproveForInterpretation = false,
    int? ExpectedProfileVersion = null);

public sealed record AdminCustomerOrderingProfileResponse(
    Guid CustomerId,
    int OrderingProfileVersion,
    bool IsOrderProfileApproved,
    IReadOnlyList<AdminCustomerFrequentProductResponse> Items);

public sealed record UpdateCustomerFrequentProductItemRequest(
    Guid ProductId,
    decimal? DefaultQuantity,
    int SortOrder,
    bool IsActive,
    Guid? PreferredMachineId = null,
    int? InterpretationPosition = null,
    string? ExpectedUnitCode = null);

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
