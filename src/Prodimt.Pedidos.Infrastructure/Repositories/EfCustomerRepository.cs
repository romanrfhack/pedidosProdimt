using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfCustomerRepository(PedidosDbContext dbContext) : ICustomerRepository
{
    public async Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Customers
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToArrayAsync(cancellationToken);
    }

    public Task<Customer?> GetByIdAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);
    }

    public Task<Customer?> GetByIdForUpdateAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return dbContext.Customers
            .SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);
    }

    public Task<Customer?> GetActiveByExactNameAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();

        return dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.IsActive && x.Name == normalizedName, cancellationToken);
    }

    public async Task<IReadOnlyList<Customer>> GetActiveAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Customers
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Customer>> GetByIdsAsync(
        IEnumerable<Guid> customerIds,
        CancellationToken cancellationToken)
    {
        var ids = customerIds.Distinct().ToArray();

        return await dbContext.Customers
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerFrequentProduct>> GetFrequentProductsAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        return await dbContext.CustomerFrequentProducts
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerFrequentProduct>> GetAllFrequentProductsAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        return await dbContext.CustomerFrequentProducts
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderBy(x => x.SortOrder)
            .ToArrayAsync(cancellationToken);
    }

    public async Task ReplaceFrequentProductsAsync(
        Guid customerId,
        IReadOnlyList<CustomerFrequentProduct> frequentProducts,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.CustomerFrequentProducts
            .Where(x => x.CustomerId == customerId)
            .ToArrayAsync(cancellationToken);

        dbContext.CustomerFrequentProducts.RemoveRange(existing);
        await dbContext.CustomerFrequentProducts.AddRangeAsync(frequentProducts, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerMachineAssignment>> GetMachineAssignmentsAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        return await dbContext.CustomerMachineAssignments
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId && x.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerMachineAssignment>> GetAllMachineAssignmentsAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        return await dbContext.CustomerMachineAssignments
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.MachineId)
            .ToArrayAsync(cancellationToken);
    }

    public async Task ReplaceMachineAssignmentsAsync(
        Guid customerId,
        IReadOnlyList<CustomerMachineAssignment> assignments,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.CustomerMachineAssignments
            .Where(x => x.CustomerId == customerId)
            .ToArrayAsync(cancellationToken);

        dbContext.CustomerMachineAssignments.RemoveRange(existing);
        await dbContext.CustomerMachineAssignments.AddRangeAsync(assignments, cancellationToken);
    }

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        await dbContext.Customers.AddAsync(customer, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
