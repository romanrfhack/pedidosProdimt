using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.CustomerOrders;

public sealed class CustomerOrderCaptureService(ICustomerRepository customers, IProductRepository products,
    IMachineRepository machines, ISalesChannelRepository channels, IOrderRepository orders,
    IOrderAuditLogRepository audit, IOrderOperationStore operations, IDateTimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CustomerNextOrderResponse> GetNextAsync(Guid customerId, CancellationToken token)
    {
        var (customer, profile) = await ReadProfileAsync(customerId, token);
        var now = clock.Now;
        var date = DeliveryDatePolicy.GetDate(now);
        // Include the persisted public line snapshots, never internal catalog/production context.
        var candidates = (await orders.GetByDateAsync(date, token)).Where(x => x.CustomerId == customerId).ToArray();
        var active = candidates.Where(IsActive).OrderByDescending(x => x.SequenceNumber).FirstOrDefault();
        var current = active ?? candidates.OrderByDescending(x => x.SequenceNumber).FirstOrDefault();
        return new CustomerNextOrderResponse(customer.Id, customer.Name, date, now,
            profile.OrderingProfileVersion, profile.IsOrderProfileApproved, active is not null,
            profile.Items.Where(x => x.IsActive && x.ProductIsActive).OrderBy(x => x.SortOrder)
                .Select(x => new CustomerCaptureProduct(x.ProductId, x.ProductName, x.UnitCode,
                    x.InterpretationPosition, x.CaptureAlias, x.SortOrder)).ToArray(),
            current is null ? null : new(current.Id, current.OrderDate, current.Status, current.SubmittedAt,
                current.DeliveryNotes, current.Lines.Select(x => new CustomerCaptureCurrentLine(x.ProductId,
                    x.ProductNameSnapshot ?? profile.Items.FirstOrDefault(p => p.ProductId == x.ProductId)?.ProductName ?? "Producto",
                    x.Quantity, x.UnitCode, x.Notes)).ToArray()),
            customer.PreferredDeliveryTime, customer.PreferredDeliveryWindowStart, customer.PreferredDeliveryWindowEnd);
    }

    public async Task<PreviewCustomerOrderCaptureResponse> PreviewAsync(Guid customerId,
        PreviewCustomerOrderCaptureRequest request, CancellationToken token)
    {
        var received = DeliveryDatePolicy.Capture(clock);
        ArgumentNullException.ThrowIfNull(request);
        var (customer, profile) = await ReadProfileAsync(customerId, token);
        var date = DeliveryDatePolicy.RequireExpectedDate(request.ExpectedDeliveryDate, received);
        return Preview(customer, profile, request.Text, date, received.Now);
    }

    public async Task<SubmitCustomerOrderCaptureResponse> SubmitAsync(Guid customerId,
        SubmitCustomerOrderCaptureRequest request, CancellationToken cancellationToken)
    {
        var received = DeliveryDatePolicy.Capture(clock);
        ArgumentNullException.ThrowIfNull(request);
        if (request.OperationId == Guid.Empty || request.ExpectedProfileVersion < 0 || request.ExpectedDeliveryDate == DateOnly.MinValue)
            throw new ArgumentException("La captura debe incluir su referencia, fecha de entrega y versión del perfil.");
        var quick = !string.IsNullOrWhiteSpace(request.Text);
        if (quick && (request.Lines is not null || request.Text!.Length > 240)
            || !quick && (request.Lines is null || request.Lines.Count is 0 or > 200))
            throw new ArgumentException("Elige captura rápida o cantidades manuales, sin mezclar ambas entradas.");
        if (request.DeliveryNotes?.Length > 500)
            throw new ArgumentException("Las notas de entrega admiten hasta 500 caracteres.");
        // Date validation belongs inside the action: an already committed attempt must replay
        // its original result even if the response was lost immediately before the cutoff.
        var receipt = await operations.ExecuteAsync($"CUSTOMER-CAPTURE:{customerId:N}:{request.OperationId:N}",
            Hash(new { customerId, request }), received.Now,
            token => customers.ExecuteOrderingProfileUpdateAsync(async inner =>
            {
                var (customer, profile) = await ReadProfileAsync(customerId, inner);
                var date = DeliveryDatePolicy.RequireExpectedDate(request.ExpectedDeliveryDate, received);
                if (profile.OrderingProfileVersion != request.ExpectedProfileVersion)
                    throw new CatalogConflictException("La configuración de productos cambió. Revisa el pedido antes de confirmar de nuevo.");
                IReadOnlyList<ResolvedCaptureLine> resolved;
                if (quick)
                {
                    var preview = Preview(customer, profile, request.Text!, date, received.Now);
                    if (!profile.IsOrderProfileApproved)
                        throw new CatalogConflictException("La captura rápida requiere que PRODIMT confirme la configuración. Puedes utilizar captura manual.");
                    if (preview.Fingerprint != request.PreviewFingerprint)
                        throw new CatalogConflictException("La captura o su configuración cambió. Revisa de nuevo el desglose.");
                    resolved = preview.Lines;
                }
                else resolved = ResolveManual(profile, request.Lines!);

                if (await orders.HasActiveCustomerOrderAsync(customerId, date, inner))
                    throw new CustomerOrderConflictException("Ya existe un pedido para esta fecha de entrega. Revísalo y comunícate con PRODIMT si necesitas modificarlo.");
                var lines = await CreateLinesAsync(customerId, resolved.Select(x => new OrderLineInput(x.ProductId,
                    x.Quantity, quick ? null : request.Lines!.Single(p => p.ProductId == x.ProductId).Notes)).ToArray(), inner);
                var channel = await channels.GetRequiredByTypeAsync(SalesChannelType.Customer, inner);
                var count = await orders.CountCustomerOrdersAsync(customerId, date, inner);
                var order = Order.CreateSubmitted(customerId, channel.Id, date, received.Now, count + 1,
                    ServiceDatePolicy.Evaluate(date, received, false), lines, customer);
                order.ApplyDeliveryChanges(null, null, null, request.DeliveryNotes);
                await orders.AddAsync(order, inner);
                await audit.AddAsync(OrderAuditLog.Create(order, OrderAuditEventType.OrderSubmitted, order.SubmittedAt,
                    AuditActorType.Customer, quick ? "Pedido del cliente confirmado mediante captura rápida." : "Pedido del cliente confirmado mediante captura manual.",
                    metadataJson: JsonSerializer.Serialize(new { mode = quick ? "quick" : "manual",
                        originalText = quick ? request.Text!.Trim() : null, request.OperationId,
                        deliveryDate = date, profile.OrderingProfileVersion, lines = resolved }, JsonOptions)), inner);
                if (order.IsLate)
                    await audit.AddAsync(OrderAuditLog.Create(order, OrderAuditEventType.OrderMarkedLate, order.SubmittedAt,
                        AuditActorType.System, "Pedido recibido después de la hora habitual para esta fecha de entrega."), inner);
                if (order.RequiresAdminReview)
                    await audit.AddAsync(OrderAuditLog.Create(order, OrderAuditEventType.OrderRequiresAdminReview, order.SubmittedAt,
                        AuditActorType.System, "Pedido del cliente enviado a revisión administrativa."), inner);
                await orders.SaveChangesAsync(inner);
                return Map(order);
            }, token), cancellationToken);
        return new(receipt.Result, receipt.Replayed);
    }

    public async Task<SubmitCustomerOrderCaptureResponse> MarkNoOrderAsync(Guid customerId,
        MarkNextCustomerNoOrderRequest request, CancellationToken cancellationToken)
    {
        var received = DeliveryDatePolicy.Capture(clock);
        ArgumentNullException.ThrowIfNull(request);
        if (request.OperationId == Guid.Empty || request.ExpectedDeliveryDate == DateOnly.MinValue)
            throw new ArgumentException("Confirma la fecha de entrega y la referencia de la operación.");
        var receipt = await operations.ExecuteAsync($"CUSTOMER-NOORDER:{customerId:N}:{request.OperationId:N}",
            Hash(new { customerId, request }), received.Now,
            token => customers.ExecuteOrderingProfileUpdateAsync(async inner =>
            {
                var (customer, _) = await ReadProfileAsync(customerId, inner);
                var date = DeliveryDatePolicy.RequireExpectedDate(request.ExpectedDeliveryDate, received);
                if (await orders.HasActiveCustomerOrderAsync(customerId, date, inner))
                    throw new CustomerOrderConflictException("Ya existe un pedido para esta fecha de entrega; no puede marcarse sin pedido.");
                var existing = await orders.GetLatestCustomerOrderAsync(customerId, date, inner);
                if (existing?.Status is OrderStatus.NoOrder) return Map(existing);
                var channel = await channels.GetRequiredByTypeAsync(SalesChannelType.Customer, inner);
                var count = await orders.CountCustomerOrdersAsync(customerId, date, inner);
                var order = Order.CreateNoOrder(customerId, channel.Id, date, received.Now, count + 1, customer);
                await orders.AddAsync(order, inner);
                await audit.AddAsync(OrderAuditLog.Create(order, OrderAuditEventType.NoOrderMarked, order.SubmittedAt,
                    AuditActorType.Customer, "El cliente confirmó que no requiere pedido para esta fecha de entrega.",
                    metadataJson: JsonSerializer.Serialize(new { request.OperationId, deliveryDate = date }, JsonOptions)), inner);
                await orders.SaveChangesAsync(inner);
                return Map(order);
            }, token), cancellationToken);
        return new(receipt.Result, receipt.Replayed);
    }

    private async Task<(Customer, CustomerOrderingProfileSnapshot)> ReadProfileAsync(Guid customerId, CancellationToken token)
    {
        var customer = await customers.GetByIdAsync(customerId, token);
        if (customer is null || !customer.IsActive) throw new KeyNotFoundException("El cliente no existe o está inactivo.");
        var profile = await customers.GetOrderingProfileSnapshotAsync(customerId, token)
            ?? throw new KeyNotFoundException("La configuración del cliente no existe.");
        return (customer, profile);
    }

    private PreviewCustomerOrderCaptureResponse Preview(Customer customer, CustomerOrderingProfileSnapshot profile, string text,
        DateOnly date, DateTimeOffset receivedAt)
    {
        var lines = OrderCaptureParser.Parse(text, profile);
        // The public semantic profile intentionally excludes all internal assignment identifiers.
        var meaning = profile.Items.Select(x => new { x.ProductId, x.ProductName, x.IsActive, x.ProductIsActive,
            x.InterpretationPosition, x.UnitCode, x.CaptureAlias }).ToArray();
        var fingerprint = Hash(new { date, text = text.Trim(), profile.OrderingProfileVersion,
            profile.IsOrderProfileApproved, meaning, customer.PreferredDeliveryTime,
            customer.PreferredDeliveryWindowStart, customer.PreferredDeliveryWindowEnd, customer.DeliveryNotes });
        return new(date, receivedAt, text.Trim(), fingerprint, profile.OrderingProfileVersion, lines);
    }

    private static IReadOnlyList<ResolvedCaptureLine> ResolveManual(CustomerOrderingProfileSnapshot profile,
        IReadOnlyList<ManualOrderCaptureLine> lines)
    {
        if (lines.Any(x => x is null || x.Quantity <= 0 || x.Quantity > 9999999999999999.99m
            || decimal.Round(x.Quantity, 2) != x.Quantity || x.Notes?.Length > 500)
            || lines.Select(x => x.ProductId).Distinct().Count() != lines.Count)
            throw new ArgumentException("Captura productos únicos con cantidades positivas de hasta dos decimales.");
        var available = profile.Items.Where(x => x.IsActive && x.ProductIsActive).ToDictionary(x => x.ProductId);
        return lines.Select(line =>
        {
            if (!available.TryGetValue(line.ProductId, out var product) || product.UnitCode is null)
                throw new ArgumentException("El producto debe estar configurado para tu pedido y tener una unidad confirmada.");
            if (product.UnitCode != line.ExpectedUnitCode)
                throw new CatalogConflictException("La unidad del producto cambió. Actualiza y revisa el pedido antes de confirmar.");
            return new ResolvedCaptureLine(product.ProductId, product.ProductName, line.Quantity,
                product.UnitCode, product.InterpretationPosition ?? 0, product.CaptureAlias);
        }).ToArray();
    }

    private async Task<OrderLine[]> CreateLinesAsync(Guid customerId, IReadOnlyList<OrderLineInput> lines, CancellationToken token)
    {
        try { return await OrderLineFactory.CreateAsync(customerId, lines, customers, products, machines, token); }
        catch (ArgumentException ex) when (ex.Message.Contains("maquina", StringComparison.OrdinalIgnoreCase))
        { throw new ArgumentException("La configuración de un producto requiere revisión de PRODIMT antes de confirmar el pedido."); }
    }

    private static bool IsActive(Order order) => order.Status is OrderStatus.Submitted or OrderStatus.PendingAdminReview or OrderStatus.Accepted;

    private static CustomerOrderResponse Map(Order order) => new(order.Id, order.CustomerId!.Value,
        order.OrderDate, order.Status, order.SequenceNumber, order.SubmittedAt, order.IsLate,
        order.RequiresAdminReview, order.AdminReviewReason);

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(value, JsonOptions)))).ToLowerInvariant();
}
