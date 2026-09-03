using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class RecipientResponsePersistenceTests(MigratedPostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-30T14:00:00Z");

    [Fact]
    public async Task DemoSeedLinksRileyUserToRileyPractitionerByStableIds()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        var link = await db.PractitionerUserLinks.SingleAsync();

        link.OrganizationId.Should().Be(DemoDataSeeder.OrganizationId);
        link.UserId.Should().Be(DemoDataSeeder.RileyUserId);
        link.PractitionerId.Should().Be(DemoDataSeeder.RileySatoId);
    }

    [Fact]
    public async Task LinkedRileyPractitionerHasASyntheticSecureMessageEndpoint()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();

        var endpoint = await db.ContactEndpoints.SingleAsync(item =>
            item.PractitionerId == DemoDataSeeder.RileySatoId
            && item.Kind == ContactEndpointKind.SecureMessage);

        endpoint.SimulationLabel.Should().Be("SIM-SECURE-0108");
    }

    [Fact]
    public async Task ReRunningSeedAddsAMissingPractitionerLinkToAnExistingOrganization()
    {
        await fixture.ResetAsync();
        await using (var remove = fixture.CreateContext())
        {
            remove.PractitionerUserLinks.Remove(await remove.PractitionerUserLinks.SingleAsync());
            await remove.SaveChangesAsync();
        }

        await using (var seed = fixture.CreateContext())
        {
            await new DemoDataSeeder(seed, fixture.DataProtectionKey).SeedAsync();
        }

        await using var verify = fixture.CreateContext();
        (await verify.PractitionerUserLinks.CountAsync(link =>
                link.UserId == DemoDataSeeder.RileyUserId
                && link.PractitionerId == DemoDataSeeder.RileySatoId))
            .Should().Be(1);
    }

    [Fact]
    public async Task OneUserCannotLinkToTwoPractitionersInTheSameOrganization()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        db.PractitionerUserLinks.Add(PractitionerUserLink.Create(
            PractitionerUserLinkId.New(),
            DemoDataSeeder.OrganizationId,
            DemoDataSeeder.RileyUserId,
            DemoDataSeeder.MayaChenId,
            Now));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DuplicateAcknowledgementForTheSamePractitionerVersionIsRejected()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var alert = await CreateAlertAsync(db);
        db.RecipientResponses.Add(Response(alert, RecipientResponseType.Acknowledged));
        await db.SaveChangesAsync();
        db.RecipientResponses.Add(Response(alert, RecipientResponseType.Acknowledged));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ConflictingTerminalDispositionForTheSamePractitionerVersionIsRejected()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var alert = await CreateAlertAsync(db);
        db.RecipientResponses.Add(Response(alert, RecipientResponseType.Declined));
        await db.SaveChangesAsync();
        db.RecipientResponses.Add(Response(alert, RecipientResponseType.Unavailable));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private static RecipientResponse Response(Alert alert, RecipientResponseType responseType)
        => RecipientResponse.Record(
            RecipientResponseId.New(),
            alert.OrganizationId,
            alert.Id,
            alert.DraftVersion,
            DemoDataSeeder.RileySatoId,
            responseType,
            DemoDataSeeder.RileyUserId,
            Now,
            responseType switch
            {
                RecipientResponseType.Acknowledged => "simulation-acknowledged",
                RecipientResponseType.Declined => "simulation-declined",
                RecipientResponseType.Unavailable => "simulation-unavailable",
                _ => "simulation-responsibility-accepted",
            });

    private static async Task<Alert> CreateAlertAsync(CriticalAlertsDbContext db)
    {
        var alert = Alert.CreateDraft(
            AlertId.New(),
            DemoDataSeeder.OrganizationId,
            DemoDataSeeder.NorthSiteId,
            DemoDataSeeder.EmergencyDepartmentId,
            DemoDataSeeder.JordanUserId,
            "SIM-PAT-PHASE8",
            "North Wing Simulation Room 8",
            "DEMO-URGENT",
            AlertSourceType.Typed,
            new ProtectedValue([1, 2, 3], "test-v1", "alert-source"),
            Now);
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        return alert;
    }
}
