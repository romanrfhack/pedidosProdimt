using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
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
                dateTimeProvider);
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
                dateTimeProvider);
            var adminOrders = new AdminOrderService(
                orderRepository,
                orderAuditRepository,
                customerRepository,
                productRepository,
                machineRepository,
                salesChannelRepository,
                dateTimeProvider);
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
