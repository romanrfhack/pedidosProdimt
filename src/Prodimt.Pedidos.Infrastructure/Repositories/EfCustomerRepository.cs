using System.Data;
using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Infrastructure.Persistence;

namespace Prodimt.Pedidos.Infrastructure.Repositories;

public sealed class EfCustomerRepository(PedidosDbContext dbContext) : ICustomerRepository
{
    private readonly Dictionary<Guid, IReadOnlyList<CustomerFrequentProduct>> _frequentProductReplacements = new();

    public Task<T> ExecuteOrderingProfileUpdateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        => CatalogTransaction.ExecuteAsync(dbContext, action, cancellationToken);

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

    public Task ReplaceFrequentProductsAsync(
        Guid customerId,
        IReadOnlyList<CustomerFrequentProduct> frequentProducts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Defer replacement to the shared commit. Updating rows one at a time cannot swap
        // unique interpretation positions. Delete + insert must share the profile/audit transaction.
        _frequentProductReplacements[customerId] = frequentProducts.ToArray();
        return Task.CompletedTask;
    }

    public async Task<CustomerOrderingProfileSnapshot?> GetOrderingProfileSnapshotAsync(Guid customerId, CancellationToken cancellationToken)
    {
        // Keep version/approval, membership and current product meaning in the same short read.
        // Product edits invalidate the profile in their own shared commit.
        try
        {
            await using var transaction = dbContext.Database.CurrentTransaction is null
                ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            var customer = await dbContext.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);
            if (customer is null) return null;
            var items = await (from item in dbContext.CustomerFrequentProducts.AsNoTracking()
                               where item.CustomerId == customerId
                               join product in dbContext.Products.AsNoTracking() on item.ProductId equals product.Id into matches
                               from product in matches.DefaultIfEmpty()
                               orderby item.SortOrder, item.ProductId
                               select new CustomerOrderingProfileItem(item.ProductId,
                                   product == null ? "Producto no encontrado" : product.Name,
                                   item.DefaultQuantity, item.SortOrder, item.IsActive,
                                   item.PreferredMachineId, item.InterpretationPosition,
                                   product == null ? null : product.UnitCode,
                                   product != null && product.IsActive, item.CaptureAlias)).ToArrayAsync(cancellationToken);
            var result = new CustomerOrderingProfileSnapshot(customer.Id, customer.OrderingProfileVersion,
                customer.IsOrderProfileApproved, items);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex) when (CatalogPersistenceConflicts.IsConcurrentChange(ex))
        {
            throw new CatalogConflictException("El catalogo cambio durante la lectura. Recarga la configuracion.");
        }
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

    public async Task<IReadOnlyList<Customer>> GetCustomersReferencingProductForUpdateAsync(Guid productId, CancellationToken cancellationToken)
    {
        return await dbContext.Customers.Where(customer =>
                dbContext.CustomerFrequentProducts.Any(item => item.CustomerId == customer.Id && item.ProductId == productId))
            .ToArrayAsync(cancellationToken);
    }

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        await dbContext.Customers.AddAsync(customer, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SavePendingChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (CatalogPersistenceConflicts.IsConcurrentChange(ex))
        {
            throw new CatalogConflictException("El perfil o catalogo cambio al guardar. Recarga la configuracion antes de reintentar.");
        }
    }

    private async Task SavePendingChangesAsync(CancellationToken cancellationToken)
    {
        if (_frequentProductReplacements.Count == 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        foreach (var (customerId, replacement) in _frequentProductReplacements)
        {
            foreach (var entry in dbContext.ChangeTracker.Entries<CustomerFrequentProduct>()
                         .Where(x => x.Entity.CustomerId == customerId).ToArray())
                entry.State = EntityState.Detached;

            await dbContext.CustomerFrequentProducts.Where(x => x.CustomerId == customerId)
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.CustomerFrequentProducts.AddRangeAsync(replacement, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        _frequentProductReplacements.Clear();
    }
}
