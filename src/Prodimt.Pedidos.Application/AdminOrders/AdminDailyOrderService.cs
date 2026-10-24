using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminImports;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed class AdminDailyOrderService(
    IOrderRepository orders,
    IOrderAuditLogRepository auditLogs,
    ICustomerRepository customers,
    IProductRepository products,
    IMachineRepository machines,
    ISalesChannelRepository salesChannels,
    IDateTimeProvider clock,
    IOrderOperationStore operationStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly OrderStatus[] ActiveStatuses = [OrderStatus.Submitted, OrderStatus.Accepted, OrderStatus.PendingAdminReview];

    public async Task<DailyOrderImportResponse> ValidateAsync(DailyOrderImportRequest request, CancellationToken cancellationToken)
        => (await PrepareAsync(request, cancellationToken)).Response;

    public async Task<DailyOrderImportResponse> ApplyAsync(DailyOrderImportRequest request, AdminActorContext? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = Key(request.Source, 80, "origen");
        var batchKey = Key(request.BatchKey, 100, "lote");
        ValidateContentBounds(request);
        var payloadHash = Hash(new { source, content = request.Content.Replace("\r\n", "\n").TrimStart('\uFEFF').Trim() });
        try
        {
            var receipt = await operationStore.ExecuteAsync($"IMPORT:{Hash(new[] { source, batchKey })}", payloadHash, clock.Now, async token =>
            {
                var prepared = await PrepareAsync(request, token);
                if (prepared.HasSourceConflict)
                    throw new CustomerOrderConflictException("Una referencia de origen ya existe con contenido distinto. Usa una correccion explicita del pedido.");
                if (!prepared.Response.CanApply) throw new ImportValidationException(prepared.Response);
                var channel = await salesChannels.GetRequiredByTypeAsync(SalesChannelType.AdminManualCapture, token);
                var results = prepared.Response.Orders.ToList();
                var projected = new Dictionary<(Guid, DateOnly), List<Order>>();
                foreach (var item in prepared.Creates)
                {
                    var customer = item.Customer;
                    var key = (customer.Id, item.ServiceDate);
                    if (!projected.TryGetValue(key, out var dayOrders))
                    {
                        dayOrders = (await orders.GetByDateAsync(item.ServiceDate, token)).Where(x => x.CustomerId == customer.Id).ToList();
                        projected.Add(key, dayOrders);
                    }
                    var hasActive = dayOrders.Any(x => ActiveStatuses.Contains(x.Status));
                    var sequence = dayOrders.Count == 0 ? 1 : dayOrders.Max(x => x.SequenceNumber) + 1;
                    var order = item.Action == "no_order"
                        ? Order.CreateNoOrder(customer.Id, channel.Id, item.ServiceDate, clock.Now, sequence, customer)
                        : Order.CreateSubmitted(customer.Id, channel.Id, item.ServiceDate, clock.Now, sequence,
                            ServiceDatePolicy.Evaluate(item.ServiceDate, clock, hasActive), item.Lines, customer);
                    if (item.Action == "no_order") order.InternalNotes = item.NoOrderNotes;
                    order.Source = source;
                    order.ExternalOrderKey = item.Key;
                    order.SourcePayloadHash = item.PayloadHash;
                    await orders.AddAsync(order, token);
                    dayOrders.Add(order);
                    await auditLogs.AddAsync(OrderAuditLog.Create(order, OrderAuditEventType.DailyOrderImported,
                        clock.Now, AuditActorType.Admin, "Pedido importado desde un lote diario revisado.",
                        actor?.ActorId, actor?.ActorDisplayName,
                        JsonSerializer.Serialize(new { source, batchKey, externalOrderKey = item.Key, request.FileName,
                            serviceDate = item.ServiceDate, item.Action, order.IsLate, order.RequiresAdminReview,
                            order.AdminReviewReason, snapshot = Snapshot(order) }, JsonOptions)), token);
                    var index = results.FindIndex(x => x.ExternalOrderKey == item.Key);
                    results[index] = results[index] with { OrderId = order.Id };
                }
                return prepared.Response with { Applied = true, Orders = results };
            }, cancellationToken);
            return receipt.Result with { Replayed = receipt.Replayed };
        }
        catch (ImportValidationException invalid) { return invalid.Response; }
    }

    public async Task<AdminOrderDetailResponse> CorrectAsync(Guid orderId, CorrectOrderRequest request,
        AdminActorContext? actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operationKey = Key(request.OperationKey, 100, "operacion");
        if (request.ExpectedVersion < 1) throw new ArgumentException("Indica la version esperada del pedido.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            throw new ArgumentException("Indica el motivo de la correccion (hasta 1000 caracteres).");
        if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Count > 200 || request.Lines.Any(x => x is null) ||
            request.Lines.Select(x => x.ProductId).Distinct().Count() != request.Lines.Count ||
            request.Lines.Any(x => x.Quantity <= 0 || x.Quantity > 9999999999999999.99m || decimal.Round(x.Quantity, 2) != x.Quantity || x.Notes?.Length > 500))
            throw new ArgumentException("La correccion requiere productos unicos con cantidades positivas de hasta dos decimales y notas de hasta 500 caracteres.");
        var hash = Hash(new { orderId, request.ExpectedVersion, reason = request.Reason.Trim(),
            lines = request.Lines.OrderBy(x => x.ProductId).ToArray() });
        var receipt = await operationStore.ExecuteAsync($"CORRECTION:{operationKey}", hash, clock.Now, async token =>
        {
            var order = await orders.GetByIdAsync(orderId, token) ?? throw new KeyNotFoundException("El pedido no existe.");
            if (order.Version != request.ExpectedVersion || !ActiveStatuses.Contains(order.Status) || order.CustomerId is null)
                throw new CustomerOrderConflictException("El pedido cambio o su estado no permite corregirlo. Recarga el detalle.");
            var before = Snapshot(order);
            var oldLines = order.Lines.ToDictionary(x => x.ProductId);
            var newInputs = request.Lines.Where(x => !oldLines.ContainsKey(x.ProductId))
                .Select(x => new OrderLineInput(x.ProductId, x.Quantity, x.Notes)).ToArray();
            var newLines = newInputs.Length == 0 ? [] : await OrderLineFactory.CreateAsync(order.CustomerId.Value,
                newInputs, customers, products, machines, token);
            var newLookup = newLines.ToDictionary(x => x.ProductId);
            var replacements = request.Lines.Select(input =>
            {
                if (!oldLines.TryGetValue(input.ProductId, out var previous)) return newLookup[input.ProductId];
                if (previous.UnitCode is null || !ProductUnitCodes.IsSupported(previous.UnitCode))
                    throw new ArgumentException("No se puede corregir una partida historica sin unidad validada.");
                return new OrderLine { Id = Guid.NewGuid(), ProductId = input.ProductId, Quantity = input.Quantity,
                    Notes = input.Notes, UnitCode = previous.UnitCode, ProductNameSnapshot = previous.ProductNameSnapshot,
                    AssignedMachineId = previous.AssignedMachineId };
            }).ToArray();
            await orders.ReplaceLinesAsync(order, replacements, token);
            await auditLogs.AddAsync(OrderAuditLog.Create(order, OrderAuditEventType.OrderCorrected, clock.Now,
                AuditActorType.Admin, "Correccion comercial: reemplazo completo de partidas.", actor?.ActorId,
                actor?.ActorDisplayName, JsonSerializer.Serialize(new { operationKey, reason = request.Reason.Trim(), before,
                    after = Snapshot(order) }, JsonOptions)), token);
            var detailService = new AdminOrderService(orders, auditLogs, customers, products, machines, salesChannels, clock);
            return await detailService.MapDetailAsync(order, token);
        }, cancellationToken);
        return receipt.Result;
    }

    private async Task<PreparedImport> PrepareAsync(DailyOrderImportRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<DailyOrderImportError>();
        var warnings = new List<string>();
        var results = new List<DailyOrderImportOrder>();
        var creates = new List<PreparedOrder>();
        string source;
        string batchKey;
        IReadOnlyList<CsvImportRow> rows;
        try
        {
            source = Key(request.Source, 80, "origen");
            batchKey = Key(request.BatchKey, 100, "lote");
            ValidateContentBounds(request);
            rows = DailyOrderCsvParser.Parse(request.Content);
            foreach (var row in rows) Key(row.Get("externalOrderKey"), 100, "referencia externa");
        }
        catch (ArgumentException ex)
        {
            return new(new(request.BatchKey ?? string.Empty, false, false, false, [], [new(null, ex.Message)], []), [], false);
        }
        var allCustomers = await customers.GetAllAsync(token);
        var allProducts = await products.GetAllAsync(token);
        var sourceConflict = false;
        foreach (var group in rows.GroupBy(x => Key(x.Get("externalOrderKey"), 100, "referencia externa")))
        {
            var first = group.First();
            try
            {
                var action = first.Get("action").ToLowerInvariant();
                if (action is not ("order" or "no_order")) throw new ArgumentException("action debe ser order o no_order; X nunca es una cantidad ni cancela al cliente.");
                if (!DateOnly.TryParseExact(first.Get("serviceDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new ArgumentException("serviceDate debe tener formato YYYY-MM-DD.");
                var customerCode = first.Get("customerExternalCode").ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(customerCode)) throw new ArgumentException("Falta customerExternalCode.");
                if (group.Any(x => !x.Get("customerExternalCode").Equals(customerCode, StringComparison.OrdinalIgnoreCase) ||
                    x.Get("serviceDate") != first.Get("serviceDate") || !x.Get("action").Equals(action, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("La misma referencia externa debe tener un solo cliente, fecha y accion.");
                var inputRows = group.ToArray();
                if (action == "no_order" && (inputRows.Length != 1 || inputRows.Any(x =>
                    x.Get("productExternalCode") != "" || x.Get("quantity") != "" || x.Get("unitCode") != "")))
                    throw new ArgumentException("no_order requiere una sola fila sin producto, cantidad ni unidad.");
                var fingerprint = Hash(new { customerCode, date, action, lines = inputRows.Select(x => new
                {
                    product = x.Get("productExternalCode").ToUpperInvariant(),
                    quantity = NormalizeQuantity(x.Get("quantity")), unit = x.Get("unitCode").ToLowerInvariant(), notes = x.Get("notes")
                }).OrderBy(x => x.product).ToArray() });
                // Resolve a prior identity before consulting mutable catalogs or current cutoff.
                var existing = await orders.GetBySourceAsync(source, group.Key, token);
                if (existing is not null)
                {
                    if (existing.SourcePayloadHash != fingerprint)
                    {
                        sourceConflict = true;
                        throw new ArgumentException("La referencia de origen ya existe con otro contenido. Corrige explicitamente el pedido existente.");
                    }
                    results.Add(new(group.Key, existing.CustomerId!.Value, existing.CustomerNameSnapshot ?? customerCode,
                        existing.OrderDate, action, "existing", existing.Id, existing.Lines.Select(x => new DailyOrderImportLine(
                            x.ProductId, x.ProductNameSnapshot ?? "Producto historico", x.Quantity, x.UnitCode ?? "", x.Notes)).ToArray()));
                    continue;
                }
                var customerMatches = allCustomers.Where(x => string.Equals(x.ExternalCode, customerCode, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (customerMatches.Length != 1 || !customerMatches[0].IsActive) throw new ArgumentException("Cliente inexistente, ambiguo o inactivo para el codigo indicado.");
                var customer = customerMatches[0];
                var inputs = new List<OrderLineInput>();
                foreach (var row in inputRows)
                {
                    if (row.Get("notes").Length > 500) throw new ArgumentException("La nota no puede exceder 500 caracteres.");
                    if (action == "no_order") continue;
                    if (!decimal.TryParse(row.Get("quantity"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity) ||
                        quantity <= 0 || quantity > 9999999999999999.99m || decimal.Round(quantity, 2) != quantity)
                        throw new ArgumentException("quantity debe ser positiva, con punto decimal y hasta dos decimales; X no es valida.");
                    var productCode = row.Get("productExternalCode");
                    var matches = allProducts.Where(x => string.Equals(x.ExternalCode, productCode, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (string.IsNullOrWhiteSpace(productCode) || matches.Length != 1 || !matches[0].IsActive)
                        throw new ArgumentException("Producto inexistente, ambiguo o inactivo para el codigo indicado.");
                    var product = matches[0];
                    if (product.UnitCode is null || !ProductUnitCodes.IsSupported(product.UnitCode) ||
                        !string.Equals(product.UnitCode, row.Get("unitCode"), StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("unitCode debe coincidir con la unidad validada del producto.");
                    inputs.Add(new(product.Id, quantity, string.IsNullOrEmpty(row.Get("notes")) ? null : row.Get("notes")));
                }
                var lines = action == "no_order" ? [] : await OrderLineFactory.CreateAsync(customer.Id, inputs, customers, products, machines, token);
                creates.Add(new(group.Key, customer, date, action, fingerprint, lines, action == "no_order" ? first.Get("notes") : null));
                results.Add(new(group.Key, customer.Id, customer.Name, date, action, "create", null,
                    lines.Select(x => new DailyOrderImportLine(x.ProductId, x.ProductNameSnapshot!, x.Quantity, x.UnitCode!, x.Notes)).ToArray()));
                if (action == "order" && ServiceDatePolicy.Evaluate(date, clock, false).IsLate)
                    warnings.Add($"{group.Key}: recibido despues del corte de su fecha de servicio; queda tardio y pendiente de revision.");
                if (action == "order" && lines.Any(x => x.AssignedMachineId is null)) warnings.Add($"{group.Key}: hay partidas sin maquina asignada.");
            }
            catch (ArgumentException ex) { errors.Add(new(first.RowNumber, ex.Message)); }
        }
        foreach (var group in creates.GroupBy(x => (x.Customer.Id, x.ServiceDate)))
        {
            var existing = (await orders.GetByDateAsync(group.Key.ServiceDate, token)).Where(x => x.CustomerId == group.Key.Id).ToArray();
            if (group.Any(x => x.Action == "no_order") && (group.Count() > 1 || existing.Any(x => ActiveStatuses.Contains(x.Status) || x.Status == OrderStatus.NoOrder)))
                errors.Add(new(null, "No se puede mezclar no_order con otros pedidos del cliente/fecha ni duplicar un no_order existente con otra referencia."));
            if (group.Count(x => x.Action == "order") > 1 || existing.Any(x => ActiveStatuses.Contains(x.Status)))
                warnings.Add("Los pedidos adicionales del mismo cliente/fecha quedaran pendientes de revision.");
        }
        return new(new(batchKey, errors.Count == 0 && results.Count > 0, false, false, results, errors, warnings.Distinct().ToArray()), creates, sourceConflict);
    }

    private static void ValidateContentBounds(DailyOrderImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content) || Encoding.UTF8.GetByteCount(request.Content) > 2 * 1024 * 1024)
            throw new ArgumentException("El CSV debe contener pedidos y no exceder 2 MB.");
        if (request.FileName?.Length > 255) throw new ArgumentException("El nombre de archivo no puede exceder 255 caracteres.");
    }

    private static string Key(string? value, int maxLength, string field)
    {
        var key = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(key) || key.Length > maxLength || !Regex.IsMatch(key, "^[A-Z0-9_.:-]+$", RegexOptions.CultureInvariant))
            throw new ArgumentException($"La clave de {field} debe tener 1 a {maxLength} caracteres ASCII: letras, numeros, guion, punto, dos puntos o guion bajo.");
        return key;
    }

    private static string NormalizeQuantity(string value) => decimal.TryParse(value, NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var number) ? number.ToString("G29", CultureInfo.InvariantCulture) : value;
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));
    private static object Snapshot(Order order) => new { order.Version, order.OrderDate, order.Status, order.InternalNotes, lines = order.Lines
        .OrderBy(x => x.ProductId).Select(x => new { x.Id, x.ProductId, productName = x.ProductNameSnapshot, x.Quantity, x.UnitCode, x.AssignedMachineId, x.Notes }).ToArray() };
    private sealed record PreparedOrder(string Key, Customer Customer, DateOnly ServiceDate, string Action, string PayloadHash, OrderLine[] Lines, string? NoOrderNotes);
    private sealed record PreparedImport(DailyOrderImportResponse Response, IReadOnlyList<PreparedOrder> Creates, bool HasSourceConflict);
    private sealed class ImportValidationException(DailyOrderImportResponse response) : Exception
    {
        public DailyOrderImportResponse Response { get; } = response;
    }
}
