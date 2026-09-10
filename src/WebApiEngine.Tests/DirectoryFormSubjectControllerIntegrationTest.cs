using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Dynamic;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StorageSystem;
using WebApiEngine.BusinessLogic;

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

    // Testzweck: Gebundene Start- und Aufgabenformulare lösen eine bereits
    // gespeicherte deaktivierte Referenz nur zur Anzeige auf; fremde Aufgaben bleiben 404.
    [Test]
    public async Task FormResolution_ShouldReturnHistoricalSubjectOnlyInVisibleBoundContext()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var seeded = await SeedDirectoryAsync(context, inactiveUserActive: true);
        await FormTestSeed.StoreAsync(
            context.Storage, "Approval", Schema(seeded.GroupId, seeded.InactiveId));
        var definition = await context.DeployAsync("assignee=\"bert\"", startFormKey: "Approval");
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        var variables = new ExpandoObject();
        ((IDictionary<string, object?>)variables)["representative"] = new Dictionary<string, object?>
        {
            ["kind"] = "user",
            ["id"] = seeded.InactiveId.ToString()
        };
        var instance = await engine.StartProcessInstance(definition.DefinitionId, variables);
        var task = (await context.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
        await SetUserActiveAsync(context, "representative-inactive", active: false);
        using var client = context.CreateClient();
        using var foreignClient = context.CreateClient(userId: Guid.NewGuid(), username: "anna");
        var body = new
        {
            subjects = new[] { new { kind = "user", id = seeded.InactiveId } }
        };

        var start = await client.PostAsJsonAsync(
            "/identity-directory/start-forms/Definitions_Completion/fields/representative/subjects/resolve",
            body);
        var ownTask = await client.PostAsJsonAsync(
            $"/identity-directory/user-tasks/{task.Id}/fields/representative/subjects/resolve",
            body);
        var foreignTask = await foreignClient.PostAsJsonAsync(
            $"/identity-directory/user-tasks/{task.Id}/fields/representative/subjects/resolve",
            body);
        var manipulated = await client.PostAsJsonAsync(
            $"/identity-directory/user-tasks/{task.Id}/fields/representative/subjects/resolve",
            new
            {
                subjects = new[] { new { kind = "user", id = seeded.OutsiderId } }
            });

        start.StatusCode.Should().Be(HttpStatusCode.OK);
        ownTask.StatusCode.Should().Be(HttpStatusCode.OK);
        foreignTask.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await ownTask.Content.ReadFromJsonAsync<JsonElement>();
        var resolved = payload.GetProperty("result").GetProperty("items").EnumerateArray().Single();
        resolved.GetProperty("subject").GetProperty("id").GetGuid().Should().Be(seeded.InactiveId);
        resolved.GetProperty("displayName").GetString().Should().Be("Ehemalige Vertretung");
        resolved.GetProperty("isActive").GetBoolean().Should().BeFalse();
        resolved.GetProperty("isSelectable").GetBoolean().Should().BeFalse();
        var manipulatedItems = (await manipulated.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("result").GetProperty("items");
        manipulatedItems.GetArrayLength().Should().Be(0);
    }

    // Testzweck: Ein weiterhin auswählbares Gruppenmitglied behält seine Beschriftung
    // beim Neuladen eines privaten Entwurfs bzw. Startformulars; fremde IDs bleiben verborgen.
    [TestCase(false)]
    [TestCase(true)]
    public async Task FormResolution_ShouldResolveActiveGroupMember(bool startForm)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var seeded = await SeedDirectoryAsync(context);
        await FormTestSeed.StoreAsync(context.Storage, "Approval", Schema(seeded.GroupId));
        using var client = context.CreateClient();
        string route;
        if (startForm)
        {
            await context.DeployAsync("", startFormKey: "Approval");
            route = "/identity-directory/start-forms/Definitions_Completion/fields/representative/subjects/resolve";
        }
        else
        {
            var task = await context.StartAsync("assignee=\"bert\"");
            (await client.PutAsJsonAsync($"/usertask/{task.Id}/draft", new
            {
                expectedRevision = 0,
                data = new { representative = new { kind = "user", id = seeded.MemberId } }
            })).EnsureSuccessStatusCode();
            route = $"/identity-directory/user-tasks/{task.Id}/fields/representative/subjects/resolve";
        }
        var response = await client.PostAsJsonAsync(route, new
        {
            subjects = new[] { new { kind = "user", id = seeded.MemberId },
                new { kind = "user", id = seeded.OutsiderId }, new { kind = "user", id = seeded.InactiveId } }
        });
        response.EnsureSuccessStatusCode();
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("result").GetProperty("items").EnumerateArray().ToArray();
        items.Should().ContainSingle();
        items[0].GetProperty("subject").GetProperty("id").GetGuid().Should().Be(seeded.MemberId);
        items[0].GetProperty("displayName").GetString().Should().Be("Anna Mitglied");
        items[0].GetProperty("isSelectable").GetBoolean().Should().BeTrue();
    }

    private static string Schema(Guid groupId, Guid? allowedUserId = null) => JsonSerializer.Serialize(new
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
                        allowedUserIds = allowedUserId is null
                            ? Array.Empty<string>()
                            : new[] { allowedUserId.Value.ToString() },
                        userMemberOfGroupIds = new[] { groupId.ToString() },
                        includeSubgroups = false
                    }
                }
            }
        }
    });

    private static async Task<(Guid MemberId, Guid InactiveId, Guid OutsiderId, Guid GroupId)> SeedDirectoryAsync(
        AuthenticatedWorkflowTestContext context,
        bool inactiveUserActive = false)
    {
        var memberImportId = Guid.NewGuid();
        var inactiveImportId = Guid.NewGuid();
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
                User(inactiveImportId, "representative-inactive", "Ehemalige Vertretung", inactiveUserActive),
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
            Memberships =
            [
                new DirectoryMembership { UserId = memberImportId, GroupId = groupImportId },
                new DirectoryMembership { UserId = inactiveImportId, GroupId = groupImportId }
            ]
        };
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(
            snapshot.Issuer, snapshot.GenerationId, now, now.AddMinutes(5))).Should().BeTrue();
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);
        var published = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        return (
            published.Users.Single(user => user.Subject == "representative-member").Id,
            published.Users.Single(user => user.Subject == "representative-inactive").Id,
            published.Users.Single(user => user.Subject == "representative-outsider").Id,
            published.Groups.Single(group => group.ExternalId == "representatives").Id);

        DirectoryUser User(Guid id, string subject, string displayName, bool active = true) => new()
        {
            Id = id,
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = AuthenticatedWorkflowTestContext.Issuer,
            Subject = subject,
            DisplayName = displayName,
            IsActive = active
        };
    }

    private static async Task SetUserActiveAsync(
        AuthenticatedWorkflowTestContext context,
        string subject,
        bool active)
    {
        var current = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        var next = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(),
            Issuer = current.Issuer,
            CompletedAtUtc = current.CompletedAtUtc.AddMinutes(1),
            Users = current.Users.Select(user => new DirectoryUser
            {
                Id = user.Id,
                SourceKind = user.SourceKind,
                Issuer = user.Issuer,
                Subject = user.Subject,
                DisplayName = user.DisplayName,
                IsActive = user.Subject == subject ? active : user.IsActive
            }).ToList(),
            Groups = current.Groups.Select(group => new DirectoryGroup
            {
                Id = group.Id,
                SourceKind = group.SourceKind,
                Issuer = group.Issuer,
                ExternalId = group.ExternalId,
                Name = group.Name,
                Path = group.Path,
                ParentId = group.ParentId,
                IsActive = group.IsActive
            }).ToList(),
            Memberships = current.Memberships.Select(membership => new DirectoryMembership
            {
                UserId = membership.UserId,
                GroupId = membership.GroupId
            }).ToList()
        };
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(
            next.Issuer, next.GenerationId, next.CompletedAtUtc, next.CompletedAtUtc.AddMinutes(5)))
            .Should().BeTrue();
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(next);
    }
}
