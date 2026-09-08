using System.Dynamic;
using JsonSerializer = System.Text.Json.JsonSerializer;
using FluentAssertions;
using Newtonsoft.Json;
using WebApiEngine.Forms;

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
}
