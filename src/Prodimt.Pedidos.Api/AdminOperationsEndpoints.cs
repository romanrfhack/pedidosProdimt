using Prodimt.Pedidos.Application.AdminOperations;

namespace Prodimt.Pedidos.Api;

public static class AdminOperationsEndpoints
{
    public static IEndpointRouteBuilder MapAdminOperationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/operations/plan", async (
            DateOnly? date, string? routeCode, AdminOperationsService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetPlanAsync(date, routeCode, cancellationToken)))
            .RequireAuthorization("AdminAccess")
            .WithName("GetOperationsPlan");
        return endpoints;
    }
}
