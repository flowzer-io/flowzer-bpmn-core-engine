using System.Text.Json;
using System.Text.Json.Nodes;

namespace WebApiEngine.Forms;

/// <summary>
/// Verlustfreie Lesekompatibilität für die exakt bekannten Regeln früher ausgelieferter
/// Formularvorlagen. Kein beliebiger JavaScript-Interpreter, kein Löschen unbekannter Regeln.
/// Persistierte Versionen bleiben unverändert; neue Veröffentlichungen binden den Zielvertrag.
/// </summary>
public static class LegacyFormSchemaUpgrade
{
    private const string LegacyDateRule = """valid = (!data.von || !input || new Date(input) >= new Date(data.von)) ? true : 'Der letzte Urlaubstag darf nicht vor dem ersten liegen.';""";
    private const string LegacySummary = """var d = function (w) { if (!w) { return ''; } var t = new Date(w); if (isNaN(t)) { return String(w); } var p = function (n) { return (n < 10 ? '0' : '') + n; }; return p(t.getDate()) + '.' + p(t.getMonth() + 1) + '.' + t.getFullYear(); }; var arten = { erholung: 'Erholungsurlaub', sonder: 'Sonderurlaub', unbezahlt: 'Unbezahlter Urlaub' }; value = [ data.mitarbeiter, arten[data.art] || data.art, (data.von && data.bis) ? (d(data.von) + ' bis ' + d(data.bis)) : '', data.arbeitstage ? (data.arbeitstage + ' Arbeitstage') : '', data.vertretung ? ('Vertretung: ' + data.vertretung) : '' ].filter(function (teil) { return teil; }).join(' · ');""";

    public static string Normalize(string schema)
    {
        if (schema.Length > 1_048_576) return schema;
        JsonObject? root;
        try { root = JsonNode.Parse(schema, documentOptions: new JsonDocumentOptions { MaxDepth = 32 }) as JsonObject; }
        catch (JsonException) { return schema; }
        if (root is null) return schema;
        var changed = false;
        foreach (var field in Components(root["components"]))
        {
            if (field["key"]?.ToString() == "bis" && field["type"]?.ToString() == "datetime"
                && field["validate"] is JsonObject validation && validation["custom"]?.ToString() == LegacyDateRule)
            {
                var flowzer = root["flowzer"] as JsonObject ?? new JsonObject();
                if (root["flowzer"] is not null && root["flowzer"] is not JsonObject) continue;
                if (flowzer["rules"] is not null && flowzer["rules"] is not JsonArray) continue;
                var rules = flowzer["rules"] as JsonArray ?? new JsonArray();
                if (flowzer["rules"] is null) flowzer["rules"] = rules;
                if (!rules.OfType<JsonObject>().Any(rule => rule["kind"]?.ToString() == "dateOrder"
                    && rule["start"]?.ToString() == "von" && rule["end"]?.ToString() == "bis"))
                    rules.Add(new JsonObject { ["kind"] = "dateOrder", ["start"] = "von", ["end"] = "bis", ["allowEqual"] = true });
                if (root["flowzer"] is null) root["flowzer"] = flowzer;
                validation.Remove("custom");
                changed = true;
            }
            if (field["key"]?.ToString() == "vorgang" && field["type"]?.ToString() == "hidden"
                && field["calculateValue"]?.ToString() == LegacySummary)
            {
                var flowzer = field["flowzer"] as JsonObject ?? new JsonObject();
                if (field["flowzer"] is not null && field["flowzer"] is not JsonObject || flowzer["calculation"] is not null) continue;
                flowzer["calculation"] = new JsonObject
                {
                    ["name"] = "leave-summary.v1",
                    ["fields"] = new JsonArray("mitarbeiter", "art", "von", "bis", "arbeitstage", "vertretung")
                };
                if (field["flowzer"] is null) field["flowzer"] = flowzer;
                field.Remove("calculateValue");
                changed = true;
            }
        }
        return changed ? root.ToJsonString() : schema;
    }

    private static IEnumerable<JsonObject> Components(JsonNode? node)
    {
        if (node is JsonArray array)
            foreach (var child in array)
                foreach (var field in Components(child)) yield return field;
        if (node is not JsonObject obj) yield break;
        if (obj.ContainsKey("type")) yield return obj;
        foreach (var name in new[] { "components", "columns", "rows" })
            foreach (var child in Components(obj[name])) yield return child;
    }
}
