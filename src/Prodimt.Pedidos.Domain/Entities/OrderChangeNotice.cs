namespace Prodimt.Pedidos.Domain.Entities;

// Acknowledgement means the change was received, never that production was completed.
public sealed class OrderChangeNotice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public int OrderVersion { get; set; }
    public int Version { get; set; } = 1;
    public DateOnly ServiceDate { get; set; }
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? RouteCode { get; set; }
    public Guid? MachineId { get; set; }
    public int? MachineNumber { get; set; }
    public string? MachineName { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public string? ActorDisplayName { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string LinesJson { get; set; } = "[]";
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedById { get; set; }
    public string? AcknowledgedBy { get; set; }
}
