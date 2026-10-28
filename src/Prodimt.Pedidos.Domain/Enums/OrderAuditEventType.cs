namespace Prodimt.Pedidos.Domain.Enums;

public enum OrderAuditEventType
{
    OrderSubmitted = 0,
    NoOrderMarked = 1,
    OrderRequiresAdminReview = 2,
    OrderMarkedLate = 3,
    AdditionalOrderDetected = 4,
    AdminDecisionRecorded = 5,
    AdminManualOrderCaptured = 6,
    AdminNoOrderMarked = 7,
    AdminOrderChanged = 8,
    DailyOrderImported = 9,
    OrderCorrected = 10,
    AdminOrderCaptureResolved = 11
}
