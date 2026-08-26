using System.Text;
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
            var applied = await PreviewThenApplyAsync(service, "corr-apply", File.ReadAllText(FixturePath()));
            applied.Applied.Should().BeTrue();
            applied.Preview.UpdateCount.Should().Be(12);

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
    public async Task ReapplyingTheSameCsvKeepsOneSourceRecordPerPractitionerAndRefreshesSyncState()
    {
        await fixture.ResetAsync();
        try
        {
            foreach (var correlationId in new[] { "corr-repeat-one", "corr-repeat-two" })
            {
                await using var db = fixture.CreateContext();
                var service = CreateService(db);
                var applied = await PreviewThenApplyAsync(service, correlationId, File.ReadAllText(FixturePath()));

                applied.Applied.Should().BeTrue();
            }

            await using var verify = fixture.CreateContext();
            (await verify.DirectorySourceRecords.CountAsync(record =>
                record.OrganizationId == DemoDataSeeder.OrganizationId
                && record.SourceSystem == DirectorySourceSystems.Csv)).Should().Be(12);
            (await verify.DirectorySyncRuns.CountAsync(run =>
                run.OrganizationId == DemoDataSeeder.OrganizationId
                && run.SourceSystem == DirectorySourceSystems.Csv
                && run.Status == DirectorySyncRunStatus.Succeeded)).Should().Be(2);
            (await verify.ContactEndpoints.CountAsync(endpoint => endpoint.OrganizationId == DemoDataSeeder.OrganizationId)).Should().Be(15);
            (await verify.ContactEndpoints.CountAsync(endpoint => endpoint.OrganizationId == DemoDataSeeder.OrganizationId && endpoint.SourceSystem == "SIM-DIRECTORY")).Should().Be(4);
            (await verify.OnCallAssignments.CountAsync(assignment =>
                assignment.OrganizationId == DemoDataSeeder.OrganizationId
                && assignment.SourceSystem == DirectorySourceSystems.Csv)).Should().Be(2);
            (await verify.OnCallAssignments.CountAsync(assignment =>
                assignment.OrganizationId == DemoDataSeeder.OrganizationId
                && assignment.SourceSystem == "SIM-DIRECTORY")).Should().Be(2);
        }
        finally
        {
            await fixture.ResetAsync();
        }
    }

    [Fact]
    public async Task ApplyRejectsBlockingConflictBeforeCreatingSyncRunOrSourceRecords()
    {
        await fixture.ResetAsync();
        try
        {
            await using var db = fixture.CreateContext();
            var beforeSources = await db.DirectorySourceRecords.CountAsync(record => record.SourceSystem == DirectorySourceSystems.Csv);
            var service = CreateService(db);
            const string csv = """
                source_record_id,first_name,last_name,simulation_code,specialty,site_code,department_code,role_title,is_primary_role,is_active,source_updated_at_utc,freshness_status
                SIM-SRC-MAYA,Maya,Chen,SIM-PRAC-0101,Emergency,SIM-SITE-NORTH,SIM-DEPT-UNKNOWN,Emergency physician,true,true,2026-08-01T12:00:00Z,current
                """;
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

            var applied = await service.ApplyAsync(
                DemoDataSeeder.OrganizationId,
                Actor,
                "corr-blocked-apply",
                stream,
                new CsvDirectorySourceAdapter(),
                CancellationToken.None);

            applied.Applied.Should().BeFalse();
            applied.SyncRunId.Should().BeNull();
            applied.Preview.Errors.Should().Contain(error => error.Code == "unknown-department");
            (await db.DirectorySourceRecords.CountAsync(record => record.SourceSystem == DirectorySourceSystems.Csv)).Should().Be(beforeSources);
            (await db.DirectorySyncRuns.CountAsync(run => run.CorrelationId == "corr-blocked-apply")).Should().Be(0);
        }
        finally
        {
            await fixture.ResetAsync();
        }
    }

    [Fact]
    public async Task ApplyRejectsSourceRecordSimulationCodeChangesBeforeMutation()
    {
        await fixture.ResetAsync();
        try
        {
            await using (var seedDb = fixture.CreateContext())
            {
                var seedService = CreateService(seedDb);
                (await PreviewThenApplyAsync(
                    seedService,
                    "corr-seed-for-code-conflict",
                    File.ReadAllText(FixturePath()))).Applied.Should().BeTrue();
            }

            await using var db = fixture.CreateContext();
            var beforeSources = await db.DirectorySourceRecords.CountAsync(record => record.SourceSystem == DirectorySourceSystems.Csv);
            const string csv = """
                source_record_id,first_name,last_name,simulation_code,specialty,site_code,department_code,role_title,is_primary_role,is_active,source_updated_at_utc,freshness_status
                SIM-SRC-MAYA,Maya,Chen,SIM-PRAC-0999,Emergency,SIM-SITE-NORTH,SIM-DEPT-EMERGENCY,Emergency physician,true,true,2026-08-01T12:00:00Z,current
                """;
            var applied = await PreviewThenApplyAsync(CreateService(db), "corr-code-conflict", csv);

            applied.Applied.Should().BeFalse();
            applied.SyncRunId.Should().BeNull();
            applied.Preview.Errors.Should().Contain(error => error.Code == "simulation-code-immutable");
            (await db.DirectorySourceRecords.CountAsync(record => record.SourceSystem == DirectorySourceSystems.Csv)).Should().Be(beforeSources);
            (await db.DirectorySyncRuns.CountAsync(run => run.CorrelationId == "corr-code-conflict")).Should().Be(0);
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
            var applied = await PreviewThenApplyAsync(service, "corr-collision", csv);

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
        var search = new DirectorySearchService(db, TimeProvider.System);

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
    public async Task ExpiredOnCallAssignmentsAreNotReportedAsCurrent()
    {
        await using var db = fixture.CreateContext();
        var search = new DirectorySearchService(db, TimeProvider.System);

        var maya = (await search.SearchAsync(
            new DirectorySearchQuery(DemoDataSeeder.OrganizationId, "SIM-PRAC-0101", true),
            CancellationToken.None)).Single();

        maya.OnCallTier.Should().BeNull();
        maya.OnCallSourceSystem.Should().BeNull();
    }

    [Fact]
    public async Task CrossOrganizationPreviewCannotUseTheSeededCatalog()
    {
        await using var db = fixture.CreateContext();
        var service = CreateService(db);
        var before = await db.Practitioners.CountAsync(practitioner => practitioner.OrganizationId == DemoDataSeeder.OrganizationId);

        await using var stream = File.OpenRead(FixturePath());
        var preview = await service.PreviewAsync(OrganizationId.New(), Actor, "corr-cross-org", stream, new CsvDirectorySourceAdapter(), CancellationToken.None);

        preview.Errors.Should().Contain(error => error.Code == "unknown-site");
        (await db.Practitioners.CountAsync(practitioner => practitioner.OrganizationId == DemoDataSeeder.OrganizationId)).Should().Be(before);
        preview.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task OnCallSiteAndDepartmentMustBelongTogether()
    {
        await using var db = fixture.CreateContext();
        var service = CreateService(db);
        const string csv = """
            source_record_id,first_name,last_name,simulation_code,specialty,site_code,department_code,role_title,is_primary_role,is_active,on_call_tier,on_call_starts_at_utc,on_call_ends_at_utc,source_updated_at_utc,freshness_status
            SIM-SRC-ONCALL-MISMATCH,Maya,Chen,SIM-PRAC-0101,Emergency,SIM-SITE-RIVERSIDE,SIM-DEPT-EMERGENCY,Emergency physician,true,true,Primary,2026-08-01T12:00:00Z,2026-08-08T12:00:00Z,2026-08-01T12:00:00Z,current
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var preview = await service.PreviewAsync(DemoDataSeeder.OrganizationId, Actor, "corr-on-call-mismatch", stream, new CsvDirectorySourceAdapter(), CancellationToken.None);

        preview.Errors.Should().Contain(error => error.Code == "site-department-mismatch");
        preview.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedChildMutationRollsBackTheWholeImportTransaction()
    {
        await fixture.ResetAsync();
        try
        {
            await using var db = fixture.CreateContext();
            var service = CreateService(db);
            var adapter = new InvalidOnCallAdapter();
            await using var previewStream = new MemoryStream();
            var preview = await service.PreviewAsync(DemoDataSeeder.OrganizationId, Actor, "corr-rollback-preview", previewStream, adapter, CancellationToken.None);
            preview.Errors.Should().BeEmpty();

            await using var applyStream = new MemoryStream();
            var act = () => service.ApplyAsync(
                DemoDataSeeder.OrganizationId,
                Actor,
                "corr-rollback",
                applyStream,
                adapter,
                CancellationToken.None,
                preview.PreviewToken);

            await act.Should().ThrowAsync<DomainException>();

            await using var verify = fixture.CreateContext();
            (await verify.Practitioners.CountAsync(practitioner => practitioner.SimulationCode == "SIM-PRAC-ROLLBACK")).Should().Be(0);
            (await verify.DirectorySourceRecords.CountAsync(record => record.SourceRecordId == "SIM-SRC-ROLLBACK")).Should().Be(0);
            (await verify.DirectorySyncRuns.CountAsync(run => run.CorrelationId == "corr-rollback")).Should().Be(0);
            (await verify.AuditEvents.CountAsync(audit => audit.CorrelationId == "corr-rollback")).Should().Be(0);
        }
        finally
        {
            await fixture.ResetAsync();
        }
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

    private async Task<DirectoryImportApplyResult> PreviewThenApplyAsync(
        DirectoryImportService service,
        string correlationId,
        string csv)
    {
        var adapter = new CsvDirectorySourceAdapter();
        await using var previewStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var preview = await service.PreviewAsync(
            DemoDataSeeder.OrganizationId,
            Actor,
            $"{correlationId}-preview",
            previewStream,
            adapter,
            CancellationToken.None);

        await using var applyStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return await service.ApplyAsync(
            DemoDataSeeder.OrganizationId,
            Actor,
            correlationId,
            applyStream,
            adapter,
            CancellationToken.None,
            preview.PreviewToken);
    }

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

    private sealed class InvalidOnCallAdapter : IDirectorySourceAdapter
    {
        public string SourceSystem => "SIM-ROLLBACK";

        public DirectoryParseResult Read(Stream source)
            => new(
                SourceSystem,
                [new NormalizedDirectoryPractitioner(
                    "SIM-SRC-ROLLBACK",
                    "Maya",
                    "Rollback",
                    "SIM-PRAC-ROLLBACK",
                    "Emergency",
                    true,
                    DateTimeOffset.Parse("2026-08-01T12:00:00Z"),
                    false,
                    "hash-rollback",
                    [new NormalizedDirectoryRole("SIM-SITE-NORTH", "SIM-DEPT-EMERGENCY", "Emergency physician", true)],
                    [],
                    [new NormalizedDirectoryOnCall(
                        "SIM-SITE-NORTH",
                        "SIM-DEPT-EMERGENCY",
                        OnCallTier.Primary,
                        DateTimeOffset.Parse("2026-08-08T12:00:00Z"),
                        DateTimeOffset.Parse("2026-08-01T12:00:00Z"))])],
                [],
                []);
    }
}
