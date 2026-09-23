using System.Text.RegularExpressions;
using System.Xml.Linq;
using WebApiEngine.Shared;

namespace WebApiEngine.ProcessPackages;

/// <summary>Die Anzeigenamen, mit denen ein Bezug im Manifest benannt wird.</summary>
public sealed record ProcessPackageDirectoryNames(
    IReadOnlyDictionary<Guid, string> Users,
    IReadOnlyDictionary<Guid, string> Groups,
    IReadOnlyDictionary<Guid, string> AiConnections)
{
    public static readonly ProcessPackageDirectoryNames Empty =
        new(new Dictionary<Guid, string>(), new Dictionary<Guid, string>(), new Dictionary<Guid, string>());
}

/// <summary>Das entschaerfte BPMN und die Bezuege, die es beim Import noch braucht.</summary>
public sealed record ProcessPackageScan(string Xml, IReadOnlyList<ProcessPackageReferenceDto> References);

/// <summary>
/// Findet in einem BPMN-Dokument alles, was nur in einer bestimmten Installation eine Bedeutung
/// hat, und ersetzt es fuer den Export durch Platzhalter.
///
/// Zwei Arten werden unterschieden. Kennungen aus dem Verzeichnis und KI-Verbindungen
/// <em>muessen</em> ersetzt werden: Sie benennen Personen beziehungsweise Zugaenge der
/// Quellinstallation und haben in einem weitergegebenen Paket nichts zu suchen. Auftragstypen,
/// Secret-<em>Namen</em>, aufgerufene Prozesse und Entscheidungen bleiben dagegen unveraendert im
/// Modell; sie werden nur genannt, damit beim Import klar ist, was die Zielinstallation
/// bereitstellen muss.
///
/// Derselbe Wert an mehreren Knoten wird zu genau einem Bezug zusammengefasst — wer eine Gruppe
/// an drei Aufgaben verwendet, soll sie beim Import einmal zuordnen und nicht dreimal.
/// </summary>
public static class ProcessPackageReferences
{
    private static readonly XNamespace Flowzer = "https://flowzer.io/schema/bpmn/1.0";

    /// <summary>Der interne Auftragstyp eines KI-Tasks; dafuer braucht niemand einen Worker.</summary>
    private const string AiWorkerType = "flowzer.ai.v1";

    /// <summary>
    /// Eine Secret-Referenz, wie Konnektoren sie schreiben: <c>secret:NAME</c>. Uebernommen wird
    /// ausschliesslich der Name — ein geheimer Wert steht ohnehin nie im Modell, und diese
    /// Auswertung darf auch keinen dorthin geratenen Wert mitnehmen.
    /// </summary>
    private static readonly Regex SecretReference =
        new(@"secret:([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Liest das Modell, sammelt die Bezuege und liefert das BPMN zurueck, das ins Paket gehoert:
    /// identisch bis auf die Platzhalter an den installationsgebundenen Stellen.
    /// </summary>
    public static ProcessPackageScan Extract(string xml, ProcessPackageDirectoryNames names)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var collector = new Collector(names);

        foreach (var assignment in document.Descendants()
                     .Where(element => element.Name == Flowzer + "taskAssignment")
                     .Where(element => string.Equals(element.Attribute("mode")?.Value, "directory", StringComparison.Ordinal))
                     .ToArray())
        {
            RewriteSingle(assignment, "assigneeId", collector, ProcessPackageReferenceKinds.DirectoryUser);
            RewriteList(assignment, "candidateUserIds", collector, ProcessPackageReferenceKinds.DirectoryUser);
            RewriteList(assignment, "candidateGroupIds", collector, ProcessPackageReferenceKinds.DirectoryGroup);
        }

        foreach (var aiTask in document.Descendants()
                     .Where(element => element.Name == Flowzer + "aiTask")
                     .ToArray())
        {
            RewriteSingle(aiTask, "connectionId", collector, ProcessPackageReferenceKinds.AiConnection);
        }

        foreach (var taskDefinition in document.Descendants()
                     .Where(element => element.Name.LocalName == "taskDefinition"))
        {
            var jobType = taskDefinition.Attribute("type")?.Value;
            if (!string.IsNullOrWhiteSpace(jobType) && !string.Equals(jobType, AiWorkerType, StringComparison.Ordinal))
                collector.Note(ProcessPackageReferenceKinds.JobType, jobType, taskDefinition);
        }

        foreach (var called in document.Descendants()
                     .Where(element => element.Name.LocalName == "calledElement"))
        {
            var processId = called.Attribute("processId")?.Value;
            if (!string.IsNullOrWhiteSpace(processId))
                collector.Note(ProcessPackageReferenceKinds.CalledProcess, processId, called);
        }

        foreach (var decision in document.Descendants()
                     .Where(element => element.Name.LocalName == "calledDecision"))
        {
            var decisionId = decision.Attribute("decisionId")?.Value;
            if (!string.IsNullOrWhiteSpace(decisionId))
                collector.Note(ProcessPackageReferenceKinds.CalledDecision, decisionId, decision);
        }

        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
                CollectSecrets(attribute.Value, element, collector);

            // Nur echte Textknoten; der zusammengesetzte Wert eines Containers wiederholte
            // jeden Fund seiner Kinder.
            foreach (var text in element.Nodes().OfType<XText>())
                CollectSecrets(text.Value, element, collector);
        }

