using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Application.Delivery;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.C2.Tests;

[CollectionDefinition("C2 SQL", DisableParallelization = true)]
public sealed class C2Collection : ICollectionFixture<C2Fixture>;

public sealed class C2Fixture : IAsyncLifetime
{
    public C2Database Database { get; } = new(C2Configuration.Read(
        Environment.GetEnvironmentVariable("PRODIMT_C2_CONFIG") ?? throw new InvalidOperationException("c2_config_required")));
    public Task InitializeAsync() => Database.InitializeAsync();
    public async Task DisposeAsync() => await Database.DisposeAsync();
}

[Collection("C2 SQL")]
public sealed class OrderingScheduleHttpSqlTests(C2Fixture fixture)
{
    public static IEnumerable<object[]> Cases => C2Scenarios.Names.Select(name => new object[] { name });
    [Theory]
    [MemberData(nameof(Cases))]
    public Task RealHttpAndSql(string name) => C2Scenarios.Run(name, fixture.Database);
}

// Shared enumeration: xUnit, portable runner and evidence all count the same cases.
// No skips, retries or date-dependent discovery.
public static class C2Scenarios
{
    private static readonly (string Name, string Now, string Date, bool Pending)[] Calendar =
    [
        ("monday_before_18", "2026-11-02T23:59:59Z", "2026-11-02", true),
        ("monday_at_18", "2026-11-03T00:00:00Z", "2026-11-04", false),
        ("monday_after_18", "2026-11-03T00:00:01Z", "2026-11-04", false),
        ("tuesday_midnight", "2026-11-03T06:00:00Z", "2026-11-04", false),
        ("tuesday_before_18", "2026-11-03T23:59:59Z", "2026-11-04", false),
        ("tuesday_at_18", "2026-11-04T00:00:00Z", "2026-11-04", false),
        ("wednesday_midnight", "2026-11-04T06:00:00Z", "2026-11-04", false),
        ("wednesday_before_18", "2026-11-04T23:59:59Z", "2026-11-04", true),
        ("wednesday_at_18", "2026-11-05T00:00:00Z", "2026-11-05", false),
        ("wednesday_before_midnight", "2026-11-05T05:59:59Z", "2026-11-05", false),
        ("thursday_midnight", "2026-11-05T06:00:00Z", "2026-11-05", false)
    ];
    private static readonly string[] Core = ["application_permissions", "schedule_inheritance_history",
        "restricted_expired_link", "expired_window_before_end", "expired_window_at_end", "expired_window_after_end",
        "revoked_link", "unknown_link", "date_crossed_before_confirm", "revoked_after_load",
        "expiry_after_load", "profile_changed_after_load", "review_accept", "review_reject", "review_adjust",
        "lost_capture_response_replay", "capture_concurrent_hosts", "customer_schedule_concurrent_hosts",
        "route_schedule_concurrent_hosts", "review_concurrent_hosts", "capture_atomic_rollback",
        "schedule_atomic_rollback", "actor_isolation"];
    public static string[] Names { get; } = Core.Concat(Calendar.Select(x => "calendar_" + x.Name))
        .Concat(new[] { "default", "route", "customer" }.SelectMany(source =>
            new[] { "before", "at", "after" }.Select(edge => $"cutoff_{source}_{edge}"))).ToArray();

