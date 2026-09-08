using System.Dynamic;
using JsonSerializer = System.Text.Json.JsonSerializer;
using FluentAssertions;
using Newtonsoft.Json;
using WebApiEngine.Forms;
using Model;
using StorageSystem;

namespace WebApiEngine.Tests;

public class FormValidationProfileTest
{
    private static IEnumerable<TestCaseData> UnsupportedSchemas()
    {
        string[] components =
        [
            "{\"type\":\"number\",\"key\":\"number\",\"validate\":{\"maxLength\":2}}",
            "{\"type\":\"checkbox\",\"key\":\"yes\",\"validate\":{\"pattern\":\"yes\"}}",
            "{\"type\":\"textfield\",\"key\":\"text\",\"validate\":{\"minSelectedCount\":2}}",
            "{\"type\":\"datetime\",\"key\":\"date\",\"datePicker\":{\"minDate\":\"today\"}}",
            "{\"type\":\"textfield\",\"key\":\"text\",\"validate\":{\"required\":\"yes\"}}",
            "{\"type\":\"textfield\",\"key\":\"text\",\"validate\":{\"minLength\":-1}}",
            "{\"type\":\"number\",\"key\":\"amount\",\"validate\":{\"min\":5,\"max\":2}}",
            "{\"type\":\"textfield\",\"key\":\"text\",\"validate\":{\"min\":2}}",
            "{\"type\":\"textfield\",\"key\":\"text\",\"multiple\":\"true\"}",
            "{\"type\":\"textfield\",\"key\":\"text\",\"conditional\":{\"when\":\"text\",\"eq\":\"yes\",\"show\":true}}",
            "{\"type\":\"textfield\",\"key\":\"text\",\"inputMask\":\"999\"}",
            "{\"type\":\"textfield\",\"key\":\"text\",\"validate\":{\"pattern\":\"(?=unsafe).*\"}}",
            "{\"type\":\"container\",\"key\":\"data\",\"components\":[]}",
            "{\"type\":\"select\",\"key\":\"choice\",\"dataSrc\":\"url\",\"data\":{\"url\":\"https://example.invalid\"}}"
        ];
        foreach (var component in components) yield return new TestCaseData("{\"components\":[" + component + "]}");
        yield return new TestCaseData("{\"components\":[],\"logic\":[{\"action\":\"untrusted\"}]}");
        yield return new TestCaseData("{\"flowzer\":{\"contractVersion\":999},\"components\":[]}");
    }

    // Testzweck: Nicht unterstützte Geschäftsregeln werden beim Deployment abgelehnt,
    // nicht etwa als reine UI-Metadaten ignoriert. Kein Regex-/Script-Fallback.
    [TestCaseSource(nameof(UnsupportedSchemas))]
    public void Compiler_ShouldRejectUnsupportedOrAmbiguousRules(string schema)
    {
        Action compile = () => FormContractCompiler.Compile(schema);
        compile.Should().Throw<InvalidOperationException>().WithMessage("*contract*");
    }

    // Testzweck: Ein leeres Array ist kein skalarer Wert; Mindestanzahlen müssen auch
    // bei leeren, ansonsten optionalen Mehrfachauswahlen gelten.
    [TestCase(false)]
    [TestCase(true)]
    public void Validator_ShouldCheckEmptyArrays(bool multiple)
    {
        var schema = JsonSerializer.Serialize(new { components = new[] {
            new { type = "select", key = "choice", multiple, validate = new { minSelectedCount = multiple ? 1 : 0 },
                data = new { values = new[] { new { value = "one" } } } }
        } });
        Action validate = () => FormSubmissionValidator.Validate(FormContractCompiler.Compile(schema), Data("{\"choice\":[]}"));
        validate.Should().Throw<FormSubmissionException>();
    }

