using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class AdminCatalogServiceTests
{
    private static readonly AdminActorContext AdminActor = new("test-admin", "Admin Test");

    [Fact]
    public async Task CustomerCatalog_CreatesCustomerAndUpdatesDeliveryWindow()
    {
        await using var fixture = await CatalogFixture.CreateAsync();

        var created = await fixture.Customers.CreateAsync(
            new UpsertAdminCustomerRequest(
                "Cliente Piloto",
                "5551234567",
                null,
                new TimeOnly(10, 0),
                new TimeOnly(11, 0),
                "Entregar en puerta"),
            AdminActor,
            CancellationToken.None);

        var updated = await fixture.Customers.UpdateAsync(
            created.Id,
            new UpsertAdminCustomerRequest(
                "Cliente Piloto",
                "5551234567",
                new TimeOnly(9, 30),
                null,
                null,
                "Entregar temprano"),
            AdminActor,
            CancellationToken.None);

        Assert.Equal("Cliente Piloto", created.Name);
        Assert.Equal(new TimeOnly(9, 30), updated.PreferredDeliveryTime);
        Assert.Null(updated.PreferredDeliveryWindowStart);
        Assert.Equal("Entregar temprano", updated.DeliveryNotes);
        Assert.True(await fixture.DbContext.AuditLogs.AnyAsync(x =>
            x.EntityId == created.Id.ToString() &&
            x.EventType == "CustomerUpdated"));
    }

    [Fact]
    public async Task CustomerCatalog_InactiveCustomerIsNotPendingAndCannotUseToken()
    {
        await using var fixture = await CatalogFixture.CreateAsync();

        var customer = await fixture.Customers.CreateAsync(
            new UpsertAdminCustomerRequest("Cliente Inactivo", null, null, null, null, null),
            AdminActor,
            CancellationToken.None);
        var token = await fixture.CustomerAccessTokens.CreateAsync(
            customer.Id,
            new CreateCustomerAccessTokenRequest("Token piloto", null),
            AdminActor,
            CancellationToken.None);

        var login = await fixture.Auth.LoginCustomerWithTokenAsync(
            new CustomerTokenLoginRequest(token.PlainToken),
            CancellationToken.None);
        Assert.Equal(customer.Id, login.CustomerId);

        await fixture.Customers.DeactivateAsync(customer.Id, AdminActor, CancellationToken.None);

        var pendingCustomers = await fixture.AdminOrders.GetPendingCustomersAsync(null, CancellationToken.None);
        Assert.DoesNotContain(pendingCustomers, x => x.CustomerId == customer.Id);
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => fixture.Auth.LoginCustomerWithTokenAsync(
            new CustomerTokenLoginRequest(token.PlainToken),
            CancellationToken.None));
    }

    [Fact]
    public async Task ProductCatalog_FrequentProductsDriveCustomerTemplateAndInactiveProductsAreHidden()
    {
        await using var fixture = await CatalogFixture.CreateAsync();

        var product = await fixture.Products.CreateAsync(
            new UpsertAdminProductRequest("Molde Piloto", "Producto para piloto"),
            AdminActor,
            CancellationToken.None);

        var frequentProducts = await fixture.Customers.ReplaceFrequentProductsAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            new UpdateCustomerFrequentProductsRequest(
            [
                new UpdateCustomerFrequentProductItemRequest(product.Id, 12, 1, true)
            ]),
            AdminActor,
            CancellationToken.None);

        Assert.Single(frequentProducts);
        Assert.Equal(product.Id, frequentProducts[0].ProductId);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Customers.ReplaceFrequentProductsAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            new UpdateCustomerFrequentProductsRequest(
            [
                new UpdateCustomerFrequentProductItemRequest(product.Id, 12, 1, true),
                new UpdateCustomerFrequentProductItemRequest(product.Id, 4, 2, true)
            ]),
            AdminActor,
            CancellationToken.None));

        var today = await fixture.CustomerOrders.GetTodayAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CancellationToken.None);
        Assert.Contains(today.Products, x => x.ProductId == product.Id);

        var customerPayload = JsonSerializer.Serialize(today, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("machine", customerPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("maquina", customerPayload, StringComparison.OrdinalIgnoreCase);

        await fixture.Products.DeactivateAsync(product.Id, AdminActor, CancellationToken.None);

        var todayAfterDeactivate = await fixture.CustomerOrders.GetTodayAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            CancellationToken.None);
        Assert.DoesNotContain(todayAfterDeactivate.Products, x => x.ProductId == product.Id);
    }

    [Fact]
    public async Task MachineCatalog_DefaultAssignmentIsInternalAndCanBeChangedByAdminOnly()
    {
        await using var fixture = await CatalogFixture.CreateAsync();

        var machine = await fixture.Machines.CreateAsync(
            new UpsertAdminMachineRequest(98, "Maquina piloto"),
            AdminActor,
            CancellationToken.None);
        var updatedMachine = await fixture.Machines.UpdateAsync(
            machine.Id,
            new UpsertAdminMachineRequest(98, "Maquina piloto actualizada"),
            AdminActor,
            CancellationToken.None);

        var assignments = await fixture.Customers.ReplaceMachineAssignmentsAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            new UpdateCustomerMachineAssignmentsRequest(
            [
                new UpdateCustomerMachineAssignmentItemRequest(updatedMachine.Id, true, true, "Default piloto")
            ]),
            AdminActor,
            CancellationToken.None);

        Assert.Single(assignments);
        Assert.True(assignments[0].IsDefault);

        var customerOrder = await fixture.CustomerOrders.SubmitAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            new SubmitCustomerOrderRequest(
            [
                new SubmitCustomerOrderLineRequest(DevelopmentSeedIds.ProductNineAndHalfId, 5, null)
            ]),
            CancellationToken.None);
        var adminDetail = await fixture.AdminOrders.GetDetailAsync(customerOrder.OrderId, CancellationToken.None);

        Assert.Contains(adminDetail.Lines, line => line.AssignedMachineId == updatedMachine.Id);

        var customerPayload = JsonSerializer.Serialize(customerOrder, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("machine", customerPayload, StringComparison.OrdinalIgnoreCase);

        await fixture.Machines.DeactivateAsync(updatedMachine.Id, AdminActor, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Customers.ReplaceMachineAssignmentsAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            new UpdateCustomerMachineAssignmentsRequest(
            [
                new UpdateCustomerMachineAssignmentItemRequest(updatedMachine.Id, true, true, "Default invalido")
            ]),
            AdminActor,
            CancellationToken.None));
    }

    [Fact]
    public async Task CustomerAccessTokens_AreHashedAllowLoginAndRevocationBlocksLogin()
    {
        await using var fixture = await CatalogFixture.CreateAsync();

        var created = await fixture.CustomerAccessTokens.CreateAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            new CreateCustomerAccessTokenRequest("Token piloto Gran Takito", null),
            AdminActor,
            CancellationToken.None);

        var persisted = await fixture.DbContext.CustomerAccessTokens.SingleAsync(x => x.Id == created.TokenId);
        Assert.NotEqual(created.PlainToken, persisted.TokenHash);
        Assert.Equal(fixture.CustomerAccessTokenHasher.HashToken(created.PlainToken), persisted.TokenHash);

        var login = await fixture.Auth.LoginCustomerWithTokenAsync(
            new CustomerTokenLoginRequest(created.PlainToken),
            CancellationToken.None);
        Assert.Equal(DevelopmentSeedIds.GranTakitoCustomerId, login.CustomerId);

        await fixture.CustomerAccessTokens.RevokeAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            created.TokenId,
            AdminActor,
            CancellationToken.None);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => fixture.Auth.LoginCustomerWithTokenAsync(
            new CustomerTokenLoginRequest(created.PlainToken),
            CancellationToken.None));
    }

    [Fact]
    public async Task AdminUsers_CanBeCreatedAndActivated()
    {
        await using var fixture = await CatalogFixture.CreateAsync();

        var created = await fixture.AdminUsers.CreateAsync(
            new CreateAdminUserRequest("operacion", "Operacion", "operacion-password"),
            AdminActor,
            CancellationToken.None);
        var deactivated = await fixture.AdminUsers.DeactivateAsync(created.Id, AdminActor, CancellationToken.None);
        var activated = await fixture.AdminUsers.ActivateAsync(created.Id, AdminActor, CancellationToken.None);

        Assert.True(created.IsActive);
        Assert.False(deactivated.IsActive);
        Assert.True(activated.IsActive);
        Assert.True(await fixture.DbContext.AuditLogs.AnyAsync(x =>
            x.EntityId == created.Id.ToString() &&
            x.EventType == "AdminUserCreated"));
    }

    [Fact]
    public async Task PilotCustomer_UnknownFlagsRemainNullAndInternalInstructionsStayPrivate()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var unknown = await fixture.Customers.CreateAsync(
            new UpsertAdminCustomerRequest("Cliente sin verificar", null, null, null, null, null),
            AdminActor, CancellationToken.None);
        Assert.Null(unknown.HasStoreKey);
        Assert.Null(unknown.PaymentTermCode);
        Assert.Null(unknown.RouteStopOrder);

        var updated = await fixture.Customers.UpdateAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            new UpsertAdminCustomerRequest("Gran Takito", null, null, null, null, "Nota visible",
                "R-DEMO", 2, true, "cash_on_delivery", "Instruccion interna ficticia"),
            AdminActor, CancellationToken.None);
        Assert.True(updated.HasStoreKey);
        Assert.Equal("R-DEMO", updated.RouteCode);
        Assert.Equal(2, updated.RouteStopOrder);
        Assert.Equal("cash_on_delivery", updated.PaymentTermCode);
        Assert.Equal("Instruccion interna ficticia", updated.InternalDeliveryInstructions);

        var today = await fixture.CustomerOrders.GetTodayAsync(DevelopmentSeedIds.GranTakitoCustomerId, CancellationToken.None);
        var payload = JsonSerializer.Serialize(today, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("hasStoreKey", payload);
        Assert.DoesNotContain("internalDeliveryInstructions", payload);
        Assert.DoesNotContain("Instruccion interna ficticia", payload);
        Assert.DoesNotContain("routeCode", payload);
    }

    [Theory]
    [InlineData(null, 1, null)]
    [InlineData("R-DEMO", 0, null)]
    [InlineData("R-DEMO", 1, "weekly")]
    public async Task PilotCustomer_RejectsInvalidDeliveryConfiguration(string? route, int stop, string? payment)
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Customers.CreateAsync(
            new UpsertAdminCustomerRequest("Invalido", null, null, null, null, null, route, stop, null, payment),
            AdminActor, CancellationToken.None));
        Assert.False(await fixture.DbContext.Customers.AnyAsync(x => x.Name == "Invalido"));
    }

    [Fact]
    public async Task PilotProduct_RequiresExplicitSupportedUnitInsteadOfAssumingKilograms()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var unknown = await fixture.Products.CreateAsync(new UpsertAdminProductRequest("Producto sin unidad", null),
            AdminActor, CancellationToken.None);
        Assert.Null(unknown.UnitCode);
        var confirmed = await fixture.Products.UpdateAsync(unknown.Id,
            new UpsertAdminProductRequest(unknown.Name, null, "dozen"), AdminActor, CancellationToken.None);
        Assert.Equal("dozen", confirmed.UnitCode);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Products.UpdateAsync(unknown.Id,
            new UpsertAdminProductRequest(unknown.Name, null, "anything"), AdminActor, CancellationToken.None));
        Assert.Equal("dozen", (await fixture.Products.GetByIdAsync(unknown.Id, CancellationToken.None)).UnitCode);
    }

    [Fact]
    public async Task PilotProfile_ApprovalIsExplicitVersionedAndUsesSeparatePositions()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var items = new[]
        {
            new UpdateCustomerFrequentProductItemRequest(DevelopmentSeedIds.ProductNineAndHalfId, null, 9, true,
                DevelopmentSeedIds.MachineOneId, 1),
            new UpdateCustomerFrequentProductItemRequest(DevelopmentSeedIds.ProductTenAndHalfId, null, 1, true,
                DevelopmentSeedIds.MachineTwoId, 2)
        };
        var result = await fixture.Customers.ReplaceFrequentProductsAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            new UpdateCustomerFrequentProductsRequest(items, true), AdminActor, CancellationToken.None);
        var approved = await fixture.Customers.GetByIdAsync(DevelopmentSeedIds.GranTakitoCustomerId, CancellationToken.None);
        Assert.True(approved.IsOrderProfileApproved);
        Assert.Equal(1, approved.OrderingProfileVersion);
        Assert.Equal(DevelopmentSeedIds.ProductTenAndHalfId, result[0].ProductId);
        Assert.Equal(2, result[0].InterpretationPosition);
        Assert.Equal(DevelopmentSeedIds.MachineTwoId, result[0].PreferredMachineId);
        Assert.Equal("kg", result[0].UnitCode);

        await fixture.Customers.ReplaceFrequentProductsAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            new UpdateCustomerFrequentProductsRequest(items), AdminActor, CancellationToken.None);
        var edited = await fixture.Customers.GetByIdAsync(DevelopmentSeedIds.GranTakitoCustomerId, CancellationToken.None);
        Assert.False(edited.IsOrderProfileApproved);
        Assert.Equal(2, edited.OrderingProfileVersion);
    }

    [Fact]
    public async Task PilotProfile_RejectsDuplicatePositionsAndIncompleteApprovalWithoutMutation()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var duplicate = new UpdateCustomerFrequentProductsRequest([
            new(DevelopmentSeedIds.ProductNineAndHalfId, null, 1, true, null, 1),
            new(DevelopmentSeedIds.ProductTenAndHalfId, null, 2, true, null, 1)
        ]);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Customers.ReplaceFrequentProductsAsync(
            DevelopmentSeedIds.GranTakitoCustomerId, duplicate, AdminActor, CancellationToken.None));
        var missingPosition = new UpdateCustomerFrequentProductsRequest([
            new(DevelopmentSeedIds.ProductNineAndHalfId, null, 1, true)
        ], true);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Customers.ReplaceFrequentProductsAsync(
            DevelopmentSeedIds.GranTakitoCustomerId, missingPosition, AdminActor, CancellationToken.None));
        var invalidId = new UpdateCustomerFrequentProductsRequest([new(Guid.Empty, null, 1, true)]);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Customers.ReplaceFrequentProductsAsync(
            DevelopmentSeedIds.GranTakitoCustomerId, invalidId, AdminActor, CancellationToken.None));
        var customer = await fixture.Customers.GetByIdAsync(DevelopmentSeedIds.GranTakitoCustomerId, CancellationToken.None);
        Assert.Equal(0, customer.OrderingProfileVersion);
        Assert.Equal(2, (await fixture.Customers.GetFrequentProductsAsync(customer.Id, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task PilotProfile_RejectsUnknownUnitAndInactivePreferredMachine()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var unknown = await fixture.Products.CreateAsync(new UpsertAdminProductRequest("Unidad pendiente", null),
            AdminActor, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Customers.ReplaceFrequentProductsAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            new UpdateCustomerFrequentProductsRequest([new(unknown.Id, null, 1, true, null, 1)], true),
            AdminActor, CancellationToken.None));
        await fixture.Machines.DeactivateAsync(DevelopmentSeedIds.MachineThreeId, AdminActor, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Customers.ReplaceFrequentProductsAsync(
            DevelopmentSeedIds.GranTakitoCustomerId,
            new UpdateCustomerFrequentProductsRequest([new(DevelopmentSeedIds.ProductNineAndHalfId, null, 1, true,
                DevelopmentSeedIds.MachineThreeId, 1)], true), AdminActor, CancellationToken.None));
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("name")]
    [InlineData("deactivate")]
    public async Task PilotProfile_ProductMeaningChangesRevokeApprovalAndIncrementVersion(string change)
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        await fixture.Customers.ReplaceFrequentProductsAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            new UpdateCustomerFrequentProductsRequest([new(DevelopmentSeedIds.ProductNineAndHalfId, null, 1, true, null, 1)], true),
            AdminActor, CancellationToken.None);
        if (change == "deactivate")
            await fixture.Products.DeactivateAsync(DevelopmentSeedIds.ProductNineAndHalfId, AdminActor, CancellationToken.None);
        else
            await fixture.Products.UpdateAsync(DevelopmentSeedIds.ProductNineAndHalfId,
                new UpsertAdminProductRequest(change == "name" ? "Molde actualizado" : "#9 1/2", "Producto demo",
                    change == "unit" ? "dozen" : "kg"), AdminActor, CancellationToken.None);

        var customer = await fixture.Customers.GetByIdAsync(DevelopmentSeedIds.GranTakitoCustomerId, CancellationToken.None);
        Assert.False(customer.IsOrderProfileApproved);
        Assert.Equal(2, customer.OrderingProfileVersion);
        Assert.True(await fixture.DbContext.AuditLogs.AnyAsync(x => x.EventType == "OrderingProfileInvalidated" &&
            x.EntityId == customer.Id.ToString()));
    }

    [Fact]
    public async Task PilotProfile_CanSwapExplicitInterpretationPositions()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        await fixture.Customers.ReplaceFrequentProductsAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            new UpdateCustomerFrequentProductsRequest([
                new(DevelopmentSeedIds.ProductNineAndHalfId, null, 1, true, null, 1),
                new(DevelopmentSeedIds.ProductTenAndHalfId, null, 2, true, null, 2)
            ], true), AdminActor, CancellationToken.None);
        var result = await fixture.Customers.ReplaceFrequentProductsAsync(DevelopmentSeedIds.GranTakitoCustomerId,
            new UpdateCustomerFrequentProductsRequest([
                new(DevelopmentSeedIds.ProductNineAndHalfId, null, 1, true, null, 2),
                new(DevelopmentSeedIds.ProductTenAndHalfId, null, 2, true, null, 1)
            ], true), AdminActor, CancellationToken.None);
        Assert.Equal(2, result.Single(x => x.ProductId == DevelopmentSeedIds.ProductNineAndHalfId).InterpretationPosition);
    }

    [Fact]
    public async Task PilotSnapshots_AllowEveryValueAcceptedByCatalogColumnLengths()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var model = fixture.DbContext.Model;
        var customer = model.FindEntityType(typeof(Prodimt.Pedidos.Domain.Entities.Customer))!;
        var order = model.FindEntityType(typeof(Prodimt.Pedidos.Domain.Entities.Order))!;
        var product = model.FindEntityType(typeof(Prodimt.Pedidos.Domain.Entities.Product))!;
        var line = model.FindEntityType(typeof(Prodimt.Pedidos.Domain.Entities.OrderLine))!;
        Assert.True(order.FindProperty("RouteCode")!.GetMaxLength() >= customer.FindProperty("RouteCode")!.GetMaxLength());
        Assert.True(order.FindProperty("PaymentTermCode")!.GetMaxLength() >= customer.FindProperty("PaymentTermCode")!.GetMaxLength());
        Assert.True(order.FindProperty("InternalDeliveryInstructions")!.GetMaxLength() >= customer.FindProperty("InternalDeliveryInstructions")!.GetMaxLength());
        Assert.True(line.FindProperty("UnitCode")!.GetMaxLength() >= product.FindProperty("UnitCode")!.GetMaxLength());
    }

    [Fact]
    public async Task PilotProfile_FailedReplacementRollsBackRowsAndProfileVersionTogether()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var repository = new EfCustomerRepository(fixture.DbContext);
        var customer = (await repository.GetByIdForUpdateAsync(DevelopmentSeedIds.GranTakitoCustomerId, CancellationToken.None))!;
        customer.RecordOrderingProfileChange(false, DateTimeOffset.UtcNow);
        await repository.ReplaceFrequentProductsAsync(customer.Id,
        [
            new Prodimt.Pedidos.Domain.Entities.CustomerFrequentProduct
            {
                CustomerId = customer.Id,
                ProductId = Guid.NewGuid(), // Invalid reference forces commit failure after delete.
                SortOrder = 1
            }
        ], CancellationToken.None);
        await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveChangesAsync(CancellationToken.None));
        fixture.DbContext.ChangeTracker.Clear();
        Assert.Equal(0, (await repository.GetByIdAsync(customer.Id, CancellationToken.None))!.OrderingProfileVersion);
        Assert.Equal(2, (await repository.GetAllFrequentProductsAsync(customer.Id, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task OrderingProfileSnapshot_ReturnsVersionApprovalAndItemsTogether_AndTracksProductInvalidation()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var customerId = DevelopmentSeedIds.GranTakitoCustomerId;
        var productId = DevelopmentSeedIds.ProductNineAndHalfId;
        await fixture.Customers.ReplaceFrequentProductsAsync(customerId,
            new([new(productId, 15, 1, true, null, 1)], true, 0), AdminActor, default);

        var approved = await fixture.Customers.GetOrderingProfileAsync(customerId, default);
        Assert.Equal(customerId, approved.CustomerId);
        Assert.Equal(1, approved.OrderingProfileVersion);
        Assert.True(approved.IsOrderProfileApproved);
        Assert.Equal(15, Assert.Single(approved.Items).DefaultQuantity);
        Assert.Equal("kg", approved.Items[0].UnitCode);

        await fixture.Products.UpdateAsync(productId, new("Producto con nueva unidad", null, "piece"), AdminActor, default);
        var changed = await fixture.Customers.GetOrderingProfileAsync(customerId, default);
        Assert.Equal(2, changed.OrderingProfileVersion);
        Assert.False(changed.IsOrderProfileApproved);
        Assert.Equal("Producto con nueva unidad", Assert.Single(changed.Items).ProductName);
        Assert.Equal("piece", changed.Items[0].UnitCode);
        Assert.Equal("kg", approved.Items[0].UnitCode); // Previous response remains an immutable snapshot.
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Customers.GetOrderingProfileAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task OrderingProfile_StaleExpectedVersionRejectsBeforeReplacingRowsOrAuditing()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var customerId = DevelopmentSeedIds.GranTakitoCustomerId;
        var productId = DevelopmentSeedIds.ProductNineAndHalfId;
        await fixture.Customers.ReplaceFrequentProductsAsync(customerId,
            new([new(productId, 15, 1, true, null, 1)], true, 0), AdminActor, default);
        var auditCount = await fixture.DbContext.AuditLogs.CountAsync();

        await Assert.ThrowsAsync<CatalogConflictException>(() => fixture.Customers.ReplaceFrequentProductsAsync(customerId,
            new([new(productId, 99, 1, true, null, 1)], false, 0), AdminActor, default));

        var current = await fixture.Customers.GetOrderingProfileAsync(customerId, default);
        Assert.Equal(1, current.OrderingProfileVersion);
        Assert.True(current.IsOrderProfileApproved);
        Assert.Equal(15, Assert.Single(current.Items).DefaultQuantity);
        Assert.Equal(auditCount, await fixture.DbContext.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task OrderingProfile_ConcurrentTrackedEditorsRollBackLosingReplacementAndAudit()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        await using var firstDb = fixture.CreateSeparateContext();
        await using var secondDb = fixture.CreateSeparateContext();
        var customerId = DevelopmentSeedIds.GranTakitoCustomerId;
        var productId = DevelopmentSeedIds.ProductNineAndHalfId;
        var firstRepository = new EfCustomerRepository(firstDb);
        var secondRepository = new EfCustomerRepository(secondDb);
        // Both editors already loaded version zero before either save starts.
        await firstRepository.GetByIdForUpdateAsync(customerId, default);
        await secondRepository.GetByIdForUpdateAsync(customerId, default);
        AdminCustomerCatalogService Service(PedidosDbContext db, EfCustomerRepository repository) => new(repository,
            new EfProductRepository(db), new EfMachineRepository(db), new EfAuditLogRepository(db),
            new FixedDateTimeProvider(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero)));

        await Service(firstDb, firstRepository).ReplaceFrequentProductsAsync(customerId,
            new([new(productId, 20, 1, true, null, 1)], true, 0), AdminActor, default);
        var auditCount = await fixture.DbContext.AuditLogs.CountAsync();
        await Assert.ThrowsAsync<CatalogConflictException>(() => Service(secondDb, secondRepository)
            .ReplaceFrequentProductsAsync(customerId, new([new(productId, 99, 1, true, null, 1)], false, 0), AdminActor, default));

        var winner = await fixture.Customers.GetOrderingProfileAsync(customerId, default);
        Assert.Equal(1, winner.OrderingProfileVersion);
        Assert.True(winner.IsOrderProfileApproved);
        Assert.Equal(20, Assert.Single(winner.Items).DefaultQuantity);
        Assert.Equal(auditCount, await fixture.DbContext.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task OrderingProfile_ApprovalRejectsChangedUnitOfNewProductWithoutReplacingOrAuditing()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var customerId = DevelopmentSeedIds.GranTakitoCustomerId;
        // This product is not in the customer's profile, so its edit cannot increment that profile's version.
        var created = await fixture.Products.CreateAsync(new("Producto nuevo para confirmar", null, "kg"), AdminActor, default);
        var before = await fixture.Customers.GetOrderingProfileAsync(customerId, default);
        await fixture.Products.UpdateAsync(created.Id, new(created.Name, null, "piece"), AdminActor, default);
        var auditCount = await fixture.DbContext.AuditLogs.CountAsync();

        await Assert.ThrowsAsync<CatalogConflictException>(() => fixture.Customers.ReplaceFrequentProductsAsync(customerId,
            new([new(created.Id, 20, 1, true, null, 1, "kg")], true, before.OrderingProfileVersion), AdminActor, default));

        var current = await fixture.Customers.GetOrderingProfileAsync(customerId, default);
        Assert.Equal(before.OrderingProfileVersion, current.OrderingProfileVersion);
        Assert.Equal(before.Items, current.Items);
        Assert.Equal(auditCount, await fixture.DbContext.AuditLogs.CountAsync());
        // A new explicit confirmation of the displayed unit may approve normally.
        await fixture.Customers.ReplaceFrequentProductsAsync(customerId,
            new([new(created.Id, 20, 1, true, null, 1, "piece")], true, before.OrderingProfileVersion), AdminActor, default);
        Assert.True((await fixture.Customers.GetOrderingProfileAsync(customerId, default)).IsOrderProfileApproved);
    }

    [Fact]
    public async Task OrderingProfileSnapshot_IncludesProductAvailabilitySeparateFromMembership()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var customerId = DevelopmentSeedIds.GranTakitoCustomerId;
        var productId = DevelopmentSeedIds.ProductNineAndHalfId;
        var before = await fixture.Customers.GetOrderingProfileAsync(customerId, default);
        Assert.True(before.Items.Single(x => x.ProductId == productId).ProductIsActive);
        await fixture.Products.DeactivateAsync(productId, AdminActor, default);

        var after = await fixture.Customers.GetOrderingProfileAsync(customerId, default);
        var row = after.Items.Single(x => x.ProductId == productId);
        Assert.True(row.IsActive);
        Assert.False(row.ProductIsActive);
        Assert.Equal(before.OrderingProfileVersion + 1, after.OrderingProfileVersion);
        Assert.False(after.IsOrderProfileApproved);
    }

    [Fact]
    public async Task OrderingProfile_RejectsNegativeVersionWithoutChangingExistingProfile()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Customers.ReplaceFrequentProductsAsync(
            DevelopmentSeedIds.GranTakitoCustomerId, new([], false, -1), AdminActor, default));
        var profile = await fixture.Customers.GetOrderingProfileAsync(DevelopmentSeedIds.GranTakitoCustomerId, default);
        Assert.Equal(0, profile.OrderingProfileVersion);
        Assert.Equal(2, profile.Items.Count);
    }

    private sealed class CatalogFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private CatalogFixture(
            SqliteConnection connection,
            PedidosDbContext dbContext,
            ICustomerAccessTokenHasher customerAccessTokenHasher,
            AdminCustomerCatalogService customers,
            AdminProductCatalogService products,
            AdminMachineCatalogService machines,
            AdminCustomerAccessTokenService customerAccessTokens,
            AdminUserCatalogService adminUsers,
            CustomerOrderService customerOrders,
            AdminOrderService adminOrders,
            PilotAuthenticationService auth)
        {
            _connection = connection;
            DbContext = dbContext;
            CustomerAccessTokenHasher = customerAccessTokenHasher;
            Customers = customers;
            Products = products;
            Machines = machines;
            CustomerAccessTokens = customerAccessTokens;
            AdminUsers = adminUsers;
            CustomerOrders = customerOrders;
            AdminOrders = adminOrders;
            Auth = auth;
        }

        public PedidosDbContext DbContext { get; }

        public PedidosDbContext CreateSeparateContext() => new(new DbContextOptionsBuilder<PedidosDbContext>()
            .UseSqlite(_connection).Options);

        public ICustomerAccessTokenHasher CustomerAccessTokenHasher { get; }

        public AdminCustomerCatalogService Customers { get; }

        public AdminProductCatalogService Products { get; }

        public AdminMachineCatalogService Machines { get; }

        public AdminCustomerAccessTokenService CustomerAccessTokens { get; }

        public AdminUserCatalogService AdminUsers { get; }

        public CustomerOrderService CustomerOrders { get; }

        public AdminOrderService AdminOrders { get; }

        public PilotAuthenticationService Auth { get; }

        public static async Task<CatalogFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<PedidosDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new PedidosDbContext(options);
            var configuration = CreateConfiguration();
            var passwordHashService = new PasswordHashService();
            var customerAccessTokenHasher = new CustomerAccessTokenHasher();
            var dateTimeProvider = new FixedDateTimeProvider(new DateTimeOffset(2026, 5, 28, 9, 0, 0, TimeSpan.Zero));

            await dbContext.Database.EnsureCreatedAsync();
            await PedidosDevelopmentSeeder.SeedAsync(
                dbContext,
                configuration,
                passwordHashService,
                customerAccessTokenHasher);

            var customerRepository = new EfCustomerRepository(dbContext);
            var productRepository = new EfProductRepository(dbContext);
            var machineRepository = new EfMachineRepository(dbContext);
            var orderRepository = new EfOrderRepository(dbContext);
            var orderAuditRepository = new EfOrderAuditLogRepository(dbContext);
            var auditLogRepository = new EfAuditLogRepository(dbContext);
            var salesChannelRepository = new EfSalesChannelRepository(dbContext);
            var customerAccessTokenRepository = new EfCustomerAccessTokenRepository(dbContext);
            var adminUserRepository = new EfAdminUserRepository(dbContext);

            var customers = new AdminCustomerCatalogService(
                customerRepository,
                productRepository,
                machineRepository,
                auditLogRepository,
                dateTimeProvider);
            var products = new AdminProductCatalogService(
                productRepository,
                auditLogRepository,
                dateTimeProvider,
                customerRepository);
            var machines = new AdminMachineCatalogService(
                machineRepository,
                auditLogRepository,
                dateTimeProvider);
            var customerAccessTokens = new AdminCustomerAccessTokenService(
                customerRepository,
                customerAccessTokenRepository,
                customerAccessTokenHasher,
                auditLogRepository,
                dateTimeProvider);
            var adminUsers = new AdminUserCatalogService(
                adminUserRepository,
                passwordHashService,
                auditLogRepository,
                dateTimeProvider);
            var customerOrders = new CustomerOrderService(
                customerRepository,
                productRepository,
                salesChannelRepository,
                orderRepository,
                orderAuditRepository,
                dateTimeProvider,
                machineRepository);
            var adminOrders = new AdminOrderService(
                orderRepository,
                orderAuditRepository,
                customerRepository,
                productRepository,
                machineRepository,
                salesChannelRepository,
                dateTimeProvider,
                new OrderChangeNoticePublisher(new EfOrderChangeNoticeRepository(dbContext), machineRepository, customerRepository));
            var auth = new PilotAuthenticationService(
                adminUserRepository,
                customerAccessTokenRepository,
                customerAccessTokenHasher,
                customerRepository,
                passwordHashService,
                new JwtTokenService(configuration, dateTimeProvider),
                dateTimeProvider);

            return new CatalogFixture(
                connection,
                dbContext,
                customerAccessTokenHasher,
                customers,
                products,
                machines,
                customerAccessTokens,
                adminUsers,
                customerOrders,
                adminOrders,
                auth);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static IConfiguration CreateConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Jwt:SigningKey"] = JwtSettings.DevelopmentSigningKey,
                    ["Authentication:Jwt:Issuer"] = "Prodimt.Pedidos.Tests",
                    ["Authentication:Jwt:Audience"] = "Prodimt.Pedidos.Tests",
                    ["Authentication:Jwt:AccessTokenMinutes"] = "60"
                })
                .Build();
        }
    }

    private sealed class FixedDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset Now { get; } = now;

        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

        public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(Now.DateTime);
    }
}
