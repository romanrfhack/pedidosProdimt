using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Domain.Services;

public static class OrderSubmissionPolicy
{
    public static readonly TimeOnly DefaultCutoffTime = new(10, 0);

    public static OrderSubmissionEvaluation Evaluate(
        TimeOnly submittedAt,
        bool hasExistingActiveOrderToday,
        TimeOnly? cutoffTime = null)
    {
        var effectiveCutoff = cutoffTime ?? DefaultCutoffTime;
        var isLate = submittedAt > effectiveCutoff;

        if (hasExistingActiveOrderToday)
        {
            return new OrderSubmissionEvaluation(
                isLate,
                true,
                AdminReviewReason.AdditionalOrderSameDay,
                OrderStatus.PendingAdminReview);
        }

        if (isLate)
        {
            return new OrderSubmissionEvaluation(
                true,
                true,
                AdminReviewReason.LateSubmission,
                OrderStatus.PendingAdminReview);
        }

        return new OrderSubmissionEvaluation(false, false, null, OrderStatus.Submitted);
    }
}
