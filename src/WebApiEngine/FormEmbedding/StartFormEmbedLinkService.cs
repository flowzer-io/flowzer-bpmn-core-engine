using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Model;
using StorageSystem;
using StorageSystem.Exceptions;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Forms;
using WebApiEngine.IdentityDirectory;
using WebApiEngine.Shared;

namespace WebApiEngine.FormEmbedding;

/// <summary>
/// Persönliche Read-only-Startformularlinks; Start/Directory erfolgen weiterhin durch
/// den authentifizierten Host. Eine Anzeige erzeugt weder Aufgabe noch Instanz oder Draft.
/// </summary>
public sealed class StartFormEmbedLinkService(BpmnBusinessLogic engine, IStorageSystem readStorage,
    ICurrentUserContextAccessor currentUser, IOptions<FormEmbeddingOptions> options,
    TimeProvider clock, IOptions<KeycloakDirectoryOptions> directoryOptions)
{
    /// <summary>Bindet den Einstieg an die angezeigte Definition und stabile Identität.</summary>
    public async Task<StartFormEmbedLinkDto?> IssueAsync(string metaId, Guid version, string hostOrigin, CancellationToken cancellationToken)
    {
        if (!options.Value.Allows(hostOrigin)) return null;
        var actor = currentUser.GetCurrentUser();
        actor.RequireResolvedUserId("opening embedded start forms");
        if (actor.Identity is null) return null;
        await readStorage.StartFormEmbedGrantStorage.CleanupExpired(clock.GetUtcNow(), cancellationToken);
        return await engine.ExecuteUserTaskMutationAsync<StartFormEmbedLinkDto?>(async storage =>
        {
            if (!await CanRead(storage, actor)) return null;
            var reference = await engine.GetStartFormReference(metaId, expectedDefinitionId: version);
            if (reference.FormKey is null) return new StartFormEmbedLinkDto { DefinitionId = version, FormLink = null };
            if (await ResolveForm(storage, reference) is null) return null;
            var now = clock.GetUtcNow(); var expires = now.AddMinutes(5);
            var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            await storage.StartFormEmbedGrantStorage.Create(new StartFormEmbedGrant
            {
                SecretHash = Hash(secret), OwnerKey = UserTaskDraftOwnerKey.Create(actor.Identity),
                OwnerUserId = actor.UserId, Owner = actor.Identity, DefinitionId = version,
                RelatedDefinitionId = metaId, HostOrigin = hostOrigin, ExpiresAtUtc = expires
            }, now);
            return new StartFormEmbedLinkDto { DefinitionId = version,
                FormLink = new FormEmbedLinkDto { Url = options.Value.PublicOrigin + "/embed.html#start." + secret, RedeemBeforeUtc = expires } };
        }, cancellationToken);
    }

    /// <summary>Einmalige Anzeige nach erneuter Directory-, Installations- und Versionsprüfung.</summary>
    public async Task<StartFormEmbedSnapshotDto?> RedeemAsync(string? secret, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || secret is null || secret.Length != 43
            || secret.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) return null;
        var hash = Hash(secret);
        if (await readStorage.StartFormEmbedGrantStorage.Find(hash, clock.GetUtcNow(), cancellationToken) is null) return null;
        return await engine.ExecuteUserTaskMutationAsync<StartFormEmbedSnapshotDto?>(async storage =>
        {
            var grant = await storage.StartFormEmbedGrantStorage.TryTake(hash, clock.GetUtcNow());
            if (grant is null || !options.Value.Allows(grant.HostOrigin)) return null;
            var actor = new CurrentUserContext(grant.OwnerUserId, "start-form-embed-read", false) { Identity = grant.Owner };
            if (!await CanRead(storage, actor)) return null;
            BpmnBusinessLogic.StartFormReference reference;
            try { reference = await engine.GetStartFormReference(grant.RelatedDefinitionId, expectedDefinitionId: grant.DefinitionId); }
            catch (Exception error) when (error is WorkflowVersionConflictException or DefinitionStorageNotFoundException or FileNotFoundException or InvalidOperationException)
            { return null; }
            var form = await ResolveForm(storage, reference);
            return form is null ? null : new StartFormEmbedSnapshotDto { DefinitionId = grant.DefinitionId,
                RelatedDefinitionId = grant.RelatedDefinitionId, HostOrigin = grant.HostOrigin, Form = form };
        }, cancellationToken);
    }

    private async Task<bool> CanRead(IStorageSystem storage, CurrentUserContext actor)
    {
        var snapshot = await storage.IdentityDirectoryStorage.GetActiveSnapshot();
        var directory = directoryOptions.Value;
        return actor.Identity is { } identity && DirectoryIdentityAccess.Resolve(actor, snapshot) is not null
            && (string.IsNullOrEmpty(directory.RootGroupId) || DirectoryGroupScope.AllowsIdentity(snapshot,
                directory.Issuer, directory.RootGroupId, identity.Issuer, identity.Subject));
    }

    private static async Task<FormDto?> ResolveForm(IStorageSystem storage, BpmnBusinessLogic.StartFormReference reference)
    {
        if (reference.FormKey is null) return null;
        var form = (await new FormKeyResolver(storage).ResolveAsync(reference.FormKey, reference.DefinitionId)).Form;
        if (form?.FormData is null) return null;
        _ = FormContractCompiler.Compile(form.FormData);
        return FormEmbeddingSchemaSupport.IsSupported(form.FormData) ? form : null;
    }

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));
}
