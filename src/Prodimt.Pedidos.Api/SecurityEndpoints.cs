using System.Security.Claims;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.Security;

namespace Prodimt.Pedidos.Api;

public static class SecurityEndpoints
{
    public static void MapSecurityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/security").RequireAuthorization("AdminAccess").WithTags("Seguridad");
        group.MapGet("", (SecurityService service, CancellationToken ct) => Execute(() => service.GetAsync(ct)));
        group.MapGet("/me", async (ClaimsPrincipal principal, SecurityService service, CancellationToken ct) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ProdimtAuthClaims.UserId), out var userId)) return Results.Unauthorized();
            return Results.Ok(new { userId, permissions = await service.GetPermissionsAsync(userId, ct) });
        });
        group.MapPost("/users", (SaveSecurityUserRequest request, ClaimsPrincipal principal, SecurityService service, CancellationToken ct)
            => Execute(() => service.SaveUserAsync(null, request, Actor(principal), ct)));
        group.MapPut("/users/{id:guid}", (Guid id, SaveSecurityUserRequest request, ClaimsPrincipal principal, SecurityService service, CancellationToken ct)
            => Execute(() => service.SaveUserAsync(id, request, Actor(principal), ct)));
        group.MapDelete("/users/{id:guid}", (Guid id, Guid expectedRevision, ClaimsPrincipal principal, SecurityService service, CancellationToken ct)
            => Execute(() => service.DeleteUserAsync(id, expectedRevision, Actor(principal), ct)));
        group.MapPost("/roles", (SaveSecurityRoleRequest request, ClaimsPrincipal principal, SecurityService service, CancellationToken ct)
            => Execute(() => service.SaveRoleAsync(null, request, Actor(principal), ct)));
        group.MapPut("/roles/{id:guid}", (Guid id, SaveSecurityRoleRequest request, ClaimsPrincipal principal, SecurityService service, CancellationToken ct)
            => Execute(() => service.SaveRoleAsync(id, request, Actor(principal), ct)));
        group.MapDelete("/roles/{id:guid}", (Guid id, Guid expectedRevision, ClaimsPrincipal principal, SecurityService service, CancellationToken ct)
            => Execute(() => service.DeleteRoleAsync(id, expectedRevision, Actor(principal), ct)));
        group.MapPost("/permissions", (SaveSecurityPermissionRequest request, ClaimsPrincipal principal, SecurityService service, CancellationToken ct)
            => Execute(() => service.SavePermissionAsync(null, request, Actor(principal), ct)));
        group.MapPut("/permissions/{id:guid}", (Guid id, SaveSecurityPermissionRequest request, ClaimsPrincipal principal, SecurityService service, CancellationToken ct)
            => Execute(() => service.SavePermissionAsync(id, request, Actor(principal), ct)));
        group.MapDelete("/permissions/{id:guid}", (Guid id, Guid expectedRevision, ClaimsPrincipal principal, SecurityService service, CancellationToken ct)
            => Execute(() => service.DeletePermissionAsync(id, expectedRevision, Actor(principal), ct)));
    }
    private static AdminActorContext Actor(ClaimsPrincipal user) => new(user.FindFirstValue(ProdimtAuthClaims.UserId),
        user.FindFirstValue(ProdimtAuthClaims.DisplayName) ?? user.Identity?.Name);
    private static async Task<IResult> Execute<T>(Func<Task<T>> operation)
    {
        try { return Results.Ok(await operation()); }
        catch (CatalogConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
    }
}
