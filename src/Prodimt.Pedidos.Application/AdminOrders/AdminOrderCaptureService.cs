using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record PreviewOrderCaptureRequest(string Text, DateOnly? ServiceDate = null);
public sealed record PreviewOrderCaptureResponse(Guid CustomerId, DateOnly ServiceDate, string Text, string Fingerprint,
    AdminCustomerOrderingProfileResponse Profile, IReadOnlyList<ResolvedCaptureLine> Lines,
    TimeOnly? PreferredDeliveryTime, TimeOnly? PreferredDeliveryWindowStart, TimeOnly? PreferredDeliveryWindowEnd);
public sealed record ManualOrderCaptureLine(Guid ProductId, decimal Quantity, string? ExpectedUnitCode, string? Notes = null);
public sealed record SubmitOrderCaptureRequest(Guid OperationId, int ExpectedProfileVersion, DateOnly? ServiceDate = null,
    string? Text = null, string? PreviewFingerprint = null, IReadOnlyList<ManualOrderCaptureLine>? Lines = null,
    bool ConfirmSeparateDelivery = false, string? DeliveryNotes = null, string? InternalNotes = null);
public sealed record SubmitOrderCaptureResponse(AdminOrderSummaryResponse Order, bool Replayed);

public sealed class AdminOrderCaptureService(ICustomerRepository customers, IOrderRepository orders,
    IOrderAuditLogRepository audit, IOrderOperationStore operations, IDateTimeProvider clock, AdminOrderService orderService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<PreviewOrderCaptureResponse> PreviewAsync(Guid customerId, PreviewOrderCaptureRequest request,
        CancellationToken cancellationToken)
        => customers.ExecuteOrderingProfileUpdateAsync(async token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var (customer, profile) = await ReadProfileAsync(customerId, token);
            return Preview(customer, profile, request.Text, request.ServiceDate ?? clock.Today);
        }, cancellationToken);

    public async Task<SubmitOrderCaptureResponse> SubmitAsync(Guid customerId, SubmitOrderCaptureRequest request,
        AdminActorContext? actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OperationId == Guid.Empty || request.ExpectedProfileVersion < 0)
            throw new ArgumentException("La captura debe incluir su referencia y la versión del perfil que se mostró.");
        var quick = !string.IsNullOrWhiteSpace(request.Text);
        if (quick && (request.Lines is not null || request.Text!.Length > 240)
            || !quick && (request.Lines is null || request.Lines.Count is 0 or > 200))
            throw new ArgumentException("Elige captura rápida o cantidades manuales, sin mezclar ambas entradas.");
        if (request.DeliveryNotes?.Length > 2000 || request.InternalNotes?.Length > 2000)
            throw new ArgumentException("Las notas deben tener hasta 2000 caracteres.");
        var date = request.ServiceDate ?? clock.Today;
        var payloadHash = Hash(new { customerId, date, request, actorId = actor?.ActorId });
        var receipt = await operations.ExecuteAsync($"CAPTURE:{customerId:N}:{request.OperationId:N}", payloadHash, clock.Now,
            token => customers.ExecuteOrderingProfileUpdateAsync(async inner =>
            {
                var (customer, profile) = await ReadProfileAsync(customerId, inner);
                if (profile.OrderingProfileVersion != request.ExpectedProfileVersion)
                    throw new CatalogConflictException("El perfil del cliente cambió. Actualiza la configuración y revisa el desglose antes de guardar.");
                IReadOnlyList<ResolvedCaptureLine> resolved;
                if (quick)
                {
                    var preview = Preview(customer, profile, request.Text!, date);
                    if (!profile.IsOrderProfileApproved)
                        throw new CatalogConflictException("Confirma los productos y su orden antes de guardar una captura rápida, o usa captura manual.");
                    if (preview.Fingerprint != request.PreviewFingerprint)
                        throw new CatalogConflictException("La captura o la configuración cambió desde la vista previa. Revisa de nuevo el desglose.");
                    resolved = preview.Lines;
                }
                else
                {
                    var lines = request.Lines!;
                    if (lines.Any(x => x is null || x.Quantity <= 0 || x.Quantity > 9999999999999999.99m
                        || decimal.Round(x.Quantity, 2) != x.Quantity || x.Notes?.Length > 500)
                        || lines.Select(x => x.ProductId).Distinct().Count() != lines.Count)
                        throw new ArgumentException("Captura productos únicos con cantidades positivas de hasta dos decimales.");
                    var available = profile.Items.Where(x => x.IsActive && x.ProductIsActive).ToDictionary(x => x.ProductId);
                    resolved = lines.Select(line =>
                    {
                        if (!available.TryGetValue(line.ProductId, out var product) || product.UnitCode is null)
                            throw new ArgumentException("El producto debe estar activo, configurado para el cliente y tener su unidad confirmada.");
                        if (product.UnitCode != line.ExpectedUnitCode)
                            throw new CatalogConflictException("La unidad del producto cambió. Actualiza la configuración antes de guardar.");
                        return new ResolvedCaptureLine(product.ProductId, product.ProductName, line.Quantity,
                            product.UnitCode, product.InterpretationPosition ?? 0, product.CaptureAlias);
                    }).ToArray();
                }
                if (await orders.HasActiveCustomerOrderAsync(customerId, date, inner) && !request.ConfirmSeparateDelivery)
                    throw new CustomerOrderConflictException("Ya existe un pedido para este cliente y fecha. Ábrelo para corregirlo o confirma que es otra entrega independiente.");
                var result = await orderService.SubmitCustomerOrderAsync(customerId, new(
                    resolved.Select(x => new AdminSubmitCustomerOrderLineRequest(x.ProductId, x.Quantity,
                        quick ? null : request.Lines!.Single(line => line.ProductId == x.ProductId).Notes)).ToArray(),
                    null, null, null, request.DeliveryNotes, request.InternalNotes, date), actor, inner);
                var order = await orders.GetByIdAsync(result.OrderId, inner)
                    ?? throw new InvalidOperationException("No se pudo verificar el pedido guardado.");
                await audit.AddAsync(OrderAuditLog.Create(order, OrderAuditEventType.AdminOrderCaptureResolved,
                    clock.Now, AuditActorType.Admin, quick ? "Captura rápida interpretada: " + request.Text!.Trim() : "Captura manual con productos del cliente.",
                    actor?.ActorId, actor?.ActorDisplayName, JsonSerializer.Serialize(new
                    {
                        mode = quick ? "quick" : "manual", originalText = quick ? request.Text!.Trim() : null,
                        request.OperationId, serviceDate = date, profile.OrderingProfileVersion,
                        fingerprint = quick ? request.PreviewFingerprint : null, lines = resolved
                    }, JsonOptions)), inner);
                // The operation receipt, order, both audit records and profile read share one transaction.
                return result;
            }, token), cancellationToken);
        return new(receipt.Result, receipt.Replayed);
    }

    private async Task<(Customer Customer, CustomerOrderingProfileSnapshot Profile)> ReadProfileAsync(Guid customerId, CancellationToken token)
    {
        var customer = await customers.GetByIdAsync(customerId, token);
        if (customer is null || !customer.IsActive) throw new KeyNotFoundException("El cliente no existe o está inactivo.");
        var profile = await customers.GetOrderingProfileSnapshotAsync(customerId, token)
            ?? throw new KeyNotFoundException("El perfil del cliente no existe.");
        return (customer, profile);
    }

    private static PreviewOrderCaptureResponse Preview(Customer customer, CustomerOrderingProfileSnapshot profile, string text, DateOnly date)
    {
        var lines = OrderCaptureParser.Parse(text, profile);
        var dto = new AdminCustomerOrderingProfileResponse(profile.CustomerId, profile.OrderingProfileVersion, profile.IsOrderProfileApproved,
            profile.Items.Select(x => new AdminCustomerFrequentProductResponse(x.ProductId, x.ProductName, x.DefaultQuantity,
                x.SortOrder, x.IsActive, x.PreferredMachineId, x.InterpretationPosition, x.UnitCode, x.ProductIsActive, x.CaptureAlias)).ToArray());
        var fingerprint = Hash(new { date, text = text.Trim(), profile = dto,
            customer.PreferredDeliveryTime, customer.PreferredDeliveryWindowStart, customer.PreferredDeliveryWindowEnd, customer.DeliveryNotes });
        return new(customer.Id, date, text.Trim(), fingerprint, dto, lines,
            customer.PreferredDeliveryTime, customer.PreferredDeliveryWindowStart, customer.PreferredDeliveryWindowEnd);
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(value, JsonOptions)))).ToLowerInvariant();
}
