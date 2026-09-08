using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Directory-Ordnerrechte ueber echte HTTP-, OIDC- und Dateiablagepfade.</summary>
[NonParallelizable]
public sealed class FolderDirectoryAssignmentIntegrationTest
{
    // Testzweck: Eine stabile Benutzerzuweisung berechtigt nur das exakte OIDC-Subject, bleibt
    // trotz abweichendem Anzeigenamen gueltig und faellt nach Deaktivierung geschlossen aus.
    [Test]
    public async Task DirectoryUserAssignment_ShouldUseExactActiveIdentity()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var directoryId = await PublishUser(context, active: true, displayName: "Anna Alt");
        using var modeler = context.CreateClient(isModeler: true);
        var folder = await CreateFolder(modeler);

        var assigned = await modeler.PutAsJsonAsync($"/folder/{folder.Id}/assignments", new
        {
            assignments = new[]
            {
                new
                {
                    referenceMode = "directory", subjectKind = "user", subject = directoryId,
                    subjectRef = new { kind = "user", id = directoryId }, role = "steward",
                    displayName = "Manipuliert"
                }
            }
        });

        assigned.StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = (await assigned.Content.ReadFromJsonAsync<ApiStatusResult<WorkflowFolderDto>>())!.Result!;
        stored.Assignments.Single().DisplayName.Should().Be("Anna Alt");
        stored.Assignments.Single().ReferenceMode.Should().Be("directory");

        using var exact = context.CreateClient(username: "neuer-anzeigename");
        var exactFolders = (await exact.GetFromJsonAsync<ApiStatusResult<WorkflowFolderDto[]>>("/folder"))!.Result!;
        exactFolders.Single(entry => entry.Id == folder.Id).MayDelegate.Should().BeTrue();

        using var impostor = context.CreateClient(userId: Guid.NewGuid(), username: "Anna Alt");
        var impostorFolders = (await impostor.GetFromJsonAsync<ApiStatusResult<WorkflowFolderDto[]>>("/folder"))!.Result!;
        impostorFolders.Single(entry => entry.Id == folder.Id).MayEdit.Should().BeFalse();

        (await PublishUser(context, active: false, displayName: "Anna Neu")).Should().Be(directoryId);
        var inactiveFolders = (await exact.GetFromJsonAsync<ApiStatusResult<WorkflowFolderDto[]>>("/folder"))!.Result!;
        inactiveFolders.Single(entry => entry.Id == folder.Id).MayEdit.Should().BeFalse();
    }

    // Testzweck: Manipulierte, unbekannte oder ohne Snapshot eingesandte Directory-Referenzen
    // werden vor dem Speichern abgelehnt; der Ordner behaelt seine bisherige Zuweisungsliste.
    [Test]
    public async Task DirectoryAssignment_ShouldRejectInvalidOrUnavailableReferences()
    {
        using (var unavailable = new AuthenticatedWorkflowTestContext())
        {
            using var client = unavailable.CreateClient(isModeler: true);
            var folder = await CreateFolder(client);
            var id = Guid.NewGuid();
            var response = await PutDirectoryAssignment(client, folder.Id, id, "user", id);
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }

        using var context = new AuthenticatedWorkflowTestContext();
        var knownId = await PublishUser(context, active: true, displayName: "Anna");
        using var modeler = context.CreateClient(isModeler: true);
        var target = await CreateFolder(modeler);

        var contradictory = await PutDirectoryAssignment(modeler, target.Id, knownId, "group", knownId);
        var unknownId = Guid.NewGuid();
        var unknown = await PutDirectoryAssignment(modeler, target.Id, unknownId, "user", unknownId);

        contradictory.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var folders = (await modeler.GetFromJsonAsync<ApiStatusResult<WorkflowFolderDto[]>>("/folder"))!.Result!;
        folders.Single(entry => entry.Id == target.Id).Assignments.Should().BeEmpty();
    }

    private static async Task<HttpResponseMessage> PutDirectoryAssignment(
        HttpClient client,
        Guid folderId,
        Guid subject,
        string subjectKind,
        Guid subjectRefId) => await client.PutAsJsonAsync($"/folder/{folderId}/assignments", new
        {
            assignments = new[]
            {
                new
                {
                    referenceMode = "directory", subjectKind, subject,
                    subjectRef = new { kind = "user", id = subjectRefId }, role = "editor"
                }
            }
        });

    private static async Task<WorkflowFolderDto> CreateFolder(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/folder", new { name = "Directory-Rechte" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ApiStatusResult<WorkflowFolderDto>>())!.Result!;
    }

    private static async Task<Guid> PublishUser(
        AuthenticatedWorkflowTestContext context,
        bool active,
        string displayName)
    {
        var now = DateTime.UtcNow;
        var snapshot = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(),
            Issuer = AuthenticatedWorkflowTestContext.Issuer,
            CompletedAtUtc = now,
            Users =
            [
                new DirectoryUser
                {
                    Id = Guid.NewGuid(),
                    SourceKind = DirectorySourceKind.Keycloak,
                    Issuer = AuthenticatedWorkflowTestContext.Issuer,
                    Subject = AuthenticatedWorkflowTestContext.UserId.ToString(),
                    DisplayName = displayName,
                    IsActive = active
                }
            ]
        };
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(
            snapshot.Issuer, snapshot.GenerationId, now, now.AddMinutes(5))).Should().BeTrue();
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);
        return (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!.Users.Single().Id;
    }
}