    // Testzweck: Layoutbedingungen gelten auch für enthaltene Pflichtfelder; inaktive
    // Werte und unzulässige Auswahlwerte werden nicht als Prozessausgaben übernommen.
    [Test]
    public void Validator_ShouldApplyParentConditionsAndStaticMultipleSelection()
    {
        var contract = FormContractCompiler.Compile("""
            {"components":[{"type":"checkbox","key":"show"},
             {"type":"panel","conditional":{"when":"show","eq":"true","show":true},"components":[
               {"type":"textfield","key":"reason","validate":{"required":true}}]},
             {"type":"select","key":"choices","multiple":true,"data":{"values":[{"value":"one"},{"value":"two"}]},
               "validate":{"maxSelectedCount":2}}]}
            """);
        var result = FormSubmissionValidator.Validate(contract, Data("{\"show\":false,\"choices\":[\"one\",\"two\"]}"));
        ((IDictionary<string, object?>)result).Should().NotContainKey("reason");
        Action inactive = () => FormSubmissionValidator.Validate(contract, Data("{\"show\":false,\"reason\":\"not allowed\"}"));
        inactive.Should().Throw<FormSubmissionException>();
        Action invalidChoice = () => FormSubmissionValidator.Validate(contract, Data("{\"choices\":[\"other\"]}"));
        invalidChoice.Should().Throw<FormSubmissionException>();
    }

    // Testzweck: Eingaben werden nicht mutiert; Transport-UserId und nur lesbare Werte
    // gelangen nicht ins Ergebnis. Fehler enthalten keine eingesandten Werte.
    [Test]
    public void Validator_ShouldNotMutateOrExposeInputValues()
    {
        var contract = FormContractCompiler.Compile("""{"components":[{"type":"textfield","key":"value"},{"type":"textfield","key":"context","disabled":true}]}""");
        var data = Data("{\"value\":\"ok\",\"context\":\"known\",\"UserId\":\"spoof\"}");
        var before = JsonSerializer.Serialize(data);
        var result = FormSubmissionValidator.Validate(contract, data, Data("{\"context\":\"known\"}"));
        JsonSerializer.Serialize(result).Should().Be("{\"value\":\"ok\"}");
        JsonSerializer.Serialize(data).Should().Be(before);
        Action invalid = () => FormSubmissionValidator.Validate(contract, Data("{\"context\":\"DO_NOT_ECHO\"}"));
        invalid.Should().Throw<FormSubmissionException>().Which.Message.Should().NotContain("DO_NOT_ECHO");
    }

    private static ExpandoObject Data(string json) => JsonConvert.DeserializeObject<ExpandoObject>(json)!;

    // Testzweck: Eine benannte, rein lokale Berechnung ersetzt Formular-JavaScript.
    // Das Ergebnis stammt vom Server; ein abweichender Browserwert wird abgelehnt.
    [Test]
    public void NamedCalculation_ShouldProduceTrustedSummaryAndRejectForgedValue()
    {
        var contract = FormContractCompiler.Compile("""
            {"components":[{"type":"textfield","key":"name"},{"type":"number","key":"days"},
             {"type":"hidden","key":"summary","flowzer":{"calculation":{"name":"join.v1","fields":["name","days"]}}}]}
            """);
        var output = (IDictionary<string, object?>)FormSubmissionValidator.Validate(contract, Data("{\"name\":\"Anna\",\"days\":3}"));
        output["summary"].Should().Be("Anna · 3");
        Action forged = () => FormSubmissionValidator.Validate(contract, Data("{\"name\":\"Anna\",\"summary\":\"forged\"}"));
        forged.Should().Throw<FormSubmissionException>();
    }

    // Testzweck: Berechnungsnamen sind eine feste Registry, niemals Code oder URLs.
    [Test]
    public void Compiler_ShouldRejectUnknownCalculation()
    {
        Action compile = () => FormContractCompiler.Compile("""
            {"components":[{"type":"hidden","key":"summary","flowzer":{"calculation":{"name":"eval","fields":[]}}}]}
            """);
        compile.Should().Throw<InvalidOperationException>();
    }

