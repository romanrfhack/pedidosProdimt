using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Security;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Domain.Services;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class ProductionManagementSqlTests(SqlServerFixture database)
{
    private const string Base = "/api/admin/operations/production";
    private const string SyntheticPassword = "Synthetic-production-operator-2026!";
    private static readonly DateOnly ServiceDate = new(2026, 10, 24);
    private static int nextMachineNumber = 19000;

    [Fact]
    public async Task PhysicalOutput_ReplaysAcrossHosts_AndDoesNotInventClientAllocations()
    {
        var scene = await SeedAsync();
        var operation = new RegisterProductionRequest(Guid.NewGuid(), ServiceDate, scene.MachineId, scene.ProductId, 12.50m);
        Guid batchId;
        await using (var host = database.CreateFactory())
        using (var admin = await SqlServerFixture.CreateAdminClientAsync(host))
        {
            var saved = await Command(admin, "/batches", operation);
            batchId = saved.Id;
            Assert.False(saved.Replayed);
        }
        await using var nextHost = database.CreateFactory();
        using var nextAdmin = await SqlServerFixture.CreateAdminClientAsync(nextHost);
        var replay = await Command(nextAdmin, "/batches", operation);
        Assert.Equal(batchId, replay.Id);
        Assert.True(replay.Replayed);
        using var changedPayload = await nextAdmin.PostAsJsonAsync(Base + "/batches", operation with { Quantity = 13m });
        Assert.Equal(HttpStatusCode.Conflict, changedPayload.StatusCode);
        var dashboard = await Dashboard(nextAdmin);
        var batch = Assert.Single(dashboard.Batches, x => x.Id == batchId);
        Assert.Equal(12.50m, batch.Quantity);
        Assert.Equal(0m, batch.AllocatedQuantity);
        Assert.Equal(12.50m, batch.AvailableQuantity);
        var line = Assert.Single(dashboard.Orders.Single(x => x.OrderId == scene.OrderId).Lines);
        Assert.Equal(0m, line.AllocatedQuantity);
        Assert.Equal(20m, line.PendingAllocationQuantity);
        await using var check = database.CreateDbContext();
        Assert.Equal(1, await check.ProductionBatches.CountAsync(x => x.Id == batchId));
        Assert.False(await check.ProductionAllocations.AnyAsync(x => x.BatchId == batchId));
        Assert.Equal(1, await check.AuditLogs.CountAsync(x => x.EntityId == batchId.ToString() && x.EventType == "ProductionBatchRegistered"));
    }

    [Fact]
    public async Task AllocationReplayAndExplicitReversal_PreserveOneHistoryAndReleaseKilosExactlyOnce()
    {
        var scene = await SeedAsync();
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        var batchId = (await Command(admin, "/batches", Batch(scene, 10m))).Id;
        var request = new AllocateProductionRequest(Guid.NewGuid(), batchId, scene.OrderId, scene.LineId, 6m, 1);
        var assigned = await Command(admin, "/allocations", request);
        var replay = await Command(admin, "/allocations", request);
        Assert.Equal(assigned.Id, replay.Id);
        Assert.True(replay.Replayed);
        var beforeReverse = await Dashboard(admin);
        Assert.Equal(4m, beforeReverse.Batches.Single(x => x.Id == batchId).AvailableQuantity);
        Assert.Equal(6m, beforeReverse.Orders.Single(x => x.OrderId == scene.OrderId).Lines.Single().AllocatedQuantity);
        Assert.Equal(2, beforeReverse.Orders.Single(x => x.OrderId == scene.OrderId).OrderVersion);

        var reverse = new ReverseProductionAllocationRequest(Guid.NewGuid(), "Synthetic allocation sent to wrong client");
        await Command(admin, $"/allocations/{assigned.Id}/reverse", reverse);
        Assert.True((await Command(admin, $"/allocations/{assigned.Id}/reverse", reverse)).Replayed);
        using var duplicateReverse = await admin.PostAsJsonAsync(Base + $"/allocations/{assigned.Id}/reverse", reverse with { OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, duplicateReverse.StatusCode);
        var afterReverse = await Dashboard(admin);
        Assert.Equal(10m, afterReverse.Batches.Single(x => x.Id == batchId).AvailableQuantity);
        Assert.Equal(0m, afterReverse.Orders.Single(x => x.OrderId == scene.OrderId).Lines.Single().AllocatedQuantity);
        Assert.Equal(3, afterReverse.Orders.Single(x => x.OrderId == scene.OrderId).OrderVersion);
        var historical = Assert.Single(afterReverse.Allocations, x => x.Id == assigned.Id);
        Assert.NotNull(historical.ReversedAt);
        Assert.Equal(reverse.Reason, historical.ReverseReason);
        await using var check = database.CreateDbContext();
        Assert.Single(await check.ProductionAllocations.Where(x => x.BatchId == batchId).ToArrayAsync());
        Assert.Equal(1, await check.AuditLogs.CountAsync(x => x.EntityId == assigned.Id.ToString() && x.EventType == "ProductionAllocated"));
        Assert.Equal(1, await check.AuditLogs.CountAsync(x => x.EntityId == assigned.Id.ToString() && x.EventType == "ProductionAllocationReversed"));
    }

    [Fact]
    public async Task ConcurrentClientsCannotAllocateTheSamePhysicalKilosTwice()
    {
        var scene = await SeedAsync();
        var second = await AddOrderAsync(scene);
        await using var host = database.CreateFactory();
        using var firstAdmin = await SqlServerFixture.CreateAdminClientAsync(host);
        using var secondAdmin = await SqlServerFixture.CreateAdminClientAsync(host);
        var batchId = (await Command(firstAdmin, "/batches", Batch(scene, 10m))).Id;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Attempt(HttpClient client, Guid orderId, Guid lineId)
        {
            await start.Task;
            return await client.PostAsJsonAsync(Base + "/allocations",
                new AllocateProductionRequest(Guid.NewGuid(), batchId, orderId, lineId, 7m, 1));
        }
        var first = Attempt(firstAdmin, scene.OrderId, scene.LineId);
        var other = Attempt(secondAdmin, second.OrderId, second.LineId);
        start.SetResult();
        var responses = await Task.WhenAll(first, other);
        try
        {
            Assert.Single(responses, x => x.IsSuccessStatusCode);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        var dashboard = await Dashboard(firstAdmin);
        var batch = dashboard.Batches.Single(x => x.Id == batchId);
        Assert.Equal(7m, batch.AllocatedQuantity);
        Assert.Equal(3m, batch.AvailableQuantity);
        await using var check = database.CreateDbContext();
        Assert.Single(await check.ProductionAllocations.Where(x => x.BatchId == batchId && x.ReversedAt == null).ToArrayAsync());
    }

    [Fact]
    public async Task AllocationCannotExceedTheRemainingDemandEvenWhenTheBatchHasStock()
    {
        var scene = await SeedAsync(quantity: 5m);
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        var batchId = (await Command(admin, "/batches", Batch(scene, 20m))).Id;
        var first = await Command(admin, "/allocations", new AllocateProductionRequest(Guid.NewGuid(), batchId, scene.OrderId, scene.LineId, 4m, 1));
        using var excess = await admin.PostAsJsonAsync(Base + "/allocations",
            new AllocateProductionRequest(Guid.NewGuid(), batchId, scene.OrderId, scene.LineId, 2m, first.OrderVersion!.Value));
        Assert.Equal(HttpStatusCode.Conflict, excess.StatusCode);
        var dashboard = await Dashboard(admin);
        Assert.Equal(16m, dashboard.Batches.Single(x => x.Id == batchId).AvailableQuantity);
        Assert.Equal(1m, dashboard.Orders.Single(x => x.OrderId == scene.OrderId).Lines.Single().PendingAllocationQuantity);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("machine")]
    [InlineData("product")]
    [InlineData("unit")]
    public async Task AllocationRequiresMatchingDateMachineProductAndUnit(string mismatch)
    {
        var scene = await SeedAsync();
        var target = await AddOrderAsync(scene,
            productId: mismatch == "product" ? scene.AlternativeProductId : null,
            unit: mismatch == "unit" ? "package" : "kg",
            date: mismatch == "date" ? ServiceDate.AddDays(1) : ServiceDate,
            machineId: mismatch == "machine" ? scene.AlternativeMachineId : null);
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        var batchId = (await Command(admin, "/batches", Batch(scene, 10m))).Id;
        using var incompatible = await admin.PostAsJsonAsync(Base + "/allocations",
            new AllocateProductionRequest(Guid.NewGuid(), batchId, target.OrderId, target.LineId, 2m, 1));
        Assert.Equal(HttpStatusCode.BadRequest, incompatible.StatusCode);
        await using var check = database.CreateDbContext();
        Assert.False(await check.ProductionAllocations.AnyAsync(x => x.BatchId == batchId));
        Assert.Equal(1, (await check.Orders.SingleAsync(x => x.Id == target.OrderId)).Version);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task NoMachineProductsAreExcludedAndCannotReceiveMachineOrPhysicalAllocations(bool productRequires, bool lineRequires)
    {
        var scene = await SeedAsync(productRequiresMachine: productRequires, lineRequiresMachine: lineRequires);
        Guid historicalBatchId;
        await using (var seed = database.CreateDbContext())
        {
            // Represents output recorded before a later catalog decision; physical history remains intact.
            var batch = new ProductionBatch { Id = Guid.NewGuid(), ServiceDate = ServiceDate, MachineId = scene.MachineId,
                ProductId = scene.ProductId, ProductNameSnapshot = "Synthetic retained output", UnitCode = "kg", Quantity = 10m,
                CreatedAt = SqlServerFixture.Now, CreatedBy = "Synthetic retained operator" };
            historicalBatchId = batch.Id;
            seed.ProductionBatches.Add(batch);
            await seed.SaveChangesAsync();
        }
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        var dashboard = await Dashboard(admin);
        Assert.DoesNotContain(dashboard.Orders, x => x.OrderId == scene.OrderId);
        Assert.Contains(dashboard.Batches, x => x.Id == historicalBatchId && x.Quantity == 10m);
        using var machine = await admin.PostAsJsonAsync(MachinePath(scene),
            new AssignProductionMachineRequest(Guid.NewGuid(), scene.AlternativeMachineId, 1, "Synthetic prohibited machine"));
        Assert.Equal(HttpStatusCode.BadRequest, machine.StatusCode);
        using var allocation = await admin.PostAsJsonAsync(Base + "/allocations",
            new AllocateProductionRequest(Guid.NewGuid(), historicalBatchId, scene.OrderId, scene.LineId, 2m, 1));
        Assert.Equal(HttpStatusCode.BadRequest, allocation.StatusCode);
        if (!productRequires)
        {
            using var production = await admin.PostAsJsonAsync(Base + "/batches", Batch(scene, 2m));
            Assert.Equal(HttpStatusCode.BadRequest, production.StatusCode);
        }
        await using var check = database.CreateDbContext();
        Assert.Equal(1, (await check.Orders.SingleAsync(x => x.Id == scene.OrderId)).Version);
        Assert.False(await check.ProductionAllocations.AnyAsync(x => x.BatchId == historicalBatchId));
    }

    [Fact]
    public async Task MachineAssignmentIsVersionedAndCannotMoveAnAllocatedLine()
    {
        var scene = await SeedAsync(assigned: false);
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        var url = MachinePath(scene);
        using var stale = await admin.PostAsJsonAsync(url,
            new AssignProductionMachineRequest(Guid.NewGuid(), scene.MachineId, 0, "Synthetic stale machine choice"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var machineRequest = new AssignProductionMachineRequest(Guid.NewGuid(), scene.MachineId, 1, "Synthetic daily machine assignment");
        Assert.Equal(2, (await Command(admin, url, machineRequest)).OrderVersion);
        Assert.True((await Command(admin, url, machineRequest)).Replayed);
        await using (var noticesDb = database.CreateDbContext())
        {
            var notices = await noticesDb.OrderChangeNotices.Where(x => x.OrderId == scene.OrderId).ToArrayAsync();
            Assert.Equal(2, notices.Length);
            Assert.Contains(notices, x => x.MachineId is null);
            Assert.Contains(notices, x => x.MachineId == scene.MachineId);
        }
        var batchId = (await Command(admin, "/batches", Batch(scene, 10m))).Id;
        var allocation = await Command(admin, "/allocations", new AllocateProductionRequest(Guid.NewGuid(), batchId, scene.OrderId, scene.LineId, 6m, 2));
        using var move = await admin.PostAsJsonAsync(url,
            new AssignProductionMachineRequest(Guid.NewGuid(), scene.AlternativeMachineId, allocation.OrderVersion!.Value, "Synthetic prohibited reassignment"));
        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);
        await using var check = database.CreateDbContext();
        Assert.Equal(scene.MachineId, (await check.OrderLines.SingleAsync(x => x.Id == scene.LineId)).AssignedMachineId);
        Assert.Single(await check.ProductionAllocations.Where(x => x.OrderLineId == scene.LineId && x.ReversedAt == null).ToArrayAsync());
    }

    [Fact]
    public async Task AuditFailureRollsBackAllocationVersionAndReceipt_ThenSameOperationCanRetry()
    {
        var scene = await SeedAsync();
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        var batchId = (await Command(admin, "/batches", Batch(scene, 10m))).Id;
        var allocation = new AllocateProductionRequest(Guid.NewGuid(), batchId, scene.OrderId, scene.LineId, 3m, 1);
        var trigger = "reject_production_audit_" + Guid.NewGuid().ToString("N");
        await ExecuteSql($"CREATE TRIGGER [{trigger}] ON dbo.AuditLogs AFTER INSERT AS THROW 51019, 'Synthetic production audit failure', 1;");
        try
        {
            try
            {
                using var failed = await admin.PostAsJsonAsync(Base + "/allocations", allocation);
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            }
            catch (Exception error) when (HasSqlError(error, 51019)) { }
            await using var check = database.CreateDbContext();
            Assert.False(await check.ProductionAllocations.AnyAsync(x => x.BatchId == batchId));
            Assert.Equal(1, (await check.Orders.SingleAsync(x => x.Id == scene.OrderId)).Version);
            var operationKey = $"PRODUCTION:ALLOCATE:{allocation.OperationId:N}";
            Assert.False(await check.OrderOperations.AnyAsync(x => x.OperationKey == operationKey));
        }
        finally { await ExecuteSql($"DROP TRIGGER [{trigger}];"); }
        Assert.False((await Command(admin, "/allocations", allocation)).Replayed);
        Assert.True((await Command(admin, "/allocations", allocation)).Replayed);
        var dashboard = await Dashboard(admin);
        Assert.Equal(7m, dashboard.Batches.Single(x => x.Id == batchId).AvailableQuantity);
        await using var verified = database.CreateDbContext();
        var saved = Assert.Single(await verified.ProductionAllocations.Where(x => x.BatchId == batchId).ToArrayAsync());
        Assert.Equal(1, await verified.AuditLogs.CountAsync(x => x.EntityId == saved.Id.ToString() && x.EventType == "ProductionAllocated"));
    }

    [Fact]
    public async Task CompatibleCorrectionsKeepTheAllocatedLineIdentityAndItsProductionHistory()
    {
        var scene = await SeedAsync();
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        var batchId = (await Command(admin, "/batches", Batch(scene, 10m))).Id;
        var allocation = await Command(admin, "/allocations", new AllocateProductionRequest(Guid.NewGuid(), batchId, scene.OrderId, scene.LineId, 6m, 1));
        using (var increase = await admin.PostAsJsonAsync($"/api/admin/orders/{scene.OrderId}/corrections",
            new CorrectOrderRequest(Guid.NewGuid().ToString("N"), allocation.OrderVersion!.Value, "Synthetic increase preserves produced assignment",
                [new AdminSubmitCustomerOrderLineRequest(scene.ProductId, 25m, "Synthetic safe preparation note")])))
            increase.EnsureSuccessStatusCode();
        var increased = (await Dashboard(admin)).Orders.Single(x => x.OrderId == scene.OrderId);
        Assert.Equal(scene.LineId, Assert.Single(increased.Lines).OrderLineId);
        Assert.Equal(6m, increased.Lines.Single().AllocatedQuantity);
        Assert.Equal(25m, increased.Lines.Single().Quantity);
        using (var reduceToAllocated = await admin.PostAsJsonAsync($"/api/admin/orders/{scene.OrderId}/corrections",
            new CorrectOrderRequest(Guid.NewGuid().ToString("N"), increased.OrderVersion, "Synthetic reduce to assigned quantity",
                [new AdminSubmitCustomerOrderLineRequest(scene.ProductId, 6m, null)])))
            reduceToAllocated.EnsureSuccessStatusCode();
        var reduced = (await Dashboard(admin)).Orders.Single(x => x.OrderId == scene.OrderId);
        Assert.Equal(scene.LineId, Assert.Single(reduced.Lines).OrderLineId);
        Assert.Equal(0m, reduced.Lines.Single().PendingAllocationQuantity);
        Assert.Equal(6m, reduced.Lines.Single().AllocatedQuantity);
        await using var verified = database.CreateDbContext();
        var retained = Assert.Single(await verified.ProductionAllocations.Where(x => x.OrderId == scene.OrderId).ToArrayAsync());
        Assert.Equal(allocation.Id, retained.Id);
        Assert.Equal(scene.LineId, retained.OrderLineId);
        Assert.Null(retained.ReversedAt);
    }

    [Fact]
    public async Task CorrectionsAndCancellationCannotSilentlyDiscardAssignedProduction()
    {
        var scene = await SeedAsync();
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        var batchId = (await Command(admin, "/batches", Batch(scene, 10m))).Id;
        var allocated = await Command(admin, "/allocations", new AllocateProductionRequest(Guid.NewGuid(), batchId, scene.OrderId, scene.LineId, 6m, 1));
        using var correction = await admin.PostAsJsonAsync($"/api/admin/orders/{scene.OrderId}/corrections",
            new CorrectOrderRequest(Guid.NewGuid().ToString("N"), allocated.OrderVersion!.Value, "Synthetic reduce below assigned output",
                [new AdminSubmitCustomerOrderLineRequest(scene.ProductId, 5m, null)]));
        Assert.Equal(HttpStatusCode.Conflict, correction.StatusCode);
        await using (var cancellation = database.CreateDbContext())
        {
            var order = await cancellation.Orders.SingleAsync(x => x.Id == scene.OrderId);
            // No cancellation route exists yet; this verifies the shared persistence guard for a future caller.
            cancellation.Entry(order).Property(x => x.Status).CurrentValue = OrderStatus.Cancelled;
            await Assert.ThrowsAsync<CustomerOrderConflictException>(() => cancellation.SaveChangesAsync());
        }
        await using var verified = database.CreateDbContext();
        var persisted = await verified.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == scene.OrderId);
        Assert.Equal(OrderStatus.Submitted, persisted.Status);
        Assert.Equal(20m, Assert.Single(persisted.Lines).Quantity);
        Assert.Equal(scene.LineId, Assert.Single(persisted.Lines).Id);
        Assert.Single(await verified.ProductionAllocations.Where(x => x.OrderId == scene.OrderId && x.ReversedAt == null).ToArrayAsync());
    }

    [Fact]
    public async Task OperationPermissionsAreIndependent_AndWithdrawalAppliesToTheNextRequest()
    {
        var scene = await SeedAsync();
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        using var anonymous = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var unauthenticated = await anonymous.GetAsync(Base + "?date=2026-10-24");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        var security = (await admin.GetFromJsonAsync<SecuritySnapshot>("/api/admin/security"))!;
        var readerKeys = new[] { "operations.read", "operations.acknowledge" };
        var readPermissions = security.Permissions.Where(x => readerKeys.Contains(x.Key)).Select(x => x.Id).ToArray();
        var produceId = security.Permissions.Single(x => x.Key == "operations.produce").Id;
        var userName = "production-reader-" + Guid.NewGuid().ToString("N");
        using (var created = await admin.PostAsJsonAsync("/api/admin/security/users", new SaveSecurityUserRequest(security.Revision,
            userName, "Synthetic production reader", SyntheticPassword, true, [], readPermissions)))
        {
            created.EnsureSuccessStatusCode();
            security = (await created.Content.ReadFromJsonAsync<SecuritySnapshot>())!;
        }
        var user = security.Users.Single(x => x.UserName == userName);
        using var restricted = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using (var loginResponse = await restricted.PostAsJsonAsync("/api/auth/admin/login", new { userName, password = SyntheticPassword }))
        {
            loginResponse.EnsureSuccessStatusCode();
            var login = (await loginResponse.Content.ReadFromJsonAsync<AdminLoginResponse>())!;
            restricted.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        }
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync(Base + "?date=2026-10-24")).StatusCode);
        using var forbiddenProduce = await restricted.PostAsJsonAsync(Base + "/batches", Batch(scene, 2m));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenProduce.StatusCode);
        using var forbiddenMachine = await restricted.PostAsJsonAsync(MachinePath(scene),
            new AssignProductionMachineRequest(Guid.NewGuid(), scene.AlternativeMachineId, 1, "Synthetic unauthorized machine"));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenMachine.StatusCode);
        using var forbiddenAllocation = await restricted.PostAsJsonAsync(Base + "/allocations",
            new AllocateProductionRequest(Guid.NewGuid(), Guid.NewGuid(), scene.OrderId, scene.LineId, 1m, 1));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenAllocation.StatusCode);
        using (var grant = await admin.PutAsJsonAsync($"/api/admin/security/users/{user.Id}", new SaveSecurityUserRequest(security.Revision,
            user.UserName, user.DisplayName, null, true, [], [..readPermissions, produceId])))
        {
            grant.EnsureSuccessStatusCode();
            security = (await grant.Content.ReadFromJsonAsync<SecuritySnapshot>())!;
        }
        await Command(restricted, "/batches", Batch(scene, 2m));
        using (var revoke = await admin.PutAsJsonAsync($"/api/admin/security/users/{user.Id}", new SaveSecurityUserRequest(security.Revision,
            user.UserName, user.DisplayName, null, true, [], readPermissions))) revoke.EnsureSuccessStatusCode();
        using var forbiddenAgain = await restricted.PostAsJsonAsync(Base + "/batches", Batch(scene, 3m));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenAgain.StatusCode);
        await using var verified = database.CreateDbContext();
        Assert.Equal(1, await verified.ProductionBatches.CountAsync(x => x.MachineId == scene.MachineId && x.ProductId == scene.ProductId));
        await verified.Database.MigrateAsync();
        Assert.False(await verified.SecurityUserPermissions.AnyAsync(x => x.UserId == user.Id && x.PermissionId == produceId));
        Assert.True(await verified.SecurityRolePermissions.AnyAsync(x => x.RoleId == SecurityDefaults.AdministratorRoleId && x.PermissionId == produceId));
        using var afterMigrationReplay = await restricted.PostAsJsonAsync(Base + "/batches", Batch(scene, 3m));
        Assert.Equal(HttpStatusCode.Forbidden, afterMigrationReplay.StatusCode);
    }

    [Fact]
    public async Task RouteFilterDoesNotAttributeUnallocatedPhysicalStockToOneRoute()
    {
        var scene = await SeedAsync();
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        await Command(admin, "/batches", Batch(scene, 12m));
        var all = await Dashboard(admin);
        var filtered = await Dashboard(admin, route: "Synthetic-no-such-route-" + Guid.NewGuid().ToString("N"));
        Assert.Empty(filtered.Orders);
        Assert.Equal("all-routes", filtered.SummaryScope);
        Assert.Equal(all.Machines.Single(x => x.MachineId == scene.MachineId), filtered.Machines.Single(x => x.MachineId == scene.MachineId));
        Assert.Equal(12m, filtered.Machines.Single(x => x.MachineId == scene.MachineId).AvailableQuantity);
    }

    private async Task<Scene> SeedAsync(decimal quantity = 20m, bool assigned = true,
        bool productRequiresMachine = true, bool lineRequiresMachine = true)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var customer = Customer.Create("Synthetic production customer " + suffix, null, null, null, null, null, SqlServerFixture.Now);
        customer.SetPilotDelivery("PRODUCTION-" + suffix[..8], 1, null, null, null, SqlServerFixture.Now);
        var product = Product.Create("Synthetic production kg " + suffix, null);
        product.SetUnitCode("kg");
        product.RequiresMachine = productRequiresMachine;
        var alternate = Product.Create("Synthetic different production kg " + suffix, null);
        alternate.SetUnitCode("kg");
        var machine = Machine.Create(Interlocked.Increment(ref nextMachineNumber), "Synthetic production machine");
        var otherMachine = Machine.Create(Interlocked.Increment(ref nextMachineNumber), "Synthetic alternate production machine");
        var line = new OrderLine { Id = Guid.NewGuid(), ProductId = product.Id, ProductNameSnapshot = product.Name,
            UnitCode = "kg", Quantity = quantity, AssignedMachineId = assigned ? machine.Id : null, RequiresMachine = lineRequiresMachine };
        var order = Order.CreateSubmitted(customer.Id, database.SalesChannelId, ServiceDate, SqlServerFixture.Now, 1,
            new OrderSubmissionEvaluation(false, false, null, OrderStatus.Submitted), [line], customer);
        await using var seed = database.CreateDbContext();
        seed.Customers.Add(customer);
        seed.Products.AddRange(product, alternate);
        seed.Machines.AddRange(machine, otherMachine);
        seed.Orders.Add(order);
        await seed.SaveChangesAsync();
        return new(customer.Id, product.Id, alternate.Id, machine.Id, otherMachine.Id, order.Id, line.Id);
    }

    private async Task<(Guid OrderId, Guid LineId)> AddOrderAsync(Scene scene, Guid? productId = null,
        string unit = "kg", DateOnly? date = null, Guid? machineId = null)
    {
        await using var seed = database.CreateDbContext();
        var customer = await seed.Customers.SingleAsync(x => x.Id == scene.CustomerId);
        var line = new OrderLine { Id = Guid.NewGuid(), ProductId = productId ?? scene.ProductId, ProductNameSnapshot = "Synthetic target kg",
            UnitCode = unit, Quantity = 20m, AssignedMachineId = machineId ?? scene.MachineId, RequiresMachine = true };
        var order = Order.CreateSubmitted(customer.Id, database.SalesChannelId, date ?? ServiceDate, SqlServerFixture.Now, 2,
            new OrderSubmissionEvaluation(false, false, null, OrderStatus.Submitted), [line], customer);
        seed.Orders.Add(order);
        await seed.SaveChangesAsync();
        return (order.Id, line.Id);
    }

    private static RegisterProductionRequest Batch(Scene scene, decimal quantity)
        => new(Guid.NewGuid(), ServiceDate, scene.MachineId, scene.ProductId, quantity);

    private static string MachinePath(Scene scene)
        => $"/api/admin/operations/orders/{scene.OrderId}/lines/{scene.LineId}/machine";

    private static async Task<ProductionCommandResponse> Command(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path.StartsWith("/api/", StringComparison.Ordinal) ? path : Base + path, request);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<ProductionCommandResponse>())!;
    }

    private static async Task<ProductionDashboardResponse> Dashboard(HttpClient client, string? route = null)
        => (await client.GetFromJsonAsync<ProductionDashboardResponse>(Base + "?date=2026-10-24" + (route is null ? "" : "&routeCode=" + Uri.EscapeDataString(route))))!;

    private async Task ExecuteSql(string text)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = text;
        await command.ExecuteNonQueryAsync();
    }

    private static bool HasSqlError(Exception error, int number) => error is SqlException sql && sql.Number == number ||
        error.InnerException is { } inner && HasSqlError(inner, number);

    private sealed record Scene(Guid CustomerId, Guid ProductId, Guid AlternativeProductId,
        Guid MachineId, Guid AlternativeMachineId, Guid OrderId, Guid LineId);
}
