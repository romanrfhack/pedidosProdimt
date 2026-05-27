using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryDataStore
{
    public static readonly Guid ExampleCustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid ProductNinePointFiveId = Guid.Parse("22222222-2222-2222-2222-222222222201");
    public static readonly Guid ProductTenId = Guid.Parse("22222222-2222-2222-2222-222222222202");
    public static readonly Guid ProductFlautaId = Guid.Parse("22222222-2222-2222-2222-222222222203");
    public static readonly Guid CustomerChannelId = Guid.Parse("33333333-3333-3333-3333-333333333301");
    public static readonly Guid CounterChannelId = Guid.Parse("33333333-3333-3333-3333-333333333302");
    public static readonly Guid AdminManualChannelId = Guid.Parse("33333333-3333-3333-3333-333333333303");

    public object SyncRoot { get; } = new();

    public List<Customer> Customers { get; } =
    [
        new()
        {
            Id = ExampleCustomerId,
            Name = "Cliente de ejemplo",
            PhoneNumber = "0000000000",
            IsActive = true,
            PreferredDeliveryWindowStart = new TimeOnly(12, 0),
            PreferredDeliveryWindowEnd = new TimeOnly(14, 0),
            DeliveryNotes = "Entrega en mostrador del cliente.",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }
    ];

    public List<Product> Products { get; } =
    [
        new() { Id = ProductNinePointFiveId, Name = "#9.5", Description = "Molde frecuente", IsActive = true },
        new() { Id = ProductTenId, Name = "#10", Description = "Molde frecuente", IsActive = true },
        new() { Id = ProductFlautaId, Name = "Flauta", Description = "Producto frecuente", IsActive = true }
    ];

    public List<CustomerFrequentProduct> CustomerFrequentProducts { get; } =
    [
        new() { CustomerId = ExampleCustomerId, ProductId = ProductNinePointFiveId, DefaultQuantity = 12, SortOrder = 1, IsActive = true },
        new() { CustomerId = ExampleCustomerId, ProductId = ProductTenId, DefaultQuantity = 8, SortOrder = 2, IsActive = true },
        new() { CustomerId = ExampleCustomerId, ProductId = ProductFlautaId, DefaultQuantity = 6, SortOrder = 3, IsActive = true }
    ];

    public List<SalesChannel> SalesChannels { get; } =
    [
        new() { Id = CustomerChannelId, Name = "Cliente", Type = SalesChannelType.Customer, IsInternal = false },
        new() { Id = CounterChannelId, Name = "Mostrador", Type = SalesChannelType.InternalCounter, IsInternal = true },
        new() { Id = AdminManualChannelId, Name = "Captura administrativa", Type = SalesChannelType.AdminManualCapture, IsInternal = true }
    ];

    public List<Order> Orders { get; } = [];
}
