using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Domain.Services;

namespace Prodimt.Pedidos.Application.CustomerOrders;

public sealed class CustomerOrderService(
    ICustomerRepository customers,
    IProductRepository products,
    ISalesChannelRepository salesChannels,
    IOrderRepository orders,
    IOrderAuditLogRepository auditLogs,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<CustomerOrderTodayResponse> GetTodayAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await GetRequiredCustomerAsync(customerId, cancellationToken);
        var frequentProducts = await customers.GetFrequentProductsAsync(customerId, cancellationToken);
        var currentOrder = await orders.GetLatestCustomerOrderAsync(customerId, dateTimeProvider.Today, cancellationToken);
        var productLookup = (await products.GetByIdsAsync(frequentProducts.Select(x => x.ProductId), cancellationToken))
            .ToDictionary(x => x.Id);

        var suggestions = frequentProducts
            .Where(x => x.IsActive && productLookup.ContainsKey(x.ProductId))
            .OrderBy(x => x.SortOrder)
            .Select(x =>
            {
                var product = productLookup[x.ProductId];
                return new ProductSuggestionDto(
                    product.Id,
                    product.Name,
                    product.Description,
                    x.DefaultQuantity ?? 0);
            })
            .ToArray();

        return new CustomerOrderTodayResponse(
            customer.Id,
            customer.Name,
            dateTimeProvider.Today,
            customer.PreferredDeliveryTime,
            customer.PreferredDeliveryWindowStart,
            customer.PreferredDeliveryWindowEnd,
            customer.DeliveryNotes,
            currentOrder is null ? null : MapCurrentOrder(currentOrder),
            suggestions);
    }

    public async Task<CustomerOrderResponse> SubmitAsync(
        Guid customerId,
        SubmitCustomerOrderRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await GetRequiredCustomerAsync(customerId, cancellationToken);
        var channel = await salesChannels.GetRequiredByTypeAsync(SalesChannelType.Customer, cancellationToken);
        var hasExistingOrder = await orders.HasActiveCustomerOrderAsync(customerId, dateTimeProvider.Today, cancellationToken);
        var existingCount = await orders.CountCustomerOrdersAsync(customerId, dateTimeProvider.Today, cancellationToken);
        var evaluation = OrderSubmissionPolicy.Evaluate(dateTimeProvider.LocalTimeOfDay, hasExistingOrder);
        var machineAssignments = await customers.GetMachineAssignmentsAsync(customerId, cancellationToken);
        var lines = ValidateAndCreateLines(request, machineAssignments);

        var order = Order.CreateSubmitted(
            customer.Id,
            channel.Id,
            dateTimeProvider.Today,
            dateTimeProvider.Now,
            existingCount + 1,
            evaluation,
            lines,
            customer);

        await orders.AddAsync(order, cancellationToken);
        await AddSubmittedOrderAuditLogsAsync(order, hasExistingOrder, cancellationToken);
        await orders.SaveChangesAsync(cancellationToken);

        return MapCustomerResponse(order);
    }

    public async Task<CustomerOrderResponse> MarkNoOrderAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await GetRequiredCustomerAsync(customerId, cancellationToken);
        var channel = await salesChannels.GetRequiredByTypeAsync(SalesChannelType.Customer, cancellationToken);
        var latestOrder = await orders.GetLatestCustomerOrderAsync(customerId, dateTimeProvider.Today, cancellationToken);

        if (latestOrder?.Status is OrderStatus.NoOrder)
        {
            return MapCustomerResponse(latestOrder);
        }

        if (latestOrder is not null && IsActiveCustomerOrder(latestOrder.Status))
        {
            throw new CustomerOrderConflictException("Ya existe un pedido para hoy; no se puede marcar No pedir hoy.");
        }

        var existingCount = await orders.CountCustomerOrdersAsync(customerId, dateTimeProvider.Today, cancellationToken);

        var order = Order.CreateNoOrder(
            customer.Id,
            channel.Id,
            dateTimeProvider.Today,
            dateTimeProvider.Now,
            existingCount + 1,
            customer);

        await orders.AddAsync(order, cancellationToken);
        await auditLogs.AddAsync(OrderAuditLog.Create(
            order,
            OrderAuditEventType.NoOrderMarked,
            order.SubmittedAt,
            AuditActorType.Customer,
            "No pedir hoy registrado por cliente."), cancellationToken);
        await orders.SaveChangesAsync(cancellationToken);

        return MapCustomerResponse(order);
    }

    private async Task<Customer> GetRequiredCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(customerId, cancellationToken);

        if (customer is null || !customer.IsActive)
        {
            throw new InvalidOperationException("Customer was not found or is inactive.");
        }

        return customer;
    }

    private static OrderLine[] ValidateAndCreateLines(
        SubmitCustomerOrderRequest request,
        IReadOnlyList<CustomerMachineAssignment> machineAssignments)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new ArgumentException("Captura al menos una cantidad o usa No pedir hoy.", nameof(request));
        }

        if (request.Lines.Any(line => line.Quantity < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Las cantidades no pueden ser negativas.");
        }

        var lines = request.Lines
            .Where(line => line.Quantity > 0)
            .Select(line => CreateLine(line, machineAssignments))
            .ToArray();

        if (lines.Length == 0)
        {
            throw new ArgumentException("Captura al menos una cantidad o usa No pedir hoy.", nameof(request));
        }

        return lines;
    }

    private static OrderLine CreateLine(
        SubmitCustomerOrderLineRequest line,
        IReadOnlyList<CustomerMachineAssignment> machineAssignments)
    {
        var assignedMachineId = machineAssignments
            .OrderByDescending(x => x.IsDefault)
            .FirstOrDefault()
            ?.MachineId;

        return new OrderLine
        {
            Id = Guid.NewGuid(),
            ProductId = line.ProductId,
            Quantity = line.Quantity,
            AssignedMachineId = assignedMachineId,
            Notes = line.Notes
        };
    }

    private static bool IsActiveCustomerOrder(OrderStatus status)
    {
        return status is OrderStatus.Submitted or OrderStatus.PendingAdminReview or OrderStatus.Accepted;
    }

    private async Task AddSubmittedOrderAuditLogsAsync(
        Order order,
        bool hasExistingOrder,
        CancellationToken cancellationToken)
    {
        await auditLogs.AddAsync(OrderAuditLog.Create(
            order,
            OrderAuditEventType.OrderSubmitted,
            order.SubmittedAt,
            AuditActorType.Customer,
            "Pedido enviado por cliente."), cancellationToken);

        if (order.IsLate)
        {
            await auditLogs.AddAsync(OrderAuditLog.Create(
                order,
                OrderAuditEventType.OrderMarkedLate,
                order.SubmittedAt,
                AuditActorType.System,
                "Pedido tardio detectado despues de la hora limite."), cancellationToken);
        }

        if (hasExistingOrder && order.AdminReviewReason is AdminReviewReason.AdditionalOrderSameDay)
        {
            await auditLogs.AddAsync(OrderAuditLog.Create(
                order,
                OrderAuditEventType.AdditionalOrderDetected,
                order.SubmittedAt,
                AuditActorType.System,
                "Segundo pedido del mismo cliente en el dia detectado."), cancellationToken);
        }

        if (order.RequiresAdminReview)
        {
            await auditLogs.AddAsync(OrderAuditLog.Create(
                order,
                OrderAuditEventType.OrderRequiresAdminReview,
                order.SubmittedAt,
                AuditActorType.System,
                "Pedido enviado a revision administrativa."), cancellationToken);
        }
    }

    private static CustomerCurrentOrderSummaryResponse MapCurrentOrder(Order order)
    {
        return new CustomerCurrentOrderSummaryResponse(
            order.Id,
            order.Status,
            order.SequenceNumber,
            order.SubmittedAt,
            order.IsLate,
            order.RequiresAdminReview,
            order.AdminReviewReason);
    }

    private static CustomerOrderResponse MapCustomerResponse(Order order)
    {
        if (order.CustomerId is null)
        {
            throw new InvalidOperationException("Customer order must have a customer id.");
        }

        return new CustomerOrderResponse(
            order.Id,
            order.CustomerId.Value,
            order.OrderDate,
            order.Status,
            order.SequenceNumber,
            order.SubmittedAt,
            order.IsLate,
            order.RequiresAdminReview,
            order.AdminReviewReason);
    }
}
