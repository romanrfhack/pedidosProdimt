using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.Delivery;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;

namespace Prodimt.Pedidos.SqlServer.Tests;

[Collection("SQL Server")]
public sealed class DeliveryManagementSqlTests(SqlServerFixture database)
{
    private const string AdminBase = "/api/admin/delivery";
    private const string OwnBase = "/api/delivery";
    private const string SyntheticPassword = "Synthetic-delivery-driver-2026!";
    private static readonly DateOnly ServiceDate = new(2026, 10, 24);

    [Fact]
    public async Task ConcurrentPreparationAndCountingUseOneTripAndOneReservation()
    {
        var scene = await SeedAsync();
        await using var firstHost = database.CreateFactory();
        await using var secondHost = database.CreateFactory();
        using var first = await DriverClientAsync(firstHost, scene.DriverUserName);
        using var second = await DriverClientAsync(secondHost, scene.DriverUserName);

        async Task<HttpStatusCode[]> Race<T>(string path, Func<T> request)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<HttpStatusCode> Send(HttpClient client)
            {
                await start.Task;
                using var response = await client.PostAsJsonAsync(path, request());
                return response.StatusCode;
            }
            var firstWrite = Send(first);
            var secondWrite = Send(second);
            start.SetResult();
            return await Task.WhenAll(firstWrite, secondWrite);
        }

