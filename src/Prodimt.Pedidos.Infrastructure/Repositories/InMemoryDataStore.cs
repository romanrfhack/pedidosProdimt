using Microsoft.Extensions.Configuration;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;
using Prodimt.Pedidos.Infrastructure.Persistence.Seed;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class InMemoryDataStore
{
    public static readonly Guid ExampleCustomerId = DevelopmentSeedIds.GranTakitoCustomerId;
    public static readonly Guid ProductNineAndHalfId = DevelopmentSeedIds.ProductNineAndHalfId;
    public static readonly Guid ProductTenAndHalfId = DevelopmentSeedIds.ProductTenAndHalfId;
    public static readonly Guid ProductElevenId = DevelopmentSeedIds.ProductElevenId;
    public static readonly Guid ProductFifteenId = DevelopmentSeedIds.ProductFifteenId;
    public static readonly Guid CustomerChannelId = DevelopmentSeedIds.CustomerChannelId;
    public static readonly Guid CounterChannelId = DevelopmentSeedIds.CounterChannelId;
    public static readonly Guid AdminManualChannelId = DevelopmentSeedIds.AdminManualChannelId;

    public InMemoryDataStore(
        IConfiguration configuration,
        IPasswordHashService passwordHashService,
        ICustomerAccessTokenHasher customerAccessTokenHasher)
    {
        var authSeed = DevelopmentAuthSeedValues.FromConfiguration(configuration);
        var now = DateTimeOffset.UtcNow;

        AdminUsers.Add(new AdminUser
        {
            Id = DevelopmentSeedIds.AdminUserId,
            UserName = authSeed.AdminUserName,
            PasswordHash = passwordHashService.HashPassword(authSeed.AdminPassword),
            DisplayName = "Administrador Demo",
            IsActive = true,
            CreatedAt = now
        });

        CustomerAccessTokens.Add(new CustomerAccessToken
        {
            Id = DevelopmentSeedIds.GranTakitoAccessTokenId,
            CustomerId = DevelopmentSeedIds.GranTakitoCustomerId,
            TokenHash = customerAccessTokenHasher.HashToken(authSeed.CustomerToken),
            DisplayName = "Token demo Gran Takito",
            IsActive = true,
            CreatedAt = now
        });
    }

    public object SyncRoot { get; } = new();

    public List<Customer> Customers { get; } =
    [
        new()
        {
            Id = ExampleCustomerId,
            Name = "Gran Takito",
            PhoneNumber = "0000000001",
            IsActive = true,
            PreferredDeliveryWindowStart = new TimeOnly(12, 0),
            PreferredDeliveryWindowEnd = new TimeOnly(14, 0),
            DeliveryNotes = "Entrega en mostrador del cliente.",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        },
        new()
        {
            Id = DevelopmentSeedIds.DemoCustomer2Id,
            Name = "Cliente Demo 2",
            PhoneNumber = "0000000002",
            IsActive = true,
            PreferredDeliveryTime = new TimeOnly(13, 30),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        },
        new()
        {
            Id = DevelopmentSeedIds.DemoCustomer3Id,
            Name = "Cliente Demo 3",
            PhoneNumber = "0000000003",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }
    ];

    public List<Product> Products { get; } =
    [
        new() { Id = ProductNineAndHalfId, Name = "#9 1/2", Description = "Producto demo", IsActive = true },
        new() { Id = ProductTenAndHalfId, Name = "#10 1/2", Description = "Producto demo", IsActive = true },
        new() { Id = ProductElevenId, Name = "#11", Description = "Producto demo", IsActive = true },
        new() { Id = ProductFifteenId, Name = "#15", Description = "Producto demo", IsActive = true }
    ];

    public List<CustomerFrequentProduct> CustomerFrequentProducts { get; } =
    [
        new() { CustomerId = ExampleCustomerId, ProductId = ProductNineAndHalfId, DefaultQuantity = 20, SortOrder = 1, IsActive = true },
        new() { CustomerId = ExampleCustomerId, ProductId = ProductTenAndHalfId, DefaultQuantity = 10, SortOrder = 2, IsActive = true },
        new() { CustomerId = DevelopmentSeedIds.DemoCustomer2Id, ProductId = ProductElevenId, DefaultQuantity = 8, SortOrder = 1, IsActive = true },
        new() { CustomerId = DevelopmentSeedIds.DemoCustomer3Id, ProductId = ProductFifteenId, DefaultQuantity = 6, SortOrder = 1, IsActive = true }
    ];

    public List<Machine> Machines { get; } =
    [
        new() { Id = DevelopmentSeedIds.MachineOneId, Number = 1, Name = "Maquina 1", IsActive = true },
        new() { Id = DevelopmentSeedIds.MachineTwoId, Number = 2, Name = "Maquina 2", IsActive = true },
        new() { Id = DevelopmentSeedIds.MachineThreeId, Number = 3, Name = "Maquina 3", IsActive = true }
    ];

    public List<CustomerMachineAssignment> CustomerMachineAssignments { get; } =
    [
        new() { CustomerId = ExampleCustomerId, MachineId = DevelopmentSeedIds.MachineOneId, IsDefault = true, IsActive = true },
        new() { CustomerId = DevelopmentSeedIds.DemoCustomer2Id, MachineId = DevelopmentSeedIds.MachineTwoId, IsDefault = true, IsActive = true },
        new() { CustomerId = DevelopmentSeedIds.DemoCustomer3Id, MachineId = DevelopmentSeedIds.MachineThreeId, IsDefault = true, IsActive = true }
    ];

    public List<SalesChannel> SalesChannels { get; } =
    [
        new() { Id = CustomerChannelId, Name = "Cliente", Type = SalesChannelType.Customer, IsInternal = false },
        new() { Id = CounterChannelId, Name = "Mostrador", Type = SalesChannelType.InternalCounter, IsInternal = true },
        new() { Id = AdminManualChannelId, Name = "Captura administrativa", Type = SalesChannelType.AdminManualCapture, IsInternal = true }
    ];

    public List<Order> Orders { get; } = [];

    public List<OrderAuditLog> OrderAuditLogs { get; } = [];

    public List<AdminUser> AdminUsers { get; } = [];

    public List<CustomerAccessToken> CustomerAccessTokens { get; } = [];
}
