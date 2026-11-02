using System.Globalization;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminCatalogs;
using Prodimt.Pedidos.Application.CustomerOrders;
using Prodimt.Pedidos.Domain.Enums;
namespace Prodimt.Pedidos.Application.AdminOrders;

public sealed record OrderDecisionNotificationResponse(string Message, string? WhatsAppUrl, DateOnly? ProposedDate);
public sealed class OrderDecisionNotificationService(IOrderRepository orders, ICustomerRepository customers, IDateTimeProvider clock)
{
    public async Task<OrderDecisionNotificationResponse> GetAsync(Guid id, CancellationToken token)
    {
        var order = await orders.GetByIdAsync(id, token) ?? throw new KeyNotFoundException("El pedido no existe.");
        if (order.Status is not (OrderStatus.Accepted or OrderStatus.Rejected))
            throw new ArgumentException("Primero registra la decisión del pedido.");
        var customer = order.CustomerId is { } cid ? await customers.GetByIdAsync(cid, token) : null;
        var name = customer?.Name ?? order.CustomerNameSnapshot ?? "cliente";
        var culture = CultureInfo.GetCultureInfo("es-MX");
        string Format(DateOnly date) => date.ToString("dddd d 'de' MMMM 'de' yyyy", culture);
        var proposed = order.Status == OrderStatus.Rejected
            ? DeliveryDatePolicy.NextBusinessDate(order.OrderDate > DeliveryDatePolicy.LocalDate(clock.Now) ? order.OrderDate : DeliveryDatePolicy.LocalDate(clock.Now))
            : (DateOnly?)null;
        var text = order.Status == OrderStatus.Accepted
            ? $"Hola, {name}. Revisamos tu solicitud y tu pedido para el {Format(order.OrderDate)} ha quedado confirmado. ¡Gracias!"
            : $"Hola, {name}. Gracias por enviarnos tu pedido. En esta ocasión, por el horario y la disponibilidad de producción o reparto, ya no nos es posible surtirlo para el {Format(order.OrderDate)}. ¿Te gustaría que revisáramos la posibilidad de surtirlo el {Format(proposed!.Value)}? Esperamos tu respuesta antes de registrar un pedido para otra fecha.";
        if (order.Status == OrderStatus.Accepted)
        {
            var lines = string.Join("\n", order.Lines.Select(line =>
                $"• {line.Quantity.ToString("0.##", culture)} {line.UnitCode}: {line.ProductNameSnapshot ?? "Producto"}"));
            text += order.AdminDecision == AdminDecision.AcceptedWithChanges
                ? "\n\nSe confirmó con los siguientes ajustes:\n" + lines
                : "\n\nDetalle confirmado:\n" + lines;
        }
        string? url = null;
        try { var phone = CustomerPhoneNumber.Normalize(customer?.PhoneNumber).TrimStart('+'); url = "https://wa.me/" + phone + "?text=" + Uri.EscapeDataString(text); }
        catch (ArgumentException) { }
        return new(text, url, proposed);
    }
}
