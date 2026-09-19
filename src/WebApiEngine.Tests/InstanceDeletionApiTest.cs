using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Aussenansicht des Loeschens von Hand und der Aufbewahrungsfrist am Katalogeintrag.
/// </summary>
[NonParallelizable]
public class InstanceDeletionApiTest
{
    private static readonly DateTime FinishedAt = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    // Testzweck: Eine beendete Instanz wird mit 204 geloescht und ist danach nicht mehr
    // abrufbar — samt allem, was an ihr hing.
    [Test]
    public async Task Delete_ShouldAnswerNoContentAndRemoveEverything_ForAFinishedInstance()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var seeded = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt);
        using var client = context.CreateClient(isOperator: true);

        var response = await client.DeleteAsync($"/instance/{seeded.InstanceId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await InstancePurgeFixture.AssertPurgedAsync(context.Storage, seeded);
        (await client.GetAsync($"/instance/{seeded.InstanceId}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Eine laufende Instanz wird nicht geloescht, sondern mit 409 abgelehnt — und sie
    // steht danach unveraendert da. Ein stilles Loeschen offener Arbeit waere der schlimmste
    // Ausgang dieser Funktion.
    [Test]
    public async Task Delete_ShouldAnswerConflictAndChangeNothing_ForARunningInstance()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var seeded = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt, finished: false);
        using var client = context.CreateClient(isOperator: true);

        var response = await client.DeleteAsync($"/instance/{seeded.InstanceId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        await InstancePurgeFixture.AssertIntactAsync(context.Storage, seeded);
    }

    // Testzweck: Ohne Betriebsrolle wird gar nicht erst geloescht. Die Rolle ist die einzige
    // Grenze zwischen „sieht seine Vorgaenge" und „entfernt fremde Vorgaenge unwiderruflich".
    [Test]
    public async Task Delete_ShouldRequireTheOperatorRole()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var seeded = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt);
        using var client = context.CreateClient(isOperator: false);

        var response = await client.DeleteAsync($"/instance/{seeded.InstanceId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await InstancePurgeFixture.AssertIntactAsync(context.Storage, seeded);
    }

    // Testzweck: Eine unbekannte Kennung ist ein 404 und kein Serverfehler.
    [Test]
    public async Task Delete_ShouldAnswerNotFound_ForAnUnknownInstance()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var client = context.CreateClient(isOperator: true);

        var response = await client.DeleteAsync($"/instance/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Die Aufbewahrungsfrist laesst sich ueber die vorhandenen Metadaten-Endpunkte
    // setzen und wieder lesen — inklusive der 0, die „nie loeschen" bedeutet und deshalb von
    // „nicht gesetzt" unterscheidbar bleiben muss.
    [Test]
    public async Task DefinitionMeta_ShouldRoundTripTheRetentionPeriodIncludingZero()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var client = context.CreateClient(isModeler: true);
        const string definitionId = "aufbewahrung-katalog";

        var created = await client.PostAsJsonAsync("/definition/meta", new BpmnMetaDefinitionDto
        {
            DefinitionId = definitionId,
            Name = "Urlaubsantrag",
            RetentionDays = 45
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Read(created)).RetentionDays.Should().Be(45);

        var updated = await client.PutAsJsonAsync("/definition/meta", new BpmnMetaDefinitionDto
        {
            DefinitionId = definitionId,
            Name = "Urlaubsantrag",
            RetentionDays = 0
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await client.GetAsync($"/definition/meta/{definitionId}");
        (await Read(fetched)).RetentionDays.Should().Be(0, "0 heisst nie loeschen, nicht nicht gesetzt");

        var cleared = await client.PutAsJsonAsync("/definition/meta", new BpmnMetaDefinitionDto
        {
            DefinitionId = definitionId,
            Name = "Urlaubsantrag",
            RetentionDays = null
        });
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Read(await client.GetAsync($"/definition/meta/{definitionId}"))).RetentionDays
            .Should().BeNull("ohne Wert gilt wieder die installationsweite Frist");
    }

    // Testzweck: Eine negative Frist ergibt keinen Sinn und wird abgelehnt, statt still als
    // „laengst abgelaufen" gelesen zu werden.
    [Test]
    public async Task DefinitionMeta_ShouldRejectANegativeRetentionPeriod()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var client = context.CreateClient(isModeler: true);

        var response = await client.PostAsJsonAsync("/definition/meta", new BpmnMetaDefinitionDto
        {
            DefinitionId = "negative-frist",
            Name = "Ungueltig",
            RetentionDays = -1
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<BpmnMetaDefinitionDto> Read(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnMetaDefinitionDto>>())!.Result!;
}
