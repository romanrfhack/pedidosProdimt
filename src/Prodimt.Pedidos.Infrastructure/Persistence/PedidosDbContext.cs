using Microsoft.EntityFrameworkCore;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence;

public sealed class PedidosDbContext(DbContextOptions<PedidosDbContext> options) : DbContext(options)
{
    public DbSet<RouteOrderSchedule> RouteOrderSchedules => Set<RouteOrderSchedule>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<CustomerProductPriceBook> CustomerProductPriceBooks => Set<CustomerProductPriceBook>();
    public DbSet<CustomerProductPriceRevision> CustomerProductPriceRevisions => Set<CustomerProductPriceRevision>();
    public DbSet<ProductConsolidationOperation> ProductConsolidationOperations => Set<ProductConsolidationOperation>();

    public DbSet<CustomerFrequentProduct> CustomerFrequentProducts => Set<CustomerFrequentProduct>();
    public DbSet<CustomerProductSetupRequest> CustomerProductSetupRequests => Set<CustomerProductSetupRequest>();
    public DbSet<CustomerProductSetupOrder> CustomerProductSetupOrders => Set<CustomerProductSetupOrder>();

    public DbSet<Machine> Machines => Set<Machine>();

    public DbSet<CustomerMachineAssignment> CustomerMachineAssignments => Set<CustomerMachineAssignment>();

    public DbSet<SalesChannel> SalesChannels => Set<SalesChannel>();

    public DbSet<OrderOperation> OrderOperations => Set<OrderOperation>();

    public DbSet<OrderChangeNotice> OrderChangeNotices => Set<OrderChangeNotice>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<ProductionBatch> ProductionBatches => Set<ProductionBatch>();
    public DbSet<ProductionAllocation> ProductionAllocations => Set<ProductionAllocation>();

    public DbSet<DeliveryRoute> DeliveryRoutes => Set<DeliveryRoute>();
    public DbSet<DeliveryRouteDayDriver> DeliveryRouteDayDrivers => Set<DeliveryRouteDayDriver>();
    public DbSet<CustomerDeliveryLocation> CustomerDeliveryLocations => Set<CustomerDeliveryLocation>();
    public DbSet<DeliveryTrip> DeliveryTrips => Set<DeliveryTrip>();
    public DbSet<DeliveryAssignment> DeliveryAssignments => Set<DeliveryAssignment>();
    public DbSet<DeliveryEvent> DeliveryEvents => Set<DeliveryEvent>();

    public DbSet<CollectionCharge> CollectionCharges => Set<CollectionCharge>();
    public DbSet<CollectionChargeValuation> CollectionChargeValuations => Set<CollectionChargeValuation>();
    public DbSet<CollectionPayment> CollectionPayments => Set<CollectionPayment>();
    public DbSet<CollectionCashReceipt> CollectionCashReceipts => Set<CollectionCashReceipt>();
    public DbSet<CollectionCashReceiptLine> CollectionCashReceiptLines => Set<CollectionCashReceiptLine>();

    public DbSet<OrderAuditLog> OrderAuditLogs => Set<OrderAuditLog>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    public DbSet<SecurityRole> SecurityRoles => Set<SecurityRole>();
    public DbSet<SecurityPermission> SecurityPermissions => Set<SecurityPermission>();
    public DbSet<SecurityUserRole> SecurityUserRoles => Set<SecurityUserRole>();
    public DbSet<SecurityRolePermission> SecurityRolePermissions => Set<SecurityRolePermission>();
    public DbSet<SecurityUserPermission> SecurityUserPermissions => Set<SecurityUserPermission>();
    public DbSet<SecurityState> SecurityStates => Set<SecurityState>();

