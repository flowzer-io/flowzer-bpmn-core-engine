using System.Net.Http.Headers;

namespace WebApiEngine.IdentityDirectory;

public sealed partial class KeycloakAdminClient
{
    /// <summary>
    /// Liest ausschließlich den konfigurierten Teilbaum und die darin gefundenen
    /// Personen. Ein realmweiter User-/Gruppenabruf mit nachträglichem Filtern
    /// würde unnötig fremde Profile lesen und ist hier bewusst ausgeschlossen.
    /// </summary>
    private async Task<KeycloakDirectorySnapshot> GetScopedSnapshotAsync(
        ValidatedConfiguration configuration,
        AccessTokenLease accessToken,
        CancellationToken cancellationToken)
    {
        var root = await GetResourceAsync<KeycloakGroupRepresentation>(
            configuration.AdminResourceEndpoint($"groups/{Uri.EscapeDataString(_options.RootGroupId)}"),
            accessToken, cancellationToken);
        if (!string.Equals(root.Id, _options.RootGroupId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(root.Path))
        {
            throw InvalidResponse("Keycloak returned an invalid configured root group.");
        }

        var groups = new Dictionary<string, KeycloakDirectoryGroup>(StringComparer.Ordinal);
        await LoadGroupTreeAsync(root, groups, new HashSet<string>(StringComparer.Ordinal),
            configuration, accessToken, cancellationToken);

        var subjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var groupId in groups.Keys.Order(StringComparer.Ordinal))
        {
            var members = await GetPagedAsync<KeycloakUserRepresentation>(
                configuration.AdminEndpoint($"groups/{Uri.EscapeDataString(groupId)}/members"),
                user => user.Id, "group member", accessToken, cancellationToken);
            foreach (var member in members) subjects.Add(RequireId(member.Id, "group member"));
        }

        var users = new List<KeycloakDirectoryUser>();
        foreach (var subject in subjects.Order(StringComparer.Ordinal))
        {
            // Ein Mitglied kann mehreren Gruppen angehören. Sein aktuelles Profil
            // wird genau einmal gelesen, statt veraltete Member-Listen zu bevorzugen.
            var user = await GetResourceAsync<KeycloakUserRepresentation>(
                configuration.AdminResourceEndpoint($"users/{Uri.EscapeDataString(subject)}"),
                accessToken, cancellationToken);
            if (!string.Equals(user.Id, subject, StringComparison.Ordinal))
            {
                throw InvalidResponse("Keycloak returned a different requested user identifier.");
            }

            var memberships = await GetPagedAsync<KeycloakGroupRepresentation>(
                configuration.AdminEndpoint($"users/{Uri.EscapeDataString(subject)}/groups"),
                group => group.Id, "membership group", accessToken, cancellationToken);
            var groupIds = new List<string>();
            foreach (var membership in memberships)
            {
                var id = RequireId(membership.Id, "membership group");
                if (groups.TryGetValue(id, out var loaded))
                {
                    if (!string.Equals(membership.Path, loaded.Path, StringComparison.Ordinal))
                        throw InvalidResponse("Keycloak returned a changed membership group path.");
                    groupIds.Add(id);
                }
                else if (string.IsNullOrWhiteSpace(membership.Path)
                         || string.Equals(membership.Path, root.Path, StringComparison.Ordinal)
                         || membership.Path.StartsWith(root.Path + "/", StringComparison.Ordinal))
                {
                    // Der Pfad dient nur als Drift-Erkennung, niemals als Freigabe.
                    // Eine neue/unbekannte Untergruppe erfordert einen neuen Vollimport.
                    throw InvalidResponse("Keycloak returned an unknown membership inside the configured tree.");
                }
            }

            // Entzug während des Imports: keine Person ohne aktuelle Mitgliedschaft
            // übernehmen. Publish deaktiviert ihren früheren Eintrag atomar.
            if (groupIds.Count == 0) continue;
            users.Add(new KeycloakDirectoryUser(subject, user.Enabled ?? false, user.Username,
                user.FirstName, user.LastName, groupIds.Order(StringComparer.Ordinal).ToArray(), user.Email));
        }

        return new KeycloakDirectorySnapshot(users, groups.Values.OrderBy(group => group.Id, StringComparer.Ordinal).ToArray());
    }

    private Task<T> GetResourceAsync<T>(Uri endpoint, AccessTokenLease accessToken, CancellationToken cancellationToken) =>
        SendJsonAsync<T>(async requestToken =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken.GetValidTokenAsync(requestToken));
            return request;
        }, "directory resource request", cancellationToken, accessToken.StrictResponse);
}
