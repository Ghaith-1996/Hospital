using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CriticalAlerts.Domain.Escalation;

public sealed record EscalationRecipientEvidence(
    Guid PractitionerId, Guid PractitionerRoleId, string DisplayName, string RoleTitle,
    string Channel, string DirectoryRevision, DateTimeOffset? DirectorySourceUpdatedAtUtc,
    string OnCallSnapshot);

public sealed record EscalationStepSnapshot(
    int Sequence, long DelaySeconds, int MaxAttempts, string RecipientSource,
    IReadOnlyList<EscalationRecipientEvidence> Recipients);

public sealed record EscalationPlanDefinition(
    Guid OrganizationId, Guid AlertId, int AlertVersion, Guid PolicyId, string PolicyVersion,
    string TriggerCondition, string StopCondition, string PrimaryEvidenceRevision,
    IReadOnlyList<EscalationStepSnapshot> Steps);

public sealed class AlertEscalationPlan
{
    public const int DemoMaximumAttempts = 10;
    private AlertEscalationPlan() { }

    public AlertEscalationPlanId Id { get; private set; }
    public OrganizationId OrganizationId { get; private set; }
    public AlertId AlertId { get; private set; }
    public int AlertVersion { get; private set; }
    public EscalationPolicyId EscalationPolicyId { get; private set; }
    public string EscalationPolicyVersion { get; private set; } = string.Empty;
    public string Revision { get; private set; } = string.Empty;
    public string DefinitionJson { get; private set; } = string.Empty;
    public UserId ConfirmedByUserId { get; private set; }
    public DateTimeOffset ConfirmedAtUtc { get; private set; }
    // Deserialize a fresh object so callers cannot mutate persisted confirmation evidence.
    public EscalationPlanDefinition Definition => JsonSerializer.Deserialize<EscalationPlanDefinition>(DefinitionJson)!;

    public static string CanonicalJson(EscalationPlanDefinition definition)
    {
        if (definition.OrganizationId == Guid.Empty || definition.AlertId == Guid.Empty
            || definition.PolicyId == Guid.Empty || definition.AlertVersion < 1
            || string.IsNullOrWhiteSpace(definition.PolicyVersion) || definition.Steps.Count == 0)
            throw new DomainException("An exact escalation plan requires organization, alert version and policy evidence.");
        var pairs = new HashSet<(Guid, string)>();
        var steps = definition.Steps.OrderBy(s => s.Sequence).Select((step, index) =>
        {
            if (step.Sequence != index + 1 || step.DelaySeconds < 0 || step.MaxAttempts < 1 || step.MaxAttempts > DemoMaximumAttempts
                || step.Recipients.Count == 0 || string.IsNullOrWhiteSpace(step.RecipientSource))
                throw new DomainException("Escalation steps require contiguous sequence, DEMO delay and bounded attempts.");
            foreach (var recipient in step.Recipients)
            {
                if (recipient.PractitionerId == Guid.Empty || recipient.PractitionerRoleId == Guid.Empty
                    || string.IsNullOrWhiteSpace(recipient.DirectoryRevision)
                    || !Enum.TryParse<NotificationChannel>(recipient.Channel, out var channel) || !Enum.IsDefined(channel)
                    || !pairs.Add((recipient.PractitionerId, recipient.Channel)))
                    throw new DomainException("Each future practitioner/channel must be valid and unique across the plan.");
                if (recipient.DirectorySourceUpdatedAtUtc is { } at) UtcInstant.Require(at, nameof(at));
            }
            return step with { Recipients = step.Recipients.OrderBy(r => r.PractitionerId).ThenBy(r => r.Channel, StringComparer.Ordinal).ToArray() };
        }).ToArray();
        return JsonSerializer.Serialize(definition with { Steps = steps });
    }

    public static string ComputeRevision(EscalationPlanDefinition definition)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson(definition))));

    public static AlertEscalationPlan Confirm(EscalationPlanDefinition definition, string expectedRevision, UserId actor, DateTimeOffset at)
    {
        if (actor.Value == Guid.Empty) throw new DomainException("Exact confirmation requires an actor.");
        var canonical = CanonicalJson(definition);
        var revision = ComputeRevision(definition);
        if (!string.Equals(expectedRevision, revision, StringComparison.Ordinal))
            throw new DomainException("The escalation plan revision changed.");
        return new AlertEscalationPlan
        {
            Id = AlertEscalationPlanId.New(),
            OrganizationId = new(definition.OrganizationId),
            AlertId = new(definition.AlertId),
            AlertVersion = definition.AlertVersion,
            EscalationPolicyId = new(definition.PolicyId),
            EscalationPolicyVersion = definition.PolicyVersion,
            Revision = revision,
            DefinitionJson = canonical,
            ConfirmedByUserId = actor,
            ConfirmedAtUtc = UtcInstant.Require(at, nameof(at)),
        };
    }
}
