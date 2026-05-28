using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed class AdminCustomerCatalogService(
    ICustomerRepository customers,
    IProductRepository products,
    IMachineRepository machines,
    IAuditLogRepository auditLogs,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<IReadOnlyList<AdminCustomerResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var allCustomers = await customers.GetAllAsync(cancellationToken);
        return allCustomers.Select(MapCustomer).ToArray();
    }

    public async Task<AdminCustomerResponse> GetByIdAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await GetRequiredCustomerAsync(customerId, cancellationToken);
        return MapCustomer(customer);
    }

    public async Task<AdminCustomerResponse> CreateAsync(
        UpsertAdminCustomerRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureNoActiveNameDuplicateAsync(request.Name, excludingCustomerId: null, cancellationToken);

        var now = dateTimeProvider.Now;
        var customer = Customer.Create(
            request.Name,
            request.PhoneNumber,
            request.PreferredDeliveryTime,
            request.PreferredDeliveryWindowStart,
            request.PreferredDeliveryWindowEnd,
            request.DeliveryNotes,
            now);

        await customers.AddAsync(customer, cancellationToken);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Customer,
            customer.Id,
            CatalogAuditEventTypes.CustomerCreated,
            now,
            actor,
            $"Cliente creado: {customer.Name}."), cancellationToken);
        await customers.SaveChangesAsync(cancellationToken);

        return MapCustomer(customer);
    }

    public async Task<AdminCustomerResponse> UpdateAsync(
        Guid customerId,
        UpsertAdminCustomerRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var customer = await GetRequiredCustomerForUpdateAsync(customerId, cancellationToken);

        if (customer.IsActive)
        {
            await EnsureNoActiveNameDuplicateAsync(request.Name, customer.Id, cancellationToken);
        }

        var now = dateTimeProvider.Now;
        customer.Update(
            request.Name,
            request.PhoneNumber,
            request.PreferredDeliveryTime,
            request.PreferredDeliveryWindowStart,
            request.PreferredDeliveryWindowEnd,
            request.DeliveryNotes,
            now);

        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Customer,
            customer.Id,
            CatalogAuditEventTypes.CustomerUpdated,
            now,
            actor,
            $"Cliente actualizado: {customer.Name}.",
            new
            {
                customer.PreferredDeliveryTime,
                customer.PreferredDeliveryWindowStart,
                customer.PreferredDeliveryWindowEnd
            }), cancellationToken);
        await customers.SaveChangesAsync(cancellationToken);

        return MapCustomer(customer);
    }

    public async Task<AdminCustomerResponse> ActivateAsync(
        Guid customerId,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var customer = await GetRequiredCustomerForUpdateAsync(customerId, cancellationToken);
        await EnsureNoActiveNameDuplicateAsync(customer.Name, customer.Id, cancellationToken);

        var now = dateTimeProvider.Now;
        customer.Activate(now);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Customer,
            customer.Id,
            CatalogAuditEventTypes.CustomerActivated,
            now,
            actor,
            $"Cliente activado: {customer.Name}."), cancellationToken);
        await customers.SaveChangesAsync(cancellationToken);

        return MapCustomer(customer);
    }

    public async Task<AdminCustomerResponse> DeactivateAsync(
        Guid customerId,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var customer = await GetRequiredCustomerForUpdateAsync(customerId, cancellationToken);
        var now = dateTimeProvider.Now;

        customer.Deactivate(now);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Customer,
            customer.Id,
            CatalogAuditEventTypes.CustomerDeactivated,
            now,
            actor,
            $"Cliente desactivado: {customer.Name}."), cancellationToken);
        await customers.SaveChangesAsync(cancellationToken);

        return MapCustomer(customer);
    }

    public async Task<IReadOnlyList<AdminCustomerFrequentProductResponse>> GetFrequentProductsAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        await GetRequiredCustomerAsync(customerId, cancellationToken);

        var frequentProducts = await customers.GetAllFrequentProductsAsync(customerId, cancellationToken);
        var productLookup = (await products.GetByIdsIncludingInactiveAsync(
                frequentProducts.Select(x => x.ProductId),
                cancellationToken))
            .ToDictionary(x => x.Id);

        return frequentProducts
            .OrderBy(x => x.SortOrder)
            .Select(item =>
            {
                productLookup.TryGetValue(item.ProductId, out var product);
                return new AdminCustomerFrequentProductResponse(
                    item.ProductId,
                    product?.Name ?? "Producto no encontrado",
                    item.DefaultQuantity,
                    item.SortOrder,
                    item.IsActive);
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<AdminCustomerFrequentProductResponse>> ReplaceFrequentProductsAsync(
        Guid customerId,
        UpdateCustomerFrequentProductsRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await GetRequiredCustomerAsync(customerId, cancellationToken);

        var items = request.Items ?? [];
        EnsureNoDuplicateIds(items.Select(x => x.ProductId), "No repitas productos frecuentes para el mismo cliente.");

        if (items.Any(x => x.DefaultQuantity < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Las cantidades default no pueden ser negativas.");
        }

        var productIds = items.Select(x => x.ProductId).Distinct().ToArray();
        var productLookup = (await products.GetByIdsIncludingInactiveAsync(productIds, cancellationToken))
            .ToDictionary(x => x.Id);

        var missingProductId = productIds.FirstOrDefault(productId => !productLookup.ContainsKey(productId));
        if (missingProductId != Guid.Empty)
        {
            throw new ArgumentException($"El producto {missingProductId} no existe.", nameof(request));
        }

        var replacement = items
            .Select((item, index) => new CustomerFrequentProduct
            {
                CustomerId = customerId,
                ProductId = item.ProductId,
                DefaultQuantity = item.DefaultQuantity,
                SortOrder = item.SortOrder > 0 ? item.SortOrder : index + 1,
                IsActive = item.IsActive
            })
            .OrderBy(x => x.SortOrder)
            .ToArray();

        await customers.ReplaceFrequentProductsAsync(customerId, replacement, cancellationToken);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Customer,
            customerId,
            CatalogAuditEventTypes.CustomerFrequentProductsUpdated,
            dateTimeProvider.Now,
            actor,
            "Productos frecuentes del cliente actualizados.",
            new
            {
                count = replacement.Length,
                activeCount = replacement.Count(x => x.IsActive),
                productIds = replacement.Select(x => x.ProductId).ToArray()
            }), cancellationToken);
        await customers.SaveChangesAsync(cancellationToken);

        return await GetFrequentProductsAsync(customerId, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminCustomerMachineAssignmentResponse>> GetMachineAssignmentsAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        await GetRequiredCustomerAsync(customerId, cancellationToken);

        var assignments = await customers.GetAllMachineAssignmentsAsync(customerId, cancellationToken);
        var machineLookup = (await machines.GetByIdsAsync(assignments.Select(x => x.MachineId), cancellationToken))
            .ToDictionary(x => x.Id);

        return assignments
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.MachineId)
            .Select(item =>
            {
                machineLookup.TryGetValue(item.MachineId, out var machine);
                return new AdminCustomerMachineAssignmentResponse(
                    item.MachineId,
                    machine?.Number ?? 0,
                    machine?.Name,
                    item.IsDefault,
                    item.IsActive,
                    item.Notes);
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<AdminCustomerMachineAssignmentResponse>> ReplaceMachineAssignmentsAsync(
        Guid customerId,
        UpdateCustomerMachineAssignmentsRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await GetRequiredCustomerAsync(customerId, cancellationToken);

        var items = request.Items ?? [];
        EnsureNoDuplicateIds(items.Select(x => x.MachineId), "No repitas maquinas en las asignaciones del mismo cliente.");

        if (items.Count(x => x.IsDefault) > 1)
        {
            throw new ArgumentException("Solo puede existir una maquina default por cliente.", nameof(request));
        }

        var machineIds = items.Select(x => x.MachineId).Distinct().ToArray();
        var machineLookup = (await machines.GetByIdsAsync(machineIds, cancellationToken))
            .ToDictionary(x => x.Id);

        var missingMachineId = machineIds.FirstOrDefault(machineId => !machineLookup.ContainsKey(machineId));
        if (missingMachineId != Guid.Empty)
        {
            throw new ArgumentException($"La maquina {missingMachineId} no existe.", nameof(request));
        }

        var inactiveDefault = items.FirstOrDefault(x =>
            x.IsDefault &&
            (!machineLookup.TryGetValue(x.MachineId, out var machine) || !machine.IsActive));

        if (inactiveDefault is not null)
        {
            throw new ArgumentException("Una maquina inactiva no puede ser default.", nameof(request));
        }

        var replacement = items
            .Select(item => new CustomerMachineAssignment
            {
                CustomerId = customerId,
                MachineId = item.MachineId,
                IsDefault = item.IsDefault,
                IsActive = item.IsActive,
                Notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim()
            })
            .ToArray();

        await customers.ReplaceMachineAssignmentsAsync(customerId, replacement, cancellationToken);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Customer,
            customerId,
            CatalogAuditEventTypes.CustomerMachineAssignmentsUpdated,
            dateTimeProvider.Now,
            actor,
            "Asignaciones internas de maquina del cliente actualizadas.",
            new
            {
                count = replacement.Length,
                defaultMachineId = replacement.FirstOrDefault(x => x.IsDefault)?.MachineId,
                machineIds = replacement.Select(x => x.MachineId).ToArray()
            }), cancellationToken);
        await customers.SaveChangesAsync(cancellationToken);

        return await GetMachineAssignmentsAsync(customerId, cancellationToken);
    }

    private async Task<Customer> GetRequiredCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(customerId, cancellationToken);

        if (customer is null)
        {
            throw new InvalidOperationException("Customer was not found.");
        }

        return customer;
    }

    private async Task<Customer> GetRequiredCustomerForUpdateAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdForUpdateAsync(customerId, cancellationToken);

        if (customer is null)
        {
            throw new InvalidOperationException("Customer was not found.");
        }

        return customer;
    }

    private async Task EnsureNoActiveNameDuplicateAsync(
        string name,
        Guid? excludingCustomerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre del cliente es obligatorio.", nameof(name));
        }

        var duplicate = await customers.GetActiveByExactNameAsync(name, cancellationToken);

        if (duplicate is not null && duplicate.Id != excludingCustomerId)
        {
            throw new ArgumentException("Ya existe un cliente activo con ese nombre.", nameof(name));
        }
    }

    private static void EnsureNoDuplicateIds(IEnumerable<Guid> ids, string message)
    {
        var seen = new HashSet<Guid>();
        foreach (var id in ids)
        {
            if (!seen.Add(id))
            {
                throw new ArgumentException(message);
            }
        }
    }

    private static AdminCustomerResponse MapCustomer(Customer customer)
    {
        return new AdminCustomerResponse(
            customer.Id,
            customer.Name,
            customer.ExternalCode,
            customer.PhoneNumber,
            customer.IsActive,
            customer.PreferredDeliveryTime,
            customer.PreferredDeliveryWindowStart,
            customer.PreferredDeliveryWindowEnd,
            customer.DeliveryNotes,
            customer.CreatedAt,
            customer.UpdatedAt);
    }
}
