using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using StorageSystem;

namespace WebApiEngine.Tests;

[NonParallelizable]
public sealed class DirectoryFormAuthoringIntegrationTest
{
    // Testzweck: Nur Modellierende dürfen im vorhandenen Formular konfigurieren;
    // die Autorenvorschau benötigt weder Instanz noch veröffentlichte Formularversion.
    [Test]
    public async Task AuthoringSearch_RequiresModelerAndExistingForm()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Picker");
        var id = (await context.Storage.FormStorage.GetFormMetadatas()).Single().FormId;
        using var worker = context.CreateClient();
        using var modeler = context.CreateClient(isModeler: true);
        var body = new { query = "anna", kind = "user" };
        (await worker.PostAsJsonAsync($"/identity-directory/authoring-forms/{id}/subjects/search", body))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await worker.PostAsJsonAsync($"/identity-directory/authoring-forms/{id}/subjects/resolve",
            new { subjects = new[] { new { kind = "user", id = Guid.NewGuid() } } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await modeler.PostAsJsonAsync($"/identity-directory/authoring-forms/{Guid.NewGuid()}/subjects/search", body))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Vorschau nutzt denselben Filterkern wie Laufzeit, liefert nur aktive
    // Gruppenmitglieder und sichere Profilfelder; weder Entwurf noch Instanz wird geschrieben.
    [Test]
    public async Task PreviewSearch_AppliesUnsavedFieldPolicyWithoutSideEffects()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Picker");
        var id = (await context.Storage.FormStorage.GetFormMetadatas()).Single().FormId;
        var userId = Guid.NewGuid(); var groupId = Guid.NewGuid(); var inactiveId = Guid.NewGuid();
        var imported = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(), Issuer = AuthenticatedWorkflowTestContext.Issuer, CompletedAtUtc = DateTime.UtcNow,
            Users = [
                new() { Id = userId, SourceKind = DirectorySourceKind.Keycloak, Issuer = AuthenticatedWorkflowTestContext.Issuer,
                    Subject = "anna", DisplayName = "Anna Beispiel", IsActive = true, Email = "anna@example.test", Username = "anna" },
                new() { Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak, Issuer = AuthenticatedWorkflowTestContext.Issuer,
                    Subject = "outside", DisplayName = "Anna Außerhalb", IsActive = true },
                new() { Id = inactiveId, SourceKind = DirectorySourceKind.Keycloak, Issuer = AuthenticatedWorkflowTestContext.Issuer,
                    Subject = "inactive", DisplayName = "Anna Inaktiv", IsActive = false }
            ],
            Groups = [new() { Id = groupId, SourceKind = DirectorySourceKind.Keycloak, Issuer = AuthenticatedWorkflowTestContext.Issuer,
                ExternalId = "team", Name = "Team", Path = "/Team", IsActive = true }],
            Memberships = [new() { UserId = userId, GroupId = groupId }, new() { UserId = inactiveId, GroupId = groupId }]
        };
        await context.Storage.IdentityDirectoryStorage.TryStartSync(imported.Issuer, imported.GenerationId,
            DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(imported);
        var snapshot = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        var group = snapshot.Groups.Single().Id;
        var schema = JsonSerializer.Serialize(new { flowzer = new { contractVersion = 2 }, components = new[] {
            new { type = "flowzerSubject", key = "representative", input = true, flowzer = new { subjectSelection = new {
                allowUsers = true, userMemberOfGroupIds = new[] { group } } } }
        }});
        using var client = context.CreateClient(isModeler: true);
        var result = await client.PostAsJsonAsync($"/identity-directory/authoring-forms/{id}/subjects/search",
            new { query = "anna", kind = "all", fieldKey = "representative", formData = schema });
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetProperty("items").EnumerateArray().Single();
        item.GetProperty("displayName").GetString().Should().Be("Anna Beispiel");
        item.GetProperty("email").GetString().Should().Be("anna@example.test");
        (await client.PostAsJsonAsync($"/identity-directory/authoring-forms/{id}/subjects/search",
            new { query = "anna", fieldKey = "unknown", formData = schema })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Auch Suche per E-Mail und gezielte Auflösung dürfen die Feldfilter nicht umgehen.
        var emailSearch = await client.PostAsJsonAsync($"/identity-directory/authoring-forms/{id}/subjects/search",
            new { query = "anna@example.test", fieldKey = "representative", formData = schema });
        emailSearch.StatusCode.Should().Be(HttpStatusCode.OK);
        (await emailSearch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetProperty("items")
            .GetArrayLength().Should().Be(1);
        var resolved = await client.PostAsJsonAsync($"/identity-directory/authoring-forms/{id}/subjects/resolve",
            new { fieldKey = "representative", formData = schema,
                subjects = snapshot.Users.Select(user => new { kind = "user", id = user.Id }).ToArray() });
        resolved.StatusCode.Should().Be(HttpStatusCode.OK);
        var resolvedItems = (await resolved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetProperty("items");
        resolvedItems.EnumerateArray().Single().GetProperty("displayName").GetString().Should().Be("Anna Beispiel");
        (await context.Storage.FormAuthoringStorage.Get(id)).Should().BeNull();
        (await context.Storage.FormStorage.GetForms(id)).Should().HaveCount(1);
    }
}
