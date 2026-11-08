using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.Delivery;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.C2.Tests;

public sealed record C2BrowserScene(C2Scene Portal, C2Scene Delivery, Guid TripId, Guid AssignmentId, Guid OrderId);

public static class C2Browser
{
    public static async Task<C2BrowserScene> Prepare(C2Database db, C2Api host)
    {
        var portal = await db.Seed("Cliente C2 revisión");
        var delivery = await db.Seed("Cliente C2 reparto");
        using var customer = await host.Customer(delivery); using var admin = await host.Admin();
        await C2Scenarios.Post<DeliveryCommandResponse>(admin, "/api/admin/delivery/routes",
            new SaveDeliveryRouteRequest(Guid.NewGuid(), delivery.RouteCode, "Ruta C2 sintética", delivery.DriverId));
        var saved = await C2Scenarios.Capture(customer, delivery, C2Scenarios.Request(delivery, await C2Scenarios.Next(customer, delivery)));
        await C2Scenarios.Post<AdminOrderSummaryResponse>(admin, $"/api/admin/orders/{saved.Order.OrderId}/review",
            new ReviewOrderRequest(AdminDecision.Accepted, "Preparación sintética C2", ExpectedVersion: 1));
        var board = await C2Scenarios.Read<DeliveryDashboardResponse>(admin, "/api/admin/delivery?date=2026-11-04");
        var trip = Assert.Single(board.Trips, x => string.Equals(x.RouteCode, delivery.RouteCode, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, trip.TripNumber);
        Assert.Equal(delivery.DriverId, trip.DriverUserId);
        Assert.Empty(trip.Assignments);
        Assert.Contains(trip.PlannedOrders!, x => x.OrderId == saved.Order.OrderId);
        var order = board.Orders.Single(x => x.OrderId == saved.Order.OrderId);
        var assignment = await C2Scenarios.Post<DeliveryCommandResponse>(admin, $"/api/admin/delivery/trips/{trip.Id}/assignments",
            new AssignDeliveryLineRequest(Guid.NewGuid(), order.OrderId, Assert.Single(order.Lines).OrderLineId, 25, trip.Version, order.OrderVersion));
        return new(portal, delivery, trip.Id, assignment.Id, order.OrderId);
    }

    // A new SQL connection after the browser actions; no HTTP fixture feeds this view.
    public static async Task<object> Verify(C2Database db, C2BrowserScene scene)
    {
        await using var check = db.Context();
        var portal = await check.Orders.Include(x => x.Lines).SingleAsync(x => x.CustomerId == scene.Portal.CustomerId);
        Assert.Equal(new DateOnly(2026, 11, 4), portal.OrderDate);
        Assert.Equal(OrderStatus.Accepted, portal.Status); Assert.Equal(50, Assert.Single(portal.Lines).Quantity);
        Assert.Equal(1, await check.OrderOperations.CountAsync(x => x.OperationKey.StartsWith($"CUSTOMER-CAPTURE:{scene.Portal.CustomerId:N}:")));
        Assert.Equal(1, await check.OrderAuditLogs.CountAsync(x => x.OrderId == portal.Id && x.EventType == OrderAuditEventType.OrderSubmitted));
        Assert.Equal(1, await check.OrderAuditLogs.CountAsync(x => x.OrderId == portal.Id && x.EventType == OrderAuditEventType.AdminDecisionRecorded));
        var original = await check.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == scene.OrderId);
        Assert.Equal(50, Assert.Single(original.Lines).Quantity);
        var assignment = await check.DeliveryAssignments.SingleAsync(x => x.Id == scene.AssignmentId);
        Assert.Equal(25, assignment.CountedQuantity); Assert.Equal(25, assignment.LoadedQuantity);
        Assert.Equal(20, assignment.DeliveredQuantity); Assert.Equal(0, assignment.ReturnedQuantity);
        Assert.Equal(0, assignment.TransferredOutQuantity); Assert.Equal(0, assignment.TransferredInQuantity);
        var custody = assignment.LoadedQuantity!.Value - assignment.DeliveredQuantity;
        Assert.Equal(5, custody); Assert.Equal(30, Assert.Single(original.Lines).Quantity - assignment.DeliveredQuantity);
        var events = await check.DeliveryEvents.Where(x => x.AssignmentId == scene.AssignmentId).ToArrayAsync();
        foreach (var type in new[] { "Counted", "Loaded", "Delivered" })
            Assert.Single(events, x => x.Type.ToString() == type);
        using var admin = await LoginForDashboard(db);
        var dashboard = await C2Scenarios.Read<DeliveryDashboardResponse>(admin.Client, "/api/admin/delivery?date=2026-11-04");
        var line = Assert.Single(dashboard.Orders.Single(x => x.OrderId == scene.OrderId).Lines);
        Assert.Equal(25, line.AvailableQuantity);
        Assert.Equal(50, line.RequestedQuantity); Assert.Equal(20, line.DeliveredQuantity);
        return new
        {
            status = "passed",
            original = 50,
            counted = 25,
            loaded = 25,
            delivered = 20,
            pending = 30,
            custody,
            available = line.AvailableQuantity,
            portalDate = portal.OrderDate,
            portalOrders = 1,
            portalReceipts = 1,
            deliveryEvents = events.Select(x => x.Type.ToString()).ToArray()
        };
    }

    private static async Task<DashboardClient> LoginForDashboard(C2Database db)
    {
        var host = db.Host(); return new(host, await host.Admin());
    }
    private sealed record DashboardClient(C2Api Host, HttpClient Client) : IDisposable
    {
        public void Dispose() { Client.Dispose(); Host.Dispose(); }
    }
}
