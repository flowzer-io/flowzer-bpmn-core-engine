using core_engine.Exceptions;
using FlowzerDmn.Evaluation;
using FlowzerDmn.Model;
using FlowzerDmn.Parsing;
using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Middleware;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Der Katalog der Entscheidungsdateien und ihr Trockenlauf.
///
/// Eine Entscheidungsdatei ist ein DMN-Dokument mit einer oder mehreren Entscheidungen. Sie
/// wird versioniert; deployt ist immer die juengste Version — Entwuerfe gibt es hier nicht.
/// Ein Business-Rule-Task ruft eine einzelne Entscheidung ueber ihre
/// <c>zeebe:calledDecision/@decisionId</c> auf, nicht die Datei.
/// </summary>
[ApiController, Route("[controller]")]
public sealed class DecisionController(
    IStorageSystem storageSystem,
    BpmnBusinessLogic bpmnBusinessLogic,
    ICurrentUserContextAccessor currentUserContextAccessor,
    IAuthorizationService authorizationService) : FlowzerControllerBase
{
    /// <summary>
    /// Meldung, wenn fuer den Trockenlauf die Rolle fehlt. Er gibt Auskunft ueber die
    /// Entscheidungslogik und ueber Werte, die man ihm mitgibt — deshalb ist er den Rollen
    /// vorbehalten, die ohnehin modellieren oder den Betrieb beobachten.
    /// </summary>
    private const string MissingDryRunPermission =
        "Den Trockenlauf einer Entscheidung duerfen nur die Rollen fuers Modellieren und fuer den Betrieb ausloesen.";

    /// <summary>Der Katalog: je Eintrag die juengste Version, bewusst ohne das XML.</summary>
    [HttpGet]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionDto[]>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiStatusResult<DecisionDefinitionDto[]>>> GetCatalog()
    {
        var entries = new List<DecisionDefinitionDto>();
        foreach (var definition in await storageSystem.DecisionStorage.GetDefinitions())
        {
            var latest = await storageSystem.DecisionStorage.GetLatestVersion(definition.DecisionDefinitionId);
            // Ein Katalogeintrag ohne Stand kann nur aus einem abgebrochenen Schreibvorgang
            // stammen. Er traegt weder Version noch Zeitpunkt und gehoert nicht in die Liste.
            if (latest is not null)
            {
                entries.Add(ToDto(definition, latest));
            }
        }

        return Ok(new ApiStatusResult<DecisionDefinitionDto[]>(entries.ToArray()));
    }

    /// <summary>
    /// Legt eine Entscheidungsdatei an. Die Kennung kommt aus <c>dmn:definitions/@id</c>; hat
    /// das Dokument keine, vergibt der Server eine.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionDto>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionDto>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<DecisionDefinitionDto>>> Create(
        SaveDecisionDefinitionRequestDto request)
    {
        if (!TryReadXml(request, out var xml))
        {
            return BadRequest(new ApiStatusResult<DecisionDefinitionDto>("xml is required."));
        }

        // Parse-Fehler des DMN-Kerns beantwortet die Problem-Details-Mechanik mit 422 samt
        // seiner Meldung; sie nennt die Stelle in der Datei.
        var parsed = DmnModelParser.Parse(xml);
        var decisionDefinitionId = string.IsNullOrWhiteSpace(parsed.Id)
            ? $"decision_{Guid.NewGuid():N}"
            : parsed.Id;

        if (!DefinitionIdRules.IsValid(decisionDefinitionId))
        {
            return BadRequest(new ApiStatusResult<DecisionDefinitionDto>(
                DefinitionIdRules.BuildErrorMessage(decisionDefinitionId)));
        }

        if (await storageSystem.DecisionStorage.GetDefinition(decisionDefinitionId) is not null)
        {
            return Conflict(new ApiStatusResult<DecisionDefinitionDto>(
                $"Es gibt bereits eine Entscheidungsdatei mit der Kennung „{decisionDefinitionId}“. "
                + "Eine neue Fassung wird mit PUT gespeichert."));
        }

        var stored = await SaveAsync(decisionDefinitionId, request.Name, parsed, xml);
        return Ok(new ApiStatusResult<DecisionDefinitionDto>(
            ToDto(ResolveName(decisionDefinitionId, request.Name, parsed), stored)));
    }

    /// <summary>Speichert das XML als neue Version einer vorhandenen Entscheidungsdatei.</summary>
    [HttpPut("{decisionDefinitionId}")]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionDto>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionDto>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<DecisionDefinitionDto>>> Update(
        string decisionDefinitionId,
        SaveDecisionDefinitionRequestDto request)
    {
        if (!TryReadXml(request, out var xml))
        {
            return BadRequest(new ApiStatusResult<DecisionDefinitionDto>("xml is required."));
        }

        DefinitionIdRules.EnsureValid(decisionDefinitionId);
        if (await storageSystem.DecisionStorage.GetDefinition(decisionDefinitionId) is null)
        {
            return NotFound(NotFoundMessage<DecisionDefinitionDto>(decisionDefinitionId));
        }

        // Die Kennung der Datei kommt aus der Adresse und nicht aus dem hochgeladenen XML:
        // Ein im Editor geaendertes definitions/@id soll den Katalogeintrag nicht wechseln,
        // sondern genau diese Datei weiterschreiben.
        var parsed = DmnModelParser.Parse(xml);
        var stored = await SaveAsync(decisionDefinitionId, request.Name, parsed, xml);

        return Ok(new ApiStatusResult<DecisionDefinitionDto>(
            ToDto(ResolveName(decisionDefinitionId, request.Name, parsed), stored)));
    }

    /// <summary>Die juengste Version einer Entscheidungsdatei samt XML.</summary>
    [HttpGet("{decisionDefinitionId}")]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionDetailDto>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiStatusResult<DecisionDefinitionDetailDto>>> Get(string decisionDefinitionId)
    {
        DefinitionIdRules.EnsureValid(decisionDefinitionId);
        var definition = await storageSystem.DecisionStorage.GetDefinition(decisionDefinitionId);
        var latest = definition is null
            ? null
            : await storageSystem.DecisionStorage.GetLatestVersion(decisionDefinitionId);

        return definition is null || latest is null
            ? NotFound(NotFoundMessage<DecisionDefinitionDetailDto>(decisionDefinitionId))
            : Ok(new ApiStatusResult<DecisionDefinitionDetailDto>(ToDetailDto(definition, latest)));
    }

    /// <summary>Die Versionsliste, bewusst ohne das XML der einzelnen Staende.</summary>
    [HttpGet("{decisionDefinitionId}/versions")]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionVersionDto[]>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionVersionDto[]>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiStatusResult<DecisionDefinitionVersionDto[]>>> GetVersions(
        string decisionDefinitionId)
    {
        DefinitionIdRules.EnsureValid(decisionDefinitionId);
        if (await storageSystem.DecisionStorage.GetDefinition(decisionDefinitionId) is null)
        {
            return NotFound(NotFoundMessage<DecisionDefinitionVersionDto[]>(decisionDefinitionId));
        }

        var versions = await storageSystem.DecisionStorage.GetVersions(decisionDefinitionId);
        return Ok(new ApiStatusResult<DecisionDefinitionVersionDto[]>(
            versions.Select(ToVersionDto).ToArray()));
    }

    /// <summary>Eine bestimmte Version samt XML.</summary>
    [HttpGet("{decisionDefinitionId}/versions/{version:int}")]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<DecisionDefinitionDetailDto>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiStatusResult<DecisionDefinitionDetailDto>>> GetVersion(
        string decisionDefinitionId,
        int version)
    {
        DefinitionIdRules.EnsureValid(decisionDefinitionId);
        var definition = await storageSystem.DecisionStorage.GetDefinition(decisionDefinitionId);
        var stored = definition is null
            ? null
            : await storageSystem.DecisionStorage.GetVersion(decisionDefinitionId, version);

        return definition is null || stored is null
            ? NotFound(new ApiStatusResult<DecisionDefinitionDetailDto>(
                $"Es gibt keine Version {version} der Entscheidungsdatei „{decisionDefinitionId}“."))
            : Ok(new ApiStatusResult<DecisionDefinitionDetailDto>(ToDetailDto(definition, stored)));
    }

    /// <summary>
    /// Entfernt eine Entscheidungsdatei samt allen Versionen.
    ///
    /// Ruft ein Workflow eine ihrer Entscheidungen auf, bleibt sie stehen: Der
    /// Business-Rule-Task liefe sonst in <c>DECISION_NOT_FOUND</c> — auch in bereits
    /// laufenden Instanzen. Der Aufrufer bekommt die betroffenen Workflows genannt.
    /// </summary>
    [HttpDelete("{decisionDefinitionId}")]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    [ProducesResponseType<ApiStatusResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiStatusResult>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiStatusResult>> Delete(string decisionDefinitionId)
    {
        var ergebnis = await bpmnBusinessLogic.DeleteDecisionIfUnused(decisionDefinitionId);

        if (ergebnis.Definition is null)
        {
            return NotFound(new ApiStatusResult(
                $"Es gibt keine Entscheidungsdatei mit der Kennung „{decisionDefinitionId}“."));
        }

        if (ergebnis.UsedBy.Count > 0)
        {
            return Conflict(new ApiStatusResult(
                $"Die Entscheidungsdatei „{ergebnis.Definition.Name}“ wird von "
                + $"{string.Join(", ", ergebnis.UsedBy)} benutzt. Erst dort ersetzen, dann loeschen."));
        }

        return Ok(new ApiStatusResult { Successful = true });
    }

    /// <summary>
    /// Rechnet eine Entscheidung der juengsten Version — ohne Instanz und ohne Seiteneffekt.
    /// </summary>
    [HttpPost("{decisionDefinitionId}/evaluate")]
    [ProducesResponseType<ApiStatusResult<DecisionEvaluationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<DecisionEvaluationDto>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiStatusResult<DecisionEvaluationDto>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiStatusResult<DecisionEvaluationDto>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiStatusResult<DecisionEvaluationDto>>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<DecisionEvaluationDto>>> Evaluate(
        string decisionDefinitionId,
        EvaluateDecisionRequestDto request)
    {
        // Der Trockenlauf steht Modellierenden und dem Betrieb offen. Eine eigene Richtlinie
        // fuer diese Oder-Verknuepfung gibt es nicht; gefragt werden die beiden vorhandenen —
        // dasselbe Vorgehen wie im AiConnectionController fuer „darf auch verwalten".
        if (!await MayDryRun())
        {
            return ForbiddenCapability<DecisionEvaluationDto>(MissingDryRunPermission);
        }

        DefinitionIdRules.EnsureValid(decisionDefinitionId);
        if (string.IsNullOrWhiteSpace(request?.DecisionId))
        {
            return BadRequest(new ApiStatusResult<DecisionEvaluationDto>("decisionId is required."));
        }

        var latest = await storageSystem.DecisionStorage.GetLatestVersion(decisionDefinitionId);
        if (latest is null)
        {
            return NotFound(NotFoundMessage<DecisionEvaluationDto>(decisionDefinitionId));
        }

        var definitions = DmnModelParser.Parse(latest.Xml);
        var variables = request.Variables is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(
                (IDictionary<string, object?>)request.Variables, StringComparer.Ordinal);

        try
        {
            var result = new DmnDecisionEvaluator(FlowzerConfig.Default.FeelEngine)
                .Evaluate(definitions, request.DecisionId, variables);
            return Ok(new ApiStatusResult<DecisionEvaluationDto>(ToDto(result)));
        }
        catch (FlowzerDmnUnavailableException exception)
        {
            // Kein Serverfehler mit Stapelspur, sondern eine Auskunft: Dieser Server kann
            // Entscheidungen gerade nicht rechnen, und warum.
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ApiStatusResult<DecisionEvaluationDto>(
                    "Dieser Server kann Entscheidungen derzeit nicht rechnen: " + exception.Message));
        }
    }

    private async Task<bool> MayDryRun() =>
        (await authorizationService.AuthorizeAsync(User, FlowzerPolicies.Operator)).Succeeded
        || (await authorizationService.AuthorizeAsync(User, FlowzerPolicies.Modeler)).Succeeded;

    private async Task<DecisionDefinitionVersion> SaveAsync(
        string decisionDefinitionId,
        string? requestedName,
        DmnDefinitions parsed,
        string xml)
    {
        var name = ResolveName(decisionDefinitionId, requestedName, parsed);
        var currentUser = currentUserContextAccessor.GetCurrentUser();

        return await bpmnBusinessLogic.SaveDecisionVersion(
            decisionDefinitionId,
            name,
            xml,
            Summarize(parsed),
            // Ohne aufgeloesten Benutzerkontext (Installation ohne Anmeldung) steht hier
            // bewusst nichts statt einer erfundenen Kennung.
            currentUser.IsFallback ? null : currentUser.UserId);
    }

    private static bool TryReadXml(SaveDecisionDefinitionRequestDto? request, out string xml)
    {
        xml = request?.Xml ?? "";
        return !string.IsNullOrWhiteSpace(xml);
    }

    private static ApiStatusResult<T> NotFoundMessage<T>(string decisionDefinitionId) =>
        new($"Es gibt keine Entscheidungsdatei mit der Kennung „{decisionDefinitionId}“.");

    /// <summary>Der Anzeigename: Wunsch des Aufrufers, sonst <c>@name</c>, sonst die Kennung.</summary>
    private static string ResolveName(string decisionDefinitionId, string? requestedName, DmnDefinitions parsed)
    {
        var requested = requestedName?.Trim();
        if (!string.IsNullOrEmpty(requested))
        {
            return requested;
        }

        var modelName = parsed.Name?.Trim();
        return string.IsNullOrEmpty(modelName) ? decisionDefinitionId : modelName;
    }

    private static IReadOnlyList<DecisionSummary> Summarize(DmnDefinitions parsed) => parsed.Decisions
        .Select(decision => new DecisionSummary(
            decision.Id,
            string.IsNullOrWhiteSpace(decision.Name) ? decision.Id : decision.Name!))
        .ToArray();

    private static DecisionDefinitionDto ToDto(DecisionDefinition definition, DecisionDefinitionVersion version) =>
        ToDto(definition.Name, version);

    private static DecisionDefinitionDto ToDto(string name, DecisionDefinitionVersion version) =>
        new()
        {
            DecisionDefinitionId = version.DecisionDefinitionId,
            Name = name,
            Version = version.Version,
            DeployedAt = version.DeployedAt,
            DeployedBy = version.DeployedBy,
            Decisions = ToDto(version.Decisions)
        };

    private static DecisionDefinitionDetailDto ToDetailDto(
        DecisionDefinition definition, DecisionDefinitionVersion version) =>
        new()
        {
            DecisionDefinitionId = version.DecisionDefinitionId,
            Name = definition.Name,
            Version = version.Version,
            DeployedAt = version.DeployedAt,
            DeployedBy = version.DeployedBy,
            Decisions = ToDto(version.Decisions),
            Xml = version.Xml
        };

    private static DecisionDefinitionVersionDto ToVersionDto(DecisionDefinitionVersion version) =>
        new()
        {
            Version = version.Version,
            DeployedAt = version.DeployedAt,
            DeployedBy = version.DeployedBy,
            Decisions = ToDto(version.Decisions)
        };

    private static IReadOnlyList<DecisionSummaryDto> ToDto(IReadOnlyList<DecisionSummary> decisions) => decisions
        .Select(decision => new DecisionSummaryDto { DecisionId = decision.DecisionId, Name = decision.Name })
        .ToArray();

    private static DecisionEvaluationDto ToDto(DmnDecisionResult result) => new()
    {
        DecisionId = result.DecisionId,
        Value = result.Value,
        MatchedRules = result.MatchedRules,
        RequiredResults = result.RequiredResults.ToDictionary(
            entry => entry.Key,
            entry => new DecisionEvaluationResultDto
            {
                DecisionId = entry.Value.DecisionId,
                Value = entry.Value.Value,
                MatchedRules = entry.Value.MatchedRules
            },
            StringComparer.Ordinal)
    };
}
