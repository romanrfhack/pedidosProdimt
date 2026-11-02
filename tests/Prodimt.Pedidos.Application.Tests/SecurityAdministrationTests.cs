using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class SecurityAdministrationTests
{
    private const string Root = "/api/admin/security";
    private const string Password = "Fictional-security-2026-only!";

    [Fact]
    public async Task SecurityAdministration_RejectsAnonymousCustomerAndUnprivilegedStaff()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Root)).StatusCode);
        using var customerLogin = await client.PostAsJsonAsync("/api/auth/customer-token",
            new { token = DevelopmentAuthSeedValues.DefaultCustomerToken });
        customerLogin.EnsureSuccessStatusCode();
        var customer = (await customerLogin.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>())!;
        Bearer(client, customer.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Root)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Root + "/me")).StatusCode);

        using var manager = await ManagerAsync(factory);
        var state = await SnapshotAsync(manager);
        state = await CreateUserAsync(manager, state, "empty-user", [], []);
        Bearer(client, await LoginAsync(client, "empty-user"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Root)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Root + "/me")).StatusCode);
        Assert.Empty(await PermissionsAsync(client));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/orders/today")).StatusCode);
    }

    [Theory]
    [InlineData("tokens.manage", "/api/admin/orders/next-delivery-date")]
    [InlineData("pricing.manage", "/api/admin/products")]
    [InlineData("operations.acknowledge", "/api/admin/operations/changes")]
    [InlineData("orders.capture", "/api/admin/customers/pending-orders")]
    public async Task DirectActionGrant_AllowsItsReferenceReadsWithoutGrantingUnrelatedWrites(string key, string reference)
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var manager = await ManagerAsync(factory);
        var state = await SnapshotAsync(manager);
        state = await CreateUserAsync(manager, state, "action-only-user", [], [Permission(state, key)]);
        using var staff = Client(factory);
        Bearer(staff, await LoginAsync(staff, "action-only-user"));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(reference)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/admin/products", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(Root)).StatusCode);
    }

    [Fact]
    public async Task MultiRoleAndDirectPermissions_UnionTheirSourcesAndRevocationUsesTheExistingJwt()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var manager = await ManagerAsync(factory);
        var state = await SnapshotAsync(manager);
        var read = Permission(state, "orders.read");
        var product = Permission(state, "products.read");
        var machine = Permission(state, "machines.read");
        state = await SaveAsync(manager, "roles", null,
            new SaveSecurityRoleRequest(state.Revision, "Captura A", "", true, [read]));
        var firstRole = state.Roles.Single(x => x.Name == "Captura A").Id;
        state = await SaveAsync(manager, "roles", null,
            new SaveSecurityRoleRequest(state.Revision, "Captura B", "", true, [read, product]));
        var secondRole = state.Roles.Single(x => x.Name == "Captura B").Id;
        state = await CreateUserAsync(manager, state, "union-user", [firstRole, secondRole], [read, machine]);
        var user = User(state, "union-user");
        var shared = user.EffectivePermissions.Single(x => x.Key == "orders.read");
        Assert.Equal(new[] { "Captura A", "Captura B" }, shared.RoleNames);
        Assert.True(shared.IsDirect);
        using var staff = Client(factory);
        Bearer(staff, await LoginAsync(staff, user.UserName));
        Assert.Equal(new[] { "machines.read", "orders.read", "products.read" }, await PermissionsAsync(staff));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/admin/machines")).StatusCode);

        state = await UpdateUserAsync(manager, state, user.Id, [secondRole], [read, machine]);
        Assert.Equal(new[] { "Captura B" }, User(state, user.UserName).EffectivePermissions.Single(x => x.Key == "orders.read").RoleNames);
        Assert.Equal(new[] { "machines.read", "orders.read", "products.read" }, await PermissionsAsync(staff));
        state = await UpdateUserAsync(manager, state, user.Id, [secondRole], [machine]);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
        Assert.False(User(state, user.UserName).EffectivePermissions.Single(x => x.Key == "orders.read").IsDirect);

        state = await UpdateUserAsync(manager, state, user.Id, [], [machine]);
        Assert.Equal(new[] { "machines.read" }, await PermissionsAsync(staff));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/admin/machines")).StatusCode);
        await UpdateUserAsync(manager, state, user.Id, [], []);
        Assert.Empty(await PermissionsAsync(staff));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/machines")).StatusCode);
    }

    [Fact]
    public async Task EditingOrDisablingARole_ChangesAllAssignedUsersOnTheirNextRequest()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var manager = await ManagerAsync(factory);
        var state = await SnapshotAsync(manager);
        var read = Permission(state, "orders.read");
        state = await SaveAsync(manager, "roles", null, new SaveSecurityRoleRequest(state.Revision, "Consulta", "", true, [read]));
        var role = state.Roles.Single(x => x.Name == "Consulta");
        state = await CreateUserAsync(manager, state, "role-reader", [role.Id], []);
        using var staff = Client(factory);
        Bearer(staff, await LoginAsync(staff, "role-reader"));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
        state = await SaveAsync(manager, "roles", role.Id, new SaveSecurityRoleRequest(state.Revision, role.Name, "", false, [read]));
        Assert.Empty(await PermissionsAsync(staff));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
        state = await SaveAsync(manager, "roles", role.Id, new SaveSecurityRoleRequest(state.Revision, role.Name, "", true, [read]));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
        await SaveAsync(manager, "roles", role.Id, new SaveSecurityRoleRequest(state.Revision, role.Name, "", true, []));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
    }

    [Fact]
    public async Task ADirectManagementPermission_EnablesWritesWithoutAnyRoleAndRevokesImmediately()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var manager = await ManagerAsync(factory);
        var state = await SnapshotAsync(manager);
        state = await CreateUserAsync(manager, state, "direct-manager", [], [Permission(state, "security.manage")]);
        var user = User(state, "direct-manager");
        Assert.Empty(user.RoleIds);
        using var delegated = Client(factory);
        Bearer(delegated, await LoginAsync(delegated, user.UserName));
        state = await SaveAsync(delegated, "roles", null,
            new SaveSecurityRoleRequest(state.Revision, "Creado mediante permiso directo", "", true, []));
        Assert.Contains(state.Roles, x => x.Name == "Creado mediante permiso directo");
        state = await UpdateUserAsync(manager, state, user.Id, [], []);
        using var denied = await delegated.PostAsJsonAsync(Root + "/roles",
            new SaveSecurityRoleRequest(state.Revision, "Sin autorización", "", true, []));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain((await SnapshotAsync(manager)).Roles, x => x.Name == "Sin autorización");
    }

    [Fact]
    public async Task LegacyCreation_DoesNotImplicitlyGrantTheAdministratorRole()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var manager = await ManagerAsync(factory);
        using var created = await manager.PostAsJsonAsync("/api/admin/users",
            new { userName = "legacy-user", displayName = "Legacy User", password = Password });
        created.EnsureSuccessStatusCode();
        var user = User(await SnapshotAsync(manager), "legacy-user");
        Assert.Empty(user.RoleIds);
        Assert.Empty(user.DirectPermissionIds);
        using var staff = Client(factory);
        Bearer(staff, await LoginAsync(staff, user.UserName));
        Assert.Empty(await PermissionsAsync(staff));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("POST", "/api/admin/users")]
    [InlineData("PATCH", "/api/admin/users/11111111-1111-1111-1111-111111111111/activate")]
    [InlineData("PATCH", "/api/admin/users/11111111-1111-1111-1111-111111111111/deactivate")]
    [InlineData("POST", "/api/admin/security/users")]
    [InlineData("PUT", "/api/admin/security/roles/11111111-1111-1111-1111-111111111111")]
    [InlineData("GET", "/API/ADMIN/USERS/")]
    public async Task OperationalDirectGrant_CannotBypassSecurityThroughCurrentOrLegacyEndpoints(string method, string path)
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var manager = await ManagerAsync(factory);
        var state = await SnapshotAsync(manager);
        state = await CreateUserAsync(manager, state, "limited-operator", [], [Permission(state, "orders.read")]);
        using var staff = Client(factory);
        Bearer(staff, await LoginAsync(staff, "limited-operator"));
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != "GET") request.Content = JsonContent.Create(new { });
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task SelfLockAndLastAdministratorGuards_RejectMutationsWithoutChangingTheGraph()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var manager = await ManagerAsync(factory);
        var state = await SnapshotAsync(manager);
        var self = User(state, DevelopmentAuthSeedValues.DefaultAdminUserName);
        var auditCount = factory.Services.GetRequiredService<InMemoryDataStore>().AuditLogs.Count;
        using var removeRoles = await manager.PutAsJsonAsync($"{Root}/users/{self.Id}",
            new SaveSecurityUserRequest(state.Revision, self.UserName, self.DisplayName, null, true, [], []));
        Assert.Equal(HttpStatusCode.Conflict, removeRoles.StatusCode);
        using var deactivate = await manager.PatchAsJsonAsync($"/api/admin/users/{self.Id}/deactivate", new { });
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        using var archive = await manager.DeleteAsync($"{Root}/users/{self.Id}?expectedRevision={state.Revision}");
        Assert.Equal(HttpStatusCode.Conflict, archive.StatusCode);
        var after = await SnapshotAsync(manager);
        Assert.Equal(state.Revision, after.Revision);
        Assert.True(User(after, self.UserName).IsActive);
        Assert.Equal(self.RoleIds, User(after, self.UserName).RoleIds);
        Assert.Equal(auditCount, factory.Services.GetRequiredService<InMemoryDataStore>().AuditLogs.Count);
    }

    [Fact]
    public async Task StaleRevisionAndUnknownAssignments_RejectInsteadOfOverwritingOrPartiallySaving()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var manager = await ManagerAsync(factory);
        var state = await SnapshotAsync(manager);
        var staleRevision = state.Revision;
        state = await SaveAsync(manager, "roles", null, new SaveSecurityRoleRequest(state.Revision, "Primero", "", true, []));
        Assert.NotEqual(staleRevision, state.Revision);
        using var stale = await manager.PostAsJsonAsync(Root + "/roles", new SaveSecurityRoleRequest(staleRevision, "Sobrescrito", "", true, []));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var invalid = await manager.PostAsJsonAsync(Root + "/users",
            new SaveSecurityUserRequest(state.Revision, "invalid-role", "Invalid role", Password, true, [Guid.NewGuid()], []));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var after = await SnapshotAsync(manager);
        Assert.Equal(state.Revision, after.Revision);
        Assert.DoesNotContain(after.Roles, x => x.Name == "Sobrescrito");
        Assert.DoesNotContain(after.Users, x => x.UserName == "invalid-role");
    }

    [Fact]
    public async Task SystemRecoveryRoleAndPermissionKeys_CannotBeRemovedOrRepurposed()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var manager = await ManagerAsync(factory);
        var state = await SnapshotAsync(manager);
        var role = state.Roles.Single(x => x.IsSystem);
        var permission = state.Permissions.Single(x => x.Key == "security.manage");
        using var roleDelete = await manager.DeleteAsync($"{Root}/roles/{role.Id}?expectedRevision={state.Revision}");
        Assert.Equal(HttpStatusCode.Conflict, roleDelete.StatusCode);
        using var roleChange = await manager.PutAsJsonAsync($"{Root}/roles/{role.Id}",
            new SaveSecurityRoleRequest(state.Revision, role.Name, role.Description, true, []));
        Assert.Equal(HttpStatusCode.Conflict, roleChange.StatusCode);
        using var keyChange = await manager.PutAsJsonAsync($"{Root}/permissions/{permission.Id}",
            new SaveSecurityPermissionRequest(state.Revision, "security.other", permission.Name, permission.Module, "", true));
        Assert.Equal(HttpStatusCode.Conflict, keyChange.StatusCode);
        using var disable = await manager.PutAsJsonAsync($"{Root}/permissions/{permission.Id}",
            new SaveSecurityPermissionRequest(state.Revision, permission.Key, permission.Name, permission.Module, "", false));
        Assert.Equal(HttpStatusCode.Conflict, disable.StatusCode);
        using var permissionDelete = await manager.DeleteAsync($"{Root}/permissions/{permission.Id}?expectedRevision={state.Revision}");
        Assert.Equal(HttpStatusCode.Conflict, permissionDelete.StatusCode);
        Assert.Equal(state.Revision, (await SnapshotAsync(manager)).Revision);
    }

    [Fact]
    public async Task DeactivateAndReactivate_DoNotResurrectTheOldJwt_AndPasswordResetRevokesIt()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var manager = await ManagerAsync(factory);
        var state = await SnapshotAsync(manager);
        state = await CreateUserAsync(manager, state, "lifecycle-user", [], [Permission(state, "orders.read")]);
        var user = User(state, "lifecycle-user");
        using var staff = Client(factory);
        var oldToken = await LoginAsync(staff, user.UserName);
        Bearer(staff, oldToken);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
        state = await UpdateUserAsync(manager, state, user.Id, [], user.DirectPermissionIds.ToArray(), false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
        state = await UpdateUserAsync(manager, state, user.Id, [], user.DirectPermissionIds.ToArray(), true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
        Bearer(staff, await LoginAsync(staff, user.UserName));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
        const string replacement = "Fictional-replacement-password-2026!";
        await SaveAsync(manager, "users", user.Id, new SaveSecurityUserRequest(state.Revision,
            user.UserName, user.DisplayName, replacement, true, [], user.DirectPermissionIds.ToArray()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
        using var oldPasswordLogin = await staff.PostAsJsonAsync("/api/auth/admin/login", new { userName = user.UserName, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);
        Bearer(staff, await LoginAsync(staff, user.UserName, replacement));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/admin/orders/today")).StatusCode);
    }

    [Fact]
    public async Task CustomCatalogCrud_ProtectsAssignedEntitiesAndArchivesUsersWithSecretFreeAudit()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var manager = await ManagerAsync(factory);
        var state = await SnapshotAsync(manager);
        state = await SaveAsync(manager, "permissions", null,
            new SaveSecurityPermissionRequest(state.Revision, "reports.read", "Consultar reporte", "Reportes", "Inicial", true));
        var permission = state.Permissions.Single(x => x.Key == "reports.read");
        state = await SaveAsync(manager, "permissions", permission.Id,
            new SaveSecurityPermissionRequest(state.Revision, permission.Key, "Reporte actualizado", "Reportes", "Revisado", true));
        state = await SaveAsync(manager, "roles", null,
            new SaveSecurityRoleRequest(state.Revision, "Reporte", "Inicial", true, [permission.Id]));
        var role = state.Roles.Single(x => x.Name == "Reporte");
        state = await CreateUserAsync(manager, state, "archive-user", [role.Id], [permission.Id]);
        var user = User(state, "archive-user");
        Assert.Equal("Reporte actualizado", user.EffectivePermissions.Single().Name);
        using var assignedRoleDelete = await manager.DeleteAsync($"{Root}/roles/{role.Id}?expectedRevision={state.Revision}");
        Assert.Equal(HttpStatusCode.Conflict, assignedRoleDelete.StatusCode);
        using var assignedPermissionDelete = await manager.DeleteAsync($"{Root}/permissions/{permission.Id}?expectedRevision={state.Revision}");
        Assert.Equal(HttpStatusCode.Conflict, assignedPermissionDelete.StatusCode);
        using var archive = await manager.DeleteAsync($"{Root}/users/{user.Id}?expectedRevision={state.Revision}");
        archive.EnsureSuccessStatusCode();
        state = (await archive.Content.ReadFromJsonAsync<SecuritySnapshot>())!;
        Assert.DoesNotContain(state.Users, x => x.Id == user.Id);
        using var archivedLogin = await manager.PostAsJsonAsync("/api/auth/admin/login", new { userName = user.UserName, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, archivedLogin.StatusCode);
        using var legacyRestore = await manager.PatchAsJsonAsync($"/api/admin/users/{user.Id}/activate", new { });
        Assert.Equal(HttpStatusCode.NotFound, legacyRestore.StatusCode);
        state = await DeleteAsync(manager, state, "roles", role.Id);
        state = await DeleteAsync(manager, state, "permissions", permission.Id);
        Assert.DoesNotContain(state.Roles, x => x.Id == role.Id);
        Assert.DoesNotContain(state.Permissions, x => x.Id == permission.Id);
        var auditText = System.Text.Json.JsonSerializer.Serialize(factory.Services.GetRequiredService<InMemoryDataStore>().AuditLogs);
        Assert.Contains("UserCreated", auditText, StringComparison.Ordinal);
        Assert.Contains("UserArchived", auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", auditText, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient Client(AuthApiAuthorizationTests.AuthApiFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpClient> ManagerAsync(AuthApiAuthorizationTests.AuthApiFactory factory)
    {
        var client = Client(factory);
        Bearer(client, await LoginAsync(client, DevelopmentAuthSeedValues.DefaultAdminUserName,
            DevelopmentAuthSeedValues.DefaultAdminPassword));
        return client;
    }

    private static async Task<string> LoginAsync(HttpClient client, string name, string password = Password)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/admin/login", new { userName = name, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdminLoginResponse>())!.AccessToken;
    }

    private static void Bearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<SecuritySnapshot> SnapshotAsync(HttpClient manager) =>
        (await manager.GetFromJsonAsync<SecuritySnapshot>(Root))!;

    private static Guid Permission(SecuritySnapshot state, string key) => state.Permissions.Single(x => x.Key == key).Id;
    private static SecurityUserDto User(SecuritySnapshot state, string name) => state.Users.Single(x => x.UserName == name);

    private static Task<SecuritySnapshot> CreateUserAsync(HttpClient manager, SecuritySnapshot state, string name,
        Guid[] roles, Guid[] grants) => SaveAsync(manager, "users", null,
            new SaveSecurityUserRequest(state.Revision, name, name, Password, true, roles, grants));

    private static Task<SecuritySnapshot> UpdateUserAsync(HttpClient manager, SecuritySnapshot state, Guid id,
        Guid[] roles, Guid[] grants, bool active = true)
    {
        var user = state.Users.Single(x => x.Id == id);
        return SaveAsync(manager, "users", id,
            new SaveSecurityUserRequest(state.Revision, user.UserName, user.DisplayName, null, active, roles, grants));
    }

    private static async Task<SecuritySnapshot> SaveAsync<T>(HttpClient manager, string kind, Guid? id, T request)
    {
        using var response = id.HasValue
            ? await manager.PutAsJsonAsync($"{Root}/{kind}/{id}", request)
            : await manager.PostAsJsonAsync($"{Root}/{kind}", request);
        Assert.True(response.IsSuccessStatusCode, $"{kind}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<SecuritySnapshot>())!;
    }

    private static async Task<SecuritySnapshot> DeleteAsync(HttpClient manager, SecuritySnapshot state, string kind, Guid id)
    {
        using var response = await manager.DeleteAsync($"{Root}/{kind}/{id}?expectedRevision={state.Revision}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SecuritySnapshot>())!;
    }

    private static async Task<string[]> PermissionsAsync(HttpClient staff)
    {
        using var response = await staff.GetAsync(Root + "/me");
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>());
        return body.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()!).Order(StringComparer.Ordinal).ToArray();
    }
}
