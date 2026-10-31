namespace Prodimt.Pedidos.Application.Abstractions;

// One consistent read of version, approval and product meaning for the profile editor.
public sealed record CustomerOrderingProfileSnapshot(
    Guid CustomerId,
    int OrderingProfileVersion,
    bool IsOrderProfileApproved,
    IReadOnlyList<CustomerOrderingProfileItem> Items);

public sealed record CustomerOrderingProfileItem(
    Guid ProductId, string ProductName, decimal? DefaultQuantity, int SortOrder, bool IsActive,
    Guid? PreferredMachineId, int? InterpretationPosition, string? UnitCode, bool ProductIsActive,
    string? CaptureAlias = null);
