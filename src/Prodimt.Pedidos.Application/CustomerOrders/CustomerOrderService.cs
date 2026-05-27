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
    IDateTimeProvider dateTimeProvider)
{
    public async Task<CustomerOrderTodayResponse> GetTodayAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await GetRequiredCustomerAsync(customerId, cancellationToken);
        var frequentProducts = await customers.GetFrequentProductsAsync(customerId, cancellationToken);
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
        var lines = request.Lines.Select(CreateLine).ToArray();

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
        await orders.SaveChangesAsync(cancellationToken);

        return MapCustomerResponse(order);
    }

    public async Task<CustomerOrderResponse> MarkNoOrderAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await GetRequiredCustomerAsync(customerId, cancellationToken);
        var channel = await salesChannels.GetRequiredByTypeAsync(SalesChannelType.Customer, cancellationToken);
        var existingCount = await orders.CountCustomerOrdersAsync(customerId, dateTimeProvider.Today, cancellationToken);

        var order = Order.CreateNoOrder(
            customer.Id,
            channel.Id,
            dateTimeProvider.Today,
            dateTimeProvider.Now,
            existingCount + 1,
            customer);

        await orders.AddAsync(order, cancellationToken);
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

    private static OrderLine CreateLine(SubmitCustomerOrderLineRequest line)
    {
        if (line.Quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Quantity cannot be negative.");
        }

        return new OrderLine
        {
            Id = Guid.NewGuid(),
            ProductId = line.ProductId,
            Quantity = line.Quantity,
            Notes = line.Notes
        };
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
            order.IsLate,
            order.RequiresAdminReview,
            order.AdminReviewReason);
    }
}
