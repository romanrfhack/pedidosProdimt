namespace Prodimt.Pedidos.Application.AdminImports;

public static class AdminImportTypes
{
    public const string Customers = "customers";
    public const string Products = "products";
    public const string CustomerFrequentProducts = "customer-frequent-products";
    public const string Machines = "machines";
    public const string CustomerMachineAssignments = "customer-machine-assignments";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Customers,
        Products,
        CustomerFrequentProducts,
        Machines,
        CustomerMachineAssignments
    };
}

public sealed record ImportCsvRequest(string Content, string? FileName);

public sealed record ImportTemplatesResponse(
    int MaxFileSizeBytes,
    string Mode,
    IReadOnlyList<ImportTemplateResponse> Templates);

public sealed record ImportTemplateResponse(
    string ImportType,
    string Description,
    IReadOnlyList<ImportColumnDto> Columns,
    string TemplatePath,
    string ExamplePath);

public sealed record ImportColumnDto(string Name, bool Required, string Description);

public sealed record ImportValidationResponse(
    string ImportType,
    int TotalRows,
    int ValidRows,
    int ErrorCount,
    int WarningCount,
    int ProposedCreateCount,
    int ProposedUpdateCount,
    int ProposedDeactivateCount,
    IReadOnlyList<ImportIssueDto> Errors,
    IReadOnlyList<ImportIssueDto> Warnings,
    IReadOnlyList<ImportProposedChangeDto> ProposedChanges);

public sealed record ImportIssueDto(
    int RowNumber,
    string? Field,
    string Code,
    string Message,
    string? RawValue);

public sealed record ImportProposedChangeDto(
    int RowNumber,
    string Action,
    string EntityType,
    string? EntityId,
    string EntityDisplayName,
    string Summary);

public sealed record ImportApplyResponse(
    string ImportType,
    int TotalRows,
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    int WarningCount,
    IReadOnlyList<Guid> AuditLogIds,
    IReadOnlyList<ImportIssueDto> Errors);