    public static async Task Run(string name, C2Database db)
    {
        Assert.Contains(name, Names);
        db.Clock.Now = DateTimeOffset.Parse("2026-11-04T18:00:00Z"); // Wednesday noon CDMX.
        if (name == "application_permissions") { await db.VerifyPermissions(); return; }
        var calendar = Calendar.FirstOrDefault(x => "calendar_" + x.Name == name);
        if (calendar.Name is not null)
        {
            db.Clock.Now = DateTimeOffset.Parse(calendar.Now);
            await CaptureAtClock(db, DateOnly.Parse(calendar.Date), calendar.Pending); return;
        }
        if (name.StartsWith("cutoff_", StringComparison.Ordinal))
        {
            var parts = name.Split('_'); var source = parts[1]; var edge = parts[2];
            var hour = source == "customer" ? 9 : source == "route" ? 11 : 10;
            db.Clock.Now = new DateTimeOffset(2026, 11, 4, hour + 6, 0, 0, TimeSpan.Zero)
                .AddSeconds(edge == "before" ? -1 : edge == "after" ? 1 : 0);
            await CaptureAtClock(db, new(2026, 11, 4), edge != "before", source, hour); return;
        }
        if (name.StartsWith("expired_window_", StringComparison.Ordinal))
        {
            var maximum = DateTimeOffset.Parse("2026-11-05T00:00:00Z"); // Monday expiry -> Wednesday 18.
            db.Clock.Now = maximum.AddSeconds(name.EndsWith("before_end", StringComparison.Ordinal) ? -1 :
                name.EndsWith("after_end", StringComparison.Ordinal) ? 1 : 0);
            var scene = await db.Seed(expires: DateTimeOffset.Parse("2026-11-02T15:00:00Z"));
            await using var host = db.Host(); using var client = host.Client();
            using var login = await client.PostAsJsonAsync("/api/auth/customer-token", new { token = scene.Token });
            Assert.Equal(name.EndsWith("before_end", StringComparison.Ordinal) ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, login.StatusCode);
            return;
        }
        switch (name)
        {
            case "schedule_inheritance_history": await Inheritance(db); break;
            case "restricted_expired_link": await Restricted(db); break;
            case "revoked_link": case "unknown_link": await UnavailableLink(db, name); break;
            case "date_crossed_before_confirm":
            case "revoked_after_load":
            case "expiry_after_load":
            case "profile_changed_after_load": await ContextChanged(db, name); break;
            case "review_accept": case "review_reject": case "review_adjust": await Review(db, name); break;
            case "lost_capture_response_replay": await Replay(db); break;
            case "capture_concurrent_hosts": await CaptureConcurrency(db); break;
            case "customer_schedule_concurrent_hosts": case "route_schedule_concurrent_hosts": await ScheduleConcurrency(db, name); break;
            case "review_concurrent_hosts": await ReviewConcurrency(db); break;
            case "capture_atomic_rollback": case "schedule_atomic_rollback": await Rollback(db, name); break;
            case "actor_isolation": await Isolation(db); break;
            default: throw new InvalidOperationException("c2_case_missing");
        }
    }

