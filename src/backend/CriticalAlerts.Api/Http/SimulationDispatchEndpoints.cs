using System.Security.Claims;
using CriticalAlerts.Api.Authentication;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Infrastructure.Dispatch;

namespace CriticalAlerts.Api.Http;

internal static class SimulationDispatchEndpoints
{
    public static void MapSimulationDispatchEndpoints(this WebApplication app, string environmentName)
    {
        if (!SimulationDispatchEnvironmentGuard.IsSimulationEnvironment(environmentName))
        {
            return;
        }

        var scenarios = app.MapGroup($"{ApiRouteConstants.BasePath}/dev/dispatch-scenarios")
            .RequireAuthorization(AuthorizationPolicies.DispatchScenarioAdministrator)
            .RequireRateLimiting("api");
        scenarios.MapGet("/{channel}", Get).Produces<SimulationDispatchScenarioView>().WithApiErrors(400);
        scenarios.MapPut("/{channel}", Set).Produces<SimulationDispatchScenarioView>().WithApiErrors(400).Produces(413);
        scenarios.MapDelete("/{channel}", Reset).Produces(204).WithApiErrors(400);
    }

    private static async Task<IResult> Get(
        ClaimsPrincipal principal,
        ISimulationDispatchScenarioStore store,
        string channel,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out _, out var organizationId))
        {
            return Unauthorized();
        }

        if (!TryParseChannel(channel, out var parsedChannel))
        {
            return Invalid("channel-invalid");
        }

        var scenario = await store.GetAsync(organizationId, parsedChannel, cancellationToken);
        return Results.Ok(new SimulationDispatchScenarioView(parsedChannel.ToString(), scenario.ToString()));
    }

    private static async Task<IResult> Set(
        ClaimsPrincipal principal,
        ISimulationDispatchScenarioStore store,
        TimeProvider time,
        string channel,
        SimulationDispatchScenarioRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Unauthorized();
        }

        if (!TryParseChannel(channel, out var parsedChannel))
        {
            return Invalid("channel-invalid");
        }

        if (request is null || !SimulationProviderScenarioCatalog.TryParse(request.Scenario, out var scenario))
        {
            return Invalid("scenario-invalid");
        }

        await store.SetAsync(
            organizationId,
            parsedChannel,
            scenario,
            userId,
            RequireUtc(time.GetUtcNow()),
            cancellationToken);
        return Results.Ok(new SimulationDispatchScenarioView(parsedChannel.ToString(), scenario.ToString()));
    }

    private static async Task<IResult> Reset(
        ClaimsPrincipal principal,
        ISimulationDispatchScenarioStore store,
        TimeProvider time,
        string channel,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Unauthorized();
        }

        if (!TryParseChannel(channel, out var parsedChannel))
        {
            return Invalid("channel-invalid");
        }

        await store.ResetAsync(
            organizationId,
            parsedChannel,
            userId,
            RequireUtc(time.GetUtcNow()),
            cancellationToken);
        return Results.NoContent();
    }

    private static bool TryParseChannel(string value, out NotificationChannel channel)
        => Enum.TryParse(value, ignoreCase: false, out channel) && Enum.IsDefined(channel);

    private static bool TryGetActor(
        ClaimsPrincipal principal,
        out UserId userId,
        out OrganizationId organizationId)
    {
        userId = default;
        organizationId = default;
        var userValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var organizationValue = principal.FindFirstValue(AuthenticationClaimTypes.OrganizationId);
        if (!Guid.TryParse(userValue, out var parsedUser) || !Guid.TryParse(organizationValue, out var parsedOrganization))
        {
            return false;
        }

        userId = new UserId(parsedUser);
        organizationId = new OrganizationId(parsedOrganization);
        return true;
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value)
        => value.Offset == TimeSpan.Zero
            ? value
            : throw new InvalidOperationException("The simulation dispatch clock must be UTC.");

    private static IResult Unauthorized()
        => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "authentication-required");

    private static IResult Invalid(string code)
        => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid simulation dispatch request", detail: code);
}

internal sealed record SimulationDispatchScenarioRequest(string? Scenario);

internal sealed record SimulationDispatchScenarioView(string Channel, string Scenario);
