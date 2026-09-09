using System.Text.Json;
using core_engine;
using FluentAssertions;

namespace core_engine_tests;

/// <summary>Vertragstests fuer das bewusst kleine, provideruebergreifende KI-Ergebnisschema.</summary>
public sealed class AiResultSchemaProfileTest
{
    private const string ClassificationSchema = """
        {
          "type": "object",
          "properties": {
            "category": { "type": "string", "enum": ["sales", "support"] },
            "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
            "tags": {
              "type": "array",
              "items": { "type": "string", "minLength": 1, "maxLength": 20 },
              "maxItems": 3
            }
          },
          "required": ["category", "confidence"],
          "additionalProperties": false
        }
        """;

    // Testzweck: Ein verschachteltes Ergebnis innerhalb des dokumentierten Profils wird
    // geprueft und als vom Quelldokument unabhaengiger JSON-Wert zurueckgegeben.
    [Test]
    public void ParseAndValidateResult_ShouldReturnValidStructuredResult()
    {
        var result = AiResultSchemaProfile.ParseAndValidateResult(
            ClassificationSchema,
            """{"category":"support","confidence":0.85,"tags":["urgent"]}""");

        result.ValueKind.Should().Be(JsonValueKind.Object);
        result.GetProperty("category").GetString().Should().Be("support");
        result.GetProperty("confidence").GetDecimal().Should().Be(0.85m);
    }

    // Testzweck: Pflichtfelder, zusaetzliche Eigenschaften und typisierte Werte werden nach
    // dem Provideraufruf erneut serverseitig statt nur durch Provider-Versprechen geprueft.
    [TestCase("{\"category\":\"support\"}", "ai.result.required", "$.confidence")]
    [TestCase("{\"category\":\"support\",\"confidence\":0.8,\"hidden\":true}", "ai.result.additional_property", "$.hidden")]
    [TestCase("{\"category\":\"finance\",\"confidence\":0.8}", "ai.result.enum", "$.category")]
    [TestCase("{\"category\":\"support\",\"confidence\":2}", "ai.result.maximum", "$.confidence")]
    [TestCase("{\"category\":\"support\",\"confidence\":0.8,\"tags\":[\"\"]}", "ai.result.min_length", "$.tags[0]")]
    public void ParseAndValidateResult_ShouldRejectSchemaViolation(
        string json,
        string expectedCode,
        string expectedPath)
    {
        var action = () => AiResultSchemaProfile.ParseAndValidateResult(ClassificationSchema, json);

        var exception = action.Should().Throw<AiResultSchemaException>().Which;
        exception.Code.Should().Be(expectedCode);
        exception.Path.Should().Be(expectedPath);
    }

    // Testzweck: Syntaktisch ungueltige und uebergrosse Providerantworten werden vor einer
    // Weitergabe an Prozessvariablen mit einem stabilen Fehlercode abgelehnt.
    [Test]
    public void ParseAndValidateResult_ShouldRejectInvalidJson()
    {
        var action = () => AiResultSchemaProfile.ParseAndValidateResult(ClassificationSchema, "not-json");

        var exception = action.Should().Throw<AiResultSchemaException>().Which;
        exception.Code.Should().Be("ai.result.invalid_json");
        exception.Path.Should().Be("$");
        exception.Message.Should().NotContain("not-json");
    }

    // Testzweck: Numerische Providerwerte ausserhalb des bewusst unterstuetzten endlichen
    // Wertebereichs werden stabil klassifiziert und koennen keine Parserexception ausloesen.
    [Test]
    public void ParseAndValidateResult_ShouldRejectNumberOutsideFiniteRange()
    {
        var action = () => AiResultSchemaProfile.ParseAndValidateResult(
            "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"number\"}}}",
            "{\"value\":1e400}");

        var exception = action.Should().Throw<AiResultSchemaException>().Which;
        exception.Code.Should().Be("ai.result.type");
        exception.Path.Should().Be("$.value");
    }

    // Testzweck: Das portable Profil lehnt unkontrollierbare Referenzen und unbekannte
    // Schluesselwoerter ab, bevor Provider sie unterschiedlich interpretieren koennen.
    [TestCase("{\"type\":\"object\",\"$ref\":\"https://example.test/schema\"}", "$.$ref")]
    [TestCase("{\"type\":\"object\",\"patternProperties\":{}}", "$.patternProperties")]
    public void ValidateSchema_ShouldRejectUnsupportedKeyword(string schema, string expectedPath)
    {
        var action = () => AiResultSchemaProfile.ValidateSchema(schema);

        var exception = action.Should().Throw<AiResultSchemaException>().Which;
        exception.Code.Should().Be("ai.result_schema.keyword_unsupported");
        exception.Path.Should().Be(expectedPath);
    }

    // Testzweck: Das Wurzelergebnis bleibt immer ein Objekt, damit deklarierte BPMN-
    // Ausgabezuordnungen nicht zwischen skalaren und strukturierten Werten wechseln.
    [Test]
    public void ValidateSchema_ShouldRequireObjectRoot()
    {
        var action = () => AiResultSchemaProfile.ValidateSchema("{\"type\":\"string\"}");

        var exception = action.Should().Throw<AiResultSchemaException>().Which;
        exception.Code.Should().Be("ai.result_schema.object_required");
        exception.Path.Should().Be("$.type");
    }

    // Testzweck: Auch bekannte Schluesselwoerter gelten nur fuer ihren Datentyp, damit
    // Provider ein Schema nicht unterschiedlich ignorieren oder umdeuten koennen.
    [Test]
    public void ValidateSchema_ShouldRejectKeywordForWrongType()
    {
        var action = () => AiResultSchemaProfile.ValidateSchema(
            "{\"type\":\"object\",\"minLength\":2}");

        var exception = action.Should().Throw<AiResultSchemaException>().Which;
        exception.Code.Should().Be("ai.result_schema.keyword_invalid");
        exception.Path.Should().Be("$.minLength");
    }

    // Testzweck: Schema-Komplexitaet ist begrenzt, damit fremde Workflowpakete keine
    // unbeschraenkte Rekursion oder uebergrosse Validierungsarbeit ausloesen koennen.
    [Test]
    public void ValidateSchema_ShouldRejectTooDeepSchema()
    {
        var schema = "{\"type\":\"object\",\"properties\":{\"a\":"
                     + string.Concat(Enumerable.Repeat("{\"type\":\"array\",\"items\":", 13))
                     + "{\"type\":\"string\"}"
                     + string.Concat(Enumerable.Repeat("}", 13))
                     + "}}";

        var action = () => AiResultSchemaProfile.ValidateSchema(schema);

        action.Should().Throw<AiResultSchemaException>()
            .Where(exception => exception.Code == "ai.result_schema.too_complex");
    }
}