        var prepared = await Race(OwnBase + "/prepare", () => new PrepareDeliveryRequest(Guid.NewGuid(), ServiceDate));
        Assert.Contains(HttpStatusCode.OK, prepared);
        Assert.All(prepared, code => Assert.True(code is HttpStatusCode.OK or HttpStatusCode.Conflict, code.ToString()));
        var dashboard = await first.GetFromJsonAsync<DeliveryDashboardResponse>(OwnBase + $"?date={ServiceDate:yyyy-MM-dd}");
        var trip = Assert.Single(dashboard!.Trips);
        var order = Assert.Single(trip.PlannedOrders!);
        var counted = await Race(OwnBase + $"/trips/{trip.Id}/planned-count", () => new CountPlannedDeliveryLineRequest(
            Guid.NewGuid(), scene.OrderId, scene.LineId, order.OrderVersion, trip.Version, 6, "Conteo parcial"));
        Assert.Single(counted, x => x == HttpStatusCode.OK);
        Assert.Single(counted, x => x == HttpStatusCode.Conflict);
        await using var check = database.CreateDbContext();
        Assert.Single(await check.DeliveryTrips.Where(x => x.RouteCode == scene.RouteCode).ToArrayAsync());
        var assigned = Assert.Single(await check.DeliveryAssignments.Where(x => x.OrderId == scene.OrderId).ToArrayAsync());
        Assert.Equal(6, assigned.CommitmentQuantity);
        Assert.Equal(4, assigned.ReleasedQuantity);
        Assert.Equal(0, assigned.CustodyQuantity);
    }

    [Fact]
    public async Task IndependentHostsCannotReserveTheSameRemainingDemandTwice()
    {
        var scene = await SeedAsync();
        await using var firstHost = database.CreateFactory();
        await using var secondHost = database.CreateFactory();
        using var firstAdmin = await SqlServerFixture.CreateAdminClientAsync(firstHost);
        using var secondAdmin = await SqlServerFixture.CreateAdminClientAsync(secondHost);
        var firstTrip = await CreateTripAsync(firstAdmin, scene, 1);
        var secondTrip = await CreateTripAsync(secondAdmin, scene, 2);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Attempt(HttpClient client, DeliveryCommandResponse trip)
        {
            await start.Task;
            return await client.PostAsJsonAsync(AdminBase + $"/trips/{trip.Id}/assignments",
                new AssignDeliveryLineRequest(Guid.NewGuid(), scene.OrderId, scene.LineId, 7, trip.Version!.Value, 1));
        }
        var first = Attempt(firstAdmin, firstTrip);
        var second = Attempt(secondAdmin, secondTrip);
        start.SetResult();
        var responses = await Task.WhenAll(first, second);
        try
        {
            Assert.Single(responses, x => x.IsSuccessStatusCode);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        var dashboard = await firstAdmin.GetFromJsonAsync<DeliveryDashboardResponse>(AdminBase + $"?date={ServiceDate:yyyy-MM-dd}&routeCode={scene.RouteCode}");
        var line = Assert.Single(dashboard!.Orders.Single(x => x.OrderId == scene.OrderId).Lines);
        Assert.Equal(10, line.RequestedQuantity);
        Assert.Equal(7, line.ReservedQuantity);
        Assert.Equal(3, line.AvailableQuantity);
        await using var check = database.CreateDbContext();
        Assert.Single(await check.DeliveryAssignments.Where(x => x.OrderId == scene.OrderId).ToArrayAsync());
    }

    [Fact]
    public async Task DriverAuthorizationAndDurableDeliveryReplaySurviveDifferentHosts()
    {
        var scene = await SeedAsync();
        await using var firstHost = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(firstHost);
        var trip = await CreateTripAsync(admin, scene, 1);
        var assignment = await Command(admin, AdminBase + $"/trips/{trip.Id}/assignments",
            new AssignDeliveryLineRequest(Guid.NewGuid(), scene.OrderId, scene.LineId, 10, trip.Version!.Value, 1));
        using var driver = await DriverClientAsync(firstHost, scene.DriverUserName);
        using var stranger = await DriverClientAsync(firstHost, scene.OtherUserName);
        using var managementAttempt = await driver.GetAsync(AdminBase + $"?date={ServiceDate:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.Forbidden, managementAttempt.StatusCode);
        using var strangerRead = await stranger.GetAsync(OwnBase + $"/trips/{trip.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, strangerRead.StatusCode);
        using var strangerWrite = await stranger.PostAsJsonAsync(OwnBase + $"/assignments/{assignment.Id}/count",
            new RecordDeliveryStageRequest(Guid.NewGuid(), assignment.Version!.Value, 1, 10));
        Assert.Equal(HttpStatusCode.Forbidden, strangerWrite.StatusCode);
        await Record(driver, trip.Id, assignment.Id, "count", 10);
        await Record(driver, trip.Id, assignment.Id, "load", 10);
        var before = await driver.GetFromJsonAsync<DeliveryTripResponse>(OwnBase + $"/trips/{trip.Id}");
        var loaded = Assert.Single(before!.Assignments);
        var deliveryRequest = new RecordDeliveryStageRequest(Guid.NewGuid(), loaded.Version, loaded.CurrentOrderVersion,
            8, "Cliente recibió menos", "Quedan dos en mi camioneta.", "in-vehicle");
        var saved = await Command(driver, OwnBase + $"/assignments/{assignment.Id}/deliver", deliveryRequest);
        await using var nextHost = database.CreateFactory();
        using var nextDriver = await DriverClientAsync(nextHost, scene.DriverUserName);
        var replay = await Command(nextDriver, OwnBase + $"/assignments/{assignment.Id}/deliver", deliveryRequest);
        Assert.True(replay.Replayed);
        Assert.Equal(saved.Id, replay.Id);
        var detail = await nextDriver.GetFromJsonAsync<DeliveryTripResponse>(OwnBase + $"/trips/{trip.Id}");
        var delivered = Assert.Single(detail!.Assignments);
        Assert.Equal(10, delivered.LoadedQuantity);
        Assert.Equal(8, delivered.DeliveredQuantity);
        Assert.Equal(2, delivered.CustodyQuantity);
        Assert.Equal(2, delivered.PendingQuantity);
        using var changedPayload = await nextDriver.PostAsJsonAsync(OwnBase + $"/assignments/{assignment.Id}/deliver",
            deliveryRequest with { Quantity = 7 });
        Assert.Equal(HttpStatusCode.Conflict, changedPayload.StatusCode);
        await using var check = database.CreateDbContext();
        Assert.Equal(10, (await check.OrderLines.SingleAsync(x => x.Id == scene.LineId)).Quantity);
    }

    [Fact]
    public async Task RevokedManagementPermissionCannotReplayAnEarlierCommandForAnotherDriver()
    {
        var scene = await SeedAsync();
        Guid elevatedUserId;
        Guid permissionId;
        await using (var setup = database.CreateDbContext())
        {
            elevatedUserId = (await setup.AdminUsers.SingleAsync(x => x.UserName == scene.OtherUserName)).Id;
            permissionId = (await setup.SecurityPermissions.SingleAsync(x => x.Key == "delivery.manage")).Id;
            setup.SecurityUserPermissions.Add(new() { UserId = elevatedUserId, PermissionId = permissionId });
            await setup.SaveChangesAsync();
        }
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        var trip = await CreateTripAsync(admin, scene, 1);
        var assignment = await Command(admin, AdminBase + $"/trips/{trip.Id}/assignments",
            new AssignDeliveryLineRequest(Guid.NewGuid(), scene.OrderId, scene.LineId, 10, trip.Version!.Value, 1));
        using var elevated = await DriverClientAsync(host, scene.OtherUserName);
        var request = new RecordDeliveryStageRequest(Guid.NewGuid(), assignment.Version!.Value, 1, 10);
        await Command(elevated, AdminBase + $"/assignments/{assignment.Id}/count", request);
        await using (var revoke = database.CreateDbContext())
        {
            var direct = await revoke.SecurityUserPermissions.SingleAsync(x => x.UserId == elevatedUserId && x.PermissionId == permissionId);
            revoke.SecurityUserPermissions.Remove(direct);
            await revoke.SaveChangesAsync();
        }
        using var managerReplay = await elevated.PostAsJsonAsync(AdminBase + $"/assignments/{assignment.Id}/count", request);
        Assert.Equal(HttpStatusCode.Forbidden, managerReplay.StatusCode);
        using var ownReplay = await elevated.PostAsJsonAsync(OwnBase + $"/assignments/{assignment.Id}/count", request);
        Assert.Equal(HttpStatusCode.Forbidden, ownReplay.StatusCode);
        var detail = await admin.GetFromJsonAsync<DeliveryTripResponse>(AdminBase + $"/trips/{trip.Id}");
        Assert.Equal(10, Assert.Single(detail!.Assignments).CountedQuantity);
        Assert.Single(detail.Events, x => x.AssignmentId == assignment.Id && x.Type == "Counted");
    }

    private static async Task<DeliveryCommandResponse> Command<T>(HttpClient client, string path, T request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeliveryCommandResponse>())!;
    }
    private static Task<DeliveryCommandResponse> CreateTripAsync(HttpClient client, Scene scene, int number)
        => Command(client, AdminBase + "/trips", new CreateDeliveryTripRequest(Guid.NewGuid(), ServiceDate,
            scene.RouteCode, number, scene.DriverId));
    private static async Task Record(HttpClient client, Guid tripId, Guid assignmentId, string stage, decimal quantity)
    {
        var trip = await client.GetFromJsonAsync<DeliveryTripResponse>(OwnBase + $"/trips/{tripId}");
        var a = trip!.Assignments.Single(x => x.Id == assignmentId);
        await Command(client, OwnBase + $"/assignments/{assignmentId}/{stage}",
            new RecordDeliveryStageRequest(Guid.NewGuid(), a.Version, a.CurrentOrderVersion, quantity));
    }
    private static async Task<HttpClient> DriverClientAsync(SqlServerFixture.SqlApiFactory factory, string userName)
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsJsonAsync("/api/auth/admin/login", new { userName, password = SyntheticPassword });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<AdminLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }
    private async Task<Scene> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var route = "D-" + suffix[..12];
        await using var db = database.CreateDbContext();
        var customer = Customer.Create("Synthetic delivery client " + suffix, null, null, null, null, null, SqlServerFixture.Now);
        customer.SetPilotDelivery(route, 1, false, "credit", "Synthetic instruction", SqlServerFixture.Now);
        var product = Product.Create("Synthetic delivery product " + suffix, null); product.SetUnitCode("kg");
        var line = new OrderLine { Id = Guid.NewGuid(), ProductId = product.Id, ProductNameSnapshot = product.Name,
            UnitCode = "kg", Quantity = 10 };
        var order = Order.CreateSubmitted(customer.Id, database.SalesChannelId, ServiceDate, SqlServerFixture.Now, 1,
            new(false, false, null, OrderStatus.Submitted), [line], customer);
        var hash = new PasswordHashService().HashPassword(SyntheticPassword);
        var driver = AdminUser.Create("delivery-" + suffix, "Synthetic driver " + suffix, hash, SqlServerFixture.Now);
        var other = AdminUser.Create("other-" + suffix, "Other synthetic driver " + suffix, hash, SqlServerFixture.Now);
        db.AddRange(customer, product, order, driver, other);
        var permissions = await db.SecurityPermissions.Where(x => x.Key == "delivery.read.own" || x.Key == "delivery.operate.own").ToArrayAsync();
        Assert.Equal(2, permissions.Length);
        foreach (var userId in new[] { driver.Id, other.Id })
            foreach (var permission in permissions) db.SecurityUserPermissions.Add(new() { UserId = userId, PermissionId = permission.Id });
        await db.SaveChangesAsync();
        await using var host = database.CreateFactory();
        using var admin = await SqlServerFixture.CreateAdminClientAsync(host);
        await Command(admin, AdminBase + "/routes", new SaveDeliveryRouteRequest(Guid.NewGuid(), route, "Synthetic route " + suffix, driver.Id));
        return new(order.Id, line.Id, route, driver.Id, driver.UserName, other.UserName);
    }
    private sealed record Scene(Guid OrderId, Guid LineId, string RouteCode, Guid DriverId, string DriverUserName, string OtherUserName);
}
