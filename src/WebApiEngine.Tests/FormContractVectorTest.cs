using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Newtonsoft.Json;
using StorageSystem;
using WebApiEngine.Forms;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace WebApiEngine.Tests;

/// <summary>Liest denselben versionierten Formularvertrag-Katalog wie die React-Konsole.</summary>
public sealed class FormContractVectorTest
{
    // Testzweck: Alle gemeinsamen Compile- und Submission-Vektoren müssen im autoritativen
    // Serververtrag exakt die dokumentierten Profile, Ausgaben und datensparsamen Fehler liefern.
    [Test]
    public void SharedVectors_ShouldMatchServerContract()
    {
        var manifest = LoadManifest();
        manifest.Version.Should().Be(1);
        manifest.Cases.Select(vector => vector.Id).Should().OnlyHaveUniqueItems();
        var directory = ToDirectorySnapshot(manifest.Directory);

        foreach (var vector in manifest.Cases)
        {
            var schema = BuildSchema(vector);
            if (vector.Phase == "compile")
            {
                Action compile = () => FormContractCompiler.Compile(schema);
                var exception = compile.Should().Throw<InvalidOperationException>(vector.Purpose).Which;
                CompileCode(exception).Should().Be(vector.Expected.Code, vector.Purpose);
                exception.Message.Should().NotContain("example.invalid", vector.Purpose);
                continue;
            }

            var contract = FormContractCompiler.Compile(schema);
            contract.ValidationProfile.Should().Be(vector.Profile, vector.Purpose);
            try
            {
                var output = FormSubmissionValidator.Validate(
                    contract,
                    ToData(vector.Input),
                    ToData(vector.Context),
                    directory);
                vector.Expected.Outcome.Should().Be("accepted", vector.Purpose);
                if (vector.Expected.Output.ValueKind != JsonValueKind.Undefined)
                {
                    JsonElement.DeepEquals(
                        JsonSerializer.SerializeToElement(output),
                        vector.Expected.Output).Should().BeTrue(vector.Purpose);
                }
            }
            catch (FormSubmissionException exception)
            {
                vector.Expected.Outcome.Should().Be("rejected", vector.Purpose);
                AssertErrors(exception.Errors, vector.Expected.Errors, vector.Purpose);
                var serialized = JsonSerializer.Serialize(exception.Errors);
                serialized.Should().NotContain("NICHT_SPIEGELN", vector.Purpose);
                exception.Message.Should().NotContain("NICHT_SPIEGELN", vector.Purpose);
            }
        }
    }

    private static FormContractVectorManifest LoadManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "FormContractVectors", "manifest.json");
        return JsonSerializer.Deserialize<FormContractVectorManifest>(
                   File.ReadAllText(path),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException("The form contract vector manifest is empty.");
    }

    private static string BuildSchema(FormContractVector vector)
    {
        if (vector.SchemaPaddingLength is null) return vector.Schema.GetRawText();
        var root = JsonNode.Parse(vector.Schema.GetRawText())?.AsObject()
                   ?? throw new InvalidDataException($"Vector '{vector.Id}' has no object schema.");
        root["padding"] = new string('x', vector.SchemaPaddingLength.Value);
        return root.ToJsonString();
    }

    private static ExpandoObject ToData(JsonElement value) =>
        JsonConvert.DeserializeObject<ExpandoObject>(
            value.ValueKind == JsonValueKind.Undefined ? "{}" : value.GetRawText())!;

    private static void AssertErrors(
        IReadOnlyDictionary<string, string[]> actual,
        JsonElement expected,
        string purpose)
    {
        expected.ValueKind.Should().Be(JsonValueKind.Object, purpose);
        var expectedEntries = expected.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.EnumerateArray().Select(value => value.GetString()!).ToArray(),
            StringComparer.Ordinal);
        actual.Keys.Should().BeEquivalentTo(expectedEntries.Keys, options => options.WithStrictOrdering(), purpose);
        foreach (var entry in expectedEntries)
            actual[entry.Key].Should().Equal(entry.Value, purpose);
    }

    private static string CompileCode(InvalidOperationException exception)
    {
        const string prefix = "Unsupported form contract: ";
        exception.Message.Should().StartWith(prefix);
        return exception.Message[prefix.Length..].TrimEnd('.');
    }

    private static DirectorySnapshot ToDirectorySnapshot(FormContractVectorDirectory source) => new()
    {
        GenerationId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
        Issuer = "https://issuer.example/realms/flowzer",
        CompletedAtUtc = DateTime.Parse("2026-09-08T10:00:00Z").ToUniversalTime(),
        Users = source.Users.Select(user => new DirectoryUser
        {
            Id = Guid.Parse(user.Id),
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = "https://issuer.example/realms/flowzer",
            Subject = user.Subject,
            DisplayName = user.DisplayName,
            IsActive = user.Active
        }).ToList(),
        Groups = source.Groups.Select(group => new DirectoryGroup
        {
            Id = Guid.Parse(group.Id),
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = "https://issuer.example/realms/flowzer",
            ExternalId = group.ExternalId,
            Name = group.Name,
            Path = group.Path,
            IsActive = group.Active
        }).ToList(),
        Memberships = source.Memberships.Select(membership => new DirectoryMembership
        {
            UserId = Guid.Parse(membership.UserId),
            GroupId = Guid.Parse(membership.GroupId)
        }).ToList()
    };

    private sealed class FormContractVectorManifest
    {
        public int Version { get; set; }
        public required FormContractVectorDirectory Directory { get; set; }
        public required FormContractVector[] Cases { get; set; }
    }

    private sealed class FormContractVectorDirectory
    {
        public FormContractVectorUser[] Users { get; set; } = [];
        public FormContractVectorGroup[] Groups { get; set; } = [];
        public FormContractVectorMembership[] Memberships { get; set; } = [];
    }

    private sealed class FormContractVectorUser
    {
        public required string Id { get; set; }
        public required string Subject { get; set; }
        public required string DisplayName { get; set; }
        public bool Active { get; set; }
    }

    private sealed class FormContractVectorGroup
    {
        public required string Id { get; set; }
        public required string ExternalId { get; set; }
        public required string Name { get; set; }
        public required string Path { get; set; }
        public bool Active { get; set; }
    }

    private sealed class FormContractVectorMembership
    {
        public required string UserId { get; set; }
        public required string GroupId { get; set; }
    }

    private sealed class FormContractVector
    {
        public required string Id { get; set; }
        public required string Purpose { get; set; }
        public required string Phase { get; set; }
        public required string Profile { get; set; }
        public required string Comparison { get; set; }
        public required JsonElement Schema { get; set; }
        public JsonElement Context { get; set; }
        public JsonElement Input { get; set; }
        public int? SchemaPaddingLength { get; set; }
        public required FormContractVectorExpected Expected { get; set; }
    }

    private sealed class FormContractVectorExpected
    {
        public required string Outcome { get; set; }
        public string? Code { get; set; }
        public JsonElement Errors { get; set; }
        public JsonElement Output { get; set; }
    }
}