        return new ProcessPackageScan(document.ToString(SaveOptions.DisableFormatting), collector.References);
    }

    /// <summary>
    /// Setzt die Zuordnung in das BPMN ein: Jeder Platzhalter wird durch die Kennung ersetzt, die
    /// im Zielsystem gemeint ist. Ein Platzhalter ohne Zuordnung ist ein Fehler und wird gemeldet,
    /// statt eine halb aufgeloeste Definition entstehen zu lassen.
    /// </summary>
    public static string Apply(string xml, IReadOnlyDictionary<string, string> mapping)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        List<string> unresolved = [];

        foreach (var assignment in document.Descendants().Where(element => element.Name == Flowzer + "taskAssignment"))
        {
            ApplySingle(assignment, "assigneeId", mapping, unresolved);
            ApplyList(assignment, "candidateUserIds", mapping, unresolved);
            ApplyList(assignment, "candidateGroupIds", mapping, unresolved);
        }

        foreach (var aiTask in document.Descendants().Where(element => element.Name == Flowzer + "aiTask"))
            ApplySingle(aiTask, "connectionId", mapping, unresolved);

        if (unresolved.Count > 0)
            throw new ProcessPackageException("package.mapping.incomplete",
                "Fuer diese Bezuege fehlt eine Zuordnung: " + string.Join(", ", unresolved.Distinct(StringComparer.Ordinal)) + ".");

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static void RewriteSingle(XElement element, string attributeName, Collector collector, string kind)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is null || string.IsNullOrWhiteSpace(attribute.Value)) return;

        attribute.Value = collector.Placeholder(kind, attribute.Value.Trim(), element);
    }

    private static void RewriteList(XElement element, string attributeName, Collector collector, string kind)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is null || string.IsNullOrWhiteSpace(attribute.Value)) return;

        attribute.Value = string.Join(",", Split(attribute.Value)
            .Select(value => collector.Placeholder(kind, value, element)));
    }

    private static void ApplySingle(
        XElement element,
        string attributeName,
        IReadOnlyDictionary<string, string> mapping,
        List<string> unresolved)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is null || !ProcessPackageFormat.IsPlaceholder(attribute.Value)) return;

        if (mapping.TryGetValue(attribute.Value, out var target) && !string.IsNullOrWhiteSpace(target))
            attribute.Value = target.Trim();
        else
            unresolved.Add(attribute.Value);
    }

    private static void ApplyList(
        XElement element,
        string attributeName,
        IReadOnlyDictionary<string, string> mapping,
        List<string> unresolved)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is null || string.IsNullOrWhiteSpace(attribute.Value)) return;

        List<string> resolved = [];
        foreach (var value in Split(attribute.Value))
        {
            if (!ProcessPackageFormat.IsPlaceholder(value))
            {
                resolved.Add(value);
                continue;
            }

            if (mapping.TryGetValue(value, out var target) && !string.IsNullOrWhiteSpace(target))
                resolved.Add(target.Trim());
            else
                unresolved.Add(value);
        }

        attribute.Value = string.Join(",", resolved);
    }

    private static void CollectSecrets(string value, XElement element, Collector collector)
    {
        if (!value.Contains("secret:", StringComparison.Ordinal)) return;

        foreach (Match match in SecretReference.Matches(value))
            collector.Note(ProcessPackageReferenceKinds.Secret, match.Groups[1].Value, element);
    }

    private static IEnumerable<string> Split(string value) => value
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Der BPMN-Knoten, an dem ein Fund haengt. Erweiterungen stehen in
    /// <c>extensionElements</c> und tragen dort eigene Kennungen; gemeint ist die Aufgabe
    /// darueber, denn nur sie sagt einer Bedienung etwas.
    /// </summary>
    private static XElement? OwningFlowNode(XElement element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (current.Name.LocalName is "extensionElements" or "definitions") continue;
            if (current.Attribute("id") is { } id && !string.IsNullOrWhiteSpace(id.Value)) return current;
        }

        return null;
    }

    /// <summary>Sammelt Bezuege, vergibt Platzhalter und fasst gleiche Werte zusammen.</summary>
    private sealed class Collector(ProcessPackageDirectoryNames names)
    {
        private readonly Dictionary<(string Kind, string Value), ProcessPackageReferenceDto> _byValue = new();
        private readonly List<ProcessPackageReferenceDto> _references = [];

        public IReadOnlyList<ProcessPackageReferenceDto> References => _references;

        /// <summary>Ersetzt einen installationsgebundenen Wert und liefert seinen Platzhalter.</summary>
        public string Placeholder(string kind, string value, XElement element)
        {
            if (_byValue.TryGetValue((kind, value), out var existing)) return existing.Id;

            var reference = Add(kind, value, Label(kind, value), element, requiresMapping: true,
                ProcessPackageFormat.Placeholder(_references.Count + 1).ToString());
            return reference.Id;
        }

        /// <summary>Nennt einen Bezug, ohne das Modell zu veraendern.</summary>
        public void Note(string kind, string value, XElement element)
        {
            if (_byValue.ContainsKey((kind, value))) return;
            Add(kind, value, value, element, requiresMapping: false, $"{kind}:{value}");
        }

        private ProcessPackageReferenceDto Add(
            string kind,
            string value,
            string label,
            XElement element,
            bool requiresMapping,
            string id)
        {
            var owner = OwningFlowNode(element);
            var reference = new ProcessPackageReferenceDto
            {
                Id = id,
                Kind = kind,
                ElementId = owner?.Attribute("id")?.Value ?? "",
                ElementName = owner?.Attribute("name")?.Value,
                Label = label,
                RequiresMapping = requiresMapping
            };
            _byValue[(kind, value)] = reference;
            _references.Add(reference);
            return reference;
        }

        /// <summary>
        /// Der Anzeigename statt der Kennung. Ist der Eintrag hier nicht mehr bekannt — geloescht
        /// oder aus einem aelteren Verzeichnisstand —, steht das ausdruecklich da. Die Kennung
        /// selbst wird auch dann nicht ausgegeben.
        /// </summary>
        private string Label(string kind, string value)
        {
            if (!Guid.TryParse(value, out var id)) return "(unbekannter Eintrag)";

            var found = kind switch
            {
                ProcessPackageReferenceKinds.DirectoryUser => names.Users.GetValueOrDefault(id),
                ProcessPackageReferenceKinds.DirectoryGroup => names.Groups.GetValueOrDefault(id),
                ProcessPackageReferenceKinds.AiConnection => names.AiConnections.GetValueOrDefault(id),
                _ => null
            };

            return string.IsNullOrWhiteSpace(found) ? "(unbekannter Eintrag)" : found;
        }
    }
}
