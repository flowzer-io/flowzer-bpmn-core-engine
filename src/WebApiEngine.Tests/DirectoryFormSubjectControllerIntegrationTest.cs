using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>Gebundene Formularsuche ueber reale HTTP-, Rechte- und Storage-Pfade.</summary>
[NonParallelizable]
public sealed class DirectoryFormSubjectControllerIntegrationTest
{
    // Testzweck: Die Startformularsuche liest ihre Filter ausschließlich aus dem gebundenen
    // Formularvertrag und liefert nur aktive direkte Mitglieder der freigegebenen Gruppe.
    [Test]
    public async Task StartFormSearch_ShouldApplyBoundFieldPolicy()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var seeded = await SeedDirectoryAsync(context);
        await FormTestSeed.StoreAsync(context.Storage, "Approval", Schema(seeded.GroupId));
        await context.DeployAsync("", startFormKey: "Approval");
        using var client = context.CreateClient();

        var response = await client.GetAsync(
            "/identity-directory/start-forms/Definitions_Completion/fields/representative/subjects?query=anna&kind=user");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = payload.GetProperty("result").GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("subject").GetProperty("id").GetGuid().Should().Be(seeded.MemberId);
    }

    // Testzweck: Eine fremde oder unbekannte Aufgabe und ein unbekanntes Feld bleiben ueber
    // denselben 404-Vertrag verborgen; es gibt keinen allgemeinen Directory-Fallback.
    [Test]
    public async Task TaskFormSearch_ShouldHideForeignTaskAndUnknownField()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var seeded = await SeedDirectoryAsync(context);
        await FormTestSeed.StoreAsync(context.Storage, "Approval", Schema(seeded.GroupId));
        var own = await context.StartAsync("assignee=\"bert\"");
        using var client = context.CreateClient();
        using var foreignClient = context.CreateClient(userId: Guid.NewGuid(), username: "anna");

        (await foreignClient.GetAsync($"/identity-directory/user-tasks/{own.Id}/fields/representative/subjects?query=anna&kind=user"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/identity-directory/user-tasks/{own.Id}/fields/unknown/subjects?query=anna&kind=user"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/identity-directory/user-tasks/{own.Id}/fields/representative/subjects?query=anna&kind=user"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string Schema(Guid groupId) => JsonSerializer.Serialize(new
    {
        flowzer = new { contractVersion = 2 },
        components = new[]
        {
            new
            {
                type = "flowzerSubject", key = "representative",
                flowzer = new
                {
                    subjectSelection = new
                    {
                        allowUsers = true,
                        allowGroups = false,
                        userMemberOfGroupIds = new[] { groupId.ToString() },
                        includeSubgroups = false
                    }
                }
            }
        }
    });

    private static async Task<(Guid MemberId, Guid GroupId)> SeedDirectoryAsync(
        AuthenticatedWorkflowTestContext context)
    {
        var memberImportId = Guid.NewGuid();
        var outsiderImportId = Guid.NewGuid();
        var groupImportId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var snapshot = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(),
            Issuer = AuthenticatedWorkflowTestContext.Issuer,
            CompletedAtUtc = now,
            Users =
            [
                User(memberImportId, "representative-member", "Anna Mitglied"),
                User(outsiderImportId, "representative-outsider", "Anna Ausserhalb")
            ],
            Groups =
            [
                new DirectoryGroup
                {
                    Id = groupImportId,
                    SourceKind = DirectorySourceKind.Keycloak,
                    Issuer = AuthenticatedWorkflowTestContext.Issuer,
                    ExternalId = "representatives",
                    Name = "Vertretungen",
                    Path = "/Vertretungen",
                    IsActive = true
                }
            ],
            Memberships = [new DirectoryMembership { UserId = memberImportId, GroupId = groupImportId }]
        };
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(
            snapshot.Issuer, snapshot.GenerationId, now, now.AddMinutes(5))).Should().BeTrue();
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);
        var published = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        return (
            published.Users.Single(user => user.Subject == "representative-member").Id,
            published.Groups.Single(group => group.ExternalId == "representatives").Id);

        DirectoryUser User(Guid id, string subject, string displayName) => new()
        {
            Id = id,
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = AuthenticatedWorkflowTestContext.Issuer,
            Subject = subject,
            DisplayName = displayName,
            IsActive = true
        };
    }
}
