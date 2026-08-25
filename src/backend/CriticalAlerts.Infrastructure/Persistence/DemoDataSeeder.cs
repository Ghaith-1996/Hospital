using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Domain.Policies;
using CriticalAlerts.Infrastructure.Protection;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Persistence;

public sealed class DemoDataSeeder
{
    public static readonly OrganizationId OrganizationId = new(Guid.Parse("11111111-1111-4111-8111-111111111111"));
    public static readonly SiteId NorthSiteId = new(Guid.Parse("11111111-1111-4111-8111-111111111201"));
    public static readonly SiteId RiversideSiteId = new(Guid.Parse("11111111-1111-4111-8111-111111111202"));
    public static readonly DepartmentId EmergencyDepartmentId = new(Guid.Parse("11111111-1111-4111-8111-111111110301"));
    public static readonly RoleId OperatorRoleId = new(Guid.Parse("11111111-1111-4111-8111-111111110401"));
    public static readonly RoleId PractitionerRoleId = new(Guid.Parse("11111111-1111-4111-8111-111111110402"));
    public static readonly UserId JordanUserId = new(Guid.Parse("11111111-1111-4111-8111-111111110501"));
    public static readonly UserId MorganUserId = new(Guid.Parse("11111111-1111-4111-8111-111111110502"));
    public static readonly UserId RileyUserId = new(Guid.Parse("11111111-1111-4111-8111-111111110503"));
    public const string JordanHandle = "sim-operator-jordan";
    public const string MorganHandle = "sim-administrator-morgan";
    public const string RileyHandle = "sim-practitioner-riley";
    public static readonly PractitionerId MayaChenId = new(Guid.Parse("11111111-1111-4111-8111-111111110101"));
    public static readonly PractitionerId TaylorKimId = new(Guid.Parse("11111111-1111-4111-8111-111111110111"));

    private readonly CriticalAlertsDbContext db;
    private readonly AesGcmSensitiveDataProtector protector;
    private static readonly DateTimeOffset SeededAt = DateTimeOffset.Parse("2026-08-01T12:00:00Z");

    public DemoDataSeeder(CriticalAlertsDbContext db, string dataProtectionKey)
    {
        this.db = db;
        protector = AesGcmSensitiveDataProtector.FromBase64(dataProtectionKey);
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.Organizations.AnyAsync(organization => organization.Id == OrganizationId, cancellationToken))
        {
            return;
        }

        var organization = Organization.CreateSimulation(OrganizationId, "Fictional Harborview Simulation Hospital", SeededAt);
        var north = Site.Create(NorthSiteId, OrganizationId, "North Wing Simulation Site", "SIM-SITE-NORTH", SeededAt);
        var riverside = Site.Create(RiversideSiteId, OrganizationId, "Riverside Annex Simulation Site", "SIM-SITE-RIVERSIDE", SeededAt);
        var emergency = Department.Create(new DepartmentId(Id("301")), OrganizationId, NorthSiteId, "Fictional Emergency Care", "SIM-DEPT-EMERGENCY", SeededAt);
        var medicine = Department.Create(new DepartmentId(Id("302")), OrganizationId, NorthSiteId, "Fictional Medicine", "SIM-DEPT-MEDICINE", SeededAt);
        var surgery = Department.Create(new DepartmentId(Id("303")), OrganizationId, RiversideSiteId, "Fictional Surgery", "SIM-DEPT-SURGERY", SeededAt);

        db.Organizations.Add(organization);
        db.Sites.AddRange(north, riverside);
        db.Departments.AddRange(emergency, medicine, surgery);

        var operatorRole = Role.Create(new RoleId(Id("401")), OrganizationId, "Operator");
        var practitionerRole = Role.Create(new RoleId(Id("402")), OrganizationId, "Practitioner");
        var administratorRole = Role.Create(new RoleId(Id("403")), OrganizationId, "Administrator");
        db.Roles.AddRange(operatorRole, practitionerRole, administratorRole);

