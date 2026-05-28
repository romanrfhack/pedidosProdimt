using Prodimt.Pedidos.Domain.Enums;
using Prodimt.Pedidos.Domain.Services;

namespace Prodimt.Pedidos.Domain.Entities;

public sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    public Guid Id { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid SalesChannelId { get; set; }

    public DateOnly OrderDate { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    public OrderStatus Status { get; private set; }

    public int SequenceNumber { get; set; }

    public bool IsLate { get; private set; }

    public bool RequiresAdminReview { get; private set; }

    public AdminReviewReason? AdminReviewReason { get; private set; }

    public AdminDecision? AdminDecision { get; private set; }

    public TimeOnly? RequestedDeliveryTime { get; set; }

    public TimeOnly? RequestedDeliveryWindowStart { get; set; }

    public TimeOnly? RequestedDeliveryWindowEnd { get; set; }

    public string? DeliveryNotes { get; set; }

    public string? InternalNotes { get; set; }

    public IReadOnlyCollection<OrderLine> Lines => _lines;

    public static Order CreateSubmitted(
        Guid customerId,
        Guid salesChannelId,
        DateOnly orderDate,
        DateTimeOffset submittedAt,
        int sequenceNumber,
        OrderSubmissionEvaluation evaluation,
        IEnumerable<OrderLine> lines,
        Customer? customer = null)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            SalesChannelId = salesChannelId,
            OrderDate = orderDate,
            SubmittedAt = submittedAt,
            Status = evaluation.Status,
            SequenceNumber = sequenceNumber,
            IsLate = evaluation.IsLate,
            RequiresAdminReview = evaluation.RequiresAdminReview,
            AdminReviewReason = evaluation.ReviewReason,
            AdminDecision = evaluation.RequiresAdminReview ? Enums.AdminDecision.Pending : null,
            RequestedDeliveryTime = customer?.PreferredDeliveryTime,
            RequestedDeliveryWindowStart = customer?.PreferredDeliveryWindowStart,
            RequestedDeliveryWindowEnd = customer?.PreferredDeliveryWindowEnd,
            DeliveryNotes = customer?.DeliveryNotes
        };

        foreach (var line in lines)
        {
            line.OrderId = order.Id;
            order._lines.Add(line);
        }

        return order;
    }

    public static Order CreateNoOrder(
        Guid customerId,
        Guid salesChannelId,
        DateOnly orderDate,
        DateTimeOffset submittedAt,
        int sequenceNumber,
        Customer? customer = null)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            SalesChannelId = salesChannelId,
            OrderDate = orderDate,
            SubmittedAt = submittedAt,
            Status = OrderStatus.NoOrder,
            SequenceNumber = sequenceNumber,
            IsLate = false,
            RequiresAdminReview = false,
            RequestedDeliveryTime = customer?.PreferredDeliveryTime,
            RequestedDeliveryWindowStart = customer?.PreferredDeliveryWindowStart,
            RequestedDeliveryWindowEnd = customer?.PreferredDeliveryWindowEnd,
            DeliveryNotes = customer?.DeliveryNotes
        };
    }

    public void ApplyAdminDecision(AdminDecision decision)
    {
        AdminDecision = decision;

        Status = decision switch
        {
            Enums.AdminDecision.Accepted or Enums.AdminDecision.AcceptedWithChanges => OrderStatus.Accepted,
            Enums.AdminDecision.Rejected => OrderStatus.Rejected,
            _ => Status
        };

        if (decision is Enums.AdminDecision.Accepted or Enums.AdminDecision.AcceptedWithChanges or Enums.AdminDecision.Rejected)
        {
            RequiresAdminReview = false;
        }
    }

    public void ApplyDeliveryChanges(
        TimeOnly? requestedDeliveryTime,
        TimeOnly? requestedDeliveryWindowStart,
        TimeOnly? requestedDeliveryWindowEnd,
        string? deliveryNotes)
    {
        if (requestedDeliveryTime is not null)
        {
            RequestedDeliveryTime = requestedDeliveryTime;
        }

        if (requestedDeliveryWindowStart is not null)
        {
            RequestedDeliveryWindowStart = requestedDeliveryWindowStart;
        }

        if (requestedDeliveryWindowEnd is not null)
        {
            RequestedDeliveryWindowEnd = requestedDeliveryWindowEnd;
        }

        if (deliveryNotes is not null)
        {
            DeliveryNotes = deliveryNotes;
        }
    }
}