    // Testzweck: Die neue Verzeichnisauswahl wird als typisierte Referenz gespeichert;
    // Anzeigenamen und E-Mail-Adressen duerfen den stabilen Identifier nicht ersetzen.
    [Test]
    public void DirectorySubjectField_ShouldAcceptOnlyEligibleTypedReferences()
    {
        var allowedUserId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var groupId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var contract = FormContractCompiler.Compile(JsonSerializer.Serialize(new
        {
            flowzer = new { contractVersion = 2 },
            components = new[]
            {
                new
                {
                    type = "flowzerSubject", key = "representative", validate = new { required = true },
                    flowzer = new
                    {
                        subjectSelection = new
                        {
                            allowUsers = true, allowGroups = false,
                            allowedUserIds = new[] { allowedUserId.ToString() },
                            userMemberOfGroupIds = new[] { groupId.ToString() }
                        }
                    }
                }
            }
        }));
        var snapshot = DirectorySnapshot(
            users: [DirectoryUser(allowedUserId, "subject-anna", true)],
            groups: [DirectoryGroup(groupId, true)],
            memberships: [new DirectoryMembership { UserId = allowedUserId, GroupId = groupId }]);

        var output = (IDictionary<string, object?>)FormSubmissionValidator.Validate(
            contract,
            Data(JsonSerializer.Serialize(new { representative = new { kind = "user", id = allowedUserId } })),
            directorySnapshot: snapshot);

        JsonSerializer.Serialize(output["representative"]).Should().Be(
            JsonSerializer.Serialize(new { kind = "user", id = allowedUserId.ToString() }));
        Action text = () => FormSubmissionValidator.Validate(contract, Data("{\"representative\":\"Anna\"}"), directorySnapshot: snapshot);
        Action wrongKind = () => FormSubmissionValidator.Validate(contract,
            Data(JsonSerializer.Serialize(new { representative = new { kind = "group", id = groupId } })), directorySnapshot: snapshot);
        text.Should().Throw<FormSubmissionException>().Which.Errors["representative"].Should().Contain("type.subject_ref");
        wrongKind.Should().Throw<FormSubmissionException>().Which.Errors["representative"].Should().Contain("selection.invalid");
    }

    // Testzweck: Mehrfachauswahl, Mindest-/Hoechstzahl und Directory-Snapshot werden gemeinsam
    // serverseitig geprueft; deaktivierte, doppelte oder manipulierte IDs gelangen nicht in den Prozess.
    [Test]
    public void DirectorySubjectField_ShouldRejectUnavailableDuplicateAndExcessiveSelections()
    {
        var activeId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var inactiveId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var contract = FormContractCompiler.Compile("""
            {"flowzer":{"contractVersion":2},"components":[{"type":"flowzerSubject","key":"reviewers","multiple":true,
              "validate":{"minSelectedCount":1,"maxSelectedCount":2},
              "flowzer":{"subjectSelection":{"allowUsers":true,"allowGroups":false}}}]}
            """);
        var snapshot = DirectorySnapshot(
            users: [DirectoryUser(activeId, "active", true), DirectoryUser(inactiveId, "inactive", false)]);

        Action unavailable = () => FormSubmissionValidator.Validate(contract,
            Data(JsonSerializer.Serialize(new { reviewers = new[] { new { kind = "user", id = inactiveId } } })), directorySnapshot: snapshot);
        Action duplicate = () => FormSubmissionValidator.Validate(contract,
            Data(JsonSerializer.Serialize(new { reviewers = new[] { new { kind = "user", id = activeId }, new { kind = "user", id = activeId } } })), directorySnapshot: snapshot);
        Action tooMany = () => FormSubmissionValidator.Validate(contract,
            Data(JsonSerializer.Serialize(new { reviewers = new[] { new { kind = "user", id = activeId }, new { kind = "user", id = Guid.NewGuid() }, new { kind = "user", id = Guid.NewGuid() } } })), directorySnapshot: snapshot);
        Action missingSnapshot = () => FormSubmissionValidator.Validate(contract,
            Data(JsonSerializer.Serialize(new { reviewers = new[] { new { kind = "user", id = activeId } } })));

        unavailable.Should().Throw<FormSubmissionException>().Which.Errors["reviewers"].Should().Contain("selection.invalid");
        duplicate.Should().Throw<FormSubmissionException>().Which.Errors["reviewers"].Should().Contain("selection.duplicate");
        tooMany.Should().Throw<FormSubmissionException>().Which.Errors["reviewers"].Should().Contain("selection.max");
        missingSnapshot.Should().Throw<FormSubmissionException>().Which.Errors["reviewers"].Should().Contain("directory.unavailable");
    }

