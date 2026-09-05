using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class AlertSourceRevisionPersistenceTests(MigratedPostgresFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditingPersistedDraftPreservesSourceInEveryRevision(bool editRecipients)
    {
        var now = DateTimeOffset.Parse("2026-09-05T12:00:00Z");
        var protector = AesGcmSensitiveDataProtector.FromBase64(fixture.DataProtectionKey);
        var sourceContext = new SensitiveDataContext(
            ProtectedValuePurposes.AlertTypedSource, DemoDataSeeder.OrganizationId.Value);
        var alert = Alert.CreateDraft(
            AlertId.New(), DemoDataSeeder.OrganizationId, DemoDataSeeder.NorthSiteId,
            DemoDataSeeder.EmergencyDepartmentId, DemoDataSeeder.JordanUserId,
            "SIM-PAT-SOURCE-CARRY",
            protector.Protect("SIM-PAT-SOURCE-CARRY", new SensitiveDataContext(
                ProtectedValuePurposes.AlertPatientReference, DemoDataSeeder.OrganizationId.Value)),
            "North Wing / Simulation Source Room", "DEMO-URGENT", AlertSourceType.Typed,
            protector.Protect("SIMULATION: original source retained after edits", sourceContext), now);

        await using (var db = fixture.CreateContext())
        {
            db.Alerts.Add(alert);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var loaded = await db.Alerts.Include(item => item.SourceRevisions)
                .SingleAsync(item => item.Id == alert.Id);
            if (editRecipients)
            {
                loaded.ReplaceRecipients(
                    [new ValidatedRecipientSelection(DemoDataSeeder.MayaChenId, null,
                        NotificationChannel.SecureMessage, "SIM-REV-SOURCE", now, "On-call not displayed")],
                    DemoDataSeeder.JordanUserId, loaded.DraftVersion, now.AddMinutes(1));
            }
            else
            {
                loaded.SetApprovedMessage(protector.Protect("SIMULATION: revised approved message",
                        new SensitiveDataContext(ProtectedValuePurposes.AlertApprovedMessage,
                            DemoDataSeeder.OrganizationId.Value)),
                    loaded.DraftVersion, now.AddMinutes(1));
            }

            await db.SaveChangesAsync();
        }

        await using var verification = fixture.CreateContext();
        var revisions = await verification.AlertSourceRevisions
            .Where(item => item.AlertId == alert.Id).ToArrayAsync();
        revisions.Select(item => item.AlertVersion.Value).Should().BeEquivalentTo([1, 2]);
        foreach (var revision in revisions)
        {
            protector.Unprotect(revision.Source, sourceContext)
                .Should().Be("SIMULATION: original source retained after edits");
        }
    }
}
