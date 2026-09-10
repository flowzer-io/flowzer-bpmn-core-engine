using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace WebApiEngine.IdentityDirectory;

/// <summary>
/// Liest das Keycloak-Admin-API mit Client-Credentials. Der Client kennt nur die für das
/// Verzeichnis benötigten Felder und führt keine schreibenden HTTP-Operationen aus.
/// </summary>
public sealed class KeycloakAdminClient : IKeycloakAdminClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly KeycloakDirectoryOptions _options;
    private readonly TimeProvider _timeProvider;

    public KeycloakAdminClient(
        HttpClient httpClient,
        IOptions<KeycloakDirectoryOptions> options,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<KeycloakDirectorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var configuration = ValidateConfiguration();
        var accessToken = new AccessTokenLease(this, configuration);
        var users = await GetPagedAsync<KeycloakUserRepresentation>(
            configuration.AdminEndpoint("users"), user => user.Id, "user", accessToken, cancellationToken);
        var rootGroups = await GetPagedAsync<KeycloakGroupRepresentation>(
            configuration.AdminEndpoint("groups"), group => group.Id, "group", accessToken, cancellationToken);

        var groups = new Dictionary<string, KeycloakDirectoryGroup>(StringComparer.Ordinal);
        var loadedGroupTrees = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in rootGroups)
        {
            await LoadGroupTreeAsync(group, groups, loadedGroupTrees, configuration, accessToken, cancellationToken);
        }

        var directoryUsers = new List<KeycloakDirectoryUser>(users.Count);
        var knownSubjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var user in users)
        {
            var subject = RequireId(user.Id, "user");
            if (!knownSubjects.Add(subject))
            {
                throw InvalidResponse("Keycloak returned the same user identifier more than once.");
            }

            var memberGroups = await GetPagedAsync<KeycloakGroupRepresentation>(
                configuration.AdminEndpoint($"users/{Uri.EscapeDataString(subject)}/groups"),
                group => group.Id,
                "membership group",
                accessToken,
                cancellationToken);

            var groupIds = memberGroups
                .Select(group => RequireId(group.Id, "group"))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (groupIds.Any(groupId => !groups.ContainsKey(groupId)))
            {
                // Eine Gruppe, die erst nach dem Hierarchieabruf sichtbar wird, koennte ohne
                // Eltern oder Kinder nur als falsche Wurzel gespeichert werden. Der gesamte
                // Lauf wird deshalb verworfen und beim naechsten Intervall konsistent neu gelesen.
                throw InvalidResponse("Keycloak returned a membership outside the loaded group hierarchy.");
            }
            directoryUsers.Add(new KeycloakDirectoryUser(
                subject,
                user.Enabled ?? false,
                user.Username,
                user.FirstName,
                user.LastName,
                groupIds));
        }

        return new KeycloakDirectorySnapshot(
            directoryUsers.OrderBy(user => user.Subject, StringComparer.Ordinal).ToArray(),
            groups.Values.OrderBy(group => group.Id, StringComparer.Ordinal).ToArray());
    }

    private async Task<AccessToken> GetAccessTokenAsync(ValidatedConfiguration configuration, CancellationToken cancellationToken)
    {
        var token = await SendJsonAsync<KeycloakTokenResponse>(_ =>
            Task.FromResult(new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = configuration.ClientId,
                    ["client_secret"] = configuration.ClientSecret
                })
            }), "token request", cancellationToken);
        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw InvalidResponse("Keycloak token response did not contain an access token.");
        }

        var lifetimeSeconds = token.ExpiresIn is > 0 ? token.ExpiresIn.Value : 60;
        return new AccessToken(
            token.AccessToken,
            _timeProvider.GetUtcNow().AddSeconds(Math.Clamp(lifetimeSeconds, 1, 86_400)));
    }

    private async Task<List<T>> GetPagedAsync<T>(
        Func<int, Uri> endpoint,
        Func<T, string?> getStableId,
        string entityName,
        AccessTokenLease accessToken,
        CancellationToken cancellationToken)
    {
        var result = new List<T>();
        var knownIdentifiers = new HashSet<string>(StringComparer.Ordinal);
        var pageSize = Math.Clamp(_options.PageSize, 1, 1_000);
        var maxPages = Math.Clamp(_options.MaxPages, 1, 100_000);
        var first = 0;

        for (var page = 0; page < maxPages; page++)
        {
            var pageOffset = first;
            var currentPage = await SendJsonAsync<List<T>>(async requestToken =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, endpoint(pageOffset));
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    await accessToken.GetValidTokenAsync(requestToken));
                return request;
            }, "directory request", cancellationToken);
            if (currentPage is null)
            {
                throw InvalidResponse("Keycloak returned an invalid directory response.");
            }

            if (currentPage.Count > pageSize)
            {
                throw InvalidResponse("Keycloak returned more entries than the requested page size.");
            }

            foreach (var entry in currentPage)
            {
                var stableId = RequireId(getStableId(entry), entityName);
                if (!knownIdentifiers.Add(stableId))
                {
                    // Offset-Pagination kann sich bei parallelen Provideränderungen überlappen.
                    // Das ist kein vollständiger Stand und darf daher nie publiziert werden.
                    throw InvalidResponse($"Keycloak returned the same {entityName} identifier more than once.");
                }
            }

            result.AddRange(currentPage);
            if (currentPage.Count < pageSize)
            {
                return result;
            }

            try
            {
                first = checked(first + pageSize);
            }
            catch (OverflowException exception)
            {
                throw new KeycloakAdminClientException(
                    KeycloakAdminClientFailureKind.InvalidResponse,
                    "Keycloak pagination exceeded the supported range.",
                    exception);
            }
        }

        throw InvalidResponse("Keycloak pagination exceeded the configured page limit.");
    }

    private async Task LoadGroupTreeAsync(
        KeycloakGroupRepresentation group,
        IDictionary<string, KeycloakDirectoryGroup> groups,
        ISet<string> loadedGroupTrees,
        ValidatedConfiguration configuration,
        AccessTokenLease accessToken,
        CancellationToken cancellationToken,
        string? parentId = null)
    {
        var groupId = RequireId(group.Id, "group");
        AddGroup(groups, group, parentId);
        if (!loadedGroupTrees.Add(groupId)) return;

        // Keycloak liefert im Root-Endpoint ausschließlich Top-Level-Gruppen. Alle Kinder
        // müssen daher separat und wiederum paginiert geladen werden.
        var children = await GetPagedAsync<KeycloakGroupRepresentation>(
            configuration.AdminEndpoint($"groups/{Uri.EscapeDataString(groupId)}/children"),
            child => child.Id,
            "group",
            accessToken,
            cancellationToken);
        foreach (var child in children)
        {
            await LoadGroupTreeAsync(child, groups, loadedGroupTrees, configuration, accessToken, cancellationToken, groupId);
        }
    }

    private async Task<T> SendJsonAsync<T>(
        Func<CancellationToken, Task<HttpRequestMessage>> createRequest,
        string operation,
        CancellationToken cancellationToken)
    {
        var retries = Math.Clamp(_options.MaxRetries, 0, 5);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.RequestTimeoutSeconds, 1, 300)));
                using var request = await createRequest(requestTimeout.Token);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    var maximumBytes = Math.Clamp(_options.MaxResponseBytes, 1_024, 32 * 1024 * 1024);
                    if (response.Content.Headers.ContentLength > maximumBytes)
                    {
                        throw InvalidResponse("Keycloak returned a directory response above the configured size limit.");
                    }

                    await using var body = await response.Content.ReadAsStreamAsync(requestTimeout.Token);
                    await using var limitedBody = new SizeLimitedReadStream(body, maximumBytes);
                    var result = await JsonSerializer.DeserializeAsync<T>(limitedBody, JsonOptions, requestTimeout.Token);
                    if (result is null)
                    {
                        throw InvalidResponse("Keycloak returned an empty response.");
                    }

                    return result;
                }

                var kind = Classify(response.StatusCode, request.Method == HttpMethod.Post);
                if (kind == KeycloakAdminClientFailureKind.Transient && attempt < retries)
                {
                    await DelayBeforeRetry(cancellationToken);
                    continue;
                }

                throw new KeycloakAdminClientException(kind, $"Keycloak {operation} failed with HTTP {(int)response.StatusCode}.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < retries)
            {
                await DelayBeforeRetry(cancellationToken);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new KeycloakAdminClientException(
                    KeycloakAdminClientFailureKind.Transient,
                    "Keycloak did not complete the response within the configured time limit.",
                    exception);
            }
            catch (HttpRequestException) when (attempt < retries)
            {
                await DelayBeforeRetry(cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new KeycloakAdminClientException(
                    KeycloakAdminClientFailureKind.Transient,
                    "Keycloak could not be reached after bounded retries.",
                    exception);
            }
            catch (JsonException exception)
            {
                throw new KeycloakAdminClientException(
                    KeycloakAdminClientFailureKind.InvalidResponse,
                    "Keycloak returned malformed JSON.",
                    exception);
            }
            catch (DirectoryResponseTooLargeException exception)
            {
                throw new KeycloakAdminClientException(
                    KeycloakAdminClientFailureKind.InvalidResponse,
                    "Keycloak returned a directory response above the configured size limit.",
                    exception);
            }
            catch (IOException) when (attempt < retries)
            {
                await DelayBeforeRetry(cancellationToken);
            }
            catch (IOException exception)
            {
                throw new KeycloakAdminClientException(
                    KeycloakAdminClientFailureKind.Transient,
                    "Keycloak response body could not be read after bounded retries.",
                    exception);
            }
        }
    }

    private Task DelayBeforeRetry(CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(_options.RetryDelayMilliseconds, 0, 30_000)), cancellationToken);

    private ValidatedConfiguration ValidateConfiguration()
    {
        if (!Uri.TryCreate(_options.ServerUrl, UriKind.Absolute, out var serverUrl)
            || serverUrl.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(serverUrl.UserInfo)
            || !string.IsNullOrEmpty(serverUrl.Query)
            || !string.IsNullOrEmpty(serverUrl.Fragment)
            || string.IsNullOrWhiteSpace(_options.Realm)
            || string.IsNullOrWhiteSpace(_options.ClientId)
            || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new KeycloakAdminClientException(
                KeycloakAdminClientFailureKind.Configuration,
                "Keycloak directory synchronization is not configured with a valid HTTPS endpoint and client credentials.");
        }

        return new ValidatedConfiguration(
            serverUrl.ToString().TrimEnd('/'),
            Uri.EscapeDataString(_options.Realm),
            _options.ClientId,
            _options.ClientSecret,
            Math.Clamp(_options.PageSize, 1, 1_000));
    }

    private static void AddGroup(
        IDictionary<string, KeycloakDirectoryGroup> destination,
        KeycloakGroupRepresentation group,
        string? parentId)
    {
        var id = RequireId(group.Id, "group");
        var directoryGroup = new KeycloakDirectoryGroup(id, group.Name, group.Path, parentId);
        if (destination.ContainsKey(id))
        {
            // Derselbe Knoten in Root-, Untergruppen- oder Geschwisterpfaden belegt eine
            // inkonsistente Quellsicht. Auch identische Werte dürfen den Fehler nicht verdecken.
            throw InvalidResponse("Keycloak returned the same group identifier more than once.");
        }

        destination.Add(id, directoryGroup);
    }

    private static string RequireId(string? id, string entity) =>
        !string.IsNullOrWhiteSpace(id)
            ? id
            : throw InvalidResponse($"Keycloak returned a {entity} without a stable identifier.");

    private static KeycloakAdminClientException InvalidResponse(string message) =>
        new(KeycloakAdminClientFailureKind.InvalidResponse, message);

    private static KeycloakAdminClientFailureKind Classify(HttpStatusCode statusCode, bool isTokenRequest) => statusCode switch
    {
        HttpStatusCode.BadRequest when isTokenRequest => KeycloakAdminClientFailureKind.Authentication,
        HttpStatusCode.Unauthorized when isTokenRequest => KeycloakAdminClientFailureKind.Authentication,
        HttpStatusCode.Unauthorized => KeycloakAdminClientFailureKind.Authorization,
        HttpStatusCode.Forbidden => KeycloakAdminClientFailureKind.Authorization,
        HttpStatusCode.NotFound => KeycloakAdminClientFailureKind.NotFound,
        HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => KeycloakAdminClientFailureKind.Transient,
        >= HttpStatusCode.InternalServerError => KeycloakAdminClientFailureKind.Transient,
        _ => KeycloakAdminClientFailureKind.Permanent
    };

    private sealed record ValidatedConfiguration(string ServerUrl, string Realm, string ClientId, string ClientSecret, int PageSize)
    {
        public Uri TokenEndpoint => new($"{ServerUrl}/realms/{Realm}/protocol/openid-connect/token", UriKind.Absolute);

        public Func<int, Uri> AdminEndpoint(string resource) => first =>
            new Uri($"{ServerUrl}/admin/realms/{Realm}/{resource}?first={first}&max={PageSize}&briefRepresentation=true", UriKind.Absolute);
    }

    private sealed class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }
    }

    private sealed class KeycloakUserRepresentation
    {
        public string? Id { get; init; }
        public bool? Enabled { get; init; }
        public string? Username { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
    }

    private sealed class KeycloakGroupRepresentation
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Path { get; init; }
        public List<KeycloakGroupRepresentation>? SubGroups { get; init; }
    }

    private sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

    private sealed class AccessTokenLease(KeycloakAdminClient owner, ValidatedConfiguration configuration)
    {
        private AccessToken? _token;

        public async Task<string> GetValidTokenAsync(CancellationToken cancellationToken)
        {
            var refreshSkew = TimeSpan.FromSeconds(Math.Clamp(owner._options.TokenRefreshSkewSeconds, 0, 300));
            if (_token is null || _token.ExpiresAtUtc <= owner._timeProvider.GetUtcNow().Add(refreshSkew))
            {
                _token = await owner.GetAccessTokenAsync(configuration, cancellationToken);
            }

            return _token.Value;
        }
    }

    /// <summary>
    /// Begrenzt auch Antworten ohne Content-Length, ohne Transportabbrueche faelschlich als
    /// dauerhaften Groessenfehler zu klassifizieren.
    /// </summary>
    private sealed class SizeLimitedReadStream(Stream inner, long maximumBytes) : Stream
    {
        private long _bytesRead;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Count(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            Count(read);
            return read;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private void Count(int read)
        {
            _bytesRead += read;
            if (_bytesRead > maximumBytes)
            {
                throw new DirectoryResponseTooLargeException();
            }
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class DirectoryResponseTooLargeException : IOException;
}