    public DbSet<CustomerAccessToken> CustomerAccessTokens => Set<CustomerAccessToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PedidosDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries().Any(x => x.Entity is CollectionCharge or CollectionChargeValuation or
                CollectionPayment or CollectionCashReceipt or CollectionCashReceiptLine &&
                x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Los movimientos de cobranza conservan su historial inmutable.");
        if (ChangeTracker.Entries<DeliveryEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Los movimientos de reparto conservan su historial inmutable.");
        var guarded = Model.FindEntityType(typeof(ProductionAllocation)) is not null &&
            (ChangeTracker.Entries<OrderLine>().Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
             ChangeTracker.Entries<Order>().Any(x => x.State is EntityState.Modified or EntityState.Deleted));
        if (!guarded) return await base.SaveChangesAsync(cancellationToken);
        if (Database.CurrentTransaction is not null)
        {
            await ValidateProductionLinksAsync(cancellationToken);
            return await base.SaveChangesAsync(cancellationToken);
        }
        // Keep the allocation read and order write together even for callers
        // outside IOrderOperationStore; an allocation cannot slip between them.
        await using var transaction = await Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        await ValidateProductionLinksAsync(cancellationToken);
        var count = await base.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return count;
    }

    private async Task ValidateProductionLinksAsync(CancellationToken cancellationToken)
    {
        await ValidateDeliveryLinksAsync(cancellationToken);
        if (Model.FindEntityType(typeof(ProductionAllocation)) is null) return;
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<OrderLine>().Where(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            var quantity = await ProductionAllocations.Where(x => x.OrderLineId == entry.Entity.Id && x.ReversedAt == null)
                .SumAsync(x => (decimal?)x.Quantity, cancellationToken) ?? 0;
            if (quantity <= 0) continue;
            var identityChanged = new[] { nameof(OrderLine.ProductId), nameof(OrderLine.UnitCode), nameof(OrderLine.AssignedMachineId), nameof(OrderLine.RequiresMachine), nameof(OrderLine.OrderId) }
                .Any(name => entry.Property(name).IsModified);
            if (entry.State == EntityState.Deleted || identityChanged || entry.Entity.Quantity < quantity)
                throw new Prodimt.Pedidos.Application.CustomerOrders.CustomerOrderConflictException(
                    "El renglón tiene kilos producidos asignados. Revierte explícitamente sus asignaciones antes de corregirlo.");
        }
        foreach (var entry in ChangeTracker.Entries<Order>().Where(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            var protectedChange = entry.State == EntityState.Deleted || entry.Property(x => x.OrderDate).IsModified ||
                (entry.Property(x => x.Status).IsModified && entry.Entity.Status is not (Prodimt.Pedidos.Domain.Enums.OrderStatus.Submitted or Prodimt.Pedidos.Domain.Enums.OrderStatus.Accepted));
            if (protectedChange && await ProductionAllocations.AnyAsync(x => x.OrderId == entry.Entity.Id && x.ReversedAt == null, cancellationToken))
                throw new Prodimt.Pedidos.Application.CustomerOrders.CustomerOrderConflictException(
                    "El pedido tiene producción asignada. Revierte explícitamente sus asignaciones antes de cambiar su fecha o cancelar.");
        }
    }
    private async Task ValidateDeliveryLinksAsync(CancellationToken cancellationToken)
    {
        if (Model.FindEntityType(typeof(DeliveryAssignment)) is null) return;
        foreach (var entry in ChangeTracker.Entries<OrderLine>().Where(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            var linked = await DeliveryAssignments.AsNoTracking().Where(x => x.OrderLineId == entry.Entity.Id).ToArrayAsync(cancellationToken);
            var commitment = linked.Sum(x => x.AssignedQuantity - x.ReleasedQuantity);
            if (commitment <= 0) continue;
            var changedIdentity = new[] { nameof(OrderLine.ProductId), nameof(OrderLine.UnitCode), nameof(OrderLine.OrderId) }
                .Any(name => entry.Property(name).IsModified);
            if (entry.State == EntityState.Deleted || changedIdentity || entry.Entity.Quantity < commitment)
                throw new Prodimt.Pedidos.Application.CustomerOrders.CustomerOrderConflictException(
                    "La partida tiene reparto reservado, cargado o entregado. Resuelve las reservas y conserva las partidas con entrega antes de corregirla.");
        }
        foreach (var entry in ChangeTracker.Entries<Order>().Where(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            var protectedChange = entry.State == EntityState.Deleted || entry.Property(x => x.OrderDate).IsModified ||
                (entry.Property(x => x.Status).IsModified && entry.Entity.Status is not (Prodimt.Pedidos.Domain.Enums.OrderStatus.Submitted or Prodimt.Pedidos.Domain.Enums.OrderStatus.Accepted));
            if (!protectedChange) continue;
            var linked = await DeliveryAssignments.AsNoTracking().Where(x => x.OrderId == entry.Entity.Id).ToArrayAsync(cancellationToken);
            if (linked.Any(x => x.AssignedQuantity > x.ReleasedQuantity))
                throw new Prodimt.Pedidos.Application.CustomerOrders.CustomerOrderConflictException(
                    "El pedido tiene reparto reservado, cargado o entregado. No se puede cancelar o cambiar su fecha sin resolverlo.");
        }
    }

}