        var jordan = UserAccount.CreateSimulation(new UserId(Id("501")), OrganizationId, "Jordan Lee", JordanHandle, SeededAt);
        var morgan = UserAccount.CreateSimulation(new UserId(Id("502")), OrganizationId, "Morgan Ellis", MorganHandle, SeededAt);
        var riley = UserAccount.CreateSimulation(RileyUserId, OrganizationId, "Riley Cole", RileyHandle, SeededAt);
        db.Users.AddRange(jordan, morgan, riley);
        db.UserRoles.AddRange(
            UserRole.Create(OrganizationId, jordan.Id, operatorRole.Id),
            UserRole.Create(OrganizationId, morgan.Id, administratorRole.Id),
            UserRole.Create(OrganizationId, riley.Id, practitionerRole.Id));
        db.ExternalIdentities.Add(ExternalIdentity.Create(
            new ExternalIdentityId(Id("601")),
            OrganizationId,
            jordan.Id,
            "simulation-dev",
            "sim-jordan-lee"));

        var practitioners = CreatePractitioners();
        db.Practitioners.AddRange(practitioners.Select(item => item.Practitioner));
        db.PractitionerRoles.AddRange(
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("701")), OrganizationId, practitioners[0].Practitioner.Id, emergency.Id, "Emergency physician", true),
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("704")), OrganizationId, practitioners[1].Practitioner.Id, medicine.Id, "Medicine consultant", true),
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("705")), OrganizationId, practitioners[2].Practitioner.Id, surgery.Id, "Surgeon", true),
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("706")), OrganizationId, practitioners[3].Practitioner.Id, medicine.Id, "Medicine consultant", true),
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("707")), OrganizationId, practitioners[4].Practitioner.Id, emergency.Id, "Emergency physician", true),
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("708")), OrganizationId, practitioners[5].Practitioner.Id, surgery.Id, "Surgeon", true),
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("709")), OrganizationId, practitioners[6].Practitioner.Id, medicine.Id, "Cardiology consultant", true),
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("710")), OrganizationId, practitioners[7].Practitioner.Id, medicine.Id, "Neurology consultant", true),
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("711")), OrganizationId, practitioners[8].Practitioner.Id, medicine.Id, "Pediatrics consultant", true),
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("702")), OrganizationId, practitioners[9].Practitioner.Id, emergency.Id, "Emergency physician", true),
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("703")), OrganizationId, practitioners[9].Practitioner.Id, medicine.Id, "Medicine consultant", false),
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("712")), OrganizationId, practitioners[10].Practitioner.Id, emergency.Id, "Emergency physician", true),
            PractitionerRoleAssignment.Create(new PractitionerRoleId(Id("713")), OrganizationId, practitioners[11].Practitioner.Id, surgery.Id, "Surgeon", true));

        AddEndpoint(practitioners[0].Practitioner, ContactEndpointKind.Sms, "+1 555 010 0101", "SIM-SMS-0101", true);
        AddEndpoint(practitioners[0].Practitioner, ContactEndpointKind.SecureMessage, "sim-secure://maya.chen", "SIM-SECURE-0101", false);
        AddEndpoint(practitioners[1].Practitioner, ContactEndpointKind.Voice, "+1 555 010 0102", "SIM-VOICE-0102", true);
        AddEndpoint(practitioners[2].Practitioner, ContactEndpointKind.SecureMessage, "sim-secure://jules.martin", "SIM-SECURE-0103", true);

        db.OnCallAssignments.AddRange(
            OnCallAssignment.Create(new OnCallAssignmentId(Id("801")), OrganizationId, practitioners[0].Practitioner.Id, NorthSiteId, emergency.Id, OnCallTier.Primary, SeededAt, SeededAt.AddDays(7), "SIM-DIRECTORY", "SIM-SRC-ONCALL-1", SeededAt),
            OnCallAssignment.Create(new OnCallAssignmentId(Id("802")), OrganizationId, practitioners[1].Practitioner.Id, NorthSiteId, medicine.Id, OnCallTier.Backup, SeededAt, SeededAt.AddDays(7), "SIM-DIRECTORY", "SIM-SRC-ONCALL-2", SeededAt));

        db.DirectorySourceRecords.AddRange(
            DirectorySourceRecord.Create(new DirectorySourceRecordId(Id("901")), OrganizationId, practitioners[0].Practitioner.Id, "SIM-DIRECTORY", "SIM-SRC-MAYA", SeededAt, "hash-maya", SeededAt, "current", false),
            DirectorySourceRecord.Create(new DirectorySourceRecordId(Id("902")), OrganizationId, practitioners[10].Practitioner.Id, "SIM-DIRECTORY", "SIM-SRC-TAYLOR", SeededAt.AddDays(-90), "hash-taylor", SeededAt.AddDays(-90), "stale", true));

        db.DirectorySyncRuns.Add(DirectorySyncRun.CreateCompleted(
            new DirectorySyncRunId(Id("911")),
            OrganizationId,
            "SIM-DIRECTORY",
            SeededAt.AddHours(-1),
            SeededAt,
            insertedCount: 12,
            updatedCount: 1,
            deactivatedCount: 2,
            rejectedCount: 0,
            DirectorySyncRunStatus.Succeeded,
            "sim-sync-001",
            "none"));

        var template = AlertTemplate.CreateDemo(new AlertTemplateId(Id("a01")), OrganizationId, SeededAt);
        var notification = NotificationPolicy.CreateDemo(new NotificationPolicyId(Id("a02")), OrganizationId);
        var escalation = EscalationPolicy.CreateDemo(new EscalationPolicyId(Id("a03")), OrganizationId);
        db.AlertTemplates.Add(template);
        db.NotificationPolicies.Add(notification);
        db.EscalationPolicies.Add(escalation);
        db.EscalationSteps.Add(EscalationStep.CreateDemo(new EscalationStepId(Id("a04")), OrganizationId, escalation.Id, 1));

        await db.SaveChangesAsync(cancellationToken);
    }

    private void AddEndpoint(Practitioner practitioner, ContactEndpointKind kind, string secretValue, string label, bool isPrimary)
    {
        db.ContactEndpoints.Add(ContactEndpoint.Create(
            ContactEndpointId.New(),
            OrganizationId,
            practitioner.Id,
            kind,
            protector.Protect(secretValue, new SensitiveDataContext("contact-endpoint", OrganizationId.Value)),
            label,
            isPrimary));
    }

    private static List<(Practitioner Practitioner, bool Active)> CreatePractitioners()
    {
        return
        [
            Person("0101", "Maya", "Chen", "Emergency", true),
            Person("0102", "Rowan", "Patel", "Medicine", true),
            Person("0103", "Jules", "Martin", "Surgery", true),
            Person("0104", "Avery", "Brooks", "Medicine", true),
            Person("0105", "Samira", "Nguyen", "Emergency", true),
            Person("0106", "Jordan", "Martin", "Surgery", true),
            Person("0107", "Casey", "Okonkwo", "Cardiology", true),
            Person("0108", "Riley", "Sato", "Neurology", true),
            Person("0109", "Quinn", "Alvarez", "Pediatrics", true),
            Person("0110", "Harper", "Singh", "Medicine", true),
            Person("0111", "Taylor", "Kim", "Emergency", false),
            Person("0112", "Cameron", "Wright", "Surgery", false),
        ];
    }

    private static (Practitioner Practitioner, bool Active) Person(string suffix, string first, string last, string specialty, bool active)
    {
        var practitioner = Practitioner.Create(
            new PractitionerId(Guid.Parse($"11111111-1111-4111-8111-11111111{suffix}")),
            OrganizationId,
            first,
            last,
            $"SIM-PRAC-{suffix}",
            specialty,
            active,
            SeededAt);
        return (practitioner, active);
    }

    private static Guid Id(string suffix) => Guid.Parse($"11111111-1111-4111-8111-111111110{suffix}");
}
