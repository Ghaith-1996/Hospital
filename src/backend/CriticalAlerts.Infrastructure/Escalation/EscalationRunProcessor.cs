using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Escalation;

public sealed record ActivatedEscalationStep(OrganizationId OrganizationId, AlertId AlertId, AlertDraftVersion AlertVersion,
    EscalationRunId EscalationRunId, int StepSequence, int MaxAttempts, IReadOnlyList<AlertRecipientSelectionId> RecipientSelectionIds,
    Guid CorrelationId, DateTimeOffset OccurredAtUtc);

// Not registered with the worker until identifier-only outbox integration is supplied.
// enqueueStep must stage its outbox/event/audit in this SAME DbContext transaction; a no-op is test-only.
public sealed class EscalationRunProcessor(CriticalAlertsDbContext db)
{
    public Task<bool> ProcessClaimAsync(EscalationClaim claim,
        Func<ActivatedEscalationStep, CancellationToken, Task> enqueueStep, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enqueueStep);
        return new EscalationRunRepository(db).ExecuteClaimAsync(claim, async (locked, cancellation) =>
        {
            var (alert, run, plan, now) = locked;
            var sequence = run.CurrentStep;
            var correlation = Guid.NewGuid();
            void Record(EscalationEventType type, EscalationFailureCategory? failure = null, AlertRecipientSelectionId? selection = null)
                => db.EscalationEvents.Add(EscalationEvent.Record(run, type, sequence, null, correlation, now, selection, failure));
            void Audit(string action, string outcome)
                => db.AuditEvents.Add(AuditEvent.Record(AuditEventId.New(), run.OrganizationId, "SimulationWorker", null,
                    action, "EscalationRun", run.Id.Value, outcome, correlation.ToString("D"), "{}", now));
            if (alert.State is AlertState.Resolved or AlertState.Cancelled)
            {
                run.Stop(alert.State, claim.LeaseOwner, now);
                Record(alert.State == AlertState.Resolved ? EscalationEventType.StoppedByResolution : EscalationEventType.StoppedByCancellation);
                Audit("escalation-stopped", run.Outcome!.Value.ToString());
                return;
            }
            var responsibility = await db.ResponsibilityAssignments.AsNoTracking().Where(x => x.OrganizationId == run.OrganizationId
                && x.AlertId == run.AlertId && x.AlertVersion == run.AlertVersion && x.ReleasedAtUtc == null && x.AcceptedAtUtc <= now)
                .OrderBy(x => x.AcceptedAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(cancellation);
            if (responsibility is not null)
            {
                run.Stop(responsibility, claim.LeaseOwner, now);
                Record(EscalationEventType.StoppedByResponsibility);
                Audit("escalation-stopped", "ResponsibilityAccepted");
                return;
            }
            if (run.State == EscalationRunState.Paused || alert.State != AlertState.Active) return;

            // Bounded DEMO tradeoff: SHARE table locks prevent membership/endpoint changes and phantoms
            // from passing validation and changing before commit. Same directory order as confirmation.
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE contact_endpoints, practitioner_roles, practitioners IN SHARE MODE", cancellation);
            now = await new DatabaseClock(db).GetUtcNowAsync(cancellation);
            var selections = await db.AlertRecipientSelections.AsNoTracking().Where(x => x.OrganizationId == run.OrganizationId
                && x.AlertId == run.AlertId && x.AlertVersion == run.AlertVersion).ToArrayAsync(cancellation);
            var consumed = run.ConsumedSignals.Select(x => x.ResponseId).ToHashSet();
            var responses = await db.RecipientResponses.AsNoTracking().Where(x => x.OrganizationId == run.OrganizationId && x.AlertId == run.AlertId
                && x.AlertVersion == run.AlertVersion && (x.ResponseType == RecipientResponseType.Declined || x.ResponseType == RecipientResponseType.Unavailable)
                && x.OccurredAtUtc <= now).OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).ToArrayAsync(cancellation);
            if (!run.ConsumedSignals.Any(x => x.StepSequence == sequence))
            {
                foreach (var response in responses.Where(x => !consumed.Contains(x.Id)))
                {
                    var source = selections.Where(x => x.PractitionerId == response.PractitionerId && x.SelectedAtUtc <= response.OccurredAtUtc)
                        .OrderBy(x => x.Id.Value).FirstOrDefault();
                    if (source is null) continue;
                    db.EscalationConsumedSignals.Add(run.ConsumeSignal(response, source, claim.LeaseOwner, now));
                    break;
                }
            }
            if (run.NextDueAtUtc > now) return;
            run.BeginProcessing(claim.LeaseOwner, now);
            Record(EscalationEventType.StepDue);
            var step = plan.Definition.Steps.SingleOrDefault(x => x.Sequence == sequence);
            var snapshots = await db.AlertEscalationRecipientSnapshots.AsNoTracking().Where(x => x.OrganizationId == run.OrganizationId
                && x.PlanId == plan.Id && x.StepSequence == sequence).ToArrayAsync(cancellation);
            var invalidPlan = step is null || snapshots.Length != step.Recipients.Count || snapshots.Length == 0
                || snapshots.Any(s => !Matches(plan, s, step!) || selections.Any(v => v.PractitionerId == s.PractitionerId && v.Channel == s.Channel));
            var failure = invalidPlan ? EscalationFailureCategory.ConfirmedPlanInvalid : (EscalationFailureCategory?)null;
            if (failure is null)
            {
                // Validate every confirmed member before adding any selection; never query for a substitute.
                foreach (var snapshot in snapshots)
                {
                    var practitionerValid = await db.Practitioners.AsNoTracking().AnyAsync(x => x.OrganizationId == run.OrganizationId
                        && x.Id == snapshot.PractitionerId && x.IsActive, cancellation);
                    var roleValid = await db.PractitionerRoles.AsNoTracking().AnyAsync(x => x.OrganizationId == run.OrganizationId
                        && x.Id == snapshot.PractitionerRoleId && x.PractitionerId == snapshot.PractitionerId, cancellation);
                    var kind = Enum.Parse<ContactEndpointKind>(snapshot.Channel.ToString());
                    var endpoint = await db.ContactEndpoints.AsNoTracking().Where(x => x.OrganizationId == run.OrganizationId
                        && x.PractitionerId == snapshot.PractitionerId && x.Kind == kind && x.IsActive)
                        .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Id)
                        .Select(x => new { x.SimulationLabel }).FirstOrDefaultAsync(cancellation);
                    // Match existing dispatch selection and safe reference rules; do not fall back to another endpoint.
                    var channelValid = endpoint is not null && endpoint.SimulationLabel.Length <= 100
                        && endpoint.SimulationLabel.StartsWith("SIM-", StringComparison.Ordinal)
                        && endpoint.SimulationLabel.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_');
                    if (!practitionerValid || !roleValid || !channelValid) failure = EscalationFailureCategory.ConfirmedRecipientUnavailable;
                }
            }
            if (failure is { } category)
            {
                run.Fail(category, claim.LeaseOwner, now);
                Record(EscalationEventType.ProcessingFailed, category);
                Audit("escalation-processing-failed", category.ToString());
                return;
            }
            var activated = snapshots.OrderBy(x => x.PractitionerId.Value).ThenBy(x => x.Channel).Select(snapshot => new AlertRecipientSelection(
                AlertRecipientSelectionId.New(), run.OrganizationId, run.AlertId, run.AlertVersion!.Value, snapshot.PractitionerId,
                snapshot.PractitionerRoleId, snapshot.Channel, snapshot.ConfirmedByUserId, now, snapshot.DirectoryRevision,
                snapshot.DirectorySourceUpdatedAtUtc, snapshot.OnCallSnapshot, RecipientSelectionSource.EscalationPolicy)).ToArray();
            db.AlertRecipientSelections.AddRange(activated);
            foreach (var selection in activated) Record(EscalationEventType.RecipientActivated, selection: selection.Id);
            Audit("escalation-recipients-activated", "activated");
            await enqueueStep(new(run.OrganizationId, run.AlertId, run.AlertVersion!.Value, run.Id, sequence, step!.MaxAttempts,
                activated.Select(x => x.Id).ToArray(), correlation, now), cancellation);
            run.Advance(plan, sequence, claim.LeaseOwner, now);
            if (run.Outcome == EscalationOutcome.Exhausted)
            {
                Record(EscalationEventType.Exhausted);
                Audit("escalation-exhausted", "manual-fallback");
            }
        }, cancellationToken);
    }

    private static bool Matches(AlertEscalationPlan plan, AlertEscalationRecipientSnapshot snapshot, EscalationStepSnapshot step)
        => snapshot.AlertId == plan.AlertId && snapshot.AlertVersion == plan.AlertVersion && snapshot.EscalationPolicyId == plan.EscalationPolicyId
            && snapshot.EscalationPolicyVersion == plan.EscalationPolicyVersion && snapshot.PlanRevision == plan.Revision
            && snapshot.ConfirmedByUserId == plan.ConfirmedByUserId && snapshot.ConfirmedAtUtc == plan.ConfirmedAtUtc
            && step.Recipients.Any(x => x.PractitionerId == snapshot.PractitionerId.Value && x.PractitionerRoleId == snapshot.PractitionerRoleId.Value
                && x.Channel == snapshot.Channel.ToString() && x.DirectoryRevision == snapshot.DirectoryRevision
                && x.DirectorySourceUpdatedAtUtc == snapshot.DirectorySourceUpdatedAtUtc && x.OnCallSnapshot == snapshot.OnCallSnapshot);
}
