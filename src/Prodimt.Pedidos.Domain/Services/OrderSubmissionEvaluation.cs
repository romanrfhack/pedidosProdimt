using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Domain.Services;

public sealed record OrderSubmissionEvaluation(
    bool IsLate,
    bool RequiresAdminReview,
    AdminReviewReason? ReviewReason,
    OrderStatus Status);
