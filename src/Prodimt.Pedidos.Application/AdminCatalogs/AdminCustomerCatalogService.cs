using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Services;

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

        ApplyPilotDelivery(customer, request, now);
        await customers.AddAsync(customer, cancellationToken);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Customer,
            customer.Id,
            CatalogAuditEventTypes.CustomerCreated,
            now,
            actor,
            $"Cliente creado: {customer.Name}.",
            new { restDays = customer.GetRestDays() }), cancellationToken);
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
        // Validate before mutating a tracked entity (including the in-memory provider).
        ApplyPilotDelivery(new Customer(), request, now);
        var previousRestDays = customer.GetRestDays();
        customer.Update(
            request.Name,
            request.PhoneNumber,
            request.PreferredDeliveryTime,
            request.PreferredDeliveryWindowStart,
            request.PreferredDeliveryWindowEnd,
            request.DeliveryNotes,
            now);
        ApplyPilotDelivery(customer, request, now);

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
                customer.PreferredDeliveryWindowEnd,
                customer.RouteCode,
                customer.RouteStopOrder,
                customer.HasStoreKey,
                customer.PaymentTermCode,
                previousRestDays,
                restDays = customer.GetRestDays(),
                hasInternalDeliveryInstructions = customer.InternalDeliveryInstructions is not null
            }), cancellationToken);
        await customers.SaveChangesAsync(cancellationToken);

        return MapCustomer(customer);
    }

    public async Task UpdatePhoneNumberAsync(
        Guid customerId,
        UpdateCustomerPhoneRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var phoneNumber = CustomerPhoneNumber.Normalize(request.PhoneNumber);
        var customer = await GetRequiredCustomerForUpdateAsync(customerId, cancellationToken);
        if (customer.PhoneNumber == phoneNumber) return;
        customer.PhoneNumber = phoneNumber;
        customer.UpdatedAt = dateTimeProvider.Now;
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Customer, customer.Id, CatalogAuditEventTypes.CustomerPhoneUpdated,
            customer.UpdatedAt, actor, "Teléfono del cliente actualizado.",
            new { phoneNumberChanged = true }), cancellationToken);
        // Both repositories share the same scoped context; telephone and audit are one SaveChanges transaction.
        await customers.SaveChangesAsync(cancellationToken);
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
        => (await GetOrderingProfileAsync(customerId, cancellationToken)).Items;

    public async Task<AdminCustomerOrderingProfileResponse> GetOrderingProfileAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var snapshot = await customers.GetOrderingProfileSnapshotAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("El cliente no existe.");
        return new(snapshot.CustomerId, snapshot.OrderingProfileVersion, snapshot.IsOrderProfileApproved,
            snapshot.Items.Select(item => new AdminCustomerFrequentProductResponse(item.ProductId, item.ProductName,
                item.DefaultQuantity, item.SortOrder, item.IsActive, item.PreferredMachineId,
                item.InterpretationPosition, item.UnitCode, item.ProductIsActive, item.CaptureAlias)).ToArray());
    }

    public Task<IReadOnlyList<AdminCustomerFrequentProductResponse>> ReplaceFrequentProductsAsync(
        Guid customerId,
        UpdateCustomerFrequentProductsRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
        => customers.ExecuteOrderingProfileUpdateAsync(
            token => ReplaceFrequentProductsCoreAsync(customerId, request, actor, token), cancellationToken);

    private async Task<IReadOnlyList<AdminCustomerFrequentProductResponse>> ReplaceFrequentProductsCoreAsync(
        Guid customerId,
        UpdateCustomerFrequentProductsRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var customer = await GetRequiredCustomerForUpdateAsync(customerId, cancellationToken);
        if (request.ExpectedProfileVersion < 0)
            throw new ArgumentException("La version esperada del perfil no puede ser negativa.", nameof(request));
        if (request.ExpectedProfileVersion is { } expectedVersion && expectedVersion != customer.OrderingProfileVersion)
            throw new CatalogConflictException("El perfil cambio desde que lo abriste. Recarga la configuracion antes de guardar.");
        var items = request.Items ?? [];
        EnsureNoDuplicateIds(items.Select(x => x.ProductId), "No repitas productos frecuentes para el mismo cliente.");
        var previous = (await customers.GetAllFrequentProductsAsync(customerId, cancellationToken)).ToDictionary(x => x.ProductId);
        var aliases = items.ToDictionary(x => x.ProductId, x => x.CaptureAlias is null
            ? previous.GetValueOrDefault(x.ProductId)?.CaptureAlias : OrderCaptureAlias.Normalize(x.CaptureAlias));
        if (aliases.Values.Where(x => x is not null).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new ArgumentException("No repitas abreviaturas para productos del mismo cliente.", nameof(request));

        if (items.Any(x => x.DefaultQuantity < 0))
            throw new ArgumentOutOfRangeException(nameof(request), "Las cantidades default no pueden ser negativas.");
        if (items.Any(x => x.InterpretationPosition <= 0))
            throw new ArgumentException("La posicion de interpretacion debe ser positiva.", nameof(request));
        if (items.Where(x => x.InterpretationPosition.HasValue).GroupBy(x => x.InterpretationPosition).Any(x => x.Count() > 1))
            throw new ArgumentException("No repitas posiciones de interpretacion para el mismo cliente.", nameof(request));

        var productIds = items.Select(x => x.ProductId).Distinct().ToArray();
        var productLookup = (await products.GetByIdsIncludingInactiveAsync(productIds, cancellationToken)).ToDictionary(x => x.Id);
        if (productIds.Any(productId => !productLookup.ContainsKey(productId)))
            throw new ArgumentException("Uno de los productos frecuentes no existe.", nameof(request));

        var preferredMachineIds = items.Where(x => x.PreferredMachineId.HasValue).Select(x => x.PreferredMachineId!.Value).Distinct().ToArray();
        var machineLookup = (await machines.GetByIdsAsync(preferredMachineIds, cancellationToken)).ToDictionary(x => x.Id);
        if (preferredMachineIds.Any(id => !machineLookup.TryGetValue(id, out var machine) || !machine.IsActive))
            throw new ArgumentException("La maquina preferida debe existir y estar activa.", nameof(request));

        var activeItems = items.Where(x => x.IsActive).ToArray();
        if (request.ApproveForInterpretation)
        {
            if (activeItems.Any(x => x.ExpectedUnitCode is not null && x.ExpectedUnitCode != productLookup[x.ProductId].UnitCode))
                throw new CatalogConflictException("La unidad de un producto cambio desde que la revisaste. Recarga la configuracion antes de aprobar.");
            if (activeItems.Length == 0 || activeItems.Any(x => !productLookup[x.ProductId].IsActive || productLookup[x.ProductId].UnitCode is null))
                throw new ArgumentException("Para aprobar el perfil se requiere al menos un producto activo con unidad confirmada.", nameof(request));
            var positions = activeItems.Select(x => x.InterpretationPosition).OrderBy(x => x).ToArray();
            if (!positions.SequenceEqual(Enumerable.Range(1, activeItems.Length).Select(x => (int?)x)))
                throw new ArgumentException("Para aprobar el perfil, asigna posiciones consecutivas desde 1 a todos los productos activos.", nameof(request));
        }

        var replacement = items.Select((item, index) => new CustomerFrequentProduct
        {
            CustomerId = customerId,
            ProductId = item.ProductId,
            DefaultQuantity = item.DefaultQuantity,
            SortOrder = item.SortOrder > 0 ? item.SortOrder : index + 1,
            IsActive = item.IsActive,
            PreferredMachineId = item.PreferredMachineId,
            InterpretationPosition = item.InterpretationPosition,
            CaptureAlias = aliases[item.ProductId]
        }).OrderBy(x => x.SortOrder).ToArray();

        var now = dateTimeProvider.Now;
        await customers.ReplaceFrequentProductsAsync(customerId, replacement, cancellationToken);
        customer.RecordOrderingProfileChange(request.ApproveForInterpretation, now);
        await auditLogs.AddAsync(CatalogAudit.Create(
            CatalogEntityTypes.Customer,
            customerId,
            CatalogAuditEventTypes.CustomerFrequentProductsUpdated,
            now,
            actor,
            "Productos frecuentes y version del perfil actualizados.",
            new
            {
                count = replacement.Length,
                activeCount = replacement.Count(x => x.IsActive),
                productIds = replacement.Select(x => x.ProductId).ToArray(),
                captureAliases = replacement.Select(x => new { x.ProductId, x.CaptureAlias }).ToArray(),
                customer.OrderingProfileVersion,
                customer.IsOrderProfileApproved
            }), cancellationToken);
        await customers.SaveChangesAsync(cancellationToken);

        return replacement.Select(item => new AdminCustomerFrequentProductResponse(item.ProductId,
            productLookup[item.ProductId].Name, item.DefaultQuantity, item.SortOrder, item.IsActive,
            item.PreferredMachineId, item.InterpretationPosition, productLookup[item.ProductId].UnitCode,
            productLookup[item.ProductId].IsActive, item.CaptureAlias)).ToArray();
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

    private static void ApplyPilotDelivery(Customer customer, UpsertAdminCustomerRequest request, DateTimeOffset now)
    {
        customer.SetPilotDelivery(request.RouteCode, request.RouteStopOrder, request.HasStoreKey,
            request.PaymentTermCode, request.InternalDeliveryInstructions, now);
        // Older callers and existing CSV imports preserve the configured schedule.
        if (request.RestDays is not null) customer.SetRestDays(request.RestDays, now);
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
            customer.UpdatedAt,
            customer.RouteCode,
            customer.RouteStopOrder,
            customer.HasStoreKey,
            customer.PaymentTermCode,
            customer.InternalDeliveryInstructions,
            customer.OrderingProfileVersion,
            customer.IsOrderProfileApproved,
            customer.GetRestDays());
    }
}
