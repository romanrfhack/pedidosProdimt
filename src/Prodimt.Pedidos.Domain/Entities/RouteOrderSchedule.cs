namespace Prodimt.Pedidos.Domain.Entities;

public sealed class RouteOrderSchedule
{
    public string RouteCode { get; set; } = string.Empty;
    public TimeOnly? AutomaticConfirmationCutoff { get; set; }
    public int Version { get; set; }
}
