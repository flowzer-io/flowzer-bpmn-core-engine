using Microsoft.Extensions.Options;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.Shared;

namespace WebApiEngine.Ai;

/// <summary>
/// Autorisierter Anwendungsfall fuer nicht geheime KI-Verbindungsmetadaten. Eingaben werden
/// gegen die Installationsgrenzen geprueft, bevor sie die persistente Ablage erreichen.
/// </summary>
public sealed class AiConnectionService(
    ITransactionalStorageProvider storageProvider,
    ICurrentUserContextAccessor currentUserAccessor,
    TimeProvider timeProvider,
    IOptions<FlowzerAiOptions> options,
    IAiSecretStore secretStore)
{
    private const int MaximumNameLength = 200;
    private const int MaximumModelLength = 200;
    private readonly FlowzerAiOptions _options = options.Value;

    public async Task<IReadOnlyList<AiConnectionDto>> ListAsync(bool includeDisabled)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        var connections = await storage.AiConnectionStorage.List();
        var projected = new List<AiConnectionDto>(connections.Count);
        foreach (var connection in connections
                     .Where(item => includeDisabled || item.Enabled)
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            projected.Add(await ToDto(connection));
        }
        return projected;
    }

    public async Task<AiConnectionDto> GetAsync(Guid id, bool includeDisabled)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        var connection = await storage.AiConnectionStorage.Get(RequireId(id));
        if (connection is null || !includeDisabled && !connection.Enabled)
            throw new AiConnectionNotFoundException();
        return await ToDto(connection);
    }

    public async Task<AiConnectionDto> CreateAsync(CreateAiConnectionRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = currentUserAccessor.GetCurrentUser()
            .RequireResolvedUserId("creating an AI connection");
        var connection = Build(
            Guid.NewGuid(),
            request.Name,
            request.Provider,
            request.Location,
            request.BaseAddress,
            request.DefaultModel,
            request.SecretReference,
            enabled: true,
            revision: 1,
            actor);

        using var storage = storageProvider.GetTransactionalStorage();
        var result = await storage.AiConnectionStorage.TryCreate(connection);
        if (result.Status != AiConnectionWriteStatus.Written)
        {
            throw new AiConnectionConflictException(0, result.CurrentRevision);
        }
        storage.CommitChanges();
        return await ToDto(result.Connection!);
    }

    public async Task<AiConnectionDto> UpdateAsync(Guid id, UpdateAiConnectionRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedRevision is < 1 or long.MaxValue)
            throw new ArgumentException("ExpectedRevision must be positive and incrementable.", nameof(request));
        var actor = currentUserAccessor.GetCurrentUser()
            .RequireResolvedUserId("updating an AI connection");

        using var storage = storageProvider.GetTransactionalStorage();
        var current = await storage.AiConnectionStorage.Get(RequireId(id))
                      ?? throw new AiConnectionNotFoundException();
        var connection = Build(
            current.Id,
            request.Name,
            request.Provider,
            request.Location,
            request.BaseAddress,
            request.DefaultModel,
            string.IsNullOrWhiteSpace(request.SecretReference)
                ? current.SecretReference
                : request.SecretReference,
            current.Enabled,
            checked(request.ExpectedRevision + 1),
            actor);
        var result = await storage.AiConnectionStorage.TryUpdate(connection, request.ExpectedRevision);
        return await FinishUpdate(storage, request.ExpectedRevision, result);
    }

    public async Task<AiConnectionDto> SetEnabledAsync(Guid id, SetAiConnectionEnabledRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedRevision is < 1 or long.MaxValue)
            throw new ArgumentException("ExpectedRevision must be positive and incrementable.", nameof(request));
        var actor = currentUserAccessor.GetCurrentUser()
            .RequireResolvedUserId("changing an AI connection status");

        using var storage = storageProvider.GetTransactionalStorage();
        var current = await storage.AiConnectionStorage.Get(RequireId(id))
                      ?? throw new AiConnectionNotFoundException();
        var changed = current with
        {
            Enabled = request.Enabled,
            Revision = checked(request.ExpectedRevision + 1),
            UpdatedAtUtc = timeProvider.GetUtcNow(),
            UpdatedByUserId = actor
        };
        var result = await storage.AiConnectionStorage.TryUpdate(changed, request.ExpectedRevision);
        return await FinishUpdate(storage, request.ExpectedRevision, result);
    }

    private async Task<AiConnectionDto> FinishUpdate(
        ITransactionalStorage storage,
        long expectedRevision,
        AiConnectionWriteResult result)
    {
        if (result.Status == AiConnectionWriteStatus.NotFound)
            throw new AiConnectionNotFoundException();
        if (result.Status == AiConnectionWriteStatus.Conflict)
            throw new AiConnectionConflictException(expectedRevision, result.CurrentRevision);
        storage.CommitChanges();
        return await ToDto(result.Connection!);
    }

    private AiConnection Build(
        Guid id,
        string name,
        AiProviderKindDto providerDto,
        AiProcessingLocationDto locationDto,
        string? baseAddress,
        string defaultModel,
        string secretReference,
        bool enabled,
        long revision,
        Guid actor)
    {
        if (!Enum.IsDefined(providerDto)) throw new ArgumentException("Provider is not supported.", nameof(providerDto));
        if (!Enum.IsDefined(locationDto)) throw new ArgumentException("Location is not supported.", nameof(locationDto));
        var provider = (AiProviderKind)providerDto;
        var location = (AiProcessingLocation)locationDto;
        if (!EnvironmentAiSecretStore.IsValidReference(secretReference, _options))
            throw new ArgumentException("SecretReference is outside the configured environment namespace.", nameof(secretReference));

        return new AiConnection(
            RequireId(id),
            Normalize(name, MaximumNameLength, nameof(name)),
            provider,
            location,
            AiConnectionSecurityPolicy.ValidateAndNormalizeTarget(provider, location, baseAddress, _options),
            Normalize(defaultModel, MaximumModelLength, nameof(defaultModel)),
            secretReference,
            enabled,
            revision,
            timeProvider.GetUtcNow(),
            actor);
    }

    private async Task<AiConnectionDto> ToDto(AiConnection connection) => new()
    {
        Id = connection.Id,
        Name = connection.Name,
        Provider = (AiProviderKindDto)connection.Provider,
        Location = (AiProcessingLocationDto)connection.Location,
        BaseAddress = connection.BaseAddress,
        DefaultModel = connection.DefaultModel,
        Enabled = connection.Enabled,
        Ready = connection.Enabled && await secretStore.ExistsAsync(connection.SecretReference),
        Revision = connection.Revision,
        UpdatedAtUtc = connection.UpdatedAtUtc
    };

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
            throw new ArgumentException($"{parameterName} must not exceed {maximumLength} characters.", parameterName);
        return trimmed;
    }

    private static Guid RequireId(Guid id) => id != Guid.Empty
        ? id
        : throw new ArgumentException("Connection ID is required.", nameof(id));
}

public sealed class AiConnectionNotFoundException()
    : KeyNotFoundException("The AI connection was not found.");

public sealed class AiConnectionConflictException(long expectedRevision, long currentRevision)
    : Exception("The AI connection has changed since it was loaded.")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long CurrentRevision { get; } = currentRevision;
}
