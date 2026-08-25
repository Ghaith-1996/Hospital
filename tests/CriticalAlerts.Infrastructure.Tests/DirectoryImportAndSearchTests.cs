using CriticalAlerts.Application.Directory;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Infrastructure.Directory;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class DirectoryImportAndSearchTests(MigratedPostgresFixture fixture)
{
    private static readonly UserId Actor = DemoDataSeeder.MorganUserId;

    [Fact]
    public async Task PreviewDoesNotMutateTheDirectory()
    {
        await using var db = fixture.CreateContext();
        var before = await db.Practitioners.CountAsync(practitioner => practitioner.OrganizationId == DemoDataSeeder.OrganizationId);
        var beforeSources = await db.DirectorySourceRecords.CountAsync(record => record.SourceSystem == DirectorySourceSystems.Csv);
        var service = CreateService(db);

        await using var stream = File.OpenRead(FixturePath());
        var preview = await service.PreviewAsync(DemoDataSeeder.OrganizationId, Actor, "corr-preview", stream, new CsvDirectorySourceAdapter(), CancellationToken.None);

        preview.Errors.Should().BeEmpty();
        preview.UpdateCount.Should().Be(12);
        preview.InsertCount.Should().Be(0);
        (await db.Practitioners.CountAsync(practitioner => practitioner.OrganizationId == DemoDataSeeder.OrganizationId)).Should().Be(before);
        (await db.DirectorySourceRecords.CountAsync(record => record.SourceSystem == DirectorySourceSystems.Csv)).Should().Be(beforeSources);
        (await db.DirectorySyncRuns.CountAsync(run => run.CorrelationId == "corr-preview")).Should().Be(0);
    }

    [Fact]
    public async Task ApplyReconcilesTheCsvAdapterWithoutMatchingByName()
    {
        await fixture.ResetAsync();
        try
        {
            await using var db = fixture.CreateContext();
            var service = CreateService(db);
            await using (var stream = File.OpenRead(FixturePath()))
            {
                var applied = await service.ApplyAsync(DemoDataSeeder.OrganizationId, Actor, "corr-apply", stream, new CsvDirectorySourceAdapter(), CancellationToken.None);
                applied.Applied.Should().BeTrue();
                applied.Preview.UpdateCount.Should().Be(12);
            }

            (await db.Practitioners.CountAsync(practitioner => practitioner.OrganizationId == DemoDataSeeder.OrganizationId)).Should().Be(12);
            (await db.DirectorySourceRecords.CountAsync(record => record.SourceSystem == DirectorySourceSystems.Csv)).Should().Be(12);
            var taylor = await db.Practitioners.SingleAsync(practitioner => practitioner.Id == DemoDataSeeder.TaylorKimId);
            taylor.IsActive.Should().BeFalse();
            var taylorSource = await db.DirectorySourceRecords.SingleAsync(record =>
                record.SourceSystem == DirectorySourceSystems.Csv && record.SourceRecordId == "SIM-SRC-TAYLOR");
            taylorSource.IsStale.Should().BeTrue();
            (await db.AuditEvents.CountAsync(item => item.Action == "directory.import.applied" && item.CorrelationId == "corr-apply")).Should().Be(1);
            (await db.OutboxMessages.CountAsync()).Should().Be(0);
        }
        finally
        {
            await fixture.ResetAsync();
        }
    }

    [Fact]
    public async Task NameCollisionWarnsAndDoesNotMergeDifferentSourceRecords()
    {
        await fixture.ResetAsync();
        try
        {
            await using var db = fixture.CreateContext();
            var service = CreateService(db);
            const string csv = """
                source_record_id,first_name,last_name,simulation_code,specialty,site_code,department_code,role_title,is_primary_role,is_active,source_updated_at_utc,freshness_status
                SIM-SRC-MAYA-2,Maya,Chen,SIM-PRAC-0999,Emergency,SIM-SITE-NORTH,SIM-DEPT-EMERGENCY,Emergency physician,true,true,2026-08-01T12:00:00Z,current
                """;
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
            var applied = await service.ApplyAsync(DemoDataSeeder.OrganizationId, Actor, "corr-collision", stream, new CsvDirectorySourceAdapter(), CancellationToken.None);

            applied.Applied.Should().BeTrue();
            applied.Preview.InsertCount.Should().Be(1);
            applied.Preview.Warnings.Should().Contain(warning => warning.Code == "name-collision-not-matched");
            (await db.Practitioners.CountAsync(practitioner =>
                practitioner.OrganizationId == DemoDataSeeder.OrganizationId
                && practitioner.FirstName == "Maya"
                && practitioner.LastName == "Chen")).Should().Be(2);
            (await db.Practitioners.CountAsync(practitioner => practitioner.SimulationCode == "SIM-PRAC-0101")).Should().Be(1);
            (await db.Practitioners.CountAsync(practitioner => practitioner.SimulationCode == "SIM-PRAC-0999")).Should().Be(1);
        }
        finally
        {
            await fixture.ResetAsync();
        }
    }

    [Fact]
    public async Task SearchReturnsSimilarNamesAndBlocksInactiveSelection()
    {
        await using var db = fixture.CreateContext();
        var search = new DirectorySearchService(db);

        var martins = await search.SearchAsync(new DirectorySearchQuery(DemoDataSeeder.OrganizationId, "Martin", true), CancellationToken.None);
        var taylor = (await search.SearchAsync(new DirectorySearchQuery(DemoDataSeeder.OrganizationId, "Kim", true), CancellationToken.None))
            .Single(item => item.SimulationCode == "SIM-PRAC-0111");
        var foreign = await search.SearchAsync(new DirectorySearchQuery(OrganizationId.New(), "Martin", true), CancellationToken.None);

        martins.Select(item => item.SimulationCode).Should().BeEquivalentTo("SIM-PRAC-0103", "SIM-PRAC-0106");
        martins.Should().OnlyContain(item => item.Selectable);
        martins.Should().OnlyContain(item => item.Specialty.Length > 0 && item.SimulationCode.StartsWith("SIM-PRAC-", StringComparison.Ordinal));
        taylor.IsActive.Should().BeFalse();
        taylor.Selectable.Should().BeFalse();
        taylor.IsStale.Should().BeTrue();
        foreign.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownDepartmentIsABlockingConflict()
    {
        await using var db = fixture.CreateContext();
        var service = CreateService(db);
        const string csv = """
            source_record_id,first_name,last_name,simulation_code,specialty,site_code,department_code,role_title,is_primary_role,is_active,source_updated_at_utc,freshness_status
            SIM-SRC-MAYA,Maya,Chen,SIM-PRAC-0101,Emergency,SIM-SITE-NORTH,SIM-DEPT-UNKNOWN,Emergency physician,true,true,2026-08-01T12:00:00Z,current
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var preview = await service.PreviewAsync(DemoDataSeeder.OrganizationId, Actor, "corr-unknown", stream, new CsvDirectorySourceAdapter(), CancellationToken.None);

        preview.Errors.Should().Contain(error => error.Code == "unknown-department");
        preview.Changes.Should().BeEmpty();
    }

    private DirectoryImportService CreateService(CriticalAlertsDbContext db)
        => new(db, AesGcmSensitiveDataProtector.FromBase64(fixture.DataProtectionKey), TimeProvider.System);

    private static string FixturePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "fixtures", "simulation", "directory-harborview.csv");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate fixtures/simulation/directory-harborview.csv.");
    }
}
