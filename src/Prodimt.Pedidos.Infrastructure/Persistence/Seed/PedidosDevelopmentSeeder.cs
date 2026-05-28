using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Infrastructure.Authentication;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Seed;

public static class PedidosDevelopmentSeeder
{
    private static readonly DateTimeOffset SeedTimestamp = new(2026, 5, 27, 0, 0, 0, TimeSpan.Zero);

    public static Task SeedAsync(PedidosDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var passwordHashService = new PasswordHashService();
        var customerAccessTokenHasher = new CustomerAccessTokenHasher();

        return SeedAsync(
            dbContext,
            configuration: null,
            passwordHashService,
            customerAccessTokenHasher,
            cancellationToken);
    }

    public static async Task SeedAsync(
        PedidosDbContext dbContext,
        IConfiguration? configuration,
        IPasswordHashService passwordHashService,
        ICustomerAccessTokenHasher customerAccessTokenHasher,
        CancellationToken cancellationToken = default)
    {
        await SeedCustomersAsync(dbContext, cancellationToken);
        await SeedProductsAsync(dbContext, cancellationToken);
        await SeedMachinesAsync(dbContext, cancellationToken);
        await SeedSalesChannelsAsync(dbContext, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await SeedFrequentProductsAsync(dbContext, cancellationToken);
        await SeedMachineAssignmentsAsync(dbContext, cancellationToken);
        await SeedAuthenticationAsync(
            dbContext,
            configuration,
            passwordHashService,
            customerAccessTokenHasher,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedCustomersAsync(PedidosDbContext dbContext, CancellationToken cancellationToken)
    {
        var customers = new[]
        {
            new Customer
            {
                Id = DevelopmentSeedIds.GranTakitoCustomerId,
                Name = "Gran Takito",
                PhoneNumber = "0000000001",
                IsActive = true,
                PreferredDeliveryWindowStart = new TimeOnly(12, 0),
                PreferredDeliveryWindowEnd = new TimeOnly(14, 0),
                DeliveryNotes = "Cliente demo para desarrollo.",
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp
            },
            new Customer
            {
                Id = DevelopmentSeedIds.DemoCustomer2Id,
                Name = "Cliente Demo 2",
                PhoneNumber = "0000000002",
                IsActive = true,
                PreferredDeliveryTime = new TimeOnly(13, 30),
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp
            },
            new Customer
            {
                Id = DevelopmentSeedIds.DemoCustomer3Id,
                Name = "Cliente Demo 3",
                PhoneNumber = "0000000003",
                IsActive = true,
                CreatedAt = SeedTimestamp,
                UpdatedAt = SeedTimestamp
            }
        };

        foreach (var customer in customers)
        {
            if (!await dbContext.Customers.AnyAsync(x => x.Id == customer.Id, cancellationToken))
            {
                dbContext.Customers.Add(customer);
            }
        }
    }

    private static async Task SeedProductsAsync(PedidosDbContext dbContext, CancellationToken cancellationToken)
    {
        var products = new[]
        {
            new Product { Id = DevelopmentSeedIds.ProductNineAndHalfId, Name = "#9 1/2", Description = "Producto demo", IsActive = true },
            new Product { Id = DevelopmentSeedIds.ProductTenAndHalfId, Name = "#10 1/2", Description = "Producto demo", IsActive = true },
            new Product { Id = DevelopmentSeedIds.ProductElevenId, Name = "#11", Description = "Producto demo", IsActive = true },
            new Product { Id = DevelopmentSeedIds.ProductFifteenId, Name = "#15", Description = "Producto demo", IsActive = true }
        };

        foreach (var product in products)
        {
            if (!await dbContext.Products.AnyAsync(x => x.Id == product.Id, cancellationToken))
            {
                dbContext.Products.Add(product);
            }
        }
    }

    private static async Task SeedMachinesAsync(PedidosDbContext dbContext, CancellationToken cancellationToken)
    {
        var machines = new[]
        {
            new Machine { Id = DevelopmentSeedIds.MachineOneId, Number = 1, Name = "Maquina 1", IsActive = true },
            new Machine { Id = DevelopmentSeedIds.MachineTwoId, Number = 2, Name = "Maquina 2", IsActive = true },
            new Machine { Id = DevelopmentSeedIds.MachineThreeId, Number = 3, Name = "Maquina 3", IsActive = true }
        };

        foreach (var machine in machines)
        {
            if (!await dbContext.Machines.AnyAsync(x => x.Id == machine.Id, cancellationToken))
            {
                dbContext.Machines.Add(machine);
            }
        }
    }

    private static async Task SeedSalesChannelsAsync(PedidosDbContext dbContext, CancellationToken cancellationToken)
    {
        var channels = new[]
        {
            new SalesChannel { Id = DevelopmentSeedIds.CustomerChannelId, Name = "Cliente", Type = SalesChannelType.Customer, IsInternal = false },
            new SalesChannel { Id = DevelopmentSeedIds.CounterChannelId, Name = "Mostrador", Type = SalesChannelType.InternalCounter, IsInternal = true },
            new SalesChannel { Id = DevelopmentSeedIds.AdminManualChannelId, Name = "Captura administrativa", Type = SalesChannelType.AdminManualCapture, IsInternal = true }
        };

        foreach (var channel in channels)
        {
            if (!await dbContext.SalesChannels.AnyAsync(x => x.Id == channel.Id, cancellationToken))
            {
                dbContext.SalesChannels.Add(channel);
            }
        }
    }

    private static async Task SeedFrequentProductsAsync(PedidosDbContext dbContext, CancellationToken cancellationToken)
    {
        var frequentProducts = new[]
        {
            new CustomerFrequentProduct
            {
                CustomerId = DevelopmentSeedIds.GranTakitoCustomerId,
                ProductId = DevelopmentSeedIds.ProductNineAndHalfId,
                DefaultQuantity = 20,
                SortOrder = 1,
                IsActive = true
            },
            new CustomerFrequentProduct
            {
                CustomerId = DevelopmentSeedIds.GranTakitoCustomerId,
                ProductId = DevelopmentSeedIds.ProductTenAndHalfId,
                DefaultQuantity = 10,
                SortOrder = 2,
                IsActive = true
            },
            new CustomerFrequentProduct
            {
                CustomerId = DevelopmentSeedIds.DemoCustomer2Id,
                ProductId = DevelopmentSeedIds.ProductElevenId,
                DefaultQuantity = 8,
                SortOrder = 1,
                IsActive = true
            },
            new CustomerFrequentProduct
            {
                CustomerId = DevelopmentSeedIds.DemoCustomer3Id,
                ProductId = DevelopmentSeedIds.ProductFifteenId,
                DefaultQuantity = 6,
                SortOrder = 1,
                IsActive = true
            }
        };

        foreach (var frequentProduct in frequentProducts)
        {
            var exists = await dbContext.CustomerFrequentProducts.AnyAsync(
                x => x.CustomerId == frequentProduct.CustomerId && x.ProductId == frequentProduct.ProductId,
                cancellationToken);

            if (!exists)
            {
                dbContext.CustomerFrequentProducts.Add(frequentProduct);
            }
        }
    }

    private static async Task SeedMachineAssignmentsAsync(PedidosDbContext dbContext, CancellationToken cancellationToken)
    {
        var assignments = new[]
        {
            new CustomerMachineAssignment
            {
                CustomerId = DevelopmentSeedIds.GranTakitoCustomerId,
                MachineId = DevelopmentSeedIds.MachineOneId,
                IsDefault = true,
                IsActive = true,
                Notes = "Asignacion demo interna."
            },
            new CustomerMachineAssignment
            {
                CustomerId = DevelopmentSeedIds.DemoCustomer2Id,
                MachineId = DevelopmentSeedIds.MachineTwoId,
                IsDefault = true,
                IsActive = true
            },
            new CustomerMachineAssignment
            {
                CustomerId = DevelopmentSeedIds.DemoCustomer3Id,
                MachineId = DevelopmentSeedIds.MachineThreeId,
                IsDefault = true,
                IsActive = true
            }
        };

        foreach (var assignment in assignments)
        {
            var exists = await dbContext.CustomerMachineAssignments.AnyAsync(
                x => x.CustomerId == assignment.CustomerId && x.MachineId == assignment.MachineId,
                cancellationToken);

            if (!exists)
            {
                dbContext.CustomerMachineAssignments.Add(assignment);
            }
        }
    }

    private static async Task SeedAuthenticationAsync(
        PedidosDbContext dbContext,
        IConfiguration? configuration,
        IPasswordHashService passwordHashService,
        ICustomerAccessTokenHasher customerAccessTokenHasher,
        CancellationToken cancellationToken)
    {
        var authSeed = DevelopmentAuthSeedValues.FromConfiguration(configuration);

        if (!await dbContext.AdminUsers.AnyAsync(x => x.Id == DevelopmentSeedIds.AdminUserId, cancellationToken))
        {
            dbContext.AdminUsers.Add(new AdminUser
            {
                Id = DevelopmentSeedIds.AdminUserId,
                UserName = authSeed.AdminUserName,
                PasswordHash = passwordHashService.HashPassword(authSeed.AdminPassword),
                DisplayName = "Administrador Demo",
                IsActive = true,
                CreatedAt = SeedTimestamp
            });
        }

        if (!await dbContext.CustomerAccessTokens.AnyAsync(x => x.Id == DevelopmentSeedIds.GranTakitoAccessTokenId, cancellationToken))
        {
            dbContext.CustomerAccessTokens.Add(new CustomerAccessToken
            {
                Id = DevelopmentSeedIds.GranTakitoAccessTokenId,
                CustomerId = DevelopmentSeedIds.GranTakitoCustomerId,
                TokenHash = customerAccessTokenHasher.HashToken(authSeed.CustomerToken),
                DisplayName = "Token demo Gran Takito",
                IsActive = true,
                CreatedAt = SeedTimestamp
            });
        }
    }
}
