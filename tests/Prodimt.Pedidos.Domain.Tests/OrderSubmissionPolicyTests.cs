using Prodimt.Pedidos.Domain.Entities;
using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Domain.Services;

namespace Prodimt.Pedidos.Domain.Tests;

public sealed class OrderSubmissionPolicyTests
{
    [Theory]
    [InlineData(9, 59)]
    [InlineData(10, 0)]
    public void BeforeOrAtCutoff_DoesNotRequireAdminReviewOnlyBecauseOfTime(int hour, int minute)
    {
        var result = OrderSubmissionPolicy.Evaluate(new TimeOnly(hour, minute), hasExistingActiveOrderToday: false);

        Assert.False(result.IsLate);
        Assert.False(result.RequiresAdminReview);
        Assert.Null(result.ReviewReason);
        Assert.Equal(OrderStatus.Submitted, result.Status);
    }

    [Fact]
    public void AfterCutoff_IsLateAndRequiresAdminReview()
    {
        var result = OrderSubmissionPolicy.Evaluate(new TimeOnly(10, 1), hasExistingActiveOrderToday: false);

        Assert.True(result.IsLate);
        Assert.True(result.RequiresAdminReview);
        Assert.Equal(AdminReviewReason.LateSubmission, result.ReviewReason);
        Assert.Equal(OrderStatus.PendingAdminReview, result.Status);
    }

    [Fact]
    public void SecondOrderSameDay_RequiresAdminReviewWithAdditionalOrderReason()
    {
        var result = OrderSubmissionPolicy.Evaluate(new TimeOnly(9, 0), hasExistingActiveOrderToday: true);

        Assert.False(result.IsLate);
        Assert.True(result.RequiresAdminReview);
        Assert.Equal(AdminReviewReason.AdditionalOrderSameDay, result.ReviewReason);
        Assert.Equal(OrderStatus.PendingAdminReview, result.Status);
    }

    [Fact]
    public void NoOrder_IsStoredAsExplicitNoOrderStatus()
    {
        var customerId = Guid.NewGuid();
        var salesChannelId = Guid.NewGuid();

        var order = Order.CreateNoOrder(
            customerId,
            salesChannelId,
            new DateOnly(2026, 5, 27),
            new DateTimeOffset(2026, 5, 27, 9, 15, 0, TimeSpan.Zero),
            sequenceNumber: 1);

        Assert.Equal(OrderStatus.NoOrder, order.Status);
        Assert.Equal(customerId, order.CustomerId);
        Assert.Empty(order.Lines);
        Assert.False(order.RequiresAdminReview);
    }
}
