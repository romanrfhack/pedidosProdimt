using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.AdminOperations;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed partial class CustomerOrderCaptureTests
{
    [Theory]
    [InlineData("2026-11-02T23:59:59Z", "2026-11-02")]
    [InlineData("2026-11-03T00:00:00Z", "2026-11-04")]
    [InlineData("2026-11-03T07:00:00Z", "2026-11-04")]
    [InlineData("2026-11-04T00:00:00Z", "2026-11-04")]
    [InlineData("2026-11-04T07:00:00Z", "2026-11-04")]
    [InlineData("2026-11-05T00:00:00Z", "2026-11-05")]
    public void ServiceDate_SkipsTuesday_AtAndAfter18(string now, string expected)
        => Assert.Equal(DateOnly.Parse(expected), DeliveryDatePolicy.GetDate(DateTimeOffset.Parse(now)));

    [Fact]
    public async Task Schedule_CustomerOverridesRoute_RemovalRestoresInheritance_AndStaleEditsFail()
    {
        await using var f = await Fixture.Create();
        var customers = new EfCustomerRepository(f.Db);
        var repo = new EfOrderingScheduleRepository(f.Db);
        var admin = new OrderingScheduleService(repo, customers, new EfAuditLogRepository(f.Db), f.Clock);
        var client = (await customers.GetByIdForUpdateAsync(f.Customer.Id, Ct))!;
        client.RouteCode = " ruta uno "; await customers.SaveChangesAsync(Ct);
        await admin.SetRouteAsync("Ruta Uno", new(new TimeOnly(10,0),0), new("test", "Test"), Ct);
        var policy = new OrderSchedulingService(repo);
        f.Clock.Now = DateTimeOffset.Parse("2026-10-31T15:30:00Z");
        var inherited = await policy.GetAsync(client, f.Clock.Now, false, Ct);
        Assert.Equal("route", inherited.CutoffSource); Assert.False(inherited.RequiresConfirmation);
        await admin.SetCustomerAsync(client.Id, new(new TimeOnly(9,0),0), new("test", "Test"), Ct);
        var own = await policy.GetAsync(client, f.Clock.Now, false, Ct);
        Assert.Equal("customer", own.CutoffSource); Assert.True(own.RequiresConfirmation);
        await Assert.ThrowsAsync<CatalogConflictException>(() => admin.SetCustomerAsync(client.Id, new(new TimeOnly(8,0),0), new("test", "Test"), Ct));
        await admin.SetCustomerAsync(client.Id, new(null,1), new("test", "Test"), Ct);
        Assert.Equal("route", (await policy.GetAsync(client, f.Clock.Now, false, Ct)).CutoffSource);
        await admin.SetRouteAsync("ruta uno", new(null,1), new("test", "Test"), Ct);
        Assert.Equal("default", (await policy.GetAsync(client, f.Clock.Now, false, Ct)).CutoffSource);
        await Assert.ThrowsAsync<ArgumentException>(() => admin.SetRouteAsync("ruta uno", new(new TimeOnly(18,0),2), new("test", "Test"), Ct));
        Assert.Equal(4, await f.Db.AuditLogs.CountAsync(x => x.EventType == "RouteScheduleChanged" || x.EventType == "CustomerScheduleChanged"));
    }

    [Fact]
    public async Task CutoffCrossedDuringCapture_QueuesUntilReviewed_ExcludesProduction_AndReplaysOnce()
    {
        await using var f = await Fixture.Create();
        var customer = (await new EfCustomerRepository(f.Db).GetByIdForUpdateAsync(f.Customer.Id, Ct))!;
        customer.AutomaticConfirmationCutoff = new(9,0); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        f.Clock.Now = DateTimeOffset.Parse("2026-10-31T14:59:59Z");
        var before = await f.Service.GetNextAsync(f.Customer.Id, Ct);
        Assert.False(before.OrderingWindow!.RequiresConfirmation);
        f.Clock.Now = DateTimeOffset.Parse("2026-10-31T15:00:00Z");
        var request = f.Manual();
        var receipt = await f.Service.SubmitAsync(f.Customer.Id, request, Ct);
        Assert.Equal(OrderStatus.PendingAdminReview, receipt.Order.Status);
        Assert.True((await f.Service.SubmitAsync(f.Customer.Id, request, Ct)).Replayed);
        var ops = new AdminOperationsService(new EfOrderRepository(f.Db), new EfCustomerRepository(f.Db), new EfMachineRepository(f.Db), f.Clock, new EfProductRepository(f.Db));
        Assert.Empty((await ops.GetPlanAsync(f.Date, null, Ct)).Orders);
        await Assert.ThrowsAsync<CustomerOrderConflictException>(() => f.Service.SubmitAsync(f.Customer.Id, f.Manual(), Ct));
        var detail = await f.AdminOrders.GetDetailAsync(receipt.Order.OrderId, Ct);
        await f.AdminOrders.ReviewAsync(receipt.Order.OrderId, new ReviewOrderRequest(AdminDecision.Accepted, null, ExpectedVersion: detail.Version), Ct);
        Assert.Single((await ops.GetPlanAsync(f.Date, null, Ct)).Orders);
        Assert.Single(await f.Db.Orders.ToArrayAsync());
    }

    [Fact]
    public async Task RestrictedExpiredLink_AlwaysQueues_EvenForNextBusinessDay()
    {
        await using var f = await Fixture.Create();
        f.Clock.Now = DateTimeOffset.Parse("2026-11-03T00:00:00Z"); // Monday 18:00 -> Wednesday.
        var next = await f.Service.GetNextAsync(f.Customer.Id, Ct, true);
        Assert.Equal(new DateOnly(2026,11,4), next.DeliveryDate);
        Assert.True(next.OrderingWindow!.RequiresDateAcceptance);
        Assert.True(next.OrderingWindow.RequiresConfirmation);
        var request = f.Manual();
        var receipt = await f.Service.SubmitAsync(f.Customer.Id, request, Ct, true);
        Assert.Equal(OrderStatus.PendingAdminReview, receipt.Order.Status);
    }

    [Fact]
    public async Task Notification_RejectionOffersWednesdayAfterMonday_WithoutCreatingAnotherOrder()
    {
        await using var f = await Fixture.Create();
        f.Clock.Now = DateTimeOffset.Parse("2026-11-02T17:00:00Z");
        var order = (await f.Service.SubmitAsync(f.Customer.Id, f.Manual(), Ct)).Order;
        await f.AdminOrders.ReviewAsync(order.OrderId, new ReviewOrderRequest(AdminDecision.Rejected, null), Ct);
        var service = new OrderDecisionNotificationService(new EfOrderRepository(f.Db), new EfCustomerRepository(f.Db), f.Clock);
        var message = await service.GetAsync(order.OrderId, Ct);
        Assert.Equal(new DateOnly(2026,11,4), message.ProposedDate);
        Assert.Contains("Esperamos tu respuesta", message.Message);
        Assert.Null(message.WhatsAppUrl); Assert.Single(await f.Db.Orders.ToArrayAsync());
    }
}

