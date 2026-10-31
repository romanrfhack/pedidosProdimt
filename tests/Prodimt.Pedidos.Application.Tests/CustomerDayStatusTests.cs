using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.Auth;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Domain.Services;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Repositories;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class CustomerDayStatusTests
{
    private const string DayUrl = "/api/admin/customers/day-status";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    private static readonly Clock Time = new();

    [Fact]
    public async Task DailyProjection_RequiresAdminAndRejectsCustomer()
    {
        await using var factory = new AuthApiAuthorizationTests.AuthApiFactory();
        using var client = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(DayUrl)).StatusCode);
        var response = await client.PostAsJsonAsync("/api/auth/customer-token", new { token = DevelopmentAuthSeedValues.DefaultCustomerToken });
        var customer = await response.Content.ReadFromJsonAsync<CustomerTokenLoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer!.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(DayUrl)).StatusCode);
    }

    [Theory]
    [InlineData(OrderStatus.Submitted, CustomerDayStatus.HasOrder, false)]
    [InlineData(OrderStatus.Accepted, CustomerDayStatus.HasOrder, false)]
    [InlineData(OrderStatus.PendingAdminReview, CustomerDayStatus.HasOrder, true)]
    [InlineData(OrderStatus.NoOrder, CustomerDayStatus.NoOrderConfirmed, false)]
    [InlineData(OrderStatus.Rejected, CustomerDayStatus.AwaitingOrder, false)]
    [InlineData(OrderStatus.Cancelled, CustomerDayStatus.AwaitingOrder, false)]
    [InlineData(OrderStatus.Draft, CustomerDayStatus.AwaitingOrder, false)]
    [InlineData(OrderStatus.Superseded, CustomerDayStatus.AwaitingOrder, false)]
    public async Task DailyProjection_DistinguishesRegisteredOrdersFromNoOrderAndUnconfirmed(
        OrderStatus status, CustomerDayStatus expected, bool review)
    {
        await using var parent = new AuthApiAuthorizationTests.AuthApiFactory();
        await using var factory = WithClock(parent);
        using var client = Client(factory);
        var store = EmptyStore(factory);
        var customer = AddCustomer(store);
        store.Orders.Add(CreateOrder(customer, Time.Today, status));
        await Authenticate(client);
        var result = await client.GetFromJsonAsync<AdminCustomerDayResponse>(DayUrl, Json);
        var row = Assert.Single(result!.Customers);
        Assert.Equal(Time.Today, result.Date);
        Assert.NotEqual(DateOnly.FromDateTime(Time.Now.UtcDateTime), result.Date);
        Assert.Equal(expected, row.Status);
        Assert.Equal(review, row.RequiresReview);
        Assert.Equal(expected == CustomerDayStatus.HasOrder ? 1 : 0, row.OrderCount);
        Assert.Single(store.Orders);
        Assert.Empty(store.AuditLogs);
    }

    [Fact]
    public async Task RestSchedule_DoesNotCreateOrders_AndRealOrderOverridesRestAndNoOrder()
    {
        await using var parent = new AuthApiAuthorizationTests.AuthApiFactory();
        await using var factory = WithClock(parent);
        using var client = Client(factory);
        var store = EmptyStore(factory);
        var customer = AddCustomer(store);
        customer.SetRestDays([2, 7], Time.Now);
        await Authenticate(client);
        Assert.Equal(CustomerDayStatus.ScheduledRest, await Status(client));
        Assert.Empty(store.Orders);
        Assert.Empty(store.AuditLogs);
        store.Orders.Add(CreateOrder(customer, Time.Today, OrderStatus.NoOrder));
        Assert.Equal(CustomerDayStatus.NoOrderConfirmed, await Status(client));
        store.Orders.Add(CreateOrder(customer, Time.Today, OrderStatus.PendingAdminReview, 2));
        Assert.Equal(CustomerDayStatus.HasOrder, await Status(client));
        // An inactive customer can still have an existing order that needs attention.
        customer.Deactivate(Time.Now);
        Assert.Equal(CustomerDayStatus.HasOrder, await Status(client));
    }

    [Fact]
    public async Task OldOrdersAndOldNoOrder_DoNotBecomeTodaysConfirmation()
    {
        await using var parent = new AuthApiAuthorizationTests.AuthApiFactory();
        await using var factory = WithClock(parent);
        using var client = Client(factory);
        var store = EmptyStore(factory);
        var customer = AddCustomer(store);
        store.Orders.Add(CreateOrder(customer, Time.Today.AddDays(-1), OrderStatus.Submitted));
        await Authenticate(client);
        Assert.Equal(CustomerDayStatus.AwaitingOrder, await Status(client));
        store.Orders.Add(CreateOrder(customer, Time.Today, OrderStatus.NoOrder, 1));
        store.Orders.Add(CreateOrder(customer, Time.Today, OrderStatus.Rejected, 2));
        Assert.Equal(CustomerDayStatus.AwaitingOrder, await Status(client));
        customer.Deactivate(Time.Now);
        Assert.Equal(CustomerDayStatus.Inactive, await Status(client));
    }

    [Fact]
    public async Task RestDays_OptionalForExistingClients_InvalidChangesDoNotMutateCustomer()
    {
        await using var parent = new AuthApiAuthorizationTests.AuthApiFactory();
        await using var factory = WithClock(parent);
        using var client = Client(factory);
        var store = EmptyStore(factory);
        await Authenticate(client);
        var request = new UpsertAdminCustomerRequest("Synthetic schedule", null, null, null, null, null, RestDays: [2, 7]);
        var created = await client.PostAsJsonAsync("/api/admin/customers", request);
        created.EnsureSuccessStatusCode();
        var row = (await created.Content.ReadFromJsonAsync<AdminCustomerResponse>())!;
        Assert.Equal([2, 7], row.RestDays);
        var url = "/api/admin/customers/" + row.Id;
        (await client.PutAsJsonAsync(url, request with { Name = "Synthetic preserved", RestDays = null })).EnsureSuccessStatusCode();
        Assert.Equal([2, 7], (await client.GetFromJsonAsync<AdminCustomerResponse>(url))!.RestDays);
        var audits = store.AuditLogs.Count;
        foreach (var invalid in new[] { new[] { 0 }, new[] { 8 }, new[] { 2, 2 } })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(url,
                request with { Name = "Must not be saved", RestDays = invalid })).StatusCode);
            Assert.Equal("Synthetic preserved", store.Customers.Single().Name);
            Assert.Equal([2, 7], store.Customers.Single().GetRestDays());
        }
        Assert.Equal(audits, store.AuditLogs.Count);
        (await client.PutAsJsonAsync(url, request with { RestDays = [] })).EnsureSuccessStatusCode();
        Assert.Empty(store.Customers.Single().GetRestDays());
        Assert.Equal(CustomerDayStatus.AwaitingOrder, await Status(client));
        Assert.Contains(store.AuditLogs, audit => audit.EventType == "CustomerUpdated"
            && audit.MetadataJson!.Contains("previousRestDays", StringComparison.Ordinal));
    }

    [Fact]
    public void Schedule_UsesIsoWeekdaysIncludingSunday_AndRejectsBeforeChangingMask()
    {
        var customer = Customer.Create("Synthetic weekdays", null, null, null, null, null, Time.Now);
        customer.SetRestDays([7, 1], Time.Now);
        Assert.Equal([1, 7], customer.GetRestDays());
        Assert.True(customer.IsScheduledRestOn(new DateOnly(2026, 10, 25)));
        Assert.True(customer.IsScheduledRestOn(new DateOnly(2026, 10, 26)));
        Assert.False(customer.IsScheduledRestOn(new DateOnly(2026, 10, 27)));
        var mask = customer.RestDaysMask;
        Assert.Throws<ArgumentException>(() => customer.SetRestDays([1, 9], Time.Now));
        Assert.Equal(mask, customer.RestDaysMask);
    }

    private static WebApplicationFactory<Program> WithClock(AuthApiAuthorizationTests.AuthApiFactory parent)
        => parent.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(Time);
        }));

    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task Authenticate(HttpClient client) => client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", await AdminPasswordApiTests.LoginAsync(client, DevelopmentAuthSeedValues.DefaultAdminPassword));

    private static InMemoryDataStore EmptyStore(WebApplicationFactory<Program> factory)
    {
        var store = factory.Services.GetRequiredService<InMemoryDataStore>();
        store.Customers.Clear(); store.Orders.Clear(); store.AuditLogs.Clear();
        return store;
    }

    private static Customer AddCustomer(InMemoryDataStore store)
    {
        var customer = Customer.Create("Synthetic daily customer", null, null, null, null, null, Time.Now);
        store.Customers.Add(customer);
        return customer;
    }

    private static Order CreateOrder(Customer customer, DateOnly date, OrderStatus status, int sequence = 1)
        => status == OrderStatus.NoOrder
            ? Order.CreateNoOrder(customer.Id, Guid.NewGuid(), date, Time.Now, sequence)
            : Order.CreateSubmitted(customer.Id, Guid.NewGuid(), date, Time.Now, sequence,
                new OrderSubmissionEvaluation(false, status == OrderStatus.PendingAdminReview, null, status), []);

    private static async Task<CustomerDayStatus> Status(HttpClient client)
        => Assert.Single((await client.GetFromJsonAsync<AdminCustomerDayResponse>(DayUrl, Json))!.Customers).Status;

    private sealed class Clock : IDateTimeProvider
    {
        // Keep bearer tokens valid against the host clock while deliberately testing
        // a different, fixed business date for the projection.
        public DateTimeOffset Now => DateTimeOffset.UtcNow;
        public DateOnly Today => new(2026, 10, 27);
        public TimeOnly LocalTimeOfDay => new(18, 30);
    }
}
