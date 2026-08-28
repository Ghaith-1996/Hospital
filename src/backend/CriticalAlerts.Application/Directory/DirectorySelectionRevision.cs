using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Directory;

public static class DirectorySelectionRevision
{
    public static string Compute(DirectorySelectionRevisionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Roles);
        ArgumentNullException.ThrowIfNull(snapshot.SourceRecords);
        ArgumentNullException.ThrowIfNull(snapshot.ActiveOnCallAssignments);
        ArgumentNullException.ThrowIfNull(snapshot.AvailableChannels);

        var canonical = new StringBuilder();
        Append(canonical, "directory-selection-revision-v1");
        Append(canonical, snapshot.OrganizationId.Value.ToString("D"));
        Append(canonical, snapshot.PractitionerId.Value.ToString("D"));
        Append(canonical, snapshot.Specialty);
        Append(canonical, snapshot.IsActive ? "active" : "inactive");

        foreach (var role in snapshot.Roles.OrderBy(role => role.PractitionerRoleId.Value))
        {
            Append(canonical, "role");
            Append(canonical, role.PractitionerRoleId.Value.ToString("D"));
            Append(canonical, role.DepartmentId.Value.ToString("D"));
            Append(canonical, role.Title);
            Append(canonical, role.IsPrimary ? "primary" : "secondary");
        }

        foreach (var source in snapshot.SourceRecords
                     .OrderBy(source => source.SourceSystem, StringComparer.Ordinal)
                     .ThenBy(source => source.SourceRecordId, StringComparer.Ordinal))
        {
            Append(canonical, "source");
            Append(canonical, source.SourceSystem);
            Append(canonical, source.SourceRecordId);
            Append(canonical, FormatUtc(source.SourceUpdatedAtUtc));
            Append(canonical, FormatUtc(source.LastSeenAtUtc));
            Append(canonical, source.IsStale ? "stale" : "current");
        }

        foreach (var assignment in snapshot.ActiveOnCallAssignments
                     .OrderBy(assignment => assignment.SiteId.Value)
                     .ThenBy(assignment => assignment.DepartmentId.Value)
                     .ThenBy(assignment => assignment.Tier)
                     .ThenBy(assignment => assignment.StartsAtUtc))
        {
            Append(canonical, "on-call");
            Append(canonical, assignment.SiteId.Value.ToString("D"));
            Append(canonical, assignment.DepartmentId.Value.ToString("D"));
            Append(canonical, assignment.Tier.ToString());
            Append(canonical, FormatUtc(assignment.StartsAtUtc));
            Append(canonical, FormatUtc(assignment.EndsAtUtc));
            Append(canonical, FormatUtc(assignment.LastSynchronizedAtUtc));
        }

        foreach (var channel in snapshot.AvailableChannels.OrderBy(channel => channel))
        {
            Append(canonical, "channel");
            Append(canonical, channel.ToString());
        }

        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void Append(StringBuilder canonical, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        canonical.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new NonUtcTimestampException("Directory selection revision timestamps must be UTC.");
        }

        return value.ToString("O", CultureInfo.InvariantCulture);
    }
}
