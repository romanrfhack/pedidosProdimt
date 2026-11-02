using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Security;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class SecurityAdministrationSqlTests : IAsyncLifetime
{
    private const string I12 = "20261101190000_AddRecoverableCustomerAccessTokens";
    private const string I18 = "20261102235000_AddSecurityAdministration";
    private const string Password = "Synthetic-Security-Password-2026!";
    private readonly SqlServerFixture database = new();
    private static readonly CancellationToken Ct = CancellationToken.None;
    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task HttpMultiroleAndDirectGrants_ResolveFromSqlOnEveryRequest_AndRevokedSessionsStayRevoked()
    {
        using var factory = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(factory);
        var snapshot = await Snapshot(admin);
        var readOrders = snapshot.Permissions.Single(x => x.Key == "orders.read").Id;
        var readProducts = snapshot.Permissions.Single(x => x.Key == "products.read").Id;
        snapshot = await Save(admin, "/api/admin/security/roles", new SaveSecurityRoleRequest(snapshot.Revision,
            "Synthetic reader one", "First source", true, [readOrders]));
        var first = snapshot.Roles.Single(x => x.Name == "Synthetic reader one").Id;
        snapshot = await Save(admin, "/api/admin/security/roles", new SaveSecurityRoleRequest(snapshot.Revision,
            "Synthetic reader two", "Second source", true, [readOrders]));
        var second = snapshot.Roles.Single(x => x.Name == "Synthetic reader two").Id;
        snapshot = await Save(admin, "/api/admin/security/users", new SaveSecurityUserRequest(snapshot.Revision,
            "synthetic-reader", "Synthetic operator", Password, true, [first, second, first], [readProducts]));
        var user = snapshot.Users.Single(x => x.UserName == "synthetic-reader");
        Assert.Equal(2, user.RoleIds.Count);
        Assert.Equal(2, user.EffectivePermissions.Single(x => x.Key == "orders.read").RoleNames.Count);
        Assert.True(user.EffectivePermissions.Single(x => x.Key == "products.read").IsDirect);

        using var restricted = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await Login(restricted, "synthetic-reader");
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync("/api/admin/orders/today")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync("/api/admin/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.GetAsync("/api/admin/security")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.GetAsync("/api/admin/users")).StatusCode);

        snapshot = await Save(admin, $"/api/admin/security/users/{user.Id}", new SaveSecurityUserRequest(snapshot.Revision,
            user.UserName, user.DisplayName, null, true, [second], [readProducts]), update: true);
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync("/api/admin/orders/today")).StatusCode);
        snapshot = await Save(admin, $"/api/admin/security/users/{user.Id}", new SaveSecurityUserRequest(snapshot.Revision,
            user.UserName, user.DisplayName, null, true, [], [readProducts]), update: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.GetAsync("/api/admin/orders/today")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync("/api/admin/products")).StatusCode);
        snapshot = await Save(admin, $"/api/admin/security/users/{user.Id}", new SaveSecurityUserRequest(snapshot.Revision,
            user.UserName, user.DisplayName, null, true, [], []), update: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.GetAsync("/api/admin/products")).StatusCode);

        snapshot = await Save(admin, $"/api/admin/security/users/{user.Id}", new SaveSecurityUserRequest(snapshot.Revision,
            user.UserName, user.DisplayName, null, false, [], []), update: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await restricted.GetAsync("/api/admin/security/me")).StatusCode);
        snapshot = await Save(admin, $"/api/admin/security/users/{user.Id}", new SaveSecurityUserRequest(snapshot.Revision,
            user.UserName, user.DisplayName, null, true, [], []), update: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await restricted.GetAsync("/api/admin/security/me")).StatusCode);
        await Login(restricted, "synthetic-reader");
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync("/api/admin/security/me")).StatusCode);
        await using var verified = database.CreateDbContext();
        Assert.False(await verified.SecurityUserRoles.AnyAsync(x => x.UserId == user.Id));
        Assert.False(await verified.SecurityUserPermissions.AnyAsync(x => x.UserId == user.Id));
        Assert.NotEqual(Guid.Empty, (await verified.AdminUsers.SingleAsync(x => x.Id == user.Id)).SessionRevision);
        var audits = await verified.AuditLogs.Where(x => x.EntityId == user.Id.ToString()).ToArrayAsync();
        Assert.True(audits.Length >= 6);
        Assert.All(audits, x => Assert.DoesNotContain(Password, x.MetadataJson ?? ""));
    }

    [Fact]
    public async Task StaleHttpRevision_Returns409AndDoesNotInsertSecondRole()
    {
        using var factory = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(factory);
        var initial = await Snapshot(admin);
        var current = await Save(admin, "/api/admin/security/roles", new SaveSecurityRoleRequest(initial.Revision,
            "Synthetic first revision", null, true, []));
        Assert.NotEqual(initial.Revision, current.Revision);
        using var stale = await admin.PostAsJsonAsync("/api/admin/security/roles", new SaveSecurityRoleRequest(initial.Revision,
            "Synthetic stale revision", null, true, []));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await using var verified = database.CreateDbContext();
        Assert.False(await verified.SecurityRoles.AnyAsync(x => x.Name == "Synthetic stale revision"));
        Assert.Equal(current.Revision, (await verified.SecurityStates.SingleAsync()).Revision);
    }

    [Fact]
    public async Task TwoConcurrentWritesWithSameRevision_HaveOneWinnerAndOneConflict()
    {
        await using var initialDb = database.CreateDbContext();
        var revision = (await new EfSecurityStore(initialDb).ReadAsync(Ct)).Revision;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Attempt(string name)
        {
            await start.Task;
            await using var db = database.CreateDbContext();
            try
            {
                return await new EfSecurityStore(db).ExecuteAsync(revision, data =>
                {
                    data.Roles.Add(new SecurityRole { Id = Guid.NewGuid(), Name = name, Description = "Synthetic concurrent save" });
                    data.Audits.Add(Audit("ConcurrentRoleCreated"));
                    return true;
                }, Ct);
            }
            catch (CatalogConflictException) { return false; }
        }
        var one = Attempt("Synthetic concurrent one");
        var two = Attempt("Synthetic concurrent two");
        start.SetResult();
        var results = await Task.WhenAll(one, two);
        Assert.Single(results, x => x);
        Assert.Single(results, x => !x);
        await using var verified = database.CreateDbContext();
        Assert.Equal(1, await verified.SecurityRoles.CountAsync(x => x.Name.StartsWith("Synthetic concurrent ")));
        Assert.Equal(1, await verified.AuditLogs.CountAsync(x => x.EventType == "ConcurrentRoleCreated"));
    }

    [Fact]
    public async Task SqlResolver_FiltersInactiveRolesPermissionsAndUsers_AndDeduplicatesSharedSources()
    {
        var userId = Guid.NewGuid(); var activeRoleId = Guid.NewGuid(); var inactiveRoleId = Guid.NewGuid();
        var activeId = Guid.NewGuid(); var inactiveId = Guid.NewGuid(); var inactiveRoleGrantId = Guid.NewGuid();
        await using (var setup = database.CreateDbContext())
        {
            var store = new EfSecurityStore(setup);
            var revision = (await store.ReadAsync(Ct)).Revision;
            await store.ExecuteAsync(revision, data =>
            {
                data.Users.Add(new AdminUser { Id = userId, UserName = "synthetic-filter-user", DisplayName = "Synthetic filters",
                    PasswordHash = "synthetic-filter-hash", CreatedAt = SqlServerFixture.Now });
                data.Roles.Add(new SecurityRole { Id = activeRoleId, Name = "Synthetic active role" });
                data.Roles.Add(new SecurityRole { Id = inactiveRoleId, Name = "Synthetic inactive role", IsActive = false });
                data.Permissions.Add(new SecurityPermission { Id = activeId, Key = "synthetic.active", Name = "Active", Module = "Synthetic" });
                data.Permissions.Add(new SecurityPermission { Id = inactiveId, Key = "synthetic.inactive", Name = "Inactive", Module = "Synthetic", IsActive = false });
                data.Permissions.Add(new SecurityPermission { Id = inactiveRoleGrantId, Key = "synthetic.inactiverole", Name = "Inactive role", Module = "Synthetic" });
                data.UserRoles.Add(new SecurityUserRole { UserId = userId, RoleId = activeRoleId });
                data.UserRoles.Add(new SecurityUserRole { UserId = userId, RoleId = inactiveRoleId });
                data.RolePermissions.Add(new SecurityRolePermission { RoleId = activeRoleId, PermissionId = activeId });
                data.RolePermissions.Add(new SecurityRolePermission { RoleId = inactiveRoleId, PermissionId = inactiveRoleGrantId });
                data.UserPermissions.Add(new SecurityUserPermission { UserId = userId, PermissionId = activeId });
                data.UserPermissions.Add(new SecurityUserPermission { UserId = userId, PermissionId = inactiveId });
                return true;
            }, Ct);
        }
        await using var check = database.CreateDbContext();
        var resolver = new EfSecurityStore(check);
        Assert.Equal(["synthetic.active"], await resolver.GetPermissionsAsync(userId, Ct));
        var current = await resolver.ReadAsync(Ct);
        await resolver.ExecuteAsync(current.Revision, data => { data.Users.Single(x => x.Id == userId).IsActive = false; return true; }, Ct);
        Assert.Empty(await resolver.GetPermissionsAsync(userId, Ct));
        current = await resolver.ReadAsync(Ct);
        await resolver.ExecuteAsync(current.Revision, data =>
        {
            var user = data.Users.Single(x => x.Id == userId); user.IsActive = true; user.IsDeleted = true; return true;
        }, Ct);
        Assert.Empty(await resolver.GetPermissionsAsync(userId, Ct));
    }

    [Fact]
    public async Task AuditInsertFailure_RollsBackRoleGrantUserAndRevisionTogether()
    {
        await using var beforeDb = database.CreateDbContext();
        var before = await new EfSecurityStore(beforeDb).ReadAsync(Ct);
        await Execute("""
            CREATE TRIGGER reject_security_test_audit ON dbo.AuditLogs AFTER INSERT AS
            BEGIN
              IF EXISTS (SELECT 1 FROM inserted WHERE EventType=N'SecurityRollbackSynthetic')
                THROW 51077,'synthetic_security_audit_failure',1;
            END;
            """);
        var roleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        try
        {
            await using var attempt = database.CreateDbContext();
            await Assert.ThrowsAsync<DbUpdateException>(() => new EfSecurityStore(attempt).ExecuteAsync(before.Revision, data =>
            {
                data.Roles.Add(new SecurityRole { Id = roleId, Name = "Synthetic rolled back role" });
                data.Users.Add(new AdminUser { Id = userId, UserName = "synthetic-rollback-user", DisplayName = "Synthetic rollback",
                    PasswordHash = "synthetic-hash", CreatedAt = SqlServerFixture.Now });
                data.UserRoles.Add(new SecurityUserRole { UserId = userId, RoleId = roleId });
                data.UserPermissions.Add(new SecurityUserPermission { UserId = userId, PermissionId = before.Permissions[0].Id });
                data.Audits.Add(Audit("SecurityRollbackSynthetic"));
                return true;
            }, Ct));
        }
        finally { await Execute("DROP TRIGGER dbo.reject_security_test_audit;"); }
        await using var verified = database.CreateDbContext();
        Assert.Equal(before.Revision, (await verified.SecurityStates.SingleAsync()).Revision);
        Assert.False(await verified.SecurityRoles.AnyAsync(x => x.Id == roleId));
        Assert.False(await verified.AdminUsers.AnyAsync(x => x.Id == userId));
        Assert.False(await verified.SecurityUserRoles.AnyAsync(x => x.UserId == userId));
        Assert.False(await verified.SecurityUserPermissions.AnyAsync(x => x.UserId == userId));
        Assert.False(await verified.AuditLogs.AnyAsync(x => x.EventType == "SecurityRollbackSynthetic"));
    }

    [Fact]
    public async Task Upgrade13To14_BackfillsExistingAdministratorsWithoutChangingPasswordsCustomerTokensOrHistory()
    {
        var isolated = new SqlConnectionStringBuilder(database.ConnectionString)
        {
            InitialCatalog = $"ProdimtSecurityUpgradeTest_{Guid.NewGuid():N}", Pooling = false
        }.ConnectionString;
        PedidosDbContext Create() => new(new DbContextOptionsBuilder<PedidosDbContext>().UseSqlServer(isolated).Options);
        try
        {
            await using var old = Create();
            await old.GetService<IMigrator>().MigrateAsync(I12);
            var active = Guid.NewGuid(); var inactive = Guid.NewGuid(); var tokenId = Guid.NewGuid();
            var customer = Customer.Create("Synthetic retained customer", null, null, null, null, null, SqlServerFixture.Now);
            old.Customers.Add(customer);
            var token = CustomerAccessToken.Create(customer.Id, "synthetic-retained-token-hash", "Synthetic retained access",
                DateTimeOffset.UtcNow.AddDays(1), SqlServerFixture.Now, true);
            token.Id = tokenId; token.ProtectedToken = "synthetic-retained-ciphertext";
            old.CustomerAccessTokens.Add(token);
            var historical = Audit("SyntheticBeforeSecurity"); old.AuditLogs.Add(historical);
            await old.SaveChangesAsync();
            await old.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.AdminUsers (Id,UserName,PasswordHash,DisplayName,IsActive,CreatedAt) VALUES ({active},N'synthetic-existing-active',N'synthetic-active-hash',N'Existing active',1,{SqlServerFixture.Now}),({inactive},N'synthetic-existing-inactive',N'synthetic-inactive-hash',N'Existing inactive',0,{SqlServerFixture.Now});");
            Assert.Equal(13, (await old.Database.GetAppliedMigrationsAsync()).Count());
            await old.GetService<IMigrator>().MigrateAsync(I18);
            await old.GetService<IMigrator>().MigrateAsync(I18);
            old.ChangeTracker.Clear();
            Assert.Equal(14, (await old.Database.GetAppliedMigrationsAsync()).Count());
            Assert.Equal(15, await old.SecurityPermissions.CountAsync());
            Assert.Equal(15, await old.SecurityRolePermissions.CountAsync());
            Assert.Equal(2, await old.SecurityUserRoles.CountAsync(x => x.RoleId == SecurityDefaults.AdministratorRoleId));
            var users = await old.AdminUsers.AsNoTracking().OrderBy(x => x.UserName).ToArrayAsync();
            Assert.Equal("synthetic-active-hash", users.Single(x => x.Id == active).PasswordHash);
            Assert.True(users.Single(x => x.Id == active).IsActive);
            Assert.Equal("synthetic-inactive-hash", users.Single(x => x.Id == inactive).PasswordHash);
            Assert.False(users.Single(x => x.Id == inactive).IsActive);
            Assert.All(users, x => { Assert.False(x.IsDeleted); Assert.Equal(Guid.Empty, x.SessionRevision); });
            var retained = await old.CustomerAccessTokens.AsNoTracking().SingleAsync(x => x.Id == tokenId);
            Assert.True(retained.IsActive); Assert.Equal(token.TokenHash, retained.TokenHash);
            Assert.Equal(token.ProtectedToken, retained.ProtectedToken); Assert.Equal(token.MetadataVersion, retained.MetadataVersion);
            Assert.Equal(token.CustomerId, retained.CustomerId); Assert.Equal(token.ExpiresAt, retained.ExpiresAt);
            Assert.True(await old.AuditLogs.AnyAsync(x => x.Id == historical.Id));
            Assert.Single(await old.AuditLogs.ToArrayAsync());
            var permissions = await new EfSecurityStore(old).GetPermissionsAsync(active, Ct);
            Assert.Contains("security.manage", permissions);
            Assert.Empty(await new EfSecurityStore(old).GetPermissionsAsync(inactive, Ct));
        }
        finally { await using var cleanup = Create(); await cleanup.Database.EnsureDeletedAsync(); }
    }

    private static async Task<SecuritySnapshot> Snapshot(HttpClient client)
        => (await client.GetFromJsonAsync<SecuritySnapshot>("/api/admin/security"))!;

    private static async Task<SecuritySnapshot> Save<T>(HttpClient client, string path, T request, bool update = false)
    {
        using var response = update ? await client.PutAsJsonAsync(path, request) : await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SecuritySnapshot>())!;
    }

    private static async Task Login(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/admin/login", new { userName = name, password = Password });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<AdminLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private static AuditLog Audit(string action) => AuditLog.Create("SecuritySynthetic", Guid.NewGuid().ToString(), action,
        SqlServerFixture.Now, AuditActorType.System, "Synthetic SQL security test.");

    private async Task Execute(string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