    // Testzweck: Der publizierbare Vertrag akzeptiert nur eindeutige UUID-Filter und eine
    // sinnvolle Kombination erlaubter Identitaetsarten; Browser-Metadaten erweitern ihn nicht.
    [TestCase("{\"allowUsers\":false,\"allowGroups\":false}")]
    // Testzweck: Ungültige UUID-Filter werden bereits beim Kompilieren abgewiesen.
    [TestCase("{\"allowUsers\":true,\"allowedUserIds\":[\"not-a-guid\"]}")]
    // Testzweck: Doppelte stabile Referenzen dürfen nicht veröffentlicht werden.
    [TestCase("{\"allowUsers\":true,\"allowedUserIds\":[\"20000000-0000-0000-0000-000000000001\",\"20000000-0000-0000-0000-000000000001\"]}")]
    // Testzweck: Verzeichnisfelder dürfen deaktivierte Identitäten nie freigeben.
    [TestCase("{\"allowUsers\":true,\"activeOnly\":false}")]
    public void DirectorySubjectField_ShouldRejectUnsafePolicies(string policy)
    {
        Action compile = () => FormContractCompiler.Compile(
            "{\"flowzer\":{\"contractVersion\":2},\"components\":[{\"type\":\"flowzerSubject\",\"key\":\"person\",\"flowzer\":{\"subjectSelection\":" + policy + "}}]}");

        compile.Should().Throw<InvalidOperationException>().WithMessage("*contract*");
    }

    // Testzweck: Vom Builder serialisierte leere Filterlisten behalten die sichere
    // Standardpolicy "alle aktiven Benutzer", statt unbemerkt jede Auswahl auszuschließen.
    [Test]
    public void DirectorySubjectField_ShouldTreatEmptyFilterArraysAsUnrestricted()
    {
        var activeId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var contract = FormContractCompiler.Compile("""
            {"flowzer":{"contractVersion":2},"components":[{"type":"flowzerSubject","key":"person",
              "flowzer":{"subjectSelection":{"allowedUserIds":[],"userMemberOfGroupIds":[]}}}]}
            """);

        Action validate = () => FormSubmissionValidator.Validate(contract,
            Data(JsonSerializer.Serialize(new { person = new { kind = "user", id = activeId } })),
            directorySnapshot: DirectorySnapshot(users: [DirectoryUser(activeId, "active", true)]));

        validate.Should().NotThrow();
    }

    private static DirectorySnapshot DirectorySnapshot(
        IReadOnlyList<DirectoryUser>? users = null,
        IReadOnlyList<DirectoryGroup>? groups = null,
        IReadOnlyList<DirectoryMembership>? memberships = null) => new()
    {
        GenerationId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
        Issuer = "https://issuer.example/realms/flowzer",
        CompletedAtUtc = DateTime.Parse("2026-09-08T10:00:00Z").ToUniversalTime(),
        Users = users?.ToList() ?? [],
        Groups = groups?.ToList() ?? [],
        Memberships = memberships?.ToList() ?? []
    };

    private static DirectoryUser DirectoryUser(Guid id, string subject, bool active) => new()
    {
        Id = id,
        SourceKind = DirectorySourceKind.Keycloak,
        Issuer = "https://issuer.example/realms/flowzer",
        Subject = subject,
        DisplayName = subject,
        IsActive = active
    };

    private static DirectoryGroup DirectoryGroup(Guid id, bool active) => new()
    {
        Id = id,
        SourceKind = DirectorySourceKind.Keycloak,
        Issuer = "https://issuer.example/realms/flowzer",
        ExternalId = id.ToString(),
        Name = "Allowed",
        Path = "/Allowed",
        IsActive = active
    };
}