public sealed class RestrictedCustomerLinkTests
{
    [Fact]
    public async Task ExpiredKnownLink_IdentifiesOwnCustomer_RestrictsLegacyAndNoOrder_AndRevocationWins()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = factory.CreateClient();
        var repo = factory.Services.GetRequiredService<ICustomerAccessTokenRepository>();
        var access = (await repo.GetByIdAsync(DevelopmentSeedIds.GranTakitoAccessTokenId, CancellationToken.None))!;
        access.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await repo.SaveChangesAsync(CancellationToken.None);
        var response = await client.PostAsJsonAsync("/api/auth/customer-token", new CustomerTokenLoginRequest("demo-customer-token"));
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        var path = $"/api/customer-orders/{session.CustomerId}";
        var next = (await client.GetFromJsonAsync<CustomerNextOrderResponse>(path+"/next"))!;
        Assert.True(next.OrderingWindow!.RequestOnly); Assert.True(next.OrderingWindow.RequiresConfirmation);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path+"/today")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path+"/next/no-order", new MarkNextCustomerNoOrderRequest(Guid.NewGuid(),next.DeliveryDate))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/customer-orders/{Guid.NewGuid()}/next")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/ordering-schedules")).StatusCode);
        access.Revoke(); await repo.SaveChangesAsync(CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path+"/next")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/customer-token", new CustomerTokenLoginRequest("demo-customer-token"))).StatusCode);
    }

    [Fact]
    public void IdentificationWindow_EndsAt18OnNextWorkingDay_AndDoesNotAcceptOldTokens()
    {
        var expiry = DateTimeOffset.Parse("2026-11-02T15:00:00Z");
        var token = CustomerAccessToken.Create(Guid.NewGuid(), "hash", null, expiry, expiry.AddDays(-1));
        var end = DateTimeOffset.Parse("2026-11-05T00:00:00Z");
        Assert.Equal(end, CustomerLinkAccessPolicy.MaximumAccessAt(token));
        Assert.True(CustomerLinkAccessPolicy.CanIdentify(token, end.AddTicks(-1)));
        Assert.False(CustomerLinkAccessPolicy.CanIdentify(token, end));
        token.Revoke(); Assert.False(CustomerLinkAccessPolicy.CanIdentify(token, expiry.AddMinutes(1)));
    }
}
