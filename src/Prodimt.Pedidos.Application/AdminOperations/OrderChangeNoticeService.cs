using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminOperations;

public sealed class OrderChangeNoticeService(IOrderChangeNoticeRepository notices, IOrderOperationStore operations,
    IDateTimeProvider clock)
{
    public async Task<OrderChangeNoticeListResponse> GetAsync(DateOnly? date, string? routeCode, Guid? machineId,
        bool pendingOnly, CancellationToken cancellationToken)
    {
        var serviceDate = date ?? clock.Today;
        var route = string.IsNullOrWhiteSpace(routeCode) ? null : routeCode.Trim();
        var filtered = (await notices.GetByDateAsync(serviceDate, cancellationToken))
            .Where(x => (route is null || string.Equals(x.RouteCode, route, StringComparison.OrdinalIgnoreCase)) &&
                (machineId is null || x.MachineId == machineId)).ToArray();
        return new(serviceDate, filtered.Count(x => x.AcknowledgedAt is null), filtered
            .Where(x => !pendingOnly || x.AcknowledgedAt is null).OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.OrderVersion).ThenBy(x => x.Id).Select(Map).ToArray());
    }

    public async Task<OrderChangeNoticeResponse> AcknowledgeAsync(Guid id, AcknowledgeOrderChangeRequest request,
        AdminActorContext? actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = request.OperationKey?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(key) || key.Length > 100 || !Regex.IsMatch(key, "^[A-Z0-9_.:-]+$", RegexOptions.CultureInvariant) || request.ExpectedVersion < 1)
            throw new ArgumentException("Indica una clave de operacion valida y la version esperada del aviso.");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { id, request.ExpectedVersion })));
        var result = await operations.ExecuteAsync("NOTICE-ACK:" + key, hash, clock.Now, async token =>
        {
            var notice = await notices.GetByIdAsync(id, token) ?? throw new KeyNotFoundException("El aviso no existe.");
            if (notice.Version != request.ExpectedVersion)
                throw new CustomerOrderConflictException("El aviso cambio. Actualiza la lista antes de confirmar su recepcion.");
            if (notice.AcknowledgedAt is null)
            {
                notice.AcknowledgedAt = clock.Now;
                notice.AcknowledgedById = actor?.ActorId;
                notice.AcknowledgedBy = actor?.ActorDisplayName;
                notice.Version++;
            }
            return Map(notice);
        }, cancellationToken);
        return result.Result;
    }

    private static OrderChangeNoticeResponse Map(OrderChangeNotice notice) => new(notice.Id, notice.OrderId,
        notice.OrderVersion, notice.Version, notice.ServiceDate, notice.CustomerId, notice.CustomerName, notice.RouteCode,
        notice.MachineId, notice.MachineNumber, notice.MachineName, notice.Kind, notice.Reason, notice.ActorDisplayName,
        notice.OccurredAt, notice.AcknowledgedAt, notice.AcknowledgedBy,
        JsonSerializer.Deserialize<OrderChangeLineResponse[]>(notice.LinesJson, OrderChangeNoticePublisher.JsonOptions) ?? []);
}
