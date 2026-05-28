using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Domain.Services;

namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed class AdminOrderService(
    IOrderRepository orders,
    IOrderAuditLogRepository auditLogs,
    ICustomerRepository customers,
    IProductRepository products,
    IMachineRepository machines,
    ISalesChannelRepository salesChannels,
    IDateTimeProvider dateTimeProvider)
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AdminOrderSummaryResponse>> GetTodayAsync(CancellationToken cancellationToken)
    {
        var todayOrders = await orders.GetByDateAsync(dateTimeProvider.Today, cancellationToken);
        return await MapSummariesAsync(todayOrders, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminOrderSummaryResponse>> GetPendingReviewAsync(CancellationToken cancellationToken)
    {
        var pendingOrders = await orders.GetPendingReviewAsync(dateTimeProvider.Today, cancellationToken);
        return await MapSummariesAsync(pendingOrders, cancellationToken);
    }

    public async Task<AdminOrderDetailResponse> GetDetailAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        return await MapDetailAsync(order, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingCustomerOrderResponse>> GetPendingCustomersAsync(
        DateOnly? orderDate,
        CancellationToken cancellationToken)
    {
        var date = orderDate ?? dateTimeProvider.Today;
        var activeCustomers = await customers.GetActiveAsync(cancellationToken);
        var respondedCustomerIds = await orders.GetCustomerIdsWithOrdersAsync(date, cancellationToken);
        var pendingCustomers = activeCustomers
            .Where(customer => !respondedCustomerIds.Contains(customer.Id))
            .OrderBy(customer => customer.Name)
            .ToArray();

        var responses = new List<PendingCustomerOrderResponse>(pendingCustomers.Length);

        foreach (var customer in pendingCustomers)
        {
            var frequentProducts = await customers.GetFrequentProductsAsync(customer.Id, cancellationToken);
            responses.Add(new PendingCustomerOrderResponse(
                customer.Id,
                customer.Name,
                customer.PhoneNumber,
                customer.PreferredDeliveryTime,
                customer.PreferredDeliveryWindowStart,
                customer.PreferredDeliveryWindowEnd,
                customer.DeliveryNotes,
                frequentProducts.Count));
        }

        return responses;
    }

    public async Task<AdminOrderTemplateResponse> GetOrderTemplateAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await GetRequiredActiveCustomerAsync(customerId, cancellationToken);
        var frequentProducts = await customers.GetFrequentProductsAsync(customerId, cancellationToken);
        var productLookup = (await products.GetByIdsAsync(frequentProducts.Select(x => x.ProductId), cancellationToken))
            .ToDictionary(x => x.Id);

        var templateProducts = frequentProducts
            .Where(x => x.IsActive && productLookup.ContainsKey(x.ProductId))
            .OrderBy(x => x.SortOrder)
            .Select(x =>
            {
                var product = productLookup[x.ProductId];
                return new AdminOrderTemplateProductResponse(
                    product.Id,
                    product.Name,
                    product.Description,
                    x.DefaultQuantity ?? 0);
            })
            .ToArray();

        return new AdminOrderTemplateResponse(
            customer.Id,
            customer.Name,
            customer.PreferredDeliveryTime,
            customer.PreferredDeliveryWindowStart,
            customer.PreferredDeliveryWindowEnd,
            customer.DeliveryNotes,
            templateProducts);
    }

    public async Task<AdminOrderSummaryResponse> SubmitCustomerOrderAsync(
        Guid customerId,
        AdminSubmitCustomerOrderRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customer = await GetRequiredActiveCustomerAsync(customerId, cancellationToken);
        var channel = await salesChannels.GetRequiredByTypeAsync(SalesChannelType.AdminManualCapture, cancellationToken);
        var hasExistingOrder = await orders.HasActiveCustomerOrderAsync(customerId, dateTimeProvider.Today, cancellationToken);
        var existingCount = await orders.CountCustomerOrdersAsync(customerId, dateTimeProvider.Today, cancellationToken);
        var evaluation = OrderSubmissionPolicy.Evaluate(dateTimeProvider.LocalTimeOfDay, hasExistingOrder);
        var machineAssignments = await customers.GetMachineAssignmentsAsync(customerId, cancellationToken);
        var lines = ValidateAndCreateAdminLines(request.Lines, machineAssignments);

        var order = Order.CreateSubmitted(
            customer.Id,
            channel.Id,
            dateTimeProvider.Today,
            dateTimeProvider.Now,
            existingCount + 1,
            evaluation,
            lines,
            customer);

        order.ApplyDeliveryChanges(
            request.RequestedDeliveryTime,
            request.RequestedDeliveryWindowStart,
            request.RequestedDeliveryWindowEnd,
            request.DeliveryNotes);

        if (request.InternalNotes is not null)
        {
            order.InternalNotes = request.InternalNotes;
        }

        await orders.AddAsync(order, cancellationToken);
        await AddAdminSubmittedAuditLogsAsync(order, hasExistingOrder, request.InternalNotes, actor, cancellationToken);
        await orders.SaveChangesAsync(cancellationToken);

        var customerNames = await GetCustomerNamesAsync([order], cancellationToken);
        return MapSummary(order, customerNames);
    }

    public async Task<AdminOrderSummaryResponse> MarkNoOrderAsync(
        Guid customerId,
        AdminMarkNoOrderRequest request,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        var customer = await GetRequiredActiveCustomerAsync(customerId, cancellationToken);
        var channel = await salesChannels.GetRequiredByTypeAsync(SalesChannelType.AdminManualCapture, cancellationToken);
        var latestOrder = await orders.GetLatestCustomerOrderAsync(customerId, dateTimeProvider.Today, cancellationToken);

        if (latestOrder?.Status is OrderStatus.NoOrder)
        {
            var existingCustomerNames = await GetCustomerNamesAsync([latestOrder], cancellationToken);
            return MapSummary(latestOrder, existingCustomerNames);
        }

        if (latestOrder is not null && IsActiveCustomerOrder(latestOrder.Status))
        {
            throw new CustomerOrderConflictException("Ya existe un pedido para hoy; no se puede marcar No pedir hoy desde administracion.");
        }

        var existingCount = await orders.CountCustomerOrdersAsync(customerId, dateTimeProvider.Today, cancellationToken);
        var order = Order.CreateNoOrder(
            customer.Id,
            channel.Id,
            dateTimeProvider.Today,
            dateTimeProvider.Now,
            existingCount + 1,
            customer);

        if (request.InternalNotes is not null)
        {
            order.InternalNotes = request.InternalNotes;
        }

        await orders.AddAsync(order, cancellationToken);
        await auditLogs.AddAsync(OrderAuditLog.Create(
            order,
            OrderAuditEventType.AdminNoOrderMarked,
            order.SubmittedAt,
            AuditActorType.Admin,
            "No pedir hoy registrado por administracion.",
            actor?.ActorId,
            actor?.ActorDisplayName,
            ToMetadataJson(new { request.InternalNotes })), cancellationToken);
        await orders.SaveChangesAsync(cancellationToken);

        var customerNames = await GetCustomerNamesAsync([order], cancellationToken);
        return MapSummary(order, customerNames);
    }

    public async Task<AdminOrderSummaryResponse> ReviewAsync(
        Guid orderId,
        ReviewOrderRequest request,
        CancellationToken cancellationToken,
        AdminActorContext? actor = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var order = await GetRequiredOrderAsync(orderId, cancellationToken);

        if (request.Decision is not (AdminDecision.Accepted or AdminDecision.Rejected or AdminDecision.AcceptedWithChanges))
        {
            throw new ArgumentException("La decision administrativa debe ser Accepted, Rejected o AcceptedWithChanges.", nameof(request));
        }

        var changes = request.Decision is AdminDecision.AcceptedWithChanges
            ? ApplyAcceptedWithChanges(order, request)
            : [];

        order.ApplyAdminDecision(request.Decision);

        if (request.InternalNotes is not null)
        {
            order.InternalNotes = request.InternalNotes;
        }

        var summary = request.Decision is AdminDecision.AcceptedWithChanges
            ? $"Decision administrativa registrada: {request.Decision}. Cambios: {FormatChangeSummary(changes)}"
            : $"Decision administrativa registrada: {request.Decision}.";

        await auditLogs.AddAsync(OrderAuditLog.Create(
            order,
            OrderAuditEventType.AdminDecisionRecorded,
            dateTimeProvider.Now,
            AuditActorType.Admin,
            summary,
            actor?.ActorId,
            actor?.ActorDisplayName,
            ToMetadataJson(new
            {
                decision = request.Decision,
                request.InternalNotes,
                changes
            })), cancellationToken);

        if (request.Decision is AdminDecision.AcceptedWithChanges && changes.Count > 0)
        {
            await auditLogs.AddAsync(OrderAuditLog.Create(
                order,
                OrderAuditEventType.AdminOrderChanged,
                dateTimeProvider.Now,
                AuditActorType.Admin,
                $"Cambios administrativos aplicados: {FormatChangeSummary(changes)}",
                actor?.ActorId,
                actor?.ActorDisplayName,
                ToMetadataJson(new { changes })), cancellationToken);
        }

        await orders.SaveChangesAsync(cancellationToken);

        var customerNames = await GetCustomerNamesAsync([order], cancellationToken);
        return MapSummary(order, customerNames);
    }

    public async Task<IReadOnlyList<OrderAuditLogResponse>> GetAuditAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(orderId, cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException("Order was not found.");
        }

        var orderAuditLogs = await auditLogs.GetByOrderIdAsync(orderId, cancellationToken);
        return orderAuditLogs.Select(MapAuditLog).ToArray();
    }

    private async Task<Order> GetRequiredOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(orderId, cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException("Order was not found.");
        }

        return order;
    }

    private async Task<Customer> GetRequiredActiveCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(customerId, cancellationToken);

        if (customer is null || !customer.IsActive)
        {
            throw new InvalidOperationException("Customer was not found or is inactive.");
        }

        return customer;
    }

    private static OrderLine[] ValidateAndCreateAdminLines(
        IReadOnlyList<AdminSubmitCustomerOrderLineRequest>? requestLines,
        IReadOnlyList<CustomerMachineAssignment> machineAssignments)
    {
        if (requestLines is null || requestLines.Count == 0)
        {
            throw new ArgumentException("Captura al menos una cantidad o marca No pedir hoy.", nameof(requestLines));
        }

        if (requestLines.Any(line => line.Quantity < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(requestLines), "Las cantidades no pueden ser negativas.");
        }

        var assignedMachineId = machineAssignments
            .OrderByDescending(x => x.IsDefault)
            .FirstOrDefault()
            ?.MachineId;

        var lines = requestLines
            .Where(line => line.Quantity > 0)
            .Select(line => new OrderLine
            {
                Id = Guid.NewGuid(),
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                AssignedMachineId = assignedMachineId,
                Notes = line.Notes
            })
            .ToArray();

        if (lines.Length == 0)
        {
            throw new ArgumentException("Captura al menos una cantidad o marca No pedir hoy.", nameof(requestLines));
        }

        return lines;
    }

    private static List<AdminOrderChangeSummary> ApplyAcceptedWithChanges(Order order, ReviewOrderRequest request)
    {
        var changes = new List<AdminOrderChangeSummary>();

        AddDeliveryChange(changes, "requestedDeliveryTime", order.RequestedDeliveryTime, request.RequestedDeliveryTime);
        AddDeliveryChange(changes, "requestedDeliveryWindowStart", order.RequestedDeliveryWindowStart, request.RequestedDeliveryWindowStart);
        AddDeliveryChange(changes, "requestedDeliveryWindowEnd", order.RequestedDeliveryWindowEnd, request.RequestedDeliveryWindowEnd);
        AddStringChange(changes, "deliveryNotes", order.DeliveryNotes, request.DeliveryNotes);

        order.ApplyDeliveryChanges(
            request.RequestedDeliveryTime,
            request.RequestedDeliveryWindowStart,
            request.RequestedDeliveryWindowEnd,
            request.DeliveryNotes);

        if (request.LineAdjustments is not null)
        {
            foreach (var adjustment in request.LineAdjustments)
            {
                if (adjustment.Quantity <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(request), "Las cantidades ajustadas deben ser mayores a cero.");
                }

                var line = order.Lines.SingleOrDefault(x => x.Id == adjustment.OrderLineId);

                if (line is null)
                {
                    throw new ArgumentException("La linea de pedido a ajustar no existe.", nameof(request));
                }

                if (line.Quantity != adjustment.Quantity)
                {
                    changes.Add(new AdminOrderChangeSummary(
                        $"line:{line.Id}:quantity",
                        line.Quantity.ToString("0.##"),
                        adjustment.Quantity.ToString("0.##")));
                    line.Quantity = adjustment.Quantity;
                }

                if (adjustment.Notes is not null && line.Notes != adjustment.Notes)
                {
                    changes.Add(new AdminOrderChangeSummary($"line:{line.Id}:notes", line.Notes, adjustment.Notes));
                    line.Notes = adjustment.Notes;
                }
            }
        }

        return changes;
    }

    private static void AddDeliveryChange(
        List<AdminOrderChangeSummary> changes,
        string field,
        TimeOnly? oldValue,
        TimeOnly? newValue)
    {
        if (newValue is not null && oldValue != newValue)
        {
            changes.Add(new AdminOrderChangeSummary(field, oldValue?.ToString("HH:mm"), newValue?.ToString("HH:mm")));
        }
    }

    private static void AddStringChange(
        List<AdminOrderChangeSummary> changes,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (newValue is not null && oldValue != newValue)
        {
            changes.Add(new AdminOrderChangeSummary(field, oldValue, newValue));
        }
    }

    private async Task AddAdminSubmittedAuditLogsAsync(
        Order order,
        bool hasExistingOrder,
        string? internalNotes,
        AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        await auditLogs.AddAsync(OrderAuditLog.Create(
            order,
            OrderAuditEventType.AdminManualOrderCaptured,
            order.SubmittedAt,
            AuditActorType.Admin,
            "Pedido capturado por administracion.",
            actor?.ActorId,
            actor?.ActorDisplayName,
            ToMetadataJson(new { internalNotes, salesChannel = SalesChannelType.AdminManualCapture.ToString() })), cancellationToken);

        if (order.IsLate)
        {
            await auditLogs.AddAsync(OrderAuditLog.Create(
                order,
                OrderAuditEventType.OrderMarkedLate,
                order.SubmittedAt,
                AuditActorType.System,
                "Pedido tardio detectado en captura administrativa despues de la hora limite.",
                metadataJson: ToMetadataJson(new { capturedBy = "Admin" })), cancellationToken);
        }

        if (hasExistingOrder && order.AdminReviewReason is AdminReviewReason.AdditionalOrderSameDay)
        {
            await auditLogs.AddAsync(OrderAuditLog.Create(
                order,
                OrderAuditEventType.AdditionalOrderDetected,
                order.SubmittedAt,
                AuditActorType.System,
                "Segundo pedido del mismo cliente en el dia detectado en captura administrativa.",
                metadataJson: ToMetadataJson(new { capturedBy = "Admin" })), cancellationToken);
        }

        if (order.RequiresAdminReview)
        {
            await auditLogs.AddAsync(OrderAuditLog.Create(
                order,
                OrderAuditEventType.OrderRequiresAdminReview,
                order.SubmittedAt,
                AuditActorType.System,
                "Pedido capturado por administracion enviado a revision administrativa.",
                metadataJson: ToMetadataJson(new { capturedBy = "Admin" })), cancellationToken);
        }
    }

    private async Task<IReadOnlyList<AdminOrderSummaryResponse>> MapSummariesAsync(
        IReadOnlyList<Order> sourceOrders,
        CancellationToken cancellationToken)
    {
        var customerNames = await GetCustomerNamesAsync(sourceOrders, cancellationToken);
        return sourceOrders.Select(order => MapSummary(order, customerNames)).ToArray();
    }

    private async Task<AdminOrderDetailResponse> MapDetailAsync(Order order, CancellationToken cancellationToken)
    {
        var customerNames = await GetCustomerNamesAsync([order], cancellationToken);
        var customerName = GetCustomerName(order, customerNames);
        var productLookup = (await products.GetByIdsAsync(order.Lines.Select(x => x.ProductId), cancellationToken))
            .ToDictionary(product => product.Id);
        var machineLookup = (await machines.GetByIdsAsync(order.Lines.Select(x => x.AssignedMachineId).OfType<Guid>(), cancellationToken))
            .ToDictionary(machine => machine.Id);
        var salesChannel = await salesChannels.GetByIdAsync(order.SalesChannelId, cancellationToken);

        var lines = order.Lines
            .OrderBy(line => line.Id)
            .Select(line =>
            {
                productLookup.TryGetValue(line.ProductId, out var product);
                var machine = line.AssignedMachineId is { } machineId && machineLookup.TryGetValue(machineId, out var foundMachine)
                    ? foundMachine
                    : null;

                return new AdminOrderLineResponse(
                    line.Id,
                    line.ProductId,
                    product?.Name ?? "Producto no encontrado",
                    line.Quantity,
                    line.Notes,
                    line.AssignedMachineId,
                    machine?.Name,
                    machine?.Number);
            })
            .ToArray();

        return new AdminOrderDetailResponse(
            order.Id,
            order.CustomerId,
            customerName,
            order.OrderDate,
            order.SubmittedAt,
            order.Status,
            order.SequenceNumber,
            order.IsLate,
            order.RequiresAdminReview,
            order.AdminReviewReason,
            order.AdminDecision,
            order.RequestedDeliveryTime,
            order.RequestedDeliveryWindowStart,
            order.RequestedDeliveryWindowEnd,
            order.DeliveryNotes,
            order.InternalNotes,
            salesChannel?.Name,
            salesChannel?.Type,
            lines);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> GetCustomerNamesAsync(
        IEnumerable<Order> sourceOrders,
        CancellationToken cancellationToken)
    {
        var customerIds = sourceOrders
            .Select(order => order.CustomerId)
            .OfType<Guid>()
            .Distinct()
            .ToArray();

        var orderCustomers = await customers.GetByIdsAsync(customerIds, cancellationToken);
        return orderCustomers.ToDictionary(customer => customer.Id, customer => customer.Name);
    }

    private static AdminOrderSummaryResponse MapSummary(Order order, IReadOnlyDictionary<Guid, string> customerNames)
    {
        return new AdminOrderSummaryResponse(
            order.Id,
            order.CustomerId,
            GetCustomerName(order, customerNames),
            order.OrderDate,
            order.SubmittedAt,
            order.Status,
            order.SequenceNumber,
            order.IsLate,
            order.RequiresAdminReview,
            order.AdminReviewReason,
            order.RequestedDeliveryTime,
            order.RequestedDeliveryWindowStart,
            order.RequestedDeliveryWindowEnd,
            order.DeliveryNotes,
            order.AdminDecision);
    }

    private static string GetCustomerName(Order order, IReadOnlyDictionary<Guid, string> customerNames)
    {
        return order.CustomerId is { } customerId && customerNames.TryGetValue(customerId, out var name)
            ? name
            : "Mostrador";
    }

    private static OrderAuditLogResponse MapAuditLog(OrderAuditLog auditLog)
    {
        return new OrderAuditLogResponse(
            auditLog.Id,
            auditLog.OrderId,
            auditLog.CustomerId,
            auditLog.EventType,
            auditLog.OccurredAt,
            auditLog.ActorType,
            auditLog.ActorId,
            auditLog.ActorDisplayName,
            auditLog.OrderStatus,
            auditLog.AdminReviewReason,
            auditLog.AdminDecision,
            auditLog.Summary,
            auditLog.MetadataJson);
    }

    private static bool IsActiveCustomerOrder(OrderStatus status)
    {
        return status is OrderStatus.Submitted or OrderStatus.PendingAdminReview or OrderStatus.Accepted;
    }

    private static string FormatChangeSummary(IReadOnlyList<AdminOrderChangeSummary> changes)
    {
        return changes.Count == 0
            ? "sin cambios detallados enviados"
            : string.Join(", ", changes.Select(change => change.Field));
    }

    private static string ToMetadataJson(object metadata)
    {
        return JsonSerializer.Serialize(metadata, MetadataJsonOptions);
    }

    private sealed record AdminOrderChangeSummary(string Field, string? OldValue, string? NewValue);
}
