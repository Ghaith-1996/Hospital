using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Dispatch;

public sealed class DeliveryStatusQueryService(CriticalAlertsDbContext db) : IDeliveryStatusQueryService
{
    public async Task<DeliveryStatusView?> GetAsync(
        OrganizationId organizationId,
        AlertId alertId,
        CancellationToken cancellationToken)
    {
        var alert = await db.Alerts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.OrganizationId == organizationId && item.Id == alertId,
                cancellationToken);
        if (alert is null)
        {
            return null;
        }

        var outbox = await db.OutboxMessages
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && item.AggregateId == alertId.Value
                && item.EventType == "AlertDispatchRequested")
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id.Value)
            .FirstOrDefaultAsync(cancellationToken);

        var attempts = await db.DeliveryAttempts
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.AlertId == alertId)
            .OrderBy(item => item.RecipientSelectionId.Value)
            .ThenBy(item => item.Channel)
            .ThenBy(item => item.AttemptNumber)
            .Select(item => new DeliveryAttemptView(
                item.RecipientSelectionId.Value,
                item.Channel.ToString(),
                item.AttemptNumber,
                item.Provider,
                item.Status.ToString(),
                item.OpenedState.ToString(),
                item.RequestedAtUtc,
                item.SubmittedAtUtc,
                item.DeliveredAtUtc,
                item.FailedAtUtc,
                string.IsNullOrEmpty(item.FailureCategory) ? null : item.FailureCategory))
            .ToArrayAsync(cancellationToken);

        return new DeliveryStatusView(
            alert.Id.Value,
            alert.ConfirmedDraftVersion?.Value ?? 0,
            alert.State.ToString(),
            outbox?.ProcessingState.ToString() ?? "NotCreated",
            attempts);
    }
}
