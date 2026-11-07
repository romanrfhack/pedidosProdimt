using Prodimt.Pedidos.Domain.Enums;

namespace Prodimt.Pedidos.Domain.Services;

public static class OrderSubmissionPolicy
{
    public static readonly TimeOnly DefaultCutoffTime = new(10, 0);

    public static OrderSubmissionEvaluation Evaluate(
        TimeOnly submittedAt,
        bool hasExistingActiveOrderToday,
        TimeOnly? cutoffTime = null)
        => Evaluate(submittedAt > (cutoffTime ?? DefaultCutoffTime), hasExistingActiveOrderToday);

    public static OrderSubmissionEvaluation Evaluate(bool isLate, bool hasExistingActiveOrderToday)
    {
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
