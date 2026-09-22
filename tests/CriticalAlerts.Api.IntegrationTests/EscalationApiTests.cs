using System.Net;
using System.Net.Http.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class EscalationApiTests(SeededPostgresApiFixture fixture)
{
    [Fact]
    public async Task OperatorOverridesAreAuthorizedVersionedAndIdempotent()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await new AlertConfirmationTests(fixture).CreateConfirmableAlertAsync(client);
        using var confirmed = await AlertConfirmationTests.ConfirmAsync(client, prepared.AlertId, prepared.Version, Guid.NewGuid().ToString());
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var db = fixture.CreateContext())
        {
            (await db.Alerts.SingleAsync(row => row.Id == new AlertId(prepared.AlertId))).MarkActive(DateTimeOffset.UtcNow, "simulation-api-test");
            await db.SaveChangesAsync();
            for (var i = 0; i < 20 && !await db.EscalationRuns.AnyAsync(row => row.AlertId == new AlertId(prepared.AlertId)); i++)
                await new EscalationProcessor(db).ProcessNextAsync("api-test-worker");
        }
        var live = await client.GetFromJsonAsync<AlertLiveView>($"/api/v1/alerts/{prepared.AlertId}/live");
        live!.Escalation!.CanPause.Should().BeTrue();
        var key = Guid.NewGuid().ToString();
        using var pause = await Send(client, prepared.AlertId, prepared.Version, "pause", key);
        pause.StatusCode.Should().Be(HttpStatusCode.OK, await pause.Content.ReadAsStringAsync());
        using var replay = await Send(client, prepared.AlertId, prepared.Version, "pause", key);
        (await replay.Content.ReadFromJsonAsync<AlertLifecycleResult>())!.Replayed.Should().BeTrue();
        using var stale = await Send(client, prepared.AlertId, prepared.Version - 1, "resume", Guid.NewGuid().ToString());
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var changedKey = await Send(client, prepared.AlertId, prepared.Version - 1, "pause", key);
        changedKey.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var practitioner = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        using var forbidden = await Send(practitioner, prepared.AlertId, prepared.Version, "resume", Guid.NewGuid().ToString());
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var resume = await Send(client, prepared.AlertId, prepared.Version, "resume", Guid.NewGuid().ToString());
        resume.StatusCode.Should().Be(HttpStatusCode.OK);
        using var missing = await Send(client, Guid.NewGuid(), prepared.Version, "pause", Guid.NewGuid().ToString());
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, Guid id, int version, string action, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{id}/escalation/{action}")
        { Content = JsonContent.Create(new EscalationOverrideRequest(version, $"simulation-{action}-requested")) };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }
}
