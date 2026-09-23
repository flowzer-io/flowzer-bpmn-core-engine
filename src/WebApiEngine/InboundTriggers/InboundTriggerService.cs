using Microsoft.Extensions.Options;
using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.InboundTriggers;

/// <summary>Die fachliche Ablehnung eines Verwaltungsaufrufs, mit dem Grund für den Aufrufer.</summary>
public sealed class InboundTriggerValidationException(string message) : Exception(message);

/// <summary>
/// Verwaltung der Auslöser: anlegen, ändern, Geheimnis erneuern, löschen.
///
/// Das Geheimnis verlässt diese Klasse genau zweimal — beim Anlegen und beim Erneuern — und
/// wird nie gespeichert. Jeder andere Weg liefert nur <see cref="InboundTriggerDto"/>.
/// </summary>
public sealed class InboundTriggerService(
    ITransactionalStorageProvider storageProvider,
    IOptions<InboundTriggerOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>Grenzen, die einen Eintrag brauchbar halten und Missbrauch als Datenablage verhindern.</summary>
    private const int MaxNameLength = 200;
    private const int MaxFieldNameLength = 200;
    private const int MaxAllowedFields = 50;
    private const int MaxCorrelationPathLength = 200;

    public async Task<IReadOnlyList<InboundTriggerDto>> ListAsync()
    {
        using var storage = storageProvider.GetTransactionalStorage();
        return (await storage.InboundTriggerStorage.GetAll()).Select(ToDto).ToArray();
    }

    public async Task<InboundTriggerSecretDto> CreateAsync(CreateInboundTriggerRequestDto request, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = RequireName(request.Name);
        var kind = ToModel(request.Kind);
        var mode = ToModel(request.VariablesMode);
        var allowedFields = NormalizeFields(request.AllowedFields);

        var installationKey = RequireInstallationKey();
        var id = Guid.NewGuid();
        var secret = InboundTriggerSecret.NewSecret();
        var trigger = new InboundTrigger
        {
            Id = id,
            Key = InboundTriggerSecret.NewKey(),
            Name = name,
            Kind = kind,
            VariablesMode = mode,
            AllowedFields = allowedFields,
            SecretHash = InboundTriggerSecret.Protect(secret, id, installationKey),
            Enabled = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            CreatedBy = userId
        };
        ApplyTarget(trigger, kind, request.DefinitionId, request.MessageName, request.CorrelationKeyPath);

        using var storage = storageProvider.GetTransactionalStorage();
        await EnsureWorkflowExists(storage, trigger);
        await storage.InboundTriggerStorage.Save(trigger);
        storage.CommitChanges();

        return new InboundTriggerSecretDto(ToDto(trigger), secret);
    }

    public async Task<InboundTriggerDto?> UpdateAsync(Guid id, UpdateInboundTriggerRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = RequireName(request.Name);
        var mode = ToModel(request.VariablesMode);
        var allowedFields = NormalizeFields(request.AllowedFields);

        using var storage = storageProvider.GetTransactionalStorage();
        var trigger = await storage.InboundTriggerStorage.Get(id);
        if (trigger is null) return null;

        trigger.Name = name;
        trigger.Enabled = request.Enabled;
        trigger.VariablesMode = mode;
        trigger.AllowedFields = allowedFields;
        // Die Art bleibt, wie sie ist: Ein bereits verteilter Schlüssel darf nicht still von
        // „startet einen Workflow" zu „stellt eine Nachricht zu" werden.
        ApplyTarget(trigger, trigger.Kind, request.DefinitionId, request.MessageName, request.CorrelationKeyPath);

        await EnsureWorkflowExists(storage, trigger);
        await storage.InboundTriggerStorage.Save(trigger);
        storage.CommitChanges();
        return ToDto(trigger);
    }

    /// <summary>
    /// Erzeugt ein neues Geheimnis und zeigt es einmal. Das alte gilt ab dem Commit nicht mehr;
    /// eine Übergangszeit mit zwei gültigen Geheimnissen gibt es bewusst nicht, weil sie das
    /// Zurückziehen eines verlorenen Geheimnisses aufschieben würde.
    /// </summary>
    public async Task<InboundTriggerSecretDto?> RotateSecretAsync(Guid id)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        var trigger = await storage.InboundTriggerStorage.Get(id);
        if (trigger is null) return null;

        var secret = InboundTriggerSecret.NewSecret();
        trigger.SecretHash = InboundTriggerSecret.Protect(secret, trigger.Id, RequireInstallationKey());
        await storage.InboundTriggerStorage.Save(trigger);
        storage.CommitChanges();
        return new InboundTriggerSecretDto(ToDto(trigger), secret);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        var removed = await storage.InboundTriggerStorage.Remove(id);
        if (removed) storage.CommitChanges();
        return removed;
    }

    /// <summary>
    /// Ohne installationsweiten Schlüssel gäbe es nichts, worunter ein Geheimnis versiegelt
    /// werden könnte. Dann wird kein Auslöser angelegt, statt einen ohne Schutz anzunehmen —
    /// wie bei der leeren Freigabeliste ausgehender Webhooks.
    /// </summary>
    private byte[] RequireInstallationKey()
    {
        var key = options.Value.ResolveKey();
        if (key.Length == 0)
        {
            throw new InboundTriggerValidationException(
                "Für Auslöser ist kein installationsweiter Schlüssel konfiguriert "
                + $"({InboundTriggerOptions.SectionName}__{nameof(InboundTriggerOptions.SecretKey)}). "
                + "Ohne ihn lässt sich kein Geheimnis sicher ablegen.");
        }

        return key;
    }

    /// <summary>
    /// Der Katalogeintrag muss es geben, bevor ein Auslöser darauf zeigt. Sonst entstünde ein
    /// Eintrag, der erst beim ersten Aufruf von außen auffällt — und dann als 422 beim fremden
    /// System, nicht beim Betrieb.
    /// </summary>
    private static async Task EnsureWorkflowExists(ITransactionalStorage storage, InboundTrigger trigger)
    {
        if (trigger.Kind != InboundTriggerKind.Start) return;
        var metaDefinitions = await storage.DefinitionStorage.GetAllMetaDefinitions();
        if (metaDefinitions.All(meta => meta.DefinitionId != trigger.DefinitionId))
        {
            throw new InboundTriggerValidationException(
                $"Es gibt keinen Workflow mit der Kennung \"{trigger.DefinitionId}\".");
        }
    }

    private static void ApplyTarget(
        InboundTrigger trigger,
        InboundTriggerKind kind,
        string? definitionId,
        string? messageName,
        string? correlationKeyPath)
    {
        if (kind == InboundTriggerKind.Start)
        {
            var id = definitionId?.Trim();
            if (string.IsNullOrEmpty(id))
                throw new InboundTriggerValidationException("Ein Auslöser der Art „start“ braucht einen Workflow.");
            trigger.DefinitionId = id;
            trigger.MessageName = null;
            trigger.CorrelationKeyPath = null;
            return;
        }

        var name = messageName?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new InboundTriggerValidationException("Ein Auslöser der Art „message“ braucht einen Nachrichtennamen.");

        var path = correlationKeyPath?.Trim();
        if (string.IsNullOrEmpty(path))
            throw new InboundTriggerValidationException("Ein Auslöser der Art „message“ braucht einen Pfad zum Korrelationsschlüssel.");
        if (path.Length > MaxCorrelationPathLength || !IsValidPath(path))
        {
            throw new InboundTriggerValidationException(
                "Der Pfad zum Korrelationsschlüssel besteht aus Feldnamen in Punktnotation, zum Beispiel „order.id“.");
        }

        trigger.MessageName = name;
        trigger.CorrelationKeyPath = path;
        trigger.DefinitionId = null;
    }

    /// <summary>Punktnotation ohne leere Abschnitte: <c>order.id</c>, nicht <c>order..id</c>.</summary>
    private static bool IsValidPath(string path) =>
        path.Split('.').All(segment => segment.Length > 0 && !segment.Any(char.IsWhiteSpace));

    private static string RequireName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new InboundTriggerValidationException("Der Auslöser braucht einen Namen.");
        if (trimmed.Length > MaxNameLength)
            throw new InboundTriggerValidationException($"Der Name darf höchstens {MaxNameLength} Zeichen lang sein.");
        return trimmed;
    }

    private static string[] NormalizeFields(string[]? fields)
    {
        if (fields is null || fields.Length == 0) return [];
        var normalized = fields
            .Select(field => field?.Trim() ?? string.Empty)
            .Where(field => field.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length > MaxAllowedFields)
            throw new InboundTriggerValidationException($"Es sind höchstens {MaxAllowedFields} Felder erlaubt.");
        if (normalized.Any(field => field.Length > MaxFieldNameLength))
            throw new InboundTriggerValidationException($"Ein Feldname darf höchstens {MaxFieldNameLength} Zeichen lang sein.");
        return normalized;
    }

    public static InboundTriggerDto ToDto(InboundTrigger trigger) => new(
        trigger.Id,
        trigger.Key,
        trigger.Name,
        trigger.Kind == InboundTriggerKind.Start ? InboundTriggerKindDto.Start : InboundTriggerKindDto.Message,
        trigger.DefinitionId,
        trigger.MessageName,
        trigger.CorrelationKeyPath,
        trigger.VariablesMode == InboundTriggerVariablesMode.Body
            ? InboundTriggerVariablesModeDto.Body
            : InboundTriggerVariablesModeDto.Fields,
        trigger.AllowedFields,
        trigger.Enabled,
        trigger.CreatedAt,
        trigger.LastUsedAt,
        trigger.UseCount,
        trigger.LastFailureAt,
        trigger.LastFailureReason);

    private static InboundTriggerKind ToModel(InboundTriggerKindDto kind) =>
        kind == InboundTriggerKindDto.Start ? InboundTriggerKind.Start : InboundTriggerKind.Message;

    private static InboundTriggerVariablesMode ToModel(InboundTriggerVariablesModeDto mode) =>
        mode == InboundTriggerVariablesModeDto.Body
            ? InboundTriggerVariablesMode.Body
            : InboundTriggerVariablesMode.Fields;
}