    public static string CustomerPath(C2Scene scene) => $"/api/customer-orders/{scene.CustomerId}";
    public static async Task<T> Read<T>(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<T>(path, C2Json.Options))!;
    public static async Task<T> Post<T>(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request, C2Json.Options);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(C2Json.Options))!;
    }
    public static async Task<CustomerNextOrderResponse> Next(HttpClient client, C2Scene scene) =>
        await Read<CustomerNextOrderResponse>(client, CustomerPath(scene) + "/next");
    public static SubmitCustomerOrderCaptureRequest Request(C2Scene scene, CustomerNextOrderResponse next, decimal quantity = 50) =>
        new(Guid.NewGuid(), next.ProfileVersion, next.DeliveryDate, Lines: [new(scene.ProductId, quantity, "kg")]);
    public static Task<SubmitCustomerOrderCaptureResponse> Capture(HttpClient client, C2Scene scene, SubmitCustomerOrderCaptureRequest request) =>
        Post<SubmitCustomerOrderCaptureResponse>(client, CustomerPath(scene) + "/capture", request);
    private static async Task Put(HttpClient client, string path, object request)
    {
        using var response = await client.PutAsJsonAsync(path, request, C2Json.Options);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    private static string Schedule(C2Scene scene) => $"/api/admin/ordering-schedules/customers/{scene.CustomerId}";
    private static object Route(C2Scene scene, TimeOnly? cutoff, int version) => new { scene.RouteCode, cutoff, expectedVersion = version };

    private static async Task CaptureAtClock(C2Database db, DateOnly expected, bool pending, string source = "default", int hour = 10)
    {
        var scene = await db.Seed(); await using var host = db.Host();
        using var admin = await host.Admin();
        if (source != "default") await Put(admin, "/api/admin/ordering-schedules/routes", Route(scene, new(11, 0), 0));
        if (source == "customer") await Put(admin, Schedule(scene), new ScheduleUpdate(new(hour, 0), 0));
        using var client = await host.Customer(scene);
        var next = await Next(client, scene);
        Assert.Equal(expected, next.DeliveryDate); Assert.Equal(pending, next.OrderingWindow!.RequiresConfirmation);
        Assert.Equal(source, next.OrderingWindow.CutoffSource);
        var result = await Capture(client, scene, Request(scene, next));
        Assert.Equal(pending ? OrderStatus.PendingAdminReview : OrderStatus.Submitted, result.Order.Status);
        await using var check = db.Context();
        var saved = await check.Orders.Include(x => x.Lines).SingleAsync(x => x.CustomerId == scene.CustomerId);
        Assert.Equal(expected, saved.OrderDate); Assert.Equal(db.Clock.Now, saved.SubmittedAt);
        Assert.Equal(50, Assert.Single(saved.Lines).Quantity); Assert.Equal(pending, saved.RequiresAdminReview);
        var audit = await check.OrderAuditLogs.SingleAsync(x => x.OrderId == saved.Id && x.EventType == OrderAuditEventType.OrderSubmitted);
        Assert.Contains(expected.ToString("yyyy-MM-dd"), audit.MetadataJson!);
    }

    private static async Task Inheritance(C2Database db)
    {
        var scene = await db.Seed(); await using var host = db.Host();
        using var admin = await host.Admin(); using var client = await host.Customer(scene);
        Assert.Equal("default", (await Next(client, scene)).OrderingWindow!.CutoffSource);
        await Put(admin, "/api/admin/ordering-schedules/routes", Route(scene, new(11, 0), 0));
        Assert.Equal("route", (await Next(client, scene)).OrderingWindow!.CutoffSource);
        await Put(admin, Schedule(scene), new ScheduleUpdate(new(9, 0), 0));
        Assert.Equal("customer", (await Next(client, scene)).OrderingWindow!.CutoffSource);
        var result = await Capture(client, scene, Request(scene, await Next(client, scene)));
        await Put(admin, Schedule(scene), new ScheduleUpdate(null, 1));
        Assert.Equal("route", (await Next(client, scene)).OrderingWindow!.CutoffSource);
        await Put(admin, "/api/admin/ordering-schedules/routes", Route(scene, null, 1));
        Assert.Equal("default", (await Next(client, scene)).OrderingWindow!.CutoffSource);
        await using var check = db.Context();
        var original = await check.Orders.SingleAsync(x => x.Id == result.Order.OrderId);
        Assert.Equal(OrderStatus.PendingAdminReview, original.Status); Assert.Equal(new DateOnly(2026, 11, 4), original.OrderDate);
        Assert.Equal(1, original.Version);
        Assert.Equal(4, await check.AuditLogs.CountAsync(x => x.EntityId == scene.CustomerId.ToString() ||
            x.EventType == "RouteScheduleChanged" && x.MetadataJson!.Contains(scene.RouteCode)));
    }

    private static async Task Restricted(C2Database db)
    {
        db.Clock.Now = DateTimeOffset.Parse("2026-11-03T00:00:00Z"); // Monday at 18 -> Wednesday.
        var scene = await db.Seed(expires: DateTimeOffset.Parse("2026-11-02T15:00:00Z"));
        await using var host = db.Host(); using var client = await host.Customer(scene);
        var next = await Next(client, scene);
        Assert.True(next.OrderingWindow!.RequestOnly); Assert.True(next.OrderingWindow.RequiresDateAcceptance);
        Assert.True(next.OrderingWindow.RequiresConfirmation); Assert.Equal(new DateOnly(2026, 11, 4), next.DeliveryDate);
        foreach (var path in new[] { "/today", "/next/no-order", "/no-order" })
        {
            using var response = path == "/today" ? await client.GetAsync(CustomerPath(scene) + path) :
                await client.PostAsJsonAsync(CustomerPath(scene) + path, new MarkNextCustomerNoOrderRequest(Guid.NewGuid(), next.DeliveryDate));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var preview = await client.PostAsJsonAsync(CustomerPath(scene) + "/preview", new PreviewCustomerOrderCaptureRequest("50", next.DeliveryDate));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var products = await client.GetAsync(CustomerPath(scene) + "/additional-products");
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
        var saved = await Capture(client, scene, Request(scene, next));
        Assert.Equal(OrderStatus.PendingAdminReview, saved.Order.Status);
        await using var check = db.Context();
        Assert.True((await check.Orders.SingleAsync(x => x.Id == saved.Order.OrderId)).RequiresAdminReview);
    }

    private static async Task UnavailableLink(C2Database db, string name)
    {
        var scene = await db.Seed();
        if (name == "revoked_link")
        {
            await using var setup = db.Context(true);
            (await setup.CustomerAccessTokens.SingleAsync(x => x.Id == scene.TokenId)).Revoke(); await setup.SaveChangesAsync();
        }
        await using var host = db.Host(); using var client = host.Client();
        using var result = await client.PostAsJsonAsync("/api/auth/customer-token", new { token = name == "unknown_link" ? "c2-unknown" : scene.Token });
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    private static async Task ContextChanged(C2Database db, string name)
    {
        if (name == "date_crossed_before_confirm") db.Clock.Now = DateTimeOffset.Parse("2026-11-02T23:59:59Z");
        var scene = await db.Seed(); await using var host = db.Host(); using var client = await host.Customer(scene);
        var next = await Next(client, scene); var request = Request(scene, next);
        if (name == "date_crossed_before_confirm") db.Clock.Now = db.Clock.Now.AddSeconds(1);
        else
        {
            await using var setup = db.Context(true);
            if (name == "revoked_after_load") (await setup.CustomerAccessTokens.SingleAsync(x => x.Id == scene.TokenId)).Revoke();
            if (name == "expiry_after_load") (await setup.CustomerAccessTokens.SingleAsync(x => x.Id == scene.TokenId)).ExpiresAt = db.Clock.Now;
            if (name == "profile_changed_after_load") (await setup.Customers.SingleAsync(x => x.Id == scene.CustomerId)).RecordOrderingProfileChange(true, db.Clock.Now);
            await setup.SaveChangesAsync();
        }
        using var response = await client.PostAsJsonAsync(CustomerPath(scene) + "/capture", request);
        if (name == "expiry_after_load")
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<SubmitCustomerOrderCaptureResponse>(C2Json.Options))!;
            Assert.Equal(OrderStatus.PendingAdminReview, result.Order.Status); return;
        }
        Assert.Equal(name == "revoked_after_load" ? HttpStatusCode.Unauthorized : HttpStatusCode.Conflict, response.StatusCode);
        await using (var check = db.Context()) Assert.False(await check.Orders.AnyAsync(x => x.CustomerId == scene.CustomerId));
        if (name == "date_crossed_before_confirm")
        {
            Assert.Contains("DeliveryDateChanged", await response.Content.ReadAsStringAsync());
            var current = await Next(client, scene); Assert.Equal(new DateOnly(2026, 11, 4), current.DeliveryDate);
            var result = await Capture(client, scene, request with { ExpectedDeliveryDate = current.DeliveryDate });
            Assert.Equal(current.DeliveryDate, result.Order.OrderDate);
        }
    }

    private static async Task Review(C2Database db, string name)
    {
        var scene = await db.Seed(); await using var host = db.Host();
        using var client = await host.Customer(scene); using var admin = await host.Admin();
        var saved = await Capture(client, scene, Request(scene, await Next(client, scene)));
        var id = saved.Order.OrderId;
        Assert.Equal(OrderStatus.PendingAdminReview, saved.Order.Status);
        var dashboard = await Read<DeliveryDashboardResponse>(admin, "/api/admin/delivery?date=2026-11-04");
        Assert.DoesNotContain(dashboard.Orders, x => x.OrderId == id);
        var plan = await Read<OperationsPlanResponse>(admin, "/api/admin/operations/plan?date=2026-11-04");
        Assert.DoesNotContain(plan.Orders, x => x.OrderId == id);
        using var invalid = await admin.PostAsJsonAsync($"/api/admin/orders/{id}/review", new { decision = "Pending", expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var decision = name == "review_reject" ? AdminDecision.Rejected : name == "review_adjust" ? AdminDecision.AcceptedWithChanges : AdminDecision.Accepted;
        Guid lineId;
        await using (var check = db.Context()) lineId = (await check.OrderLines.SingleAsync(x => x.OrderId == id)).Id;
        var request = new ReviewOrderRequest(decision, "Decisión C2", LineAdjustments: name == "review_adjust" ? [new(lineId, 45, "Ajuste C2")] : null, ExpectedVersion: 1);
        await Post<AdminOrderSummaryResponse>(admin, $"/api/admin/orders/{id}/review", request);
        // Review has version-based conflict, not a durable operation UUID contract.
        using var repeat = await admin.PostAsJsonAsync($"/api/admin/orders/{id}/review", request, C2Json.Options);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        await using var read = db.Context();
        var persisted = await read.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == id);
        Assert.Equal(new DateOnly(2026, 11, 4), persisted.OrderDate);
        Assert.Equal(decision == AdminDecision.Rejected ? OrderStatus.Rejected : OrderStatus.Accepted, persisted.Status);
        Assert.Equal(name == "review_adjust" ? 45 : 50, Assert.Single(persisted.Lines).Quantity);
        Assert.Equal(1, await read.OrderAuditLogs.CountAsync(x => x.OrderId == id && x.EventType == OrderAuditEventType.AdminDecisionRecorded));
        Assert.Equal(name == "review_adjust" ? 1 : 0, await read.OrderAuditLogs.CountAsync(x => x.OrderId == id && x.EventType == OrderAuditEventType.AdminOrderChanged));
        dashboard = await Read<DeliveryDashboardResponse>(admin, "/api/admin/delivery?date=2026-11-04");
        Assert.Equal(decision != AdminDecision.Rejected, dashboard.Orders.Any(x => x.OrderId == id));
        plan = await Read<OperationsPlanResponse>(admin, "/api/admin/operations/plan?date=2026-11-04");
        Assert.Equal(decision != AdminDecision.Rejected, plan.Orders.Any(x => x.OrderId == id));
    }

    private static async Task Replay(C2Database db)
    {
        var scene = await db.Seed(); SubmitCustomerOrderCaptureRequest request;
        await using (var first = db.Host()) using (var client = await first.Customer(scene))
        {
            request = Request(scene, await Next(client, scene));
            // Discard the body after the real server committed. The next host cannot
            // obtain an ID from client state and must resolve the durable receipt.
            using var lost = await client.PostAsJsonAsync(CustomerPath(scene) + "/capture", request);
            Assert.Equal(HttpStatusCode.OK, lost.StatusCode);
        }
        db.Clock.Now = DateTimeOffset.Parse("2026-11-05T00:00:00Z");
        await using var second = db.Host(); using var retry = await second.Customer(scene);
        var repeated = await Capture(retry, scene, request); Assert.True(repeated.Replayed);
        using var different = await retry.PostAsJsonAsync(CustomerPath(scene) + "/capture", request with { DeliveryNotes = "Diferente" });
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        await CheckSingle(db, scene, repeated.Order.OrderId);
    }

    private static async Task CaptureConcurrency(C2Database db)
    {
        var scene = await db.Seed(); await using var a = db.Host(); await using var b = db.Host();
        using var first = await a.Customer(scene); using var second = await b.Customer(scene);
        var next = await Next(first, scene);
        await Race(() => first.PostAsJsonAsync(CustomerPath(scene) + "/capture", Request(scene, next)),
            () => second.PostAsJsonAsync(CustomerPath(scene) + "/capture", Request(scene, next)));
        await CheckSingle(db, scene);
    }

    private static async Task ScheduleConcurrency(C2Database db, string name)
    {
        var scene = await db.Seed(); await using var a = db.Host(); await using var b = db.Host();
        using var first = await a.Admin(); using var second = await b.Admin();
        var route = name.StartsWith("route_", StringComparison.Ordinal);
        if (route) await Put(first, "/api/admin/ordering-schedules/routes", Route(scene, new(10, 0), 0));
        var path = route ? "/api/admin/ordering-schedules/routes" : Schedule(scene);
        object Update(int hour) => route ? Route(scene, new(hour, 0), 1) : new ScheduleUpdate(new(hour, 0), 0);
        await Race(() => first.PutAsJsonAsync(path, Update(8)), () => second.PutAsJsonAsync(path, Update(9)));
        await using var read = db.Context();
        if (route) Assert.Equal(2, (await read.RouteOrderSchedules.SingleAsync(x => x.RouteCode == scene.RouteCode)).Version);
        else Assert.Equal(1, (await read.Customers.SingleAsync(x => x.Id == scene.CustomerId)).OrderingScheduleVersion);
        Assert.Equal(route ? 2 : 1, await read.AuditLogs.CountAsync(x => route ?
            x.EventType == "RouteScheduleChanged" && x.MetadataJson!.Contains(scene.RouteCode) :
            x.EntityId == scene.CustomerId.ToString() && x.EventType == "CustomerScheduleChanged"));
    }

    private static async Task ReviewConcurrency(C2Database db)
    {
        var scene = await db.Seed(); await using var a = db.Host(); await using var b = db.Host();
        using var customer = await a.Customer(scene); using var first = await a.Admin(); using var second = await b.Admin();
        var saved = await Capture(customer, scene, Request(scene, await Next(customer, scene)));
        var path = $"/api/admin/orders/{saved.Order.OrderId}/review";
        await Race(() => first.PostAsJsonAsync(path, new ReviewOrderRequest(AdminDecision.Accepted, null, ExpectedVersion: 1), C2Json.Options),
            () => second.PostAsJsonAsync(path, new ReviewOrderRequest(AdminDecision.Rejected, null, ExpectedVersion: 1), C2Json.Options));
        await using var check = db.Context();
        Assert.Equal(1, await check.OrderAuditLogs.CountAsync(x => x.OrderId == saved.Order.OrderId && x.EventType == OrderAuditEventType.AdminDecisionRecorded));
    }

    private static async Task Race(Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Go(Func<Task<HttpResponseMessage>> action) { await start.Task; return await action(); }
        var a = Go(first); var b = Go(second); start.SetResult();
        var responses = await Task.WhenAll(a, b);
        try { Assert.Single(responses, x => x.IsSuccessStatusCode); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    private static async Task CheckSingle(C2Database db, C2Scene scene, Guid? expected = null)
    {
        await using var check = db.Context();
        var order = await check.Orders.Include(x => x.Lines).SingleAsync(x => x.CustomerId == scene.CustomerId);
        if (expected.HasValue) Assert.Equal(expected.Value, order.Id);
        Assert.Equal(50, Assert.Single(order.Lines).Quantity);
        Assert.Single(await check.OrderOperations.Where(x => x.OperationKey.StartsWith($"CUSTOMER-CAPTURE:{scene.CustomerId:N}:")).ToArrayAsync());
        Assert.Equal(1, await check.OrderAuditLogs.CountAsync(x => x.OrderId == order.Id && x.EventType == OrderAuditEventType.OrderSubmitted));
    }

    private static async Task Rollback(C2Database db, string name)
    {
        var scene = await db.Seed(); await using var host = db.Host();
        using var client = await host.Customer(scene); using var admin = await host.Admin();
        var capture = name == "capture_atomic_rollback";
        var request = Request(scene, await Next(client, scene));
        var table = capture ? "OrderOperations" : "AuditLogs";
        var predicate = capture ? $"OperationKey <> N'CUSTOMER-CAPTURE:{scene.CustomerId:N}:{request.OperationId:N}'" :
            $"EntityId <> N'{scene.CustomerId}'";
        await db.SetupSql($"ALTER TABLE dbo.[{table}] ADD CONSTRAINT CK_C2_DeliberateFailure CHECK ({predicate})");
        try
        {
            using var failed = capture ? await client.PostAsJsonAsync(CustomerPath(scene) + "/capture", request) :
                await admin.PutAsJsonAsync(Schedule(scene), new ScheduleUpdate(new(8, 0), 0));
            Assert.False(failed.IsSuccessStatusCode);
            Assert.Contains(547, host.SqlFaults.Numbers); // The deliberate CHECK, not an unrelated API failure.
            await using var check = db.Context();
            Assert.False(await check.Orders.AnyAsync(x => x.CustomerId == scene.CustomerId));
            Assert.False(await check.OrderOperations.AnyAsync(x => x.OperationKey.Contains(scene.CustomerId.ToString("N"))));
            Assert.False(await check.OrderAuditLogs.AnyAsync(x => x.CustomerId == scene.CustomerId));
            Assert.False(await check.AuditLogs.AnyAsync(x => x.EntityId == scene.CustomerId.ToString()));
            Assert.Equal(0, (await check.Customers.SingleAsync(x => x.Id == scene.CustomerId)).OrderingScheduleVersion);
        }
        finally { await db.SetupSql($"ALTER TABLE dbo.[{table}] DROP CONSTRAINT CK_C2_DeliberateFailure"); }
        if (capture) { await Capture(client, scene, request); await CheckSingle(db, scene); }
        else await Put(admin, Schedule(scene), new ScheduleUpdate(new(8, 0), 0));
    }

    private static async Task Isolation(C2Database db)
    {
        var scene = await db.Seed(); var other = await db.Seed(); await using var host = db.Host();
        using var client = await host.Customer(scene); using var denied = await host.Admin(scene.DeniedName);
        using var driver = await host.Admin(scene.DriverName);
        using var alien = await client.GetAsync(CustomerPath(other) + "/next"); Assert.Equal(HttpStatusCode.Forbidden, alien.StatusCode);
        using var alienWrite = await client.PostAsJsonAsync(CustomerPath(other) + "/capture", Request(other, await Next(client, scene)));
        Assert.Equal(HttpStatusCode.Forbidden, alienWrite.StatusCode);
        foreach (var actor in new[] { client, denied, driver })
        {
            using var read = await actor.GetAsync("/api/admin/ordering-schedules"); Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
            using var write = await actor.PutAsJsonAsync(Schedule(scene), new ScheduleUpdate(new(8, 0), 0));
            Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        }
        var foreign = await C2Browser.Prepare(db, host);
        using var foreignRead = await driver.GetAsync($"/api/delivery/trips/{foreign.TripId}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignRead.StatusCode);
        using var foreignWrite = await driver.PostAsJsonAsync($"/api/delivery/assignments/{foreign.AssignmentId}/count",
            new RecordDeliveryStageRequest(Guid.NewGuid(), 1, 2, 25));
        Assert.Equal(HttpStatusCode.Forbidden, foreignWrite.StatusCode);
        await using var check = db.Context();
        Assert.Null((await check.DeliveryAssignments.SingleAsync(x => x.Id == foreign.AssignmentId)).CountedQuantity);
        Assert.Equal(0, (await check.Customers.SingleAsync(x => x.Id == scene.CustomerId)).OrderingScheduleVersion);
        Assert.False(await check.Orders.AnyAsync(x => x.CustomerId == other.CustomerId));
    }
}
