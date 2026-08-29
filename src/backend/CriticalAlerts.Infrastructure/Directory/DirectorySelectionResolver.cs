using System.Security.Cryptography;
using System.Text;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Directory;

public sealed class DirectorySelectionResolver(CriticalAlertsDbContext db) : IDirectorySelectionResolver
{
    public async Task<IReadOnlyList<ValidatedRecipientSelection>> ResolveAsync(
        OrganizationId organizationId,
        IReadOnlyCollection<DirectorySelectionCandidate> candidates,
        DateTimeOffset selectedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (selectedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new NonUtcTimestampException($"{nameof(selectedAtUtc)} must be stored as UTC.");
        }

        var selectedAt = selectedAtUtc;
        var candidateArray = candidates.ToArray();
        ValidateCandidates(candidateArray);

        var practitionerIds = candidateArray.Select(candidate => candidate.PractitionerId).Distinct().ToArray();
        var practitioners = await db.Practitioners
            .AsNoTracking()
            .Where(practitioner => practitioner.OrganizationId == organizationId && practitionerIds.Contains(practitioner.Id))
            .ToDictionaryAsync(practitioner => practitioner.Id, cancellationToken);
        var roles = await db.PractitionerRoles
            .AsNoTracking()
            .Where(role => role.OrganizationId == organizationId && practitionerIds.Contains(role.PractitionerId))
            .ToArrayAsync(cancellationToken);
        var sourceRecords = (await db.DirectorySourceRecords
                .AsNoTracking()
                .Where(record => record.OrganizationId == organizationId)
                .ToArrayAsync(cancellationToken))
            .Where(record => record.PractitionerId is PractitionerId mapped && practitionerIds.Contains(mapped))
            .ToArray();
        var onCallAssignments = await db.OnCallAssignments
            .AsNoTracking()
            .Where(assignment => assignment.OrganizationId == organizationId && practitionerIds.Contains(assignment.PractitionerId))
            .ToArrayAsync(cancellationToken);
        var endpointKinds = await db.ContactEndpoints
            .AsNoTracking()
            .Where(endpoint => endpoint.OrganizationId == organizationId
                && practitionerIds.Contains(endpoint.PractitionerId)
                && endpoint.IsActive)
            .Select(endpoint => new { endpoint.PractitionerId, endpoint.Kind })
            .ToArrayAsync(cancellationToken);

        var resolved = new List<ValidatedRecipientSelection>(candidateArray.Length);
        foreach (var candidate in candidateArray)
        {
            if (!practitioners.TryGetValue(candidate.PractitionerId, out var practitioner))
            {
                throw new DirectorySelectionValidationException(
                    "recipient-not-found",
                    "The selected practitioner is not available in this organization.");
            }

            if (!practitioner.IsActive)
            {
                throw new DirectorySelectionValidationException(
                    "recipient-inactive",
                    "Inactive practitioners cannot be selected.");
            }

            var practitionerRoles = roles
                .Where(role => role.PractitionerId == practitioner.Id)
                .OrderByDescending(role => role.IsPrimary)
                .ThenBy(role => role.Title, StringComparer.Ordinal)
                .ThenBy(role => role.Id.Value)
                .ToArray();
            if (candidate.PractitionerRoleId is PractitionerRoleId requestedRole
                && practitionerRoles.All(role => role.Id != requestedRole))
            {
                throw new DirectorySelectionValidationException(
                    "recipient-role-invalid",
                    "The selected practitioner role is not assigned to the practitioner.");
            }

            var availableChannels = endpointKinds
                .Where(endpoint => endpoint.PractitionerId == practitioner.Id)
                .Select(endpoint => ToNotificationChannel(endpoint.Kind))
                .Distinct()
                .OrderBy(channel => channel)
                .ToArray();
            if (!availableChannels.Contains(candidate.Channel))
            {
                throw new DirectorySelectionValidationException(
                    "recipient-channel-unavailable",
                    "The selected channel is not available for the practitioner.");
            }

            var practitionerSourceRecords = sourceRecords
                .Where(record => record.PractitionerId == practitioner.Id)
                .ToArray();
            var currentOnCallAssignments = onCallAssignments
                .Where(assignment => assignment.PractitionerId == practitioner.Id
                    && assignment.StartsAtUtc <= selectedAt
                    && selectedAt < assignment.EndsAtUtc)
                .ToArray();
            var revision = DirectorySelectionRevision.Compute(new DirectorySelectionRevisionSnapshot(
                organizationId,
                practitioner.Id,
                practitioner.Specialty,
                practitioner.IsActive,
                practitionerRoles
                    .Select(role => new DirectoryRoleRevision(role.Id, role.DepartmentId, role.Title, role.IsPrimary))
                    .ToArray(),
                practitionerSourceRecords
                    .Select(record => new DirectorySourceRevision(
                        record.SourceSystem,
                        record.SourceRecordId,
                        record.SourceUpdatedAtUtc,
                        record.LastSeenAtUtc,
                        record.IsStale))
                    .ToArray(),
                currentOnCallAssignments
                    .Select(assignment => new DirectoryOnCallRevision(
                        assignment.SiteId,
                        assignment.DepartmentId,
                        assignment.Tier,
                        assignment.StartsAtUtc,
                        assignment.EndsAtUtc,
                        assignment.LastSynchronizedAtUtc))
                    .ToArray(),
                availableChannels));
            if (!FixedEquals(revision, candidate.PresentedRevision))
            {
                throw new DirectorySelectionRevisionConflictException();
            }

            var latestSource = practitionerSourceRecords
                .OrderByDescending(record => record.LastSeenAtUtc)
                .FirstOrDefault();
            var latestOnCall = currentOnCallAssignments
                .OrderByDescending(assignment => assignment.LastSynchronizedAtUtc)
                .FirstOrDefault();
            resolved.Add(new ValidatedRecipientSelection(
                practitioner.Id,
                candidate.PractitionerRoleId,
                candidate.Channel,
                revision,
                latestSource?.SourceUpdatedAtUtc,
                latestOnCall?.Tier.ToString()));
        }

        return resolved;
    }

    private static void ValidateCandidates(IReadOnlyCollection<DirectorySelectionCandidate> candidates)
    {
        var pairs = new HashSet<(PractitionerId PractitionerId, NotificationChannel Channel)>();
        foreach (var candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (!IsSafeRevision(candidate.PresentedRevision))
            {
                throw new DirectorySelectionValidationException(
                    "invalid-directory-revision",
                    "The selected directory revision is invalid. Reload and reselect recipients.");
            }

            if (!pairs.Add((candidate.PractitionerId, candidate.Channel)))
            {
                throw new DirectorySelectionValidationException(
                    "duplicate-recipient",
                    "Each practitioner and channel pair may be selected only once.");
            }
        }
    }

    private static bool IsSafeRevision(string value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 128
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool FixedEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));

    private static NotificationChannel ToNotificationChannel(ContactEndpointKind kind)
        => kind switch
        {
            ContactEndpointKind.SecureMessage => NotificationChannel.SecureMessage,
            ContactEndpointKind.Sms => NotificationChannel.Sms,
            ContactEndpointKind.Voice => NotificationChannel.Voice,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported contact endpoint kind."),
        };
}
