using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.Collections;
using Prodimt.Pedidos.Application.Delivery;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Domain.Services;
using Prodimt.Pedidos.Infrastructure.Authentication;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class CollectionsSqlTests(SqlServerFixture database)
{
    private const string Password = "Synthetic-collections-test-2026!";
    private const string MachineMigration = "20261117145405_AddMachineWorkspace";
    private static readonly DateOnly ServiceDate = new(2026, 10, 24);

    [Fact]
    public async Task UpgradeFromMachineWorkspacePreservesCustomCounterRoleUsersAndCatalog()
    {
        var isolated = new SqlServerFixture { InitialMigration = MachineMigration };
        try
        {
            await isolated.InitializeAsync();
            var catalog = await isolated.CreateCatalogAsync();
            var customRoleId = Guid.NewGuid();
            var user = AdminUser.Create("counter-" + Guid.NewGuid().ToString("N"),
                "Mostrador sintético existente", "unchanged-password-hash", SqlServerFixture.Now);
            Guid retainedPermission;
            await using (var seed = isolated.CreateDbContext())
            {
                Assert.Equal(21, (await seed.Database.GetAppliedMigrationsAsync()).Count());
                retainedPermission = await seed.SecurityPermissions.Where(x => x.Key == "orders.read").Select(x => x.Id).SingleAsync();
                seed.AddRange(user,
                    new SecurityRole { Id = customRoleId, Name = "Administración – Mostrador", Description = "Rol personalizado retenido", IsActive = true },
                    new SecurityUserRole { UserId = user.Id, RoleId = customRoleId },
                    new SecurityUserPermission { UserId = user.Id, PermissionId = retainedPermission },
                    new SecurityRolePermission { RoleId = customRoleId, PermissionId = retainedPermission });
                await seed.SaveChangesAsync();
                await seed.Database.ExecuteSqlRawAsync("CREATE USER [prodimt_pedidos_app] WITHOUT LOGIN;");
            }

            await using (var upgraded = isolated.CreateDbContext())
            {
                await upgraded.Database.MigrateAsync();
                var migrations = (await upgraded.Database.GetAppliedMigrationsAsync()).ToArray();
                Assert.Equal(22, migrations.Length);
                Assert.Equal(MachineMigration, migrations[^2]);
                var customer = await upgraded.Customers.SingleAsync(x => x.Id == catalog.CustomerId);
                Assert.Equal(catalog.CustomerCode, customer.ExternalCode);
                Assert.True(await upgraded.Products.AnyAsync(x => x.Id == catalog.ProductId));
                var retained = await upgraded.AdminUsers.SingleAsync(x => x.Id == user.Id);
                Assert.Equal("unchanged-password-hash", retained.PasswordHash);
                Assert.True(await upgraded.SecurityUserRoles.AnyAsync(x => x.UserId == user.Id && x.RoleId == customRoleId));
                Assert.True(await upgraded.SecurityUserPermissions.AnyAsync(x => x.UserId == user.Id && x.PermissionId == retainedPermission));
                var role = await upgraded.SecurityRoles.SingleAsync(x => x.Id == customRoleId);
                Assert.Equal("Administración – Mostrador", role.Name);
                Assert.Equal("Rol personalizado retenido", role.Description);
                Assert.False(role.IsSystem);
                var grants = await (from grant in upgraded.SecurityRolePermissions
                    join permission in upgraded.SecurityPermissions on grant.PermissionId equals permission.Id
                    where grant.RoleId == customRoleId select permission.Key).ToArrayAsync();
                Assert.Contains("orders.read", grants);
                Assert.Contains("operations.collections.receive", grants);
                Assert.Contains("operations.collections.read", grants);
                Assert.DoesNotContain("operations.collections.work", grants);
                Assert.Single(await upgraded.SecurityUserRoles.Where(x => x.UserId == user.Id).ToArrayAsync());
                Assert.Empty(await upgraded.Set<CollectionPayment>().ToArrayAsync());
                Assert.Empty(await upgraded.Set<CollectionCharge>().ToArrayAsync());
                await upgraded.Database.ExecuteSqlRawAsync("""
                    EXECUTE AS USER=N'prodimt_pedidos_app';
                    BEGIN TRY
                        IF EXISTS (SELECT 1 FROM (VALUES
                            (N'dbo.CollectionCharges'),(N'dbo.CollectionChargeValuations'),(N'dbo.CollectionPayments'),
                            (N'dbo.CollectionCashReceipts'),(N'dbo.CollectionCashReceiptLines')) AS tables([Name])
                            WHERE COALESCE(HAS_PERMS_BY_NAME([Name],'OBJECT','SELECT'),0)<>1
                               OR COALESCE(HAS_PERMS_BY_NAME([Name],'OBJECT','INSERT'),0)<>1
                               OR COALESCE(HAS_PERMS_BY_NAME([Name],'OBJECT','UPDATE'),0)<>0
                               OR COALESCE(HAS_PERMS_BY_NAME([Name],'OBJECT','DELETE'),0)<>0)
                            THROW 51024,N'Los permisos de cobranza del usuario de aplicación no son de sólo inserción.',1;
                    END TRY
                    BEGIN CATCH
                        REVERT;
                        THROW;
                    END CATCH;
                    REVERT;
                    """);
                await upgraded.Database.MigrateAsync();
                Assert.Equal(22, (await upgraded.Database.GetAppliedMigrationsAsync()).Count());
            }
        }
        finally
        {
            await isolated.DisposeAsync();
        }
    }

    [Fact]
    public async Task AmbiguousCounterRoleNamesDoNotGrantCashPermissionsToExistingAccounts()
    {
        var isolated = new SqlServerFixture { InitialMigration = MachineMigration };
        try
        {
            await isolated.InitializeAsync();
            var firstRoleId = Guid.NewGuid();
            var secondRoleId = Guid.NewGuid();
            var user = AdminUser.Create("ambiguous-counter-" + Guid.NewGuid().ToString("N"),
                "Cuenta sintética con roles ambiguos", "unchanged-password-hash", SqlServerFixture.Now);
            await using (var seed = isolated.CreateDbContext())
            {
                var readPermission = await seed.SecurityPermissions.SingleAsync(x => x.Key == "orders.read");
                seed.AddRange(user,
                    new SecurityRole { Id = firstRoleId, Name = "Administración – Mostrador", IsActive = true },
                    new SecurityRole { Id = secondRoleId, Name = " Administración — Mostrador", IsActive = true },
                    new SecurityUserRole { UserId = user.Id, RoleId = firstRoleId },
                    new SecurityUserRole { UserId = user.Id, RoleId = secondRoleId },
                    new SecurityRolePermission { RoleId = firstRoleId, PermissionId = readPermission.Id },
                    new SecurityRolePermission { RoleId = secondRoleId, PermissionId = readPermission.Id });
                await seed.SaveChangesAsync();
            }

            await using var upgraded = isolated.CreateDbContext();
            await upgraded.Database.MigrateAsync();
            Assert.Equal(22, (await upgraded.Database.GetAppliedMigrationsAsync()).Count());
            var retainedRoles = await upgraded.SecurityRoles
                .Where(x => x.Id == firstRoleId || x.Id == secondRoleId).ToArrayAsync();
            Assert.Equal(2, retainedRoles.Length);
            Assert.All(retainedRoles, role => Assert.False(role.IsSystem));
            var retainedGrants = await (from grant in upgraded.SecurityRolePermissions
                join permission in upgraded.SecurityPermissions on grant.PermissionId equals permission.Id
                where grant.RoleId == firstRoleId || grant.RoleId == secondRoleId
                select permission.Key).ToArrayAsync();
            Assert.Equal(2, retainedGrants.Length);
            Assert.All(retainedGrants, key => Assert.Equal("orders.read", key));
            Assert.Equal(2, await upgraded.SecurityUserRoles.CountAsync(x => x.UserId == user.Id));
            Assert.Equal("unchanged-password-hash", (await upgraded.AdminUsers.SingleAsync(x => x.Id == user.Id)).PasswordHash);

            var cashier = await upgraded.SecurityRoles.SingleAsync(x => x.Id == SecurityDefaults.CounterCashierRoleId);
            Assert.Equal("Administración - Mostrador (caja)", cashier.Name);
            Assert.True(cashier.IsActive);
            var cashierGrants = await (from grant in upgraded.SecurityRolePermissions
                join permission in upgraded.SecurityPermissions on grant.PermissionId equals permission.Id
                where grant.RoleId == cashier.Id select permission.Key).ToArrayAsync();
            Assert.Contains("operations.collections.receive", cashierGrants);
            Assert.Contains("operations.collections.read", cashierGrants);
            Assert.DoesNotContain("operations.collections.work", cashierGrants);
            Assert.DoesNotContain("security.manage", cashierGrants);
            Assert.False(await upgraded.SecurityUserRoles.AnyAsync(x => x.RoleId == cashier.Id));
        }
        finally
        {
            await isolated.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReservedCashierRoleIdCollisionRejectsMigrationWithoutChangingExistingSecurity()
    {
        var isolated = new SqlServerFixture { InitialMigration = MachineMigration };
        try
        {
            await isolated.InitializeAsync();
            var user = AdminUser.Create("unrelated-role-" + Guid.NewGuid().ToString("N"),
                "Cuenta sintética ajena a caja", "unchanged-password-hash", SqlServerFixture.Now);
            Guid previousSecurityRevision;
            int previousPermissionCount;
            await using (var seed = isolated.CreateDbContext())
            {
                previousSecurityRevision = (await seed.SecurityStates.SingleAsync()).Revision;
                previousPermissionCount = await seed.SecurityPermissions.CountAsync();
                var permission = await seed.SecurityPermissions.SingleAsync(x => x.Key == "orders.read");
                seed.AddRange(user,
                    new SecurityRole { Id = SecurityDefaults.CounterCashierRoleId, Name = "Almacén sintético", IsActive = true },
                    new SecurityUserRole { UserId = user.Id, RoleId = SecurityDefaults.CounterCashierRoleId },
                    new SecurityRolePermission { RoleId = SecurityDefaults.CounterCashierRoleId, PermissionId = permission.Id });
                await seed.SaveChangesAsync();
            }

            await using (var attempted = isolated.CreateDbContext())
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => attempted.Database.MigrateAsync());
                Assert.Equal(51023, error.Number);
            }
            await using var unchanged = isolated.CreateDbContext();
            Assert.Equal(21, (await unchanged.Database.GetAppliedMigrationsAsync()).Count());
            Assert.Equal(previousPermissionCount, await unchanged.SecurityPermissions.CountAsync());
            Assert.Equal(previousSecurityRevision, (await unchanged.SecurityStates.SingleAsync()).Revision);
            var role = await unchanged.SecurityRoles.SingleAsync(x => x.Id == SecurityDefaults.CounterCashierRoleId);
            Assert.Equal("Almacén sintético", role.Name);
            Assert.False(role.IsSystem);
            Assert.True(await unchanged.SecurityUserRoles.AnyAsync(x => x.UserId == user.Id && x.RoleId == role.Id));
            var grants = await (from grant in unchanged.SecurityRolePermissions
                join permission in unchanged.SecurityPermissions on grant.PermissionId equals permission.Id
                where grant.RoleId == role.Id select permission.Key).ToArrayAsync();
            Assert.Equal("orders.read", Assert.Single(grants));
            var tables = await unchanged.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name LIKE N'Collection%'").SingleAsync();
            Assert.Equal(0, tables);
        }
        finally
        {
            await isolated.DisposeAsync();
        }
    }

    [Fact]
    public async Task DriverPaymentAndCashierReceiptAreAtomicIdempotentAndScoped()
    {
        var scene = await SeedAsync();
        await using var firstHost = database.CreateFactory();
        await using var secondHost = database.CreateFactory();
        using var driver = await LoginAsync(firstHost, scene.DriverUserName);
        using var retryDriver = await LoginAsync(secondHost, scene.DriverUserName);
        using var outsider = await LoginAsync(firstHost, scene.OtherDriverUserName);
        using var cashier = await LoginAsync(firstHost, scene.CashierUserName);
        var previewPath = $"/api/delivery/assignments/{scene.AssignmentId}/collection-preview?quantity=5";
        using (var forbiddenPreview = await outsider.GetAsync(previewPath))
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenPreview.StatusCode);
        using (var forbiddenCashier = await driver.GetAsync("/api/admin/collections/cashier?date=2026-10-24"))
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenCashier.StatusCode);
        using (var noSelfGrant = await cashier.GetAsync("/api/admin/security"))
            Assert.Equal(HttpStatusCode.Forbidden, noSelfGrant.StatusCode);
        using (var cannotCreatePrivilegedUser = await cashier.PostAsJsonAsync("/api/admin/security/users",
            new { userName = "forbidden", displayName = "Forbidden", roleIds = new[] { SecurityDefaults.AdministratorRoleId } }))
            Assert.Equal(HttpStatusCode.Forbidden, cannotCreatePrivilegedUser.StatusCode);

        var preview = (await driver.GetFromJsonAsync<CollectionPreviewResponse>(previewPath))!;
        Assert.Equal(100m, preview.TicketTotal);
        var deliveryPath = $"/api/delivery/assignments/{scene.AssignmentId}/deliver";
        var stale = new RecordDeliveryStageRequest(Guid.NewGuid(), 1, 1, 5,
            Reason: "Entrega parcial sintética", RemainderDisposition: "in-vehicle",
            CollectedAmount: 80, CollectionVersion: "stale-preview");
        using (var rolledBack = await driver.PostAsJsonAsync(deliveryPath, stale))
            Assert.Equal(HttpStatusCode.Conflict, rolledBack.StatusCode);
        await using (var checkRollback = database.CreateDbContext())
        {
            Assert.Equal(0, (await checkRollback.DeliveryAssignments.SingleAsync(x => x.Id == scene.AssignmentId)).DeliveredQuantity);
            Assert.Empty(await checkRollback.Set<CollectionPayment>().Where(x => x.CustomerId == scene.CustomerId).ToArrayAsync());
            Assert.Empty(await checkRollback.Set<CollectionCharge>().Where(x => x.CustomerId == scene.CustomerId).ToArrayAsync());
            Assert.False(await checkRollback.DeliveryEvents.AnyAsync(x => x.AssignmentId == scene.AssignmentId && x.Type == "Delivered"));
        }

        var request = stale with { OperationId = Guid.NewGuid(), CollectionVersion = preview.Version };
        using (var wrongDriver = await outsider.PostAsJsonAsync(deliveryPath, request))
            Assert.Equal(HttpStatusCode.Forbidden, wrongDriver.StatusCode);
        var saved = await PostAsync<DeliveryCommandResponse>(driver, deliveryPath, request);
        Assert.False(saved.Replayed);
        var replay = await PostAsync<DeliveryCommandResponse>(retryDriver, deliveryPath, request);
        Assert.True(replay.Replayed);

        var trip = (await driver.GetFromJsonAsync<CollectionTripResponse>($"/api/delivery/trips/{scene.TripId}/collections"))!;
        Assert.Equal(80m, trip.PaidAmount);
        Assert.Equal(80m, trip.PendingCashAmount);
        var accountPath = $"/api/admin/collections/customers/{scene.CustomerId}";
        var beforeReceipt = (await cashier.GetFromJsonAsync<CollectionCustomerAccountResponse>(accountPath))!;
        Assert.Equal(100m, beforeReceipt.ChargesTotal);
        Assert.Equal(80m, beforeReceipt.PaidTotal);
        Assert.Equal(20m, beforeReceipt.Balance);

        var dashboard = (await cashier.GetFromJsonAsync<CollectionCashDashboardResponse>(
            "/api/admin/collections/cashier?date=2026-10-24&routeCode=" + scene.RouteCode))!;
        var row = Assert.Single(dashboard.Trips);
        Assert.Equal(80m, row.PendingAmount);
        var receipt = new ReceiveCollectionCashRequest(Guid.NewGuid(), scene.TripId, row.Version, 80m, 75m,
            "Diferencia sintética de cinco pesos en caja.");
        using (var driverCannotReceive = await driver.PostAsJsonAsync("/api/admin/collections/receipts", receipt))
            Assert.Equal(HttpStatusCode.Forbidden, driverCannotReceive.StatusCode);
        await PostAsync<CollectionCommandResponse>(cashier, "/api/admin/collections/receipts", receipt);
        Assert.True((await PostAsync<CollectionCommandResponse>(cashier, "/api/admin/collections/receipts", receipt)).Replayed);

        var afterReceipt = (await cashier.GetFromJsonAsync<CollectionCustomerAccountResponse>(accountPath))!;
        Assert.Equal(20m, afterReceipt.Balance);
        var received = (await cashier.GetFromJsonAsync<CollectionCashDashboardResponse>(
            "/api/admin/collections/cashier?date=2026-10-24&routeCode=" + scene.RouteCode))!.Trips.Single();
        Assert.Equal(0m, received.PendingAmount);
        Assert.Equal(75m, received.ReceivedAmount);
        Assert.Equal(5m, received.Difference);
        await using var check = database.CreateDbContext();
        Assert.Equal(5m, (await check.DeliveryAssignments.SingleAsync(x => x.Id == scene.AssignmentId)).DeliveredQuantity);
        Assert.Single(await check.Set<CollectionPayment>().Where(x => x.CustomerId == scene.CustomerId).ToArrayAsync());
        Assert.Single(await check.Set<CollectionCharge>().Where(x => x.CustomerId == scene.CustomerId).ToArrayAsync());
        Assert.Single(await check.Set<CollectionCashReceipt>().Where(x => x.TripId == scene.TripId).ToArrayAsync());
        Assert.Single(await check.DeliveryEvents.Where(x => x.AssignmentId == scene.AssignmentId && x.Type == "Delivered").ToArrayAsync());
    }

    [Fact]
    public async Task ConcurrentCashReceiptsCannotAccountForTheSameCustomerCashTwice()
    {
        var scene = await SeedAsync();
        await using var firstHost = database.CreateFactory();
        await using var secondHost = database.CreateFactory();
        using var driver = await LoginAsync(firstHost, scene.DriverUserName);
        using var firstCashier = await LoginAsync(firstHost, scene.CashierUserName);
        using var secondCashier = await LoginAsync(secondHost, scene.CashierUserName);
        var preview = (await driver.GetFromJsonAsync<CollectionPreviewResponse>(
            $"/api/delivery/assignments/{scene.AssignmentId}/collection-preview?quantity=10"))!;
        await PostAsync<DeliveryCommandResponse>(driver, $"/api/delivery/assignments/{scene.AssignmentId}/deliver",
            new RecordDeliveryStageRequest(Guid.NewGuid(), 1, 1, 10,
                CollectedAmount: 80, CollectionVersion: preview.Version));
        var dashboard = (await firstCashier.GetFromJsonAsync<CollectionCashDashboardResponse>(
            "/api/admin/collections/cashier?date=2026-10-24&routeCode=" + scene.RouteCode))!;
        var version = Assert.Single(dashboard.Trips).Version;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpStatusCode> ReceiveAsync(HttpClient cashier)
        {
            await start.Task;
            using var response = await cashier.PostAsJsonAsync("/api/admin/collections/receipts",
                new ReceiveCollectionCashRequest(Guid.NewGuid(), scene.TripId, version, 80, 80, null));
            return response.StatusCode;
        }

        var first = ReceiveAsync(firstCashier);
        var second = ReceiveAsync(secondCashier);
        start.SetResult();
        var statuses = await Task.WhenAll(first, second);
        Assert.Single(statuses, status => status == HttpStatusCode.OK);
        Assert.Single(statuses, status => status == HttpStatusCode.Conflict);
        await using var check = database.CreateDbContext();
        Assert.Single(await check.Set<CollectionCashReceipt>().Where(x => x.TripId == scene.TripId).ToArrayAsync());
        var paymentIds = await check.Set<CollectionPayment>().Where(x => x.TripId == scene.TripId).Select(x => x.Id).ToArrayAsync();
        Assert.Equal(80m, await check.Set<CollectionCashReceiptLine>().Where(x => paymentIds.Contains(x.PaymentId)).SumAsync(x => x.Amount));
        var after = (await firstCashier.GetFromJsonAsync<CollectionCashDashboardResponse>(
            "/api/admin/collections/cashier?date=2026-10-24&routeCode=" + scene.RouteCode))!.Trips.Single();
        Assert.Equal(0m, after.PendingAmount);
        Assert.Equal(80m, after.ReceivedAmount);
    }

    private async Task<Scene> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var hasher = new PasswordHashService();
        var driver = AdminUser.Create("collection-driver-" + suffix, "Chofer sintético", hasher.HashPassword(Password), SqlServerFixture.Now);
        var otherDriver = AdminUser.Create("collection-outsider-" + suffix, "Otro chofer sintético", hasher.HashPassword(Password), SqlServerFixture.Now);
        var cashier = AdminUser.Create("collection-counter-" + suffix, "Mostrador sintético", hasher.HashPassword(Password), SqlServerFixture.Now);
        var routeCode = "COLLECTION-" + suffix;
        var customer = Customer.Create("Cliente cobranza " + suffix, null, null, null, null, null, SqlServerFixture.Now);
        customer.SetPilotDelivery(routeCode, 1, false, "cash_on_delivery", "Entrega sintética", SqlServerFixture.Now);
        var product = Product.Create("Tortilla de cobranza " + suffix, null);
        product.SetUnitCode("kg");
        product.RequiresMachine = false;
        var line = new OrderLine { Id = Guid.NewGuid(), ProductId = product.Id, ProductNameSnapshot = product.Name,
            Quantity = 10, UnitCode = "kg", RequiresMachine = false, UnitPriceSnapshot = 10m, CurrencyCodeSnapshot = "MXN" };
        var order = Order.CreateSubmitted(customer.Id, database.SalesChannelId, ServiceDate, SqlServerFixture.Now, 1,
            new OrderSubmissionEvaluation(false, false, null, OrderStatus.Submitted), [line], customer);
        var route = new DeliveryRoute { Code = routeCode, Name = "Ruta cobranza sintética", DefaultDriverUserId = driver.Id };
        var trip = new DeliveryTrip { Id = Guid.NewGuid(), Date = ServiceDate, RouteCode = routeCode, RouteName = route.Name,
            TripNumber = 1, DriverUserId = driver.Id, DriverName = driver.DisplayName, CreatedAt = SqlServerFixture.Now };
        var assignment = new DeliveryAssignment { Id = Guid.NewGuid(), TripId = trip.Id, OrderId = order.Id,
            OrderLineId = line.Id, ProductId = product.Id, ProductName = product.Name, UnitCode = "kg",
            CustomerId = customer.Id, CustomerName = customer.Name, SequenceNumber = order.SequenceNumber,
            RequestedQuantity = 10, AssignedQuantity = 10, CountedQuantity = 10, LoadedQuantity = 10,
            AcknowledgedOrderVersion = order.Version, PaymentTermCode = "cash_on_delivery" };
        await using var seed = database.CreateDbContext();
        seed.AddRange(driver, otherDriver, cashier, customer, product, order, route, trip, assignment);
        seed.SecurityUserRoles.AddRange(
            new SecurityUserRole { UserId = driver.Id, RoleId = SecurityDefaults.DriverVanRoleId },
            new SecurityUserRole { UserId = otherDriver.Id, RoleId = SecurityDefaults.DriverVanRoleId },
            new SecurityUserRole { UserId = cashier.Id, RoleId = SecurityDefaults.CounterCashierRoleId });
        await seed.SaveChangesAsync();
        return new(customer.Id, trip.Id, assignment.Id, routeCode, driver.UserName, otherDriver.UserName, cashier.UserName);
    }

    private static async Task<HttpClient> LoginAsync(SqlServerFixture.SqlApiFactory factory, string userName)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var login = await client.PostAsJsonAsync("/api/auth/admin/login", new { userName, password = Password });
        login.EnsureSuccessStatusCode();
        var session = (await login.Content.ReadFromJsonAsync<AdminLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record Scene(Guid CustomerId, Guid TripId, Guid AssignmentId, string RouteCode,
        string DriverUserName, string OtherDriverUserName, string CashierUserName);
}
