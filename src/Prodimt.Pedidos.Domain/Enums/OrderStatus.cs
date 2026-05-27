namespace Prodimt.Pedidos.Domain.Enums;

public enum OrderStatus
{
    Draft = 0,
    Submitted = 1,
    PendingAdminReview = 2,
    Accepted = 3,
    Rejected = 4,
    Cancelled = 5,
    NoOrder = 6,
    Superseded = 7
}
